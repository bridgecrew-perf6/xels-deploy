﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Builder.Feature;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.MemoryPool.Interfaces;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Features.SmartContracts.Caching;
using Xels.Bitcoin.Features.SmartContracts.PoA;
using Xels.Bitcoin.Features.SmartContracts.PoW;
using Xels.Bitcoin.Features.SmartContracts.ReflectionExecutor.Controllers;
using Xels.Bitcoin.Features.SmartContracts.Wallet;
using Xels.Bitcoin.Utilities;
using Stratis.SmartContracts;
using Xels.SmartContracts.CLR;
using Xels.SmartContracts.CLR.Caching;
using Xels.SmartContracts.CLR.Compilation;
using Xels.SmartContracts.CLR.Decompilation;
using Xels.SmartContracts.CLR.Loader;
using Xels.SmartContracts.CLR.Local;
using Xels.SmartContracts.CLR.ResultProcessors;
using Xels.SmartContracts.CLR.Serialization;
using Xels.SmartContracts.CLR.Validation;
using Xels.SmartContracts.Core;
using Xels.SmartContracts.Core.Receipts;
using Xels.SmartContracts.Core.State;
using Xels.SmartContracts.Core.Util;

namespace Xels.Bitcoin.Features.SmartContracts
{
    public sealed class SmartContractFeature : FullNodeFeature
    {
        private readonly IConsensusManager consensusManager;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IStateRepositoryRoot stateRoot;

        public SmartContractFeature(IConsensusManager consensusLoop, ILoggerFactory loggerFactory, Network network, IStateRepositoryRoot stateRoot)
        {
            this.consensusManager = consensusLoop;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.stateRoot = stateRoot;
        }

        public override Task InitializeAsync()
        {
            // TODO: This check should be more robust
            Guard.Assert(this.network.Consensus.ConsensusFactory is SmartContractPowConsensusFactory
                         || this.network.Consensus.ConsensusFactory is SmartContractPoAConsensusFactory
                         || this.network.Consensus.ConsensusFactory is SmartContractCollateralPoAConsensusFactory);

            this.stateRoot.SyncToRoot(((ISmartContractBlockHeader)this.consensusManager.Tip.Header).HashStateRoot.ToBytes());

            this.logger.LogInformation("Smart Contract Feature Injected.");
            return Task.CompletedTask;
        }

        public override void Dispose()
        {
        }
    }

    public class SmartContractOptions
    {
        public SmartContractOptions(IServiceCollection services, Network network)
        {
            this.Services = services;
            this.Network = network;
        }

