﻿using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Builder.Feature;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Features.Interop.ETHClient;
using Xels.Bitcoin.Features.Interop.Payloads;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.P2P.Protocol.Payloads;
using Xels.Features.FederatedPeg.Conversion;
using Xels.Features.FederatedPeg.Coordination;
using Xels.Features.FederatedPeg.Payloads;

namespace Xels.Bitcoin.Features.Interop
{
    /// <summary>
    /// A class containing all the related configuration to add chain interop functionality to the full node.
    /// </summary>
    public sealed class InteropFeature : FullNodeFeature
    {
        private readonly IConnectionManager connectionManager;
        private readonly IConversionRequestCoordinationService conversionRequestCoordinationService;
        private readonly IConversionRequestFeeService conversionRequestFeeService;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IETHCompatibleClientProvider ethClientProvider;
        private readonly IFederationManager federationManager;
        private readonly InteropPoller interopPoller;
        private readonly InteropSettings interopSettings;
        private readonly Network network;

        public InteropFeature(
            IConnectionManager connectionManager,
            IConversionRequestCoordinationService conversionRequestCoordinationService,
            IConversionRequestFeeService conversionRequestFeeService,
            IConversionRequestRepository conversionRequestRepository,
            IETHCompatibleClientProvider ethCompatibleClientProvider,
            IFederationManager federationManager,
            IFullNode fullNode,
            InteropPoller interopPoller,
            InteropSettings interopSettings,
            Network network)
        {
            this.connectionManager = connectionManager;
            this.conversionRequestCoordinationService = conversionRequestCoordinationService;
            this.conversionRequestFeeService = conversionRequestFeeService;
            this.conversionRequestRepository = conversionRequestRepository;
            this.ethClientProvider = ethCompatibleClientProvider;
            this.federationManager = federationManager;
            this.interopPoller = interopPoller;
            this.interopSettings = interopSettings;
            this.network = network;

            var payloadProvider = (PayloadProvider)fullNode.Services.ServiceProvider.GetService(typeof(PayloadProvider));
            payloadProvider.AddPayload(typeof(ConversionRequestPayload));
            payloadProvider.AddPayload(typeof(FeeProposalPayload));
            payloadProvider.AddPayload(typeof(FeeAgreePayload));
        }

        /// <inheritdoc/>
        public override Task InitializeAsync()
        {
            // For now as only ethereum is supported we need set this to the quorum amount in the eth settings class.
            // Refactor this to a base.
            this.conversionRequestCoordinationService.RegisterConversionRequestQuorum(this.interopSettings.GetSettingsByChain(Wallet.DestinationChain.ETH).MultisigWalletQuorum);

            this.interopPoller?.InitializeAsync();

            NetworkPeerConnectionParameters networkPeerConnectionParameters = this.connectionManager.Parameters;
            networkPeerConnectionParameters.TemplateBehaviors.Add(new InteropBehavior(this.network, this.conversionRequestCoordinationService, this.conversionRequestFeeService, this.conversionRequestRepository, this.ethClientProvider, this.federationManager));

            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        public override void Dispose()
        {
            this.interopPoller?.Dispose();
        }
    }

    public static partial class IFullNodeBuilderExtensions
    {
        /// <summary>
        /// Adds chain Interoperability to the node.
        /// </summary>
        /// <param name="fullNodeBuilder">The full node builder instance.</param>
        public static IFullNodeBuilder AddInteroperability(this IFullNodeBuilder fullNodeBuilder)
        {
            LoggingConfiguration.RegisterFeatureNamespace<InteropFeature>("interop");

            fullNodeBuilder.ConfigureFeature(features =>
                features
                    .AddFeature<InteropFeature>()
                    .FeatureServices(services => services
                    .AddSingleton<InteropSettings>()
                    .AddSingleton<IETHClient, ETHClient.ETHClient>()
                    .AddSingleton<IBNBClient, BNBClient>()
                    .AddSingleton<IETHCompatibleClientProvider, ETHCompatibleClientProvider>()
                    .AddSingleton<InteropPoller>()
                    ));

            return fullNodeBuilder;
        }
    }
}
