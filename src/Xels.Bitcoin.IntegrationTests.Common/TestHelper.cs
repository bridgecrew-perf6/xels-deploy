﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.Miner.Interfaces;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Controllers;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.IntegrationTests.Common.Runners;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.Extensions;

namespace Xels.Bitcoin.IntegrationTests.Common
{
    public class TestHelper
    {
        public static bool AreNodesSynced(CoreNode node1, CoreNode node2, bool ignoreMempool = false)
        {
            if (node1.runner is BitcoinCoreRunner || node2.runner is BitcoinCoreRunner)
            {
                return node1.CreateRPCClient().GetBestBlockHash() == node2.CreateRPCClient().GetBestBlockHash();
            }

            // If the nodes are at genesis they are considered synced.
            if (node1.FullNode.ChainIndexer.Tip.Height == 0 && node2.FullNode.ChainIndexer.Tip.Height == 0)
                return true;

            if (node1.FullNode.ChainIndexer.Tip.HashBlock != node2.FullNode.ChainIndexer.Tip.HashBlock)
                return false;

            if (node1.FullNode.ChainBehaviorState.ConsensusTip.HashBlock != node2.FullNode.ChainBehaviorState.ConsensusTip.HashBlock)
                return false;

            // Check that node1 tip exists in node2 store (either in disk or in the pending list)
            if (node1.FullNode.BlockStore().GetBlock(node2.FullNode.ChainBehaviorState.ConsensusTip.HashBlock) == null)
                return false;

            // Check that node2 tip exists in node1 store (either in disk or in the pending list)
            if (node2.FullNode.BlockStore().GetBlock(node1.FullNode.ChainBehaviorState.ConsensusTip.HashBlock) == null)
                return false;

            if (!ignoreMempool)
            {
                if (node1.FullNode.MempoolManager().InfoAll().Count != node2.FullNode.MempoolManager().InfoAll().Count)
                    return false;
            }

            if ((node1.FullNode.WalletManager().ContainsWallets) && (node2.FullNode.WalletManager().ContainsWallets))
                if (node1.FullNode.WalletManager().WalletTipHash != node2.FullNode.WalletManager().WalletTipHash)
                    return false;

            if (node1.CreateRPCClient().GetBestBlockHash() != node2.CreateRPCClient().GetBestBlockHash())
                return false;

            return true;
        }

        public static (bool Passed, string Message) AreNodesSyncedMessage(CoreNode node1, CoreNode node2, bool ignoreMempool = false)
        {
            // TODO: This does not check mempool equivalence, so this method actually behaves differently in tests involving bitcoind!
            if (node1.runner is BitcoinCoreRunner || node2.runner is BitcoinCoreRunner)
            {
                return (node1.CreateRPCClient().GetBestBlockHash() == node2.CreateRPCClient().GetBestBlockHash(), "[BEST_BLOCK_HASH_DOES_MATCH]");
            }

            // If the nodes are at genesis they are considered synced.
            if (node1.FullNode.ChainIndexer.Tip.Height == 0 && node2.FullNode.ChainIndexer.Tip.Height == 0)
                return (true, "[TIPS_ARE_AT_GENESIS]");

            if (node1.FullNode.ChainIndexer.Tip.HashBlock != node2.FullNode.ChainIndexer.Tip.HashBlock)
                return (false, $"[CHAIN_TIP_HASH_DOES_NOT_MATCH_{node1.FullNode.ChainIndexer.Tip}_{node2.FullNode.ChainIndexer.Tip}]");

            if (node1.FullNode.ChainBehaviorState.ConsensusTip.HashBlock != node2.FullNode.ChainBehaviorState.ConsensusTip.HashBlock)
                return (false, $"[CONSENSUS_TIP_HASH_DOES_MATCH]_{node1.FullNode.ChainBehaviorState.ConsensusTip}_{node2.FullNode.ChainBehaviorState.ConsensusTip}]");

            // Check that node1 tip exists in node2 store (either in disk or in the pending list)
            if (node1.FullNode.BlockStore().GetBlock(node2.FullNode.ChainBehaviorState.ConsensusTip.HashBlock) == null)
                return (false, "[NODE2_TIP_NOT_IN_NODE1_STORE]");

            // Check that node2 tip exists in node1 store (either in disk or in the pending list)
            if (node2.FullNode.BlockStore().GetBlock(node1.FullNode.ChainBehaviorState.ConsensusTip.HashBlock) == null)
                return (false, "[NODE1_TIP_NOT_IN_NODE2_STORE]");

            if (!ignoreMempool)
            {
                if (node1.FullNode.MempoolManager().InfoAll().Count != node2.FullNode.MempoolManager().InfoAll().Count)
                    return (false, "[NODE1_MEMPOOL_COUNT_NOT_EQUAL_NODE2_MEMPOOL_COUNT]");
            }

            if ((node1.FullNode.WalletManager().ContainsWallets) && (node2.FullNode.WalletManager().ContainsWallets))
                if (node1.FullNode.WalletManager().WalletTipHash != node2.FullNode.WalletManager().WalletTipHash)
                    return (false, "[WALLET_TIP_HASH_DOESNOT_MATCH]");

            if (node1.CreateRPCClient().GetBestBlockHash() != node2.CreateRPCClient().GetBestBlockHash())
                return (false, "[RPC_CLIENT_BEST_BLOCK_HASH_DOES_NOT_MATCH]");

            return (true, string.Empty);
        }

