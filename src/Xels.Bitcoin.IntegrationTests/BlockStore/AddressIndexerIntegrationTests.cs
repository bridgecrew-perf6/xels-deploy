﻿using NBitcoin;
using Xels.Bitcoin.Features.BlockStore.AddressIndexing;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.IntegrationTests.Common.ReadyData;
using Xels.Bitcoin.IntegrationTests.Common.TestNetworks;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace Xels.Bitcoin.IntegrationTests.BlockStore
{
    public sealed class AddressIndexerIntegrationTests
    {
        [Fact]
        public void IndexAddresses_All_Nodes_Synced()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var network = new BitcoinRegTest();

                var nodeConfig = new NodeConfigParameters
                {
                    { "-addressindex", "1" }
                };

                CoreNode xelsNode1 = builder.CreateXelsPowNode(network, "ai-1-xelsNode1", configParameters: nodeConfig).WithDummyWallet().Start();
                CoreNode xelsNode2 = builder.CreateXelsPowNode(network, "ai-1-xelsNode2", configParameters: nodeConfig).WithDummyWallet().Start();
                CoreNode xelsNode3 = builder.CreateXelsPowNode(network, "ai-1-xelsNode3", configParameters: nodeConfig).WithDummyWallet().Start();

                // Connect all the nodes.
                TestHelper.Connect(xelsNode1, xelsNode2);
                TestHelper.Connect(xelsNode1, xelsNode3);
                TestHelper.Connect(xelsNode2, xelsNode3);

                // Mine up to a height of 100.
                TestHelper.MineBlocks(xelsNode1, 100);

                TestBase.WaitLoop(() => xelsNode1.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 100);
                TestBase.WaitLoop(() => xelsNode2.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 100);
                TestBase.WaitLoop(() => xelsNode3.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 100);
            }
        }

        [Fact]
        public void IndexAddresses_All_Nodes_Synced_Reorg()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var network = new BitcoinRegTest();

                var nodeConfig = new NodeConfigParameters
                {
                    { "-addressindex", "1" }
                };

                var minerA = builder.CreateXelsPowNode(network, "ai-2-minerA", configParameters: nodeConfig).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest10Miner).Start();
                var minerB = builder.CreateXelsPowNode(network, "ai-2-minerB", configParameters: nodeConfig).WithDummyWallet().Start();
                var syncer = builder.CreateXelsPowNode(network, "ai-2-syncer", configParameters: nodeConfig).Start();

                // Sync the network to height 10.
                TestHelper.ConnectAndSync(syncer, minerA, minerB);

                // Disconnect syncer from miner B
                TestHelper.Disconnect(syncer, minerB);

                // MinerA = 15
                // MinerB = 10
                // Syncer = 15
                TestHelper.MineBlocks(minerA, 5);
                TestBase.WaitLoop(() => TestHelper.AreNodesSynced(syncer, minerA));

                // MinerA = 15
                // MinerB = 15
                // Syncer = 15
                TestHelper.Connect(syncer, minerB);

                // Disconnect syncer from miner A
                TestHelper.Disconnect(syncer, minerA);

                // MinerA = 15
                // MinerB = 25
                // Syncer = 25
                TestHelper.MineBlocks(minerB, 10);
                TestBase.WaitLoop(() => TestHelper.AreNodesSynced(syncer, minerB));

                // MinerA = 35
                // MinerB = 25
                // Syncer = 25
                TestHelper.MineBlocks(minerA, 20);
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerA, 35));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerB, 25));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(syncer, 25));

                TestHelper.Connect(syncer, minerA);

                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(syncer, 35));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerA, 35));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerB, 35));

                TestBase.WaitLoop(() => minerA.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 35);
                TestBase.WaitLoop(() => minerB.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 35);
                TestBase.WaitLoop(() => syncer.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 35);
            }
        }

        [Fact]
        public void IndexAddresses_All_Nodes_Synced_Reorg_With_UTXOs()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var network = new BitcoinRegTestOverrideCoinbaseMaturity(5);

                var nodeConfig = new NodeConfigParameters
                {
                    { "-addressindex", "1" }
                };

                var minerA = builder.CreateXelsPowNode(network, "ai-3-minerA", configParameters: nodeConfig).WithWallet().Start();
                var minerB = builder.CreateXelsPowNode(network, "ai-3-minerB", configParameters: nodeConfig).WithWallet().Start();
                var syncer = builder.CreateXelsPowNode(network, "ai-3-syncer", configParameters: nodeConfig).WithWallet().Start();

                // minerA mines to height 10
                // MinerA = 10
                // MinerB = 10
                // Syncer = 10
                TestHelper.MineBlocks(minerA, 10);

                // Sync the network to height 10.
                TestHelper.ConnectAndSync(syncer, minerA);
                TestHelper.ConnectAndSync(syncer, minerB);

                // Disconnect syncer from miner A
                TestHelper.Disconnect(syncer, minerA);
                TestHelper.Disconnect(syncer, minerB);

                // minerB mines to height 10
                // MinerA = 10
                // MinerB = 20
                // Syncer = 10
                TestHelper.MineBlocks(minerB, 10);
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerA, 10));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerB, 20));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(syncer, 10));

                // Miner A mines on its own chain.
                // MinerA = 25
                // MinerB = 20
                // Syncer = 10
                TestHelper.MineBlocks(minerA, 15);
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerA, 25));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerB, 20));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(syncer, 10));

                // Reconnect syncer to minerA.
                TestHelper.Connect(syncer, minerA);
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerA, 25));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerB, 20));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(syncer, 25));

                // Spend some coins on minerA by sending 10 STRAT to syncer.
                TestHelper.SendCoins(minerA, minerA, new[] { syncer }, Money.Coins(10));

                // Miner A mines the transaction and advances onto 35.
                // MinerA = 40
                // MinerB = 20
                // Syncer = 20
                TestHelper.MineBlocks(minerA, 14);
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerA, 40));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerB, 20));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(syncer, 40));

                // minerB mines to height 50
                // MinerA = 40
                // MinerB = 50
                // Syncer = 40
                TestHelper.MineBlocks(minerB, 40);

                // Reconnect minerB (the longer chain), this will trigger the reorg.
                TestHelper.Connect(syncer, minerB);

                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(syncer, 60));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerA, 60));
                TestBase.WaitLoop(() => TestHelper.IsNodeSyncedAtHeight(minerB, 60));

                TestBase.WaitLoop(() => minerA.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 60);
                TestBase.WaitLoop(() => minerB.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 60);
                TestBase.WaitLoop(() => syncer.FullNode.NodeService<IAddressIndexer>().IndexerTip.Height == 60);

                // The transaction got reverted.
                TestHelper.CheckWalletBalance(syncer, 0);
            }
        }

        [Fact]
        public void IndexAddresses_All_Nodes_Synced_Reorg_Connected()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var network = new BitcoinRegTest();

                var nodeConfig = new NodeConfigParameters
                {
                    { "-addressindex", "1" }
                };

                var minerA = builder.CreateXelsPowNode(network, "ai-4-minerA", configParameters: nodeConfig).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest10Miner).Start();
                var minerB = builder.CreateXelsPowNode(network, "ai-4-minerB", configParameters: nodeConfig).WithDummyWallet().Start();
                var syncer = builder.CreateXelsPowNode(network, "ai-4-syncer", configParameters: nodeConfig).Start();

                // Sync the network to height 10.
                TestHelper.ConnectAndSync(syncer, minerA);
                TestHelper.ConnectAndSync(syncer, minerB);

                // Stop sending blocks from miner A to syncer
                TestHelper.DisableBlockPropagation(minerA, syncer);

                // Stop sending blocks from miner B to syncer
                TestHelper.DisableBlockPropagation(minerB, syncer);

                // Miner A advances 2 blocks [12]
                // Syncer = 10
                // Miner A = 12
                // Miner B = 10
                TestHelper.MineBlocks(minerA, 2);
                Assert.True(TestHelper.IsNodeSyncedAtHeight(syncer, 10));
                Assert.True(TestHelper.IsNodeSyncedAtHeight(minerA, 12));
                Assert.True(TestHelper.IsNodeSyncedAtHeight(minerB, 10));

                // Miner B advances 1 block [11]
                // Syncer = 10
                // Miner A = 12
                // Miner B = 11
                TestHelper.MineBlocks(minerB, 1);
                Assert.True(TestHelper.IsNodeSyncedAtHeight(syncer, 10));
                Assert.True(TestHelper.IsNodeSyncedAtHeight(minerA, 12));
                Assert.True(TestHelper.IsNodeSyncedAtHeight(minerB, 11));

                // Enable sending blocks from miner A to syncer
                TestHelper.EnableBlockPropagation(minerA, syncer);
                // Enable sending blocks from miner B to syncer
                TestHelper.EnableBlockPropagation(minerB, syncer);

                // Miner B advances 2 blocks [13]
                // Syncer = 13
                // Miner A = 13
                // Miner B = 13
                TestHelper.MineBlocks(minerA, 1, false);
                TestHelper.MineBlocks(minerB, 1, false);

                Assert.True(TestHelper.IsNodeSyncedAtHeight(syncer, 13));
                Assert.True(TestHelper.IsNodeSyncedAtHeight(minerA, 13));
                Assert.True(TestHelper.IsNodeSyncedAtHeight(minerB, 13));
            }
        }
    }
}
