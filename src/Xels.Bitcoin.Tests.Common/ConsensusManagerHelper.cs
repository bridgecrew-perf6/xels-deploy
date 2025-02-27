﻿using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.BlockPulling;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Consensus.Rules;
using Xels.Bitcoin.Consensus.Validators;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.P2P;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.P2P.Protocol.Payloads;
using Xels.Bitcoin.Utilities;

namespace Xels.Bitcoin.Tests.Common
{
    public static class ConsensusManagerHelper
    {
        public static ConsensusManager CreateConsensusManager(
            Network network,
            string dataDir = null,
            ChainState chainState = null,
            InMemoryCoinView inMemoryCoinView = null,
            ChainIndexer chainIndexer = null,
            ConsensusRuleEngine consensusRules = null)
        {
            string[] param = dataDir == null ? new string[] { } : new string[] { $"-datadir={dataDir}" };

            var nodeSettings = new NodeSettings(network, args: param);

            ILoggerFactory loggerFactory = nodeSettings.LoggerFactory;
            IDateTimeProvider dateTimeProvider = DateTimeProvider.Default;

            var signals = new Signals.Signals(loggerFactory, null);
            var asyncProvider = new AsyncProvider(loggerFactory, signals);

            network.Consensus.Options = new ConsensusOptions();

            // Dont check PoW of a header in this test.
            network.Consensus.ConsensusRules.HeaderValidationRules.RemoveAll(x => x == typeof(CheckDifficultyPowRule));

            var consensusSettings = new ConsensusSettings(nodeSettings);

            if (chainIndexer == null)
                chainIndexer = new ChainIndexer(network);

            if (inMemoryCoinView == null)
                inMemoryCoinView = new InMemoryCoinView(new HashHeightPair(chainIndexer.Tip));

            var connectionManagerSettings = new ConnectionManagerSettings(nodeSettings);

            var selfEndpointTracker = new SelfEndpointTracker(loggerFactory, connectionManagerSettings);
            var peerAddressManager = new PeerAddressManager(DateTimeProvider.Default, nodeSettings.DataFolder, loggerFactory, selfEndpointTracker);
            var networkPeerFactory = new NetworkPeerFactory(network,
                dateTimeProvider,
                loggerFactory, new PayloadProvider().DiscoverPayloads(),
                new SelfEndpointTracker(loggerFactory, connectionManagerSettings),
                new Mock<IInitialBlockDownloadState>().Object,
                connectionManagerSettings,
                asyncProvider,
                peerAddressManager);

            var peerDiscovery = new PeerDiscovery(asyncProvider, loggerFactory, network, networkPeerFactory, new NodeLifetime(), nodeSettings, peerAddressManager);
            var connectionSettings = new ConnectionManagerSettings(nodeSettings);
            var connectionManager = new ConnectionManager(dateTimeProvider, loggerFactory, network, networkPeerFactory, nodeSettings,
                new NodeLifetime(), new NetworkPeerConnectionParameters(), peerAddressManager, new IPeerConnector[] { },
                peerDiscovery, selfEndpointTracker, connectionSettings, new VersionProvider(), new Mock<INodeStats>().Object, asyncProvider, new PayloadProvider());

            if (chainState == null)
                chainState = new ChainState();
            var peerBanning = new PeerBanning(connectionManager, loggerFactory, dateTimeProvider, peerAddressManager);
            var deployments = new NodeDeployments(network, chainIndexer);

            if (consensusRules == null)
            {
                consensusRules = new PowConsensusRuleEngine(network, loggerFactory, dateTimeProvider, chainIndexer, deployments, consensusSettings,
                    new Checkpoints(), inMemoryCoinView, chainState, new InvalidBlockHashStore(dateTimeProvider), new NodeStats(dateTimeProvider, nodeSettings, new Mock<IVersionProvider>().Object), asyncProvider, new ConsensusRulesContainer()).SetupRulesEngineParent();
            }

            var tree = new ChainedHeaderTree(network, loggerFactory, new HeaderValidator(consensusRules, loggerFactory), new Checkpoints(),
                new ChainState(), new Mock<IFinalizedBlockInfoRepository>().Object, consensusSettings, new InvalidBlockHashStore(new DateTimeProvider()), new ChainWorkComparer());

            var consensus = new ConsensusManager(tree, network, loggerFactory, chainState, new IntegrityValidator(consensusRules, loggerFactory),
                new PartialValidator(asyncProvider, consensusRules, loggerFactory), new FullValidator(consensusRules, loggerFactory), consensusRules,
                new Mock<IFinalizedBlockInfoRepository>().Object, new Signals.Signals(loggerFactory, null), peerBanning, new Mock<IInitialBlockDownloadState>().Object, chainIndexer,
                new Mock<IBlockPuller>().Object, new Mock<IBlockStore>().Object, new Mock<IConnectionManager>().Object, new Mock<INodeStats>().Object, new NodeLifetime(), consensusSettings, dateTimeProvider);

            return consensus;
        }
    }
}