        public static bool IsNodeSynced(CoreNode node)
        {
            // TODO: Need test for bitcoin runner too?

            // If the node is at genesis it is considered synced.
            if (node.FullNode.ChainIndexer.Tip.Height == 0)
                return true;

            if (node.FullNode.ChainIndexer.Tip.HashBlock != node.FullNode.ChainBehaviorState.ConsensusTip.HashBlock)
                return false;

            // Check that node1 tip exists in store (either in disk or in the pending list)
            if (node.FullNode.BlockStore().GetBlock(node.FullNode.ChainBehaviorState.ConsensusTip.HashBlock) == null)
                return false;

            if ((node.FullNode.WalletManager().ContainsWallets) &&
                (node.FullNode.ChainIndexer.Tip.HashBlock != node.FullNode.WalletManager().WalletTipHash))
                return false;

            return true;
        }

        /// <summary>
        /// Ensures a node is internally synced and at a given height.
        /// </summary>
        /// <param name="node">This node.</param>
        /// <param name="height">At which height should it be synced to.</param>
        /// <returns>Returns <c>true</c> if the node is synced at a given height.</returns>
        public static bool IsNodeSyncedAtHeight(CoreNode node, int height, int waitTimeSeconds = 60)
        {
            TestBase.WaitLoopMessage(() => { return (node.FullNode.ConsensusManager().Tip.Height == height, $"Node height: {node.FullNode.ConsensusManager().Tip.Height}; Expected height: {height}"); }, waitTimeSeconds);
            return true;
        }

        public static void TriggerSync(CoreNode node)
        {
            foreach (INetworkPeer connectedPeer in node.FullNode.ConnectionManager.ConnectedPeers)
                connectedPeer.Behavior<ConsensusManagerBehavior>().ResyncAsync().GetAwaiter().GetResult();
        }

        /// <summary>
        /// Determines whether or not the node has any connections.
        /// </summary>
        /// <param name="node">The node to check.</param>
        /// <returns>Returns <c>true</c> if the node does not have any connected peers.</returns>
        public static bool IsNodeConnected(CoreNode node)
        {
            return node.FullNode.ConnectionManager.ConnectedPeers.Any();
        }

        public static void WaitForNodeToSync(params CoreNode[] nodes)
        {
            nodes.ToList().ForEach(n => TestBase.WaitLoop(() => IsNodeSynced(n)));
            nodes.Skip(1).ToList().ForEach(n => TestBase.WaitLoop(() => AreNodesSynced(nodes.First(), n)));
        }

