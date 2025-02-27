﻿using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Builder.Feature;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.PoA.Behaviors;
using Xels.Bitcoin.Features.PoA.Voting;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.P2P.Protocol.Behaviors;
using Xels.Bitcoin.P2P.Protocol.Payloads;

namespace Xels.Bitcoin.Features.PoA
{
    public class PoAFeature : FullNodeFeature
    {
        public const string ReconstructFederationFlag = "reconstructfederation";

        /// <summary>Manager of node's network connections.</summary>
        private readonly IConnectionManager connectionManager;

        /// <summary>Thread safe chain of block headers from genesis.</summary>
        private readonly ChainIndexer chainIndexer;

        private readonly IFederationManager federationManager;

        /// <summary>Provider of IBD state.</summary>
        private readonly IInitialBlockDownloadState initialBlockDownloadState;

        private readonly IConsensusManager consensusManager;

        /// <summary>A handler that can manage the lifetime of network peers.</summary>
        private readonly IPeerBanning peerBanning;

        /// <summary>Factory for creating loggers.</summary>
        private readonly ILoggerFactory loggerFactory;

        private readonly IPoAMiner miner;

        private readonly VotingManager votingManager;

        private readonly IFederationHistory federationHistory;

        private readonly Network network;

        private readonly IWhitelistedHashesRepository whitelistedHashesRepository;

        private readonly IIdleFederationMembersKicker idleFederationMembersKicker;

        private readonly IChainState chainState;

        private readonly IBlockStoreQueue blockStoreQueue;

        private readonly NodeSettings nodeSettings;

        private readonly ReconstructFederationService reconstructFederationService;

        public PoAFeature(
            IFederationManager federationManager,
            PayloadProvider payloadProvider,
            IConnectionManager connectionManager,
            ChainIndexer chainIndexer,
            IInitialBlockDownloadState initialBlockDownloadState,
            IConsensusManager consensusManager,
            IPeerBanning peerBanning,
            ILoggerFactory loggerFactory,
            VotingManager votingManager,
            IFederationHistory federationHistory,
            Network network,
            IWhitelistedHashesRepository whitelistedHashesRepository,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            IChainState chainState,
            IBlockStoreQueue blockStoreQueue,
            NodeSettings nodeSettings,
            ReconstructFederationService reconstructFederationService,
            IPoAMiner miner = null
           )
        {
            this.federationManager = federationManager;
            this.connectionManager = connectionManager;
            this.chainIndexer = chainIndexer;
            this.initialBlockDownloadState = initialBlockDownloadState;
            this.consensusManager = consensusManager;
            this.peerBanning = peerBanning;
            this.loggerFactory = loggerFactory;
            this.miner = miner;
            this.votingManager = votingManager;
            this.federationHistory = federationHistory;
            this.whitelistedHashesRepository = whitelistedHashesRepository;
            this.network = network;
            this.idleFederationMembersKicker = idleFederationMembersKicker;
            this.chainState = chainState;
            this.blockStoreQueue = blockStoreQueue;
            this.nodeSettings = nodeSettings;
            this.reconstructFederationService = reconstructFederationService;

            payloadProvider.DiscoverPayloads(this.GetType().Assembly);
        }

        /// <inheritdoc />
        public override Task InitializeAsync()
        {
            NetworkPeerConnectionParameters connectionParameters = this.connectionManager.Parameters;

            this.ReplaceConsensusManagerBehavior(connectionParameters);

            this.ReplaceBlockStoreBehavior(connectionParameters);

            var options = (PoAConsensusOptions)this.network.Consensus.Options;

            if (options.VotingEnabled)
            {
                var rebuildFederation = this.nodeSettings?.ConfigReader.GetOrDefault(PoAFeature.ReconstructFederationFlag, false) ?? false;
                if (rebuildFederation || (this.votingManager.PollsRepository.CurrentTip?.Height ?? 0) > this.chainIndexer?.Tip.Height)
                {
                    this.votingManager.PollsRepository.Reset();
                    this.reconstructFederationService.SetReconstructionFlag(false);
                }

                // If we are kicking members, we need to initialize this component before the VotingManager.
                // The VotingManager may tally votes and execute federation changes, but the IdleKicker needs to know who the current block is from.
                // The IdleKicker can much more easily find out who the block is from if it receives the block first.
                if (options.AutoKickIdleMembers)
                {
                    this.idleFederationMembersKicker.Initialize();
                    this.votingManager.Initialize(this.federationHistory, this.idleFederationMembersKicker);
                }
                else
                {
                    this.votingManager.Initialize(this.federationHistory);
                }
            }

            this.federationManager.Initialize();
            this.whitelistedHashesRepository.Initialize();

            if (!this.votingManager.Synchronize(this.chainIndexer.Tip))
                throw new System.OperationCanceledException();

            this.federationHistory.Initialize();

            // If the node is started in devmode, its role must be of miner in order to mine.
            // If devmode is not specified, initialize mining as per normal.
            if (this.nodeSettings.DevMode == null || this.nodeSettings.DevMode == DevModeNodeRole.Miner)
                this.miner?.InitializeMining();

            return Task.CompletedTask;
        }

        /// <summary>Replaces default <see cref="ConsensusManagerBehavior"/> with <see cref="PoAConsensusManagerBehavior"/>.</summary>
        /// <param name="connectionParameters">See <see cref="NetworkPeerConnectionParameters"/>.</param>
        private void ReplaceConsensusManagerBehavior(NetworkPeerConnectionParameters connectionParameters)
        {
            INetworkPeerBehavior defaultConsensusManagerBehavior = connectionParameters.TemplateBehaviors.FirstOrDefault(behavior => behavior is ConsensusManagerBehavior);

            if (defaultConsensusManagerBehavior == null)
            {
                throw new MissingServiceException(typeof(ConsensusManagerBehavior), "Missing expected ConsensusManagerBehavior.");
            }

            connectionParameters.TemplateBehaviors.Remove(defaultConsensusManagerBehavior);
            connectionParameters.TemplateBehaviors.Add(new PoAConsensusManagerBehavior(this.chainIndexer, this.initialBlockDownloadState, this.consensusManager, this.peerBanning, this.loggerFactory));
        }

        /// <summary>Replaces default <see cref="PoABlockStoreBehavior"/> with <see cref="PoABlockStoreBehavior"/>.</summary>
        /// <param name="connectionParameters">See <see cref="NetworkPeerConnectionParameters"/>.</param>
        private void ReplaceBlockStoreBehavior(NetworkPeerConnectionParameters connectionParameters)
        {
            INetworkPeerBehavior defaultBlockStoreBehavior = connectionParameters.TemplateBehaviors.FirstOrDefault(behavior => behavior is BlockStoreBehavior);

            if (defaultBlockStoreBehavior == null)
            {
                throw new MissingServiceException(typeof(BlockStoreBehavior), "Missing expected BlockStoreBehavior.");
            }

            connectionParameters.TemplateBehaviors.Remove(defaultBlockStoreBehavior);
            connectionParameters.TemplateBehaviors.Add(new PoABlockStoreBehavior(this.chainIndexer, this.chainState, this.loggerFactory, this.consensusManager, this.blockStoreQueue));
        }

        /// <inheritdoc />
        public override void Dispose()
        {
            this.miner?.Dispose();

            this.votingManager.Dispose();

            this.idleFederationMembersKicker.Dispose();
        }
    }
}
