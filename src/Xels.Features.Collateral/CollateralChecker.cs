﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Controllers.Models;
using Xels.Bitcoin.EventBus;
using Xels.Bitcoin.Features.BlockStore.AddressIndexing;
using Xels.Bitcoin.Features.BlockStore.Controllers;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Features.PoA.Events;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Utilities;
using Xels.Features.PoA.Collateral.CounterChain;

namespace Xels.Features.Collateral
{
    /// <summary>Class that checks if federation members fulfill the collateral requirement.</summary>
    public interface ICollateralChecker : IDisposable
    {
        Task InitializeAsync();

        /// <summary>Checks if given federation member fulfills the collateral requirement.</summary>
        /// <param name="federationMember">The federation member whose collateral will be checked.</param>
        /// <param name="heightToCheckAt">Counter chain height at which collateral should be checked.</param>
        /// <returns><c>True</c> if the collateral requirement is fulfilled and <c>false</c> otherwise.</returns>
        bool CheckCollateral(IFederationMember federationMember, int heightToCheckAt);

        int GetCounterChainConsensusHeight();
    }

    public class CollateralChecker : ICollateralChecker
    {
        private readonly IBlockStoreClient blockStoreClient;

        private readonly IFederationManager federationManager;

        private readonly ISignals signals;
        private readonly IAsyncProvider asyncProvider;

        private readonly ILogger logger;

        /// <summary>Protects access to <see cref="balancesDataByAddress"/> and <see cref="counterChainConsensusTipHeight"/>.</summary>
        private readonly object locker;

        private readonly CancellationTokenSource cancellationSource;

        private SubscriptionToken memberAddedToken, memberKickedToken;

        private const int CollateralUpdateIntervalSeconds = 20;

        private readonly int maxReorgLength;

        /// <summary>Verbose address data mapped by address.</summary>
        /// <remarks>
        /// Deposits are not updated if federation member doesn't have collateral requirement enabled.
        /// All access should be protected by <see cref="locker"/>.
        /// </remarks>
        private readonly Dictionary<string, AddressIndexerData> balancesDataByAddress;

        /// <summary>Consensus tip height of a counter chain.</summary>
        /// <remarks>All access should be protected by <see cref="locker"/>.</remarks>
        private int counterChainConsensusTipHeight;

        private Task updateCollateralContinuouslyTask;

        private bool collateralUpdated;

        public CollateralChecker(IHttpClientFactory httpClientFactory, ICounterChainSettings settings, IFederationManager federationManager,
            ISignals signals, Network network, IAsyncProvider asyncProvider, INodeLifetime nodeLifetime)
        {
            this.federationManager = federationManager;
            this.signals = signals;
            this.asyncProvider = asyncProvider;

            this.maxReorgLength = AddressIndexer.GetMaxReorgOrFallbackMaxReorg(network);
            this.cancellationSource = CancellationTokenSource.CreateLinkedTokenSource(nodeLifetime.ApplicationStopping);
            this.locker = new object();
            this.balancesDataByAddress = new Dictionary<string, AddressIndexerData>();
            this.logger = LogManager.GetCurrentClassLogger();
            this.blockStoreClient = new BlockStoreClient(httpClientFactory, $"http://{settings.CounterChainApiHost}", settings.CounterChainApiPort);
        }

        public async Task InitializeAsync()
        {
            this.memberAddedToken = this.signals.Subscribe<FedMemberAdded>(this.OnFedMemberAdded);
            this.memberKickedToken = this.signals.Subscribe<FedMemberKicked>(this.OnFedMemberKicked);

            foreach (CollateralFederationMember federationMember in this.federationManager.GetFederationMembers()
                .Cast<CollateralFederationMember>().Where(x => x.CollateralAmount != null && x.CollateralAmount > 0))
            {
                this.logger.LogInformation("Initializing federation member {0} with amount {1}.", federationMember.CollateralMainchainAddress, federationMember.CollateralAmount);
                this.balancesDataByAddress.Add(federationMember.CollateralMainchainAddress, null);
            }

            while (!this.cancellationSource.IsCancellationRequested)
            {
                await this.UpdateCollateralInfoAsync(this.cancellationSource.Token).ConfigureAwait(false);

                if (this.collateralUpdated)
                    break;

                this.logger.LogWarning("Node initialization will not continue until the gateway node responds.");
                await this.DelayCollateralCheckAsync().ConfigureAwait(false);
            }

            this.updateCollateralContinuouslyTask = this.UpdateCollateralInfoContinuouslyAsync();

#pragma warning disable 4014
            this.asyncProvider.RegisterTask($"{nameof(CollateralChecker)}.{nameof(this.updateCollateralContinuouslyTask)}", this.updateCollateralContinuouslyTask);
#pragma warning restore 4014
        }