        public static void DisableBlockPropagation(CoreNode from, CoreNode to)
        {
            from.FullNode.ConnectionManager.ConnectedPeers.FindByEndpoint(to.Endpoint).Behavior<BlockStoreBehavior>().CanRespondToGetDataPayload = false;
        }

        public static void EnableBlockPropagation(CoreNode from, CoreNode to)
        {
            from.FullNode.ConnectionManager.ConnectedPeers.FindByEndpoint(to.Endpoint).Behavior<BlockStoreBehavior>().CanRespondToGetDataPayload = true;
        }

        public static void WaitForNodeToSyncIgnoreMempool(params CoreNode[] nodes)
        {
            nodes.ToList().ForEach(node => TestBase.WaitLoop(() => IsNodeSynced(node)));
            nodes.Skip(1).ToList().ForEach(node => TestBase.WaitLoop(() => AreNodesSynced(nodes.First(), node, true)));
        }

        public static (HdAddress AddressUsed, List<uint256> BlockHashes) MineBlocks(CoreNode node, int numberOfBlocks, bool syncNode = true, string walletName = "mywallet", string walletPassword = "password", string accountName = "account 0", string miningAddress = null)
        {
            Guard.NotNull(node, nameof(node));

            if (numberOfBlocks == 0)
                throw new ArgumentOutOfRangeException(nameof(numberOfBlocks), "Number of blocks must be greater than zero.");

            SetMinerSecret(node, walletName, walletPassword, accountName, miningAddress);

            var script = new ReserveScript { ReserveFullNodeScript = node.MinerSecret.ScriptPubKey };
            var blockHashes = node.FullNode.Services.ServiceProvider.GetService<IPowMining>().GenerateBlocks(script, (ulong)numberOfBlocks, uint.MaxValue);

            if (syncNode)
                TestBase.WaitLoop(() => IsNodeSynced(node));

            return (node.MinerHDAddress, blockHashes);
        }

        public static void SetMinerSecret(CoreNode coreNode, string walletName = "mywallet", string walletPassword = "password", string accountName = "account 0", string miningAddress = null)
        {
            if (coreNode.MinerSecret == null)
            {
                Wallet wallet = coreNode.FullNode.WalletManager().GetWallet(walletName);
                HdAccount account = wallet.GetAccount(accountName);

                HdAddress address;
                if (!string.IsNullOrEmpty(miningAddress))
                {
                    address = account.ExternalAddresses.Concat(account.InternalAddresses).Single(add => add.Address == miningAddress);
                }
                else
                {
                    address = account.GetFirstUnusedReceivingAddress();
                }

                coreNode.MinerHDAddress = address;

                Key extendedPrivateKey = wallet.GetExtendedPrivateKeyForAddress(walletPassword, address).PrivateKey;
                coreNode.SetMinerSecret(new BitcoinSecret(extendedPrivateKey, coreNode.FullNode.Network));
            }
        }

