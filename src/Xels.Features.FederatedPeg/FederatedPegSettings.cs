﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using NBitcoin;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.Extensions;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.TargetChain;

namespace Xels.Features.FederatedPeg
{
    /// <inheritdoc />
    public sealed class FederatedPegSettings : IFederatedPegSettings
    {
        /// <summary>The amount to filter inputs by.</summary>
        public const decimal UtxoAmountThreshold = 0.001m;

        public const string WalletSyncFromHeightParam = "walletsyncfromheight";

        public const string RedeemScriptParam = "redeemscript";

        public const string PublicKeyParam = "publickey";

        public const string FederationKeysParam = "federationkeys";

        public const string FederationQuorumParam = "federationquorum";

        public const string FederationIpsParam = "federationips";

        public const string CounterChainDepositBlock = "counterchaindepositblock";

        private const string ThresholdAmountSmallDepositParam = "thresholdamountsmalldeposit";
        private const string ThresholdAmountNormalDepositParam = "thresholdamountnormaldeposit";

        private const string MinimumConfirmationsSmallDepositsParam = "minconfirmationssmalldeposits";
        private const string MinimumConfirmationsNormalDepositsParam = "minconfirmationsnormaldeposits";

        /// <summary>
        /// The fee taken by the federation to build withdrawal transactions. The federation will keep most of this.
        /// </summary>
        /// <remarks>
        /// Changing <see cref="CrossChainTransferFee"/> affects both the deposit threshold on this chain and the withdrawal transaction fee on this chain.
        /// This value shouldn't be different for the 2 pegged chain nodes or deposits could be extracted that don't have the amount required to
        /// cover the withdrawal fee on the other chain.
        ///
        /// TODO: This should be configurable on the Network level in the future, but individual nodes shouldn't be tweaking it.
        /// </remarks>
        public static readonly Money CrossChainTransferFee = Money.Coins(0.001m);

        /// <summary>
        /// Only look for deposits above a certain value. This avoids issues with dust lingering around or fees not being covered.
        /// </summary>
        public static readonly Money CrossChainTransferMinimum = Money.Coins(1m);

        /// <summary>
        /// Fee applied to consolidating transactions.
        /// </summary>
        public static readonly Money ConsolidationFee = Money.Coins(0.01m);

        public const string MaximumPartialTransactionsParam = "maxpartials";

        /// <summary>
        /// The maximum number of inputs we want our built withdrawal transactions to have. We don't want them to get too big for Standardness reasons.
        /// </summary>
        public const int MaxInputs = 50;

        public FederatedPegSettings(NodeSettings nodeSettings, CounterChainNetworkWrapper counterChainNetworkWrapper)
        {
            Guard.NotNull(nodeSettings, nameof(nodeSettings));

            TextFileConfiguration configReader = nodeSettings.ConfigReader;

            this.IsMainChain = configReader.GetOrDefault("mainchain", false);
            if (!this.IsMainChain && !configReader.GetOrDefault("sidechain", false))
                throw new ConfigurationException("Either -mainchain or -sidechain must be specified");

            string redeemScriptRaw = configReader.GetOrDefault<string>(RedeemScriptParam, null);
            Console.WriteLine(redeemScriptRaw);

            PayToMultiSigTemplateParameters para = null;

            if (!string.IsNullOrEmpty(redeemScriptRaw))
            {
                var redeemScript = new Script(redeemScriptRaw);
                para = PayToMultiSigTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript) ??
                    PayToFederationTemplate.Instance.ExtractScriptPubKeyParameters(redeemScript, nodeSettings.Network);
            }

            if (para == null)
            {
                string pubKeys = configReader.GetOrDefault<string>(FederationKeysParam, null);
                if (string.IsNullOrEmpty(pubKeys))
                    throw new ConfigurationException($"Either -{RedeemScriptParam} or -{FederationKeysParam} must be specified.");

                para.PubKeys = pubKeys.Split(",").Select(s => new PubKey(s.Trim())).ToArray();
                para.SignatureCount = (pubKeys.Length + 1) / 2;
            }

            para.SignatureCount = configReader.GetOrDefault(FederationQuorumParam, para.SignatureCount);

            IFederation federation;
            federation = new Federation(para.PubKeys, para.SignatureCount);
            nodeSettings.Network.Federations.RegisterFederation(federation);
            counterChainNetworkWrapper.CounterChainNetwork.Federations.RegisterFederation(federation);

