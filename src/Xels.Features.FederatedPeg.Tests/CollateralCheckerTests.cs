﻿using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Controllers.Models;
using Xels.Bitcoin.EventBus;
using Xels.Bitcoin.Features.BlockStore.Controllers;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Features.PoA.Voting;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.FederatedPeg.Tests.Utils;
using Xels.Sidechains.Networks;
using Xunit;

namespace Xels.Features.FederatedPeg.Tests
{
    public class CollateralCheckerTests
    {
        private ICollateralChecker collateralChecker;
        private List<CollateralFederationMember> collateralFederationMembers;
        private readonly int collateralCheckHeight = 2000;

        private void InitializeCollateralChecker([CallerMemberName] string callingMethod = "")
        {
            var loggerFactory = new LoggerFactory();
            IHttpClientFactory clientFactory = new Bitcoin.Controllers.HttpClientFactory();

            Network network = CirrusNetwork.NetworksSelector.Regtest();

            this.collateralFederationMembers = new List<CollateralFederationMember>()
            {
                new CollateralFederationMember(new PubKey("036317d97f911ce899fd0a360866d19f2dca5252c7960d4652d814ab155a8342de"), false, new Money(100), "addr1"),
                new CollateralFederationMember(new PubKey("02a08d72d47b3103261163c15aa2f6b0d007e1872ad9f5fddbfbd27bdb738156e9"), false, new Money(500), "addr2"),
                new CollateralFederationMember(new PubKey("03634c79d4e8e915cfb9f7bbef57bed32d715150836b7845b1a14c93670d816ab6"), false, new Money(100_000), "addr3")
            };

            List<IFederationMember> federationMembers = (network.Consensus.Options as PoAConsensusOptions).GenesisFederationMembers;
            federationMembers.Clear();
            federationMembers.AddRange(this.collateralFederationMembers);

            var dataFolder = TestBase.CreateTestDir(callingMethod);
            FederatedPegSettings fedPegSettings = FedPegTestsHelper.CreateSettings(network, KnownNetworks.StraxRegTest, dataFolder, out NodeSettings nodeSettings);

            var counterChainSettings = new CounterChainSettings(nodeSettings, new CounterChainNetworkWrapper(Networks.Strax.Regtest()));
            var asyncMock = new Mock<IAsyncProvider>();
            asyncMock.Setup(a => a.RegisterTask(It.IsAny<string>(), It.IsAny<Task>()));

            ISignals signals = new Signals(loggerFactory, new DefaultSubscriptionErrorHandler(loggerFactory));
            var dbreezeSerializer = new DBreezeSerializer(network.Consensus.ConsensusFactory);
            var asyncProvider = new AsyncProvider(loggerFactory, signals);

            var chainIndexerMock = new Mock<ChainIndexer>();
            var header = new BlockHeader();
            chainIndexerMock.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));
            var fullNode = new Mock<IFullNode>();

            IFederationManager federationManager = new FederationManager(fullNode.Object, network, nodeSettings, signals, counterChainSettings);

            var votingManager = new VotingManager(federationManager, new Mock<IPollResultExecutor>().Object, new Mock<INodeStats>().Object, nodeSettings.DataFolder, dbreezeSerializer, signals, network, chainIndexerMock.Object);
            var federationHistory = new FederationHistory(federationManager, network, votingManager);
            votingManager.Initialize(federationHistory);

            fullNode.Setup(x => x.NodeService<VotingManager>(It.IsAny<bool>())).Returns(votingManager);

            federationManager.Initialize();

            this.collateralChecker = new CollateralChecker(clientFactory, counterChainSettings, federationManager, signals, network, asyncMock.Object, (new Mock<INodeLifetime>()).Object);
        }

        [Fact]
        public async Task InitializationTakesForeverIfCounterNodeIsOfflineAsync()
        {
            InitializeCollateralChecker();

            Task initTask = this.collateralChecker.InitializeAsync();

            await Task.Delay(10_000);

            // Task never finishes since counter chain node doesn't respond.
            Assert.False(initTask.IsCompleted);
        }

        [Fact]
        public async Task CanInitializeAndCheckCollateralAsync()
        {
            InitializeCollateralChecker();

            var blockStoreClientMock = new Mock<IBlockStoreClient>();

            var collateralData = new VerboseAddressBalancesResult(this.collateralCheckHeight + 1000)
            {
                BalancesData = new List<AddressIndexerData>()
                {
                    new AddressIndexerData()
                    {
                        Address = this.collateralFederationMembers[0].CollateralMainchainAddress,
                        BalanceChanges = new List<AddressBalanceChange>() { new AddressBalanceChange() { BalanceChangedHeight = 0, Deposited = true, Satoshi = this.collateralFederationMembers[0].CollateralAmount } }
                    },
                    new AddressIndexerData()
                    {
                        Address = this.collateralFederationMembers[1].CollateralMainchainAddress,
                        BalanceChanges = new List<AddressBalanceChange>() { new AddressBalanceChange() { BalanceChangedHeight = 0, Deposited = true, Satoshi = this.collateralFederationMembers[1].CollateralAmount + 10 } }
                    },
                    new AddressIndexerData()
                    {
                        Address = this.collateralFederationMembers[2].CollateralMainchainAddress,
                        BalanceChanges = new List<AddressBalanceChange>() { new AddressBalanceChange() { BalanceChangedHeight = 0, Deposited = true, Satoshi = this.collateralFederationMembers[2].CollateralAmount - 10 } }
                    }
                }
            };

            blockStoreClientMock.Setup(x => x.GetVerboseAddressesBalancesDataAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>())).ReturnsAsync(collateralData);

            this.collateralChecker.SetPrivateVariableValue("blockStoreClient", blockStoreClientMock.Object);

            await this.collateralChecker.InitializeAsync();

            Assert.True(this.collateralChecker.CheckCollateral(this.collateralFederationMembers[0], this.collateralCheckHeight));
            Assert.True(this.collateralChecker.CheckCollateral(this.collateralFederationMembers[1], this.collateralCheckHeight));
            Assert.False(this.collateralChecker.CheckCollateral(this.collateralFederationMembers[2], this.collateralCheckHeight));

            // Now change what the client returns and make sure collateral check fails after update.
            AddressIndexerData updated = collateralData.BalancesData.First(b => b.Address == this.collateralFederationMembers[0].CollateralMainchainAddress);
            updated.BalanceChanges.First().Satoshi = this.collateralFederationMembers[0].CollateralAmount - 1;

            // Wait CollateralUpdateIntervalSeconds + 1 seconds

            await Task.Delay(21_000);
            Assert.False(this.collateralChecker.CheckCollateral(this.collateralFederationMembers[0], this.collateralCheckHeight));

            this.collateralChecker.Dispose();
        }
    }
}
