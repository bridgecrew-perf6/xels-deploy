﻿using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Builder.Feature;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.MemoryPool.Fee;
using Xels.Bitcoin.Features.MemoryPool.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;
using TracerAttributes;

[assembly: InternalsVisibleTo("Xels.Bitcoin.Features.MemoryPool.Tests")]

namespace Xels.Bitcoin.Features.MemoryPool
{
    /// <summary>
    /// Transaction memory pool feature for the Full Node.
    /// </summary>
    /// <seealso cref="https://github.com/bitcoin/bitcoin/blob/6dbcc74a0e0a7d45d20b03bb4eb41a027397a21d/src/txmempool.cpp"/>
    public class MempoolFeature : FullNodeFeature
    {
        /// <summary>Connection manager for managing node connections.</summary>
        private readonly IConnectionManager connectionManager;

        /// <summary>Observes block signal notifications from signals.</summary>
        private readonly MempoolSignaled mempoolSignaled;

        /// <summary>Observes reorg signal notifications from signals.</summary>
        private readonly BlocksDisconnectedSignaled blocksDisconnectedSignaled;

        /// <summary>Memory pool node behavior for managing attached node messages.</summary>
        private readonly MempoolBehavior mempoolBehavior;

        /// <summary>Memory pool manager for managing external access to memory pool.</summary>
        private readonly MempoolManager mempoolManager;

        /// <summary>Instance logger for the memory pool component.</summary>
        private readonly ILogger logger;

        /// <summary>
        /// Constructs a memory pool feature.
        /// </summary>
        /// <param name="connectionManager">Connection manager for managing node connections.</param>
        /// <param name="mempoolSignaled">Observes block signal notifications from signals.</param>
        /// <param name="blocksDisconnectedSignaled">Observes reorged headers signal notifications from signals.</param>
        /// <param name="mempoolBehavior">Memory pool node behavior for managing attached node messages.</param>
        /// <param name="mempoolManager">Memory pool manager for managing external access to memory pool.</param>
        /// <param name="loggerFactory">Logger factory for creating instance logger.</param>
        /// <param name="nodeStats">See <see cref="INodeStats"/>.</param>
        public MempoolFeature(
            IConnectionManager connectionManager,
            MempoolSignaled mempoolSignaled,
            BlocksDisconnectedSignaled blocksDisconnectedSignaled,
            MempoolBehavior mempoolBehavior,
            MempoolManager mempoolManager,
            ILoggerFactory loggerFactory,
            INodeStats nodeStats)
        {
            this.connectionManager = connectionManager;
            this.mempoolSignaled = mempoolSignaled;
            this.blocksDisconnectedSignaled = blocksDisconnectedSignaled;
            this.mempoolBehavior = mempoolBehavior;
            this.mempoolManager = mempoolManager;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        [NoTrace]
        private void AddComponentStats(StringBuilder log)
        {
            if (this.mempoolManager != null)
            {
                log.AppendLine(">> Mempool");
                log.AppendLine(this.mempoolManager.PerformanceCounter.ToString());
                log.AppendLine();
            }
        }

        /// <inheritdoc />
        public override async Task InitializeAsync()
        {
            await this.mempoolManager.LoadPoolAsync().ConfigureAwait(false);

            this.connectionManager.Parameters.TemplateBehaviors.Add(this.mempoolBehavior);
            this.mempoolSignaled.Start();

            this.blocksDisconnectedSignaled.Initialize();
        }

        /// <summary>
        /// Prints command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            MempoolSettings.PrintHelp(network);
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            MempoolSettings.BuildDefaultConfigurationFile(builder, network);
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            this.logger.LogInformation("Saving Memory Pool.");

            MemPoolSaveResult result = this.mempoolManager.SavePool();
            if (result.Succeeded)
            {
                this.logger.LogInformation($"Memory Pool Saved {result.TrxSaved} transactions");
            }
            else
            {
                this.logger.LogWarning("Memory Pool Not Saved!");
            }

            this.blocksDisconnectedSignaled.Dispose();

            this.mempoolSignaled.Stop();
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderMempoolExtension
    {
        /// <summary>
        /// Include the memory pool feature and related services in the full node.
        /// </summary>
        /// <param name="fullNodeBuilder">Full node builder.</param>
        /// <returns>The full node builder.</returns>
        public static IFullNodeBuilder UseMempool(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<MempoolFeature>("mempool");
            LoggingConfiguration.RegisterFeatureNamespace<BlockPolicyEstimator>("estimatefee");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                .AddFeature<MempoolFeature>()
                .DependOn<ConsensusFeature>()
                .FeatureServices(services =>
                    {
                        services.AddSingleton<MempoolSchedulerLock>();
                        services.AddSingleton<ITxMempool, TxMempool>();
                        services.AddSingleton<BlockPolicyEstimator>();
                        services.AddSingleton<IMempoolValidator, MempoolValidator>();
                        services.AddSingleton<MempoolOrphans>();
                        services.AddSingleton<MempoolManager>()
                            .AddSingleton<IPooledTransaction, MempoolManager>(provider => provider.GetService<MempoolManager>())
                            .AddSingleton<IPooledGetUnspentTransaction, MempoolManager>(provider => provider.GetService<MempoolManager>());
                        services.AddSingleton<MempoolBehavior>();
                        services.AddSingleton<MempoolSignaled>();
                        services.AddSingleton<BlocksDisconnectedSignaled>();
                        services.AddSingleton<IMempoolPersistence, MempoolPersistence>();
                        services.AddSingleton<MempoolSettings>();

                        foreach (var ruleType in fullNodeBuilder.Network.Consensus.MempoolRules)
                            services.AddSingleton(typeof(IMempoolRule), ruleType);
                    });
            });

            return fullNodeBuilder;
        }
    }
}
