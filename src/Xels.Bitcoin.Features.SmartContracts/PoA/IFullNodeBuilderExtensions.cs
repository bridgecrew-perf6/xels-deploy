﻿using Microsoft.Extensions.DependencyInjection;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Features.PoA.Voting;
using Xels.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Xels.Bitcoin.Features.SmartContracts.Rules;

namespace Xels.Bitcoin.Features.SmartContracts.PoA
{
    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Adds common PoA functionality to the side chain node.
        /// </summary>
        public static IFullNodeBuilder AddPoAFeature(this IFullNodeBuilder fullNodeBuilder)
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        // Voting & Polls 
                        services.AddSingleton<IFederationHistory, FederationHistory>();
                        services.AddSingleton<VotingManager>();
                        services.AddSingleton<IWhitelistedHashesRepository, WhitelistedHashesRepository>();
                        services.AddSingleton<IPollResultExecutor, PollResultExecutor>();
                        services.AddSingleton<IIdleFederationMembersKicker, IdleFederationMembersKicker>();
                        services.AddSingleton<ReconstructFederationService, ReconstructFederationService>();

                        // Federation Awareness
                        services.AddSingleton<IFederationManager, FederationManager>();
                        services.AddSingleton<ISlotsManager, SlotsManager>();

                        // Rule Related
                        services.AddSingleton<PoABlockHeaderValidator>();

                        // Settings
                        services.AddSingleton<PoASettings>();
                    });
            });

            return fullNodeBuilder;
        }

        /// <summary>
        /// Configures the side chain node with the PoA consensus rule engine.
        /// </summary>
        public static IFullNodeBuilder UsePoAConsensus(this IFullNodeBuilder fullNodeBuilder, DbType dbType = DbType.Leveldb)
        {
            LoggingConfiguration.RegisterFeatureNamespace<ConsensusFeature>("consensus");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<ConsensusFeature>()
                    .DependOn<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        services.ConfigureCoinDatabaseImplementation(dbType);
                        services.AddSingleton(typeof(IContractTransactionPartialValidationRule), typeof(SmartContractFormatLogic));
                        services.AddSingleton<IConsensusRuleEngine, PoAConsensusRuleEngine>();
                        services.AddSingleton<ICoinView, CachedCoinView>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}