        /// <summary>
        /// This should only be used if we need to create a block manually, in all other cases please use
        /// <see cref="CoreNode.GenerateXelsWithMiner"/>
        /// </summary>
        /// <param name="coreNode">The node we want to create the block with.</param>
        /// <param name="transactions">Transactions we want to manually include in the block.</param>
        /// <param name="nonce">Optional nonce.</param>
        public static Block GenerateBlockManually(CoreNode coreNode, List<Transaction> transactions, uint nonce = 0, bool callBlockMinedAsync = true)
        {
            var block = coreNode.FullNode.Network.CreateBlock();
            block.Header.HashPrevBlock = coreNode.FullNode.ChainIndexer.Tip.HashBlock;
            block.Header.Bits = block.Header.GetWorkRequired(coreNode.FullNode.Network, coreNode.FullNode.ChainIndexer.Tip);
            block.Header.UpdateTime(DateTimeOffset.UtcNow, coreNode.FullNode.Network, coreNode.FullNode.ChainIndexer.Tip);

            var coinbase = coreNode.FullNode.Network.CreateTransaction();
            coinbase.AddInput(TxIn.CreateCoinbase(coreNode.FullNode.ChainIndexer.Height + 1));
            coinbase.AddOutput(new TxOut(coreNode.FullNode.Network.GetReward(coreNode.FullNode.ChainIndexer.Height + 1), coreNode.MinerSecret.GetAddress()));
            block.AddTransaction(coinbase);

            if (transactions.Any())
            {
                transactions = Reorder(transactions);
                block.Transactions.AddRange(transactions);
            }

            block.UpdateMerkleRoot();

            while (!block.CheckProofOfWork())
                block.Header.Nonce = ++nonce;

            // This will set the block size.
            block = Block.Load(block.ToBytes(), coreNode.FullNode.Network.Consensus.ConsensusFactory);

            if (callBlockMinedAsync)
            {
                coreNode.FullNode.ConsensusManager().BlockMinedAsync(block).GetAwaiter().GetResult();
            }
            return block;
        }