        public IServiceCollection Services { get; }
        public Network Network { get; }
    }

    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Adds the smart contract feature to the node.
        /// </summary>
        public static IFullNodeBuilder AddSmartContracts(this IFullNodeBuilder fullNodeBuilder, Action<SmartContractOptions> options = null, Action<SmartContractOptions> preOptions = null)
        {
            LoggingConfiguration.RegisterFeatureNamespace<SmartContractFeature>("smartcontracts");

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features
                    .AddFeature<SmartContractFeature>()
                    .FeatureServices(services =>
                    {
                        // Before setting up, invoke any additional options.
                        preOptions?.Invoke(new SmartContractOptions(services, fullNodeBuilder.Network));

                        // STATE ----------------------------------------------------------------------------
                        services.AddSingleton<DBreezeContractStateStore>();
                        services.AddSingleton<NoDeleteContractStateSource>();
                        services.AddSingleton<IStateRepositoryRoot, StateRepositoryRoot>();

                        // CONSENSUS ------------------------------------------------------------------------
                        services.Replace(ServiceDescriptor.Singleton<IMempoolValidator, SmartContractMempoolValidator>());
                        services.AddSingleton<StandardTransactionPolicy, SmartContractTransactionPolicy>();

                        // CONTRACT EXECUTION ---------------------------------------------------------------
                        services.AddSingleton<IInternalExecutorFactory, InternalExecutorFactory>();
                        services.AddSingleton<IContractAssemblyCache, ContractAssemblyCache>();
                        services.AddSingleton<IVirtualMachine, ReflectionVirtualMachine>();
                        services.AddSingleton<IAddressGenerator, AddressGenerator>();
                        services.AddSingleton<ILoader, ContractAssemblyLoader>();
                        services.AddSingleton<IContractModuleDefinitionReader, ContractModuleDefinitionReader>();
                        services.AddSingleton<IStateFactory, StateFactory>();
                        services.AddSingleton<SmartContractTransactionPolicy>();
                        services.AddSingleton<IStateProcessor, StateProcessor>();
                        services.AddSingleton<ISmartContractStateFactory, SmartContractStateFactory>();
                        services.AddSingleton<ILocalExecutor, LocalExecutor>();
                        services.AddSingleton<IBlockExecutionResultCache, BlockExecutionResultCache>();

                        // RECEIPTS -------------------------------------------------------------------------
                        services.AddSingleton<IReceiptRepository, PersistentReceiptRepository>();

                        // UTILS ----------------------------------------------------------------------------
                        services.AddSingleton<ISenderRetriever, SenderRetriever>();

                        services.AddSingleton<IMethodParameterSerializer, MethodParameterByteSerializer>();
                        services.AddSingleton<IMethodParameterStringSerializer, MethodParameterStringSerializer>();
                        services.AddSingleton<ICallDataSerializer, CallDataSerializer>();

                        // After setting up, invoke any additional options which can replace services as required.
                        options?.Invoke(new SmartContractOptions(services, fullNodeBuilder.Network));

                        // Controllers, necessary for DIing into the dynamic controller api.
                        // Use AddScoped for instance-per-request lifecycle, ref. https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection?view=aspnetcore-2.2#scoped
                        services.AddScoped<SmartContractsController>();
                        services.AddScoped<SmartContractWalletController>();
                    });
            });

            return fullNodeBuilder;
        }

        /// <summary>Adds Proof-of-Authority mining to the side chain node.</summary>
        /// <typeparam name="T">The type of block definition to use.</typeparam>
        public static IFullNodeBuilder AddPoAMiningCapability<T>(this IFullNodeBuilder fullNodeBuilder) where T : BlockDefinition
        {
            fullNodeBuilder.ConfigureFeature(features =>
            {
                IFeatureRegistration feature = fullNodeBuilder.Features.FeatureRegistrations.FirstOrDefault(f => f.FeatureType == typeof(PoAFeature));
                feature.FeatureServices(services =>
                {
                    services.AddSingleton<IPoAMiner, PoAMiner>();
                    services.AddSingleton<MinerSettings>();
                    services.AddSingleton<BlockDefinition, T>();
                    services.AddSingleton<IBlockBufferGenerator, BlockBufferGenerator>();
                });
            });

            return fullNodeBuilder;
        }

        /// <summary>
        /// This node will be configured with the reflection contract executor.
        /// <para>
        /// Should we require another executor, we will need to create a separate daemon and network.
        /// </para>
        /// </summary>
        public static SmartContractOptions UseReflectionExecutor(this SmartContractOptions options)
        {
            IServiceCollection services = options.Services;

            // Validator
            services.AddSingleton<ISmartContractValidator, SmartContractValidator>();

            // Executor et al.
            services.AddSingleton<IContractRefundProcessor, ContractRefundProcessor>();
            services.AddSingleton<IContractTransferProcessor, ContractTransferProcessor>();
            services.AddSingleton<IKeyEncodingStrategy, BasicKeyEncodingStrategy>();
            services.AddSingleton<IContractExecutorFactory, ReflectionExecutorFactory>();
            services.AddSingleton<IMethodParameterSerializer, MethodParameterByteSerializer>();
            services.AddSingleton<IContractPrimitiveSerializer, ContractPrimitiveSerializer>();
            services.AddSingleton<ISerializer, Serializer>();

            // Controllers + utils
            services.AddSingleton<CSharpContractDecompiler>();

            return options;
        }
    }
}