            this.MultiSigRedeemScript = federation.MultisigScript;
            this.MultiSigAddress = this.MultiSigRedeemScript.Hash.GetAddress(nodeSettings.Network);
            this.MultiSigM = para.SignatureCount;
            this.MultiSigN = para.PubKeys.Length;
            this.FederationPublicKeys = para.PubKeys;

            this.PublicKey = configReader.GetOrDefault<string>(PublicKeyParam, null);

            if (this.FederationPublicKeys.All(p => p != new PubKey(this.PublicKey)))
            {
                throw new ConfigurationException("Please make sure the public key passed as parameter was used to generate the multisig redeem script.");
            }

            // Federation IPs - These are required to receive and sign withdrawal transactions.
            string federationIpsRaw = configReader.GetOrDefault<string>(FederationIpsParam, null);

            if (federationIpsRaw == null)
                throw new ConfigurationException("Federation IPs must be specified.");

            IEnumerable<IPEndPoint> endPoints = federationIpsRaw.Split(',').Select(a => a.ToIPEndPoint(nodeSettings.Network.DefaultPort));

            this.FederationNodeIpEndPoints = new HashSet<IPEndPoint>(endPoints, new IPEndPointComparer());
            this.FederationNodeIpAddresses = new HashSet<IPAddress>(endPoints.Select(x => x.Address), new IPAddressComparer());

            // These values are only configurable for tests at the moment. Fed members on live networks shouldn't play with them.
            this.CounterChainDepositStartBlock = configReader.GetOrDefault(CounterChainDepositBlock, 1);

            this.SmallDepositThresholdAmount = Money.Coins(configReader.GetOrDefault(ThresholdAmountSmallDepositParam, 50));
            this.NormalDepositThresholdAmount = Money.Coins(configReader.GetOrDefault(ThresholdAmountNormalDepositParam, 1000));

            this.MinimumConfirmationsSmallDeposits = configReader.GetOrDefault(MinimumConfirmationsSmallDepositsParam, 25);
            this.MinimumConfirmationsNormalDeposits = configReader.GetOrDefault(MinimumConfirmationsNormalDepositsParam, 80);
            this.MinimumConfirmationsLargeDeposits = (int)nodeSettings.Network.Consensus.MaxReorgLength + 1;
            this.MinimumConfirmationsDistributionDeposits = (int)nodeSettings.Network.Consensus.MaxReorgLength + 1;
            this.MinimumConfirmationsConversionDeposits = (int)nodeSettings.Network.Consensus.MaxReorgLength + 1;

            this.MaximumPartialTransactionThreshold = configReader.GetOrDefault(MaximumPartialTransactionsParam, CrossChainTransferStore.MaximumPartialTransactions);
            this.WalletSyncFromHeight = configReader.GetOrDefault(WalletSyncFromHeightParam, 0);
        }

        /// <inheritdoc/>
        public bool IsMainChain { get; }

        /// <inheritdoc/>
        public int MaximumPartialTransactionThreshold { get; }

        /// <inheritdoc />
        public int MinimumConfirmationsSmallDeposits { get; }

        /// <inheritdoc />
        public int MinimumConfirmationsNormalDeposits { get; }

        /// <inheritdoc />
        public int MinimumConfirmationsLargeDeposits { get; }

        public int MinimumConfirmationsDistributionDeposits { get; }

        public int MinimumConfirmationsConversionDeposits { get; }

        /// <inheritdoc />
        public Money SmallDepositThresholdAmount { get; }

        /// <inheritdoc />
        public Money NormalDepositThresholdAmount { get; }

        /// <inheritdoc/>
        public HashSet<IPEndPoint> FederationNodeIpEndPoints { get; }

        /// <inheritdoc/>
        public HashSet<IPAddress> FederationNodeIpAddresses { get; }

        /// <inheritdoc/>
        public string PublicKey { get; }

        /// <inheritdoc/>
        public PubKey[] FederationPublicKeys { get; }

        /// <inheritdoc/>
        public int WalletSyncFromHeight { get; }

        /// <inheritdoc/>
        public int MultiSigM { get; }

        /// <inheritdoc/>
        public int MultiSigN { get; }

        /// <inheritdoc/>
        public int CounterChainDepositStartBlock { get; }

        /// <inheritdoc/>
        public BitcoinAddress MultiSigAddress { get; }

        /// <inheritdoc/>
        public Script MultiSigRedeemScript { get; }
    }
}