﻿using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.Consensus.Interfaces;
using Xels.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Xels.Bitcoin.Features.Consensus.Rules;
using Xels.Bitcoin.Interfaces;

namespace Xels.Bitcoin.Features.Consensus
{
    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderConsensusExtension
    {
        public static IFullNodeBuilder UsePowConsensus(this IFullNodeBuilder fullNodeBuilder, DbType coindbType = DbType.Leveldb)
        {
            LoggingConfiguration.RegisterFeatureNamespace<PowConsensusFeature>("powconsensus");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<PowConsensusFeature>()
                    .FeatureServices(services =>
                    {
                        ConfigureCoinDatabaseImplementation(services, coindbType);

                        services.AddSingleton<ConsensusOptions, ConsensusOptions>();
                        services.AddSingleton<ICoinView, CachedCoinView>();
                        services.AddSingleton<IConsensusRuleEngine, PowConsensusRuleEngine>();
                        services.AddSingleton<IChainState, ChainState>();
                        services.AddSingleton<ConsensusQuery>()
                            .AddSingleton<INetworkDifficulty, ConsensusQuery>(provider => provider.GetService<ConsensusQuery>())
                            .AddSingleton<IGetUnspentTransaction, ConsensusQuery>(provider => provider.GetService<ConsensusQuery>());
                    });
            });

            return fullNodeBuilder;
        }

        public static IFullNodeBuilder UsePosConsensus(this IFullNodeBuilder fullNodeBuilder, DbType coindbType = DbType.Leveldb)
        {
            LoggingConfiguration.RegisterFeatureNamespace<PosConsensusFeature>("posconsensus");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<PosConsensusFeature>()
                    .FeatureServices(services =>
                    {
                        services.ConfigureCoinDatabaseImplementation(coindbType);

                        services.AddSingleton(provider => (IStakedb)provider.GetService<ICoindb>());
                        services.AddSingleton<ICoinView, CachedCoinView>();
                        services.AddSingleton<StakeChainStore>().AddSingleton<IStakeChain, StakeChainStore>(provider => provider.GetService<StakeChainStore>());
                        services.AddSingleton<IStakeValidator, StakeValidator>();
                        services.AddSingleton<IRewindDataIndexCache, RewindDataIndexCache>();
                        services.AddSingleton<IConsensusRuleEngine, PosConsensusRuleEngine>();
                        services.AddSingleton<IChainState, ChainState>();
                        services.AddSingleton<ConsensusQuery>()
                            .AddSingleton<INetworkDifficulty, ConsensusQuery>(provider => provider.GetService<ConsensusQuery>())
                            .AddSingleton<IGetUnspentTransaction, ConsensusQuery>(provider => provider.GetService<ConsensusQuery>());

                        services.AddSingleton<IProvenBlockHeaderStore, ProvenBlockHeaderStore>();

                        if (coindbType == DbType.Leveldb)
                            services.AddSingleton<IProvenBlockHeaderRepository, LevelDbProvenBlockHeaderRepository>();

                        if (coindbType == DbType.RocksDb)
                            services.AddSingleton<IProvenBlockHeaderRepository, RocksDbProvenBlockHeaderRepository>();
                    });
            });

            return fullNodeBuilder;
        }

        public static void ConfigureCoinDatabaseImplementation(this IServiceCollection services, DbType coindbType)
        {
            switch (coindbType)
            {
                case DbType.Dbreeze:
                    services.AddSingleton<ICoindb, DBreezeCoindb>();
                    break;

                case DbType.Leveldb:
                    services.AddSingleton<ICoindb, LevelDbCoindb>();
                    break;

                case DbType.RocksDb:
                    services.AddSingleton<ICoindb, RocksDbCoindb>();
                    break;

                default:
                    services.AddSingleton<ICoindb, LevelDbCoindb>();
                    break;
            }
        }
    }
}
