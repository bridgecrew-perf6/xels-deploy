﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Builder.Feature;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner.Interfaces;
using Xels.Bitcoin.Features.Miner.Staking;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Mining;

[assembly: InternalsVisibleTo("Xels.Bitcoin.Features.Miner.Tests")]

namespace Xels.Bitcoin.Features.Miner
{
    /// <summary>
    /// Provides an ability to mine or stake.
    /// </summary>
    public class MiningFeature : FullNodeFeature
    {
        private readonly ConnectionManagerSettings connectionManagerSettings;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>Settings relevant to mining or staking.</summary>
        private readonly MinerSettings minerSettings;

        /// <summary>Settings relevant to node.</summary>
        private readonly NodeSettings nodeSettings;

        /// <summary>POW miner.</summary>
        private readonly IPowMining powMining;

        /// <summary>POS staker.</summary>
        private readonly IPosMinting posMinting;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>State of time synchronization feature that stores collected data samples.</summary>
        private readonly ITimeSyncBehaviorState timeSyncBehaviorState;

        public MiningFeature(
            ConnectionManagerSettings connectionManagerSettings,
            Network network,
            MinerSettings minerSettings,
            NodeSettings nodeSettings,
            ILoggerFactory loggerFactory,
            ITimeSyncBehaviorState timeSyncBehaviorState,
            IPowMining powMining,
            IPosMinting posMinting = null)
        {
            this.connectionManagerSettings = connectionManagerSettings;
            this.network = network;
            this.minerSettings = minerSettings;
            this.nodeSettings = nodeSettings;
            this.powMining = powMining;
            this.timeSyncBehaviorState = timeSyncBehaviorState;
            this.posMinting = posMinting;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <summary>
        /// Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            MinerSettings.PrintHelp(network);
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            MinerSettings.BuildDefaultConfigurationFile(builder, network);
        }

        /// <summary>
        /// Starts staking a wallet.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="walletPassword">The password of the wallet.</param>
        public void StartStaking(string walletName, string walletPassword)
        {
            if (this.timeSyncBehaviorState.IsSystemTimeOutOfSync)
            {
                string errorMessage = "Staking cannot start, your system time does not match that of other nodes on the network." + Environment.NewLine
                                    + "Please adjust your system time and restart the node.";
                this.logger.LogError(errorMessage);
                throw new ConfigurationException(errorMessage);
            }

            if (!string.IsNullOrEmpty(walletName) && !string.IsNullOrEmpty(walletPassword))
            {
                this.logger.LogInformation("Staking enabled on wallet '{0}'.", walletName);

                this.posMinting.Stake(new List<WalletSecret>()
                {
                    new WalletSecret()
                    {
                        WalletPassword = walletPassword,
                        WalletName = walletName
                    }
                });
            }
            else
            {
                string errorMessage = "Staking not started, wallet name or password were not provided.";
                this.logger.LogError(errorMessage);
                throw new ConfigurationException(errorMessage);
            }
        }

        /// <summary>
        /// Starts staking with multiple wallets simultaneously.
        /// </summary>
        /// <param name="walletSecrets">The list of wallet credentials.</param>
        public void StartMultiStaking(List<WalletSecret> walletSecrets)
        {
            if (this.timeSyncBehaviorState.IsSystemTimeOutOfSync)
            {
                string errorMessage = "Staking cannot start, your system time does not match that of other nodes on the network." + Environment.NewLine
                                                                                                                                  + "Please adjust your system time and restart the node.";
                this.logger.LogError(errorMessage);
                throw new ConfigurationException(errorMessage);
            }

            if (walletSecrets.Any(walletSecret => string.IsNullOrEmpty(walletSecret.WalletName) || string.IsNullOrEmpty(walletSecret.WalletPassword)))
            {
                string errorMessage = "Staking not started, wallet name or password were not provided for at least one wallet.";
                this.logger.LogError(errorMessage);
                throw new ConfigurationException(errorMessage);
            }

            this.logger.LogInformation("Staking enabled on multiple wallets.");

            this.posMinting.Stake(walletSecrets);
        }