        public int GetCounterChainConsensusHeight()
        {
            lock (this.locker)
            {
                return this.counterChainConsensusTipHeight;
            }
        }

        /// <summary>Continuously updates info about money deposited to fed member's addresses.</summary>
        /// <returns>The asynchronous task.</returns>
        private async Task UpdateCollateralInfoContinuouslyAsync()
        {
            while (!this.cancellationSource.IsCancellationRequested)
            {
                await this.UpdateCollateralInfoAsync(this.cancellationSource.Token).ConfigureAwait(false);

                await this.DelayCollateralCheckAsync().ConfigureAwait(false);
            }
        }

        /// <summary>
        /// Delay checking the federation member's collateral with <see cref="CollateralUpdateIntervalSeconds"/> seconds.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        private async Task DelayCollateralCheckAsync()
        {
            try
            {
                await Task.Delay(CollateralUpdateIntervalSeconds * 1000, this.cancellationSource.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                this.logger.LogTrace("(-)[CANCELLED]");
            }
        }

        private async Task UpdateCollateralInfoAsync(CancellationToken cancellation)
        {
            List<string> addressesToCheck;

            lock (this.locker)
            {
                addressesToCheck = this.balancesDataByAddress.Keys.ToList();
            }

            if (addressesToCheck.Count == 0)
            {
                this.logger.LogInformation("None of the federation members has a collateral requirement configured.");
            }

            this.logger.LogDebug("Addresses to check {0}.", addressesToCheck.Count);

            VerboseAddressBalancesResult verboseAddressBalanceResult = await this.blockStoreClient.GetVerboseAddressesBalancesDataAsync(addressesToCheck, cancellation).ConfigureAwait(false);

            if (verboseAddressBalanceResult == null)
            {
                this.logger.LogWarning("Failed to update collateral, please ensure that the mainnet gateway node is running and it's API feature is enabled.");
                this.logger.LogTrace("(-)[CALL_RETURNED_NULL_RESULT]:false");
                return;
            }

            if (!string.IsNullOrEmpty(verboseAddressBalanceResult.Reason))
            {
                this.logger.LogWarning("Failed to fetch address balances from the counter chain node : {0}", verboseAddressBalanceResult.Reason);
                this.logger.LogTrace("(-)[FAILED]:{0}", verboseAddressBalanceResult.Reason);
                return;
            }

            this.logger.LogDebug("Addresses received {0}.", verboseAddressBalanceResult.BalancesData.Count);

            if (verboseAddressBalanceResult.BalancesData.Count != addressesToCheck.Count)
            {
                this.logger.LogDebug("Expected {0} data entries but received {1}.", addressesToCheck.Count, verboseAddressBalanceResult.BalancesData.Count);
                this.logger.LogTrace("(-)[CALL_RETURNED_INCONSISTENT_DATA]:false");
                return;
            }

            lock (this.locker)
            {
                foreach (AddressIndexerData balanceData in verboseAddressBalanceResult.BalancesData)
                {
                    this.logger.LogDebug("Updating federation member address {0}.", balanceData.Address);
                    this.balancesDataByAddress[balanceData.Address] = balanceData;
                }

                this.counterChainConsensusTipHeight = verboseAddressBalanceResult.ConsensusTipHeight;
            }

            this.collateralUpdated = true;
        }

        /// <inheritdoc />
        public bool CheckCollateral(IFederationMember federationMember, int heightToCheckAt)
        {
            if (!this.collateralUpdated)
            {
                this.logger.LogDebug("(-)[NOT_INITIALIZED]");
                throw new Exception("Component is not initialized!");
            }

            var member = federationMember as CollateralFederationMember;

            if (member == null)
            {
                this.logger.LogDebug("(-)[WRONG_TYPE]");
                throw new ArgumentException($"{nameof(federationMember)} should be of type: {nameof(CollateralFederationMember)}.");
            }

            lock (this.locker)
            {
                if (heightToCheckAt > this.counterChainConsensusTipHeight - this.maxReorgLength)
                {
                    this.logger.LogDebug("(-)[HEIGHTTOCHECK_HIGHER_THAN_COUNTER_TIP_LESS_MAXREORG]:{0}={1}, {2}={3}:false", nameof(heightToCheckAt), heightToCheckAt, nameof(this.counterChainConsensusTipHeight), this.counterChainConsensusTipHeight);
                    return false;
                }
            }

            if ((member.CollateralAmount == null) || (member.CollateralAmount == 0))
            {
                this.logger.LogDebug("(-)[NO_COLLATERAL_REQUIREMENT]:true");
                return true;
            }

            lock (this.locker)
            {
                AddressIndexerData balanceData = this.balancesDataByAddress[member.CollateralMainchainAddress];

                if (balanceData == null)
                {
                    // No data. Assume collateral is 0. It's ok if there is no collateral set for that fed member.
                    this.logger.LogDebug("(-)[NO_DATA]:{0}={1}", nameof(this.balancesDataByAddress.Count), this.balancesDataByAddress.Count);
                    return 0 >= member.CollateralAmount;
                }

                long balance = balanceData.BalanceChanges.Where(x => x.BalanceChangedHeight <= heightToCheckAt).CalculateBalance();

                this.logger.LogInformation("Calculated balance for '{0}' at {1} is {2}, collateral requirement is {3}.", member.CollateralMainchainAddress, heightToCheckAt, Money.Satoshis(balance).ToUnit(MoneyUnit.BTC), member.CollateralAmount);

                return balance >= member.CollateralAmount.Satoshi;
            }
        }

        private void OnFedMemberKicked(FedMemberKicked fedMemberKicked)
        {
            lock (this.locker)
            {
                if (fedMemberKicked.KickedMember is CollateralFederationMember collateralFederationMember)
                {
                    this.logger.LogDebug("Removing federation member '{0}' with collateral address '{1}'.", collateralFederationMember.PubKey, collateralFederationMember.CollateralMainchainAddress);
                    if (!string.IsNullOrEmpty(collateralFederationMember.CollateralMainchainAddress))
                        this.balancesDataByAddress.Remove(collateralFederationMember.CollateralMainchainAddress);
                    else
                        this.logger.LogDebug("(-)[NO_COLLATERAL_ADDRESS]:{0}='{1}'", nameof(fedMemberKicked.KickedMember.PubKey), fedMemberKicked.KickedMember.PubKey);
                }
                else
                {
                    this.logger.LogError("(-)[NOT_A_COLLATERAL_MEMBER]:{0}='{1}'", nameof(fedMemberKicked.KickedMember.PubKey), fedMemberKicked.KickedMember.PubKey);
                }
            }
        }

        private void OnFedMemberAdded(FedMemberAdded fedMemberAdded)
        {
            lock (this.locker)
            {
                if (fedMemberAdded.AddedMember is CollateralFederationMember collateralFederationMember)
                {
                    if (string.IsNullOrEmpty(collateralFederationMember.CollateralMainchainAddress))
                    {
                        this.logger.LogDebug("(-)[NO_COLLATERAL_ADDRESS]:{0}='{1}'", nameof(fedMemberAdded.AddedMember.PubKey), fedMemberAdded.AddedMember.PubKey);
                        return;
                    }

                    if (!this.balancesDataByAddress.ContainsKey(collateralFederationMember.CollateralMainchainAddress))
                    {
                        this.logger.LogDebug("Adding federation member '{0}' with collateral address '{1}'.", collateralFederationMember.PubKey, collateralFederationMember.CollateralMainchainAddress);
                        this.balancesDataByAddress.Add(collateralFederationMember.CollateralMainchainAddress, null);
                    }
                }
                else
                {
                    this.logger.LogError("(-)[NOT_A_COLLATERAL_MEMBER]:{0}='{1}'", nameof(fedMemberAdded.AddedMember.PubKey), fedMemberAdded.AddedMember.PubKey);
                }
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.signals.Unsubscribe(this.memberAddedToken);
            this.signals.Unsubscribe(this.memberKickedToken);

            this.cancellationSource.Cancel();

            this.updateCollateralContinuouslyTask?.GetAwaiter().GetResult();
        }
    }
}