        public static List<Transaction> Reorder(List<Transaction> transactions)
        {
            if (transactions.Count == 0)
                return transactions;

            var result = new List<Transaction>();
            var dictionary = transactions.ToDictionary(t => t.GetHash(), t => new TransactionNode(t));
            foreach (TransactionNode transaction in dictionary.Select(d => d.Value))
            {
                foreach (TxIn input in transaction.Transaction.Inputs)
                {
                    TransactionNode node = dictionary.TryGet(input.PrevOut.Hash);
                    if (node != null)
                    {
                        transaction.DependsOn.Add(node);
                    }
                }
            }

            while (dictionary.Count != 0)
            {
                foreach (TransactionNode node in dictionary.Select(d => d.Value).ToList())
                {
                    foreach (TransactionNode parent in node.DependsOn.ToList())
                    {
                        if (!dictionary.ContainsKey(parent.Hash))
                            node.DependsOn.Remove(parent);
                    }

                    if (node.DependsOn.Count == 0)
                    {
                        result.Add(node.Transaction);
                        dictionary.Remove(node.Hash);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// Disconnects a node from another and waits until the operation completes.
        /// </summary>
        /// <param name="thisNode">The node that will be disconnected from.</param>
        /// <param name="nodeToDisconnect">The node that will be disconnected.</param>
        public static void Disconnect(CoreNode thisNode, CoreNode nodeToDisconnect)
        {
            if (!IsNodeConnectedTo(thisNode, nodeToDisconnect))
                return;

            thisNode.CreateRPCClient().RemoveNode(nodeToDisconnect.Endpoint);
            TestBase.WaitLoop(() => !IsNodeConnectedTo(thisNode, nodeToDisconnect));
        }

        /// <summary>
        /// Disconnects a node from all connections and waits until the operation completes.
        /// </summary>
        /// <param name="nodes">The nodes that will be disconnected.</param>
        public static void DisconnectAll(params CoreNode[] nodes)
        {
            foreach (var node in nodes)
            {
                foreach (var peer in node.FullNode.ConnectionManager.ConnectedPeers.ToList())
                {
                    node.CreateRPCClient().RemoveNode(peer.PeerEndPoint);
                }

                TestBase.WaitLoop(() => node.FullNode.ConnectionManager.ConnectedPeers.Where(p => !p.Inbound).Count() == 0);
            }

            foreach (var node in nodes)
            {
                TestBase.WaitLoop(() => !IsNodeConnected(node));
            }
        }

        private class TransactionNode
        {
            public uint256 Hash = null;
            public Transaction Transaction = null;
            public List<TransactionNode> DependsOn = new List<TransactionNode>();

            public TransactionNode(Transaction tx)
            {
                this.Transaction = tx;
                this.Hash = tx.GetHash();
            }
        }

        public static TransactionBuildContext CreateTransactionBuildContext(
            Network network,
            string sendingWalletName,
            string sendingAccountName,
            string sendingPassword,
            ICollection<Recipient> recipients,
            FeeType feeType,
            int minConfirmations)
        {
            return new TransactionBuildContext(network)
            {
                AccountReference = new WalletAccountReference(sendingWalletName, sendingAccountName),
                MinConfirmations = minConfirmations,
                FeeType = feeType,
                WalletPassword = sendingPassword,
                Recipients = recipients.ToList()
            };
        }

        /// <summary>
        /// Connects a node to another and waits for the operation to complete.
        /// </summary>
        /// <param name="thisNode">The node the connection will be established from.</param>
        /// <param name="connectToNode">The node that will be connected to.</param>
        public static void Connect(CoreNode thisNode, CoreNode connectToNode)
        {
            var cancellation = new CancellationTokenSource((int)TimeSpan.FromSeconds(30).TotalMilliseconds);

            var isConnecting = false;

            TestBase.WaitLoop(() =>
            {
                try
                {
                    if (IsNodeConnectedTo(thisNode, connectToNode))
                        return true;

                    // Don't try the same connection again until it failed or connected.
                    if (!isConnecting)
                    {
                        thisNode.CreateRPCClient().AddNode(connectToNode.Endpoint, true);
                        isConnecting = true;
                    }
                }
                catch (Exception)
                {
                    // The connect request failed, probably due to a web exception so try again.
                    isConnecting = false;
                }

                return false;

            }, retryDelayInMiliseconds: 500, cancellationToken: cancellation.Token);
        }

        /// <summary>
        /// This connect method will only retry the connection if an WebException occurred.
        /// <para>
        /// In cases where we expect the node to disconnect, this should be used.
        /// </para>
        /// </summary>
        /// <param name="thisNode">The node the connection will be established from.</param>
        /// <param name="connectToNode">The node that will be connected to.</param>
        public static void ConnectNoCheck(CoreNode thisNode, CoreNode connectToNode)
        {
            var cancellation = new CancellationTokenSource((int)TimeSpan.FromSeconds(30).TotalMilliseconds);

            TestBase.WaitLoop(() =>
            {
                try
                {
                    thisNode.CreateRPCClient().AddNode(connectToNode.Endpoint, true);
                    return true;
                }
                catch (WebException)
                {
                    return false;
                }
                catch (Exception)
                {
                    return true;
                }
            }, retryDelayInMiliseconds: 500, cancellationToken: cancellation.Token);
        }

        /// <summary>
        /// Connects a node to a set of other nodes and waits for all the nodes to sync.
        /// </summary>
        /// <param name="thisNode">The node the connection will be established from.</param>
        /// <param name="to">The nodes to connect to.</param>
        public static void ConnectAndSync(CoreNode thisNode, params CoreNode[] to)
        {
            ConnectAndSync(thisNode, false, to);
        }

        /// <summary>
        /// Connects a node to a set of other nodes and waits for all the nodes to sync.
        /// </summary>
        /// <param name="thisNode">The node the connection will be established from.</param>
        /// <param name="ignoreMempool">Ignore differences between mempools.</param>
        /// <param name="to">The nodes to connect to.</param>
        public static void ConnectAndSync(CoreNode thisNode, bool ignoreMempool, params CoreNode[] to)
        {
            foreach (CoreNode coreNode in to)
                Connect(thisNode, coreNode);

            foreach (CoreNode coreNode in to)
                TestBase.WaitLoopMessage(() => AreNodesSyncedMessage(thisNode, coreNode, ignoreMempool), waitTimeSeconds: 120);
        }

        /// <summary>
        /// Checks to see whether a node is connected to another.
        /// </summary>
        /// <param name="thisNode">The node we want to check from.</param>
        /// <param name="isConnectedToNode">The node that will be checked.</param>
        /// <returns>Returns <c>true</c> if the address exists in this node's connected peers collection.</returns>
        public static bool IsNodeConnectedTo(CoreNode thisNode, CoreNode isConnectedToNode)
        {
            if (thisNode.runner is BitcoinCoreRunner)
            {
                return IsBitcoinCoreConnectedTo(thisNode, isConnectedToNode);
            }
            else
            {
                if (thisNode.FullNode.ConnectionManager.ConnectedPeers.Any(p => p.PeerEndPoint.Match(isConnectedToNode.Endpoint)))
                    return true;

                // The peer might be connected via an inbound connection.
                if (isConnectedToNode.runner is BitcoinCoreRunner)
                    return IsBitcoinCoreConnectedTo(isConnectedToNode, thisNode);
                else
                    return isConnectedToNode.FullNode.ConnectionManager.ConnectedPeers.Any(p => p.PeerEndPoint.Match(thisNode.Endpoint));
            }
        }

        private static bool IsBitcoinCoreConnectedTo(CoreNode thisNode, CoreNode isConnectedToNode)
        {
            if (!(thisNode.runner is BitcoinCoreRunner))
                throw new ArgumentException($"{0} is not a bitcoin core node.");

            var thisNodePeers = thisNode.CreateRPCClient().GetPeersInfo();
            return thisNodePeers.Any(p => p.Address.Match(isConnectedToNode.Endpoint));
        }

        /// <summary>
        /// A helper that constructs valid and various types of invalid blocks manually.
        /// </summary>
        public static BlockBuilder BuildBlocks { get { return new BlockBuilder(); } }

        private const string Password = "password";
        private const string Name = "mywallet";
        private const string AccountName = "account 0";

        public static bool CheckWalletBalance(CoreNode node, Money amount)
        {
            var total = node.FullNode.WalletManager().GetSpendableTransactionsInWallet(Name).Sum(s => s.Transaction.Amount);
            return total == amount;
        }

        public static void SendCoins(CoreNode miner, CoreNode sender, CoreNode[] receivers, Money amount, List<OutPoint> outPoints = null, int? utxoCount = 1)
        {
            var recipients = new List<Recipient>(receivers.Length);

            foreach (var receiver in receivers)
            {
                var receivingAddress = receiver.FullNode.WalletManager().GetUnusedAddress(new WalletAccountReference(Name, AccountName));
                Money singleUtxoAmount = amount / utxoCount;

                for (int i = 0; i < utxoCount; i++)
                    recipients.Add(new Recipient { ScriptPubKey = receivingAddress.ScriptPubKey, Amount = singleUtxoAmount });
            }

            var context = CreateContext(sender.FullNode.Network, new WalletAccountReference(Name, AccountName), Password, recipients, FeeType.Medium, (int)sender.FullNode.Network.Consensus.CoinbaseMaturity, outPoints);

            var transaction = sender.FullNode.WalletTransactionHandler().BuildTransaction(context);

            sender.FullNode.NodeController<WalletController>().SendTransactionAsync(new SendTransactionRequest(transaction.ToHex())).GetAwaiter().GetResult();

            MineBlocks(miner, 1);

            foreach (var receiver in receivers)
            {
                TestBase.WaitLoop(() => receiver.FullNode.WalletManager().GetSpendableTransactionsInWallet(Name).Any());
            }

            //if (sender != receiver)
            //    TestBase.WaitLoop(() => CheckWalletBalance(receiver, amount));
        }

        private static TransactionBuildContext CreateContext(Network network, WalletAccountReference accountReference, string password, List<Recipient> recipients, FeeType feeType, int minConfirmations, List<OutPoint> outPoints)
        {
            return new TransactionBuildContext(network)
            {
                AccountReference = accountReference,
                MinConfirmations = minConfirmations,
                FeeType = feeType,
                WalletPassword = password,
                Recipients = recipients,
                SelectedInputs = outPoints
            };
        }
    }
}