        /// <summary>
        /// Stop any staking wallets.
        /// </summary>
        public void StopStaking()
        {
            this.posMinting?.StopStake();
            this.logger.LogInformation("Staking stopped.");
        }

        /// <summary>
        /// Stop the Proof of Work miner.
        /// </summary>
        public void StopMining()
        {
            this.powMining?.StopMining();
            this.logger.LogInformation("Mining stopped.");
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            if (this.minerSettings.Mine)
            {
                string mineToAddress = this.minerSettings.MineAddress;
                // if (string.IsNullOrEmpty(mineToAddress)) ;
                //    TODO: get an address from the wallet.

                if (!string.IsNullOrEmpty(mineToAddress))
                {
                    this.logger.LogInformation("Mining enabled.");

                    this.powMining.Mine(BitcoinAddress.Create(mineToAddress, this.network).ScriptPubKey);
                }
            }

            if (this.minerSettings.Stake)
            {
                this.StartStaking(this.minerSettings.WalletName, this.minerSettings.WalletPassword);
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            this.StopMining();
            this.StopStaking();
        }

        /// <inheritdoc />
        public override void ValidateDependencies(IFullNodeServiceProvider services)
        {
            if (services.ServiceProvider.GetService<IPosMinting>() != null)
            {
                services.Features.EnsureFeature<BaseWalletFeature>();
            }

            // Mining and staking require block store feature.
            if (this.minerSettings.Mine || this.minerSettings.Stake)
            {
                services.Features.EnsureFeature<BlockStoreFeature>();
                var storeSettings = services.ServiceProvider.GetService<StoreSettings>();
                if (storeSettings.PruningEnabled)
                    throw new ConfigurationException("BlockStore prune mode is incompatible with mining and staking.");
            }
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderMiningExtension
    {
        /// <summary>
        /// Adds a mining feature to the node being initialized.
        /// </summary>
        /// <param name="fullNodeBuilder">The object used to build the current node.</param>
        /// <returns>The full node builder, enriched with the new component.</returns>
        public static IFullNodeBuilder AddMining(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<MiningFeature>("mining");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<MiningFeature>()
                    .DependOn<MempoolFeature>()
                    .DependOn<RPCFeature>()
                    .DependOn<BaseWalletFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IPowMining, PowMining>();
                        services.AddSingleton<IBlockProvider, BlockProvider>();
                        services.AddSingleton<BlockDefinition, PowBlockDefinition>();
                        services.AddSingleton<MinerSettings>();
                    });
            });

            return fullNodeBuilder;
        }

        /// <summary>
        /// Adds POW and POS miner components to the node, so that it can mine or stake.
        /// </summary>
        /// <param name="fullNodeBuilder">The object used to build the current node.</param>
        /// <param name="straxMode">Indicates whether the <see cref="StraxMinting"/> class should be used.</param>
        /// <returns>The full node builder, enriched with the new component.</returns>
        public static IFullNodeBuilder AddPowPosMining(this IFullNodeBuilder fullNodeBuilder, bool straxMode)
        {
            LoggingConfiguration.RegisterFeatureNamespace<MiningFeature>("mining");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<MiningFeature>()
                    .DependOn<MempoolFeature>()
                    .DependOn<RPCFeature>()
                    // TODO: Need a better way to check dependencies. This is really just dependent on IWalletManager...
                    // Alternatively "DependsOn" should take a list of features that will satisfy the dependency.
                    //.DependOn<WalletFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IPowMining, PowMining>();

                        if (straxMode)
                            services.AddSingleton<IPosMinting, StraxMinting>();
                        else
                            services.AddSingleton<IPosMinting, PosMinting>();

                        services.AddSingleton<IBlockProvider, BlockProvider>();
                        services.AddSingleton<BlockDefinition, PowBlockDefinition>();
                        services.AddSingleton<BlockDefinition, PosBlockDefinition>();
                        services.AddSingleton<BlockDefinition, PosPowBlockDefinition>();
                        services.AddSingleton<MinerSettings>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}