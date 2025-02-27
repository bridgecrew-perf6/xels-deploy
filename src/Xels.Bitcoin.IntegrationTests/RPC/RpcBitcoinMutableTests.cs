﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Networks.Deployments;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities.Extensions;
using Xunit;

namespace Xels.Bitcoin.IntegrationTests.RPC
{
    // TODO: These tests all need to run against SBFN too
    /// <summary>
    /// These tests are for RPC tests that require modifying the chain/nodes.
    /// Setup of the chain or nodes can be done in each test.
    /// </summary>
    public class RpcBitcoinMutableTests
    {
        private const string BitcoinCoreVersion15 = "0.15.1";
        private readonly Network regTest;
        private readonly Network testNet;

        public RpcBitcoinMutableTests()
        {
            this.regTest = KnownNetworks.RegTest;
            this.testNet = KnownNetworks.TestNet;
        }

        /// <summary>
        /// <seealso cref="https://github.com/MetacoSA/NBitcoin/blob/master/NBitcoin.Tests/RPCClientTests.cs">NBitcoin test CanGetRawMemPool</seealso>
        /// </summary>
        [Fact]
        public void GetRawMemPoolWithValidTxThenReturnsSameTx()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode().Start();

                RPCClient rpcClient = node.CreateRPCClient();

                // generate 101 blocks
                node.GenerateAsync(101).GetAwaiter().GetResult();

                CoreNode sfn = builder.CreateXelsPowNode(this.regTest).Start();

                TestHelper.ConnectAndSync(node, sfn);

                uint256 txid = rpcClient.SendToAddress(new Key().PubKey.GetAddress(rpcClient.Network), Money.Coins(1.0m), "hello", "world");

                uint256[] ids = rpcClient.GetRawMempool();
                Assert.Single(ids);
                Assert.Equal(txid, ids[0]);

                RPCClient sfnRpc = sfn.CreateRPCClient();

                // It seems to take a while for the transaction to actually propagate, so we have to wait for it before checking the txid is correct.
                TestBase.WaitLoop(() => sfnRpc.GetRawMempool().Length == 1);

                ids = sfnRpc.GetRawMempool();
                Assert.Single(ids);
                Assert.Equal(txid, ids[0]);
            }
        }

        /// <summary>
        /// <seealso cref="https://github.com/MetacoSA/NBitcoin/blob/master/NBitcoin.Tests/RPCClientTests.cs">NBitcoin test CanAddNodes</seealso>
        /// </summary>
        [Fact]
        public void CanAddRemoveNode()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode nodeA = builder.CreateBitcoinCoreNode().Start();
                CoreNode nodeB = builder.CreateBitcoinCoreNode().Start();

                RPCClient rpc = nodeA.CreateRPCClient();
                rpc.RemoveNodeAsync(nodeA.Endpoint);
                rpc.AddNode(nodeB.Endpoint);

                AddedNodeInfo[] info = null;
                TestBase.WaitLoop(() =>
                {
                    info = rpc.GetAddedNodeInfo(true);
                    return info != null && info.Length > 0;
                });
                Assert.NotNull(info);
                Assert.NotEmpty(info);

                //For some reason this one does not pass anymore in 0.13.1
                //Assert.Equal(nodeB.Endpoint, info.First().Addresses.First().Address);
                AddedNodeInfo oneInfo = rpc.GetAddedNodeInfo(true, nodeB.Endpoint);
                Assert.NotNull(oneInfo);
                Assert.Equal(nodeB.Endpoint.ToString(), oneInfo.AddedNode.ToString());
                oneInfo = rpc.GetAddedNodeInfo(true, nodeA.Endpoint);
                Assert.Null(oneInfo);
                rpc.RemoveNode(nodeB.Endpoint);

                TestBase.WaitLoop(() =>
                {
                    info = rpc.GetAddedNodeInfo(true);
                    return info.Length == 0;
                });

                Assert.Empty(info);
            }
        }

        [Fact]
        public void CanSendCommand()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                RPCResponse response = rpcClient.SendCommand(RPCOperations.getinfo);
                Assert.NotNull(response.Result);

                CoreNode sfn = builder.CreateXelsPowNode(this.regTest).Start();

                RPCClient sfnRpc = sfn.CreateRPCClient();

                response = sfnRpc.SendCommand(RPCOperations.getinfo);
                Assert.NotNull(response.Result);
            }
        }

        [Fact]
        public void CanGetGenesisFromRPC()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                RPCResponse response = rpcClient.SendCommand(RPCOperations.getblockhash, 0);
                string actualGenesis = (string)response.Result;
                Assert.Equal(this.regTest.GetGenesis().GetHash().ToString(), actualGenesis);
                Assert.Equal(this.regTest.GetGenesis().GetHash(), rpcClient.GetBestBlockHash());

                CoreNode sfn = builder.CreateXelsPowNode(this.regTest).Start();
                TestHelper.ConnectAndSync(node, sfn);

                rpcClient = sfn.CreateRPCClient();

                response = rpcClient.SendCommand(RPCOperations.getblockhash, 0);
                actualGenesis = (string)response.Result;
                Assert.Equal(this.regTest.GetGenesis().GetHash().ToString(), actualGenesis);
                Assert.Equal(this.regTest.GetGenesis().GetHash(), rpcClient.GetBestBlockHash());
            }
        }

        [Fact]
        public void CanSignRawTransaction()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: "0.18.0", useNewConfigStyle: true).Start();

                CoreNode sfn = builder.CreateXelsPowNode(this.regTest).WithWallet().Start();

                TestHelper.ConnectAndSync(node, sfn);

                RPCClient rpcClient = node.CreateRPCClient();
                RPCClient sfnRpc = sfn.CreateRPCClient();

                // Need one block per node so they can each fund a transaction.
                rpcClient.Generate(1);

                TestHelper.ConnectAndSync(node, sfn);

                sfnRpc.Generate(1);

                TestHelper.ConnectAndSync(node, sfn);
                
                // And then enough blocks mined on top for the coinbases to mature.
                rpcClient.Generate(101);

                TestHelper.ConnectAndSync(node, sfn);

                var tx = new Transaction();
                tx.Outputs.Add(new TxOut(Money.Coins(1.0m), new Key()));
                FundRawTransactionResponse funded = rpcClient.FundRawTransaction(tx);

                // signrawtransaction was removed in 0.18. So just use its equivalent so that we can test SFN's ability to call signrawtransaction.
                RPCResponse response = rpcClient.SendCommand("signrawtransactionwithwallet", tx.ToHex());
                
                Assert.NotNull(response.Result["hex"]);

                sfnRpc.WalletPassphrase(sfn.WalletPassword, 60);

                tx = new Transaction();
                tx.Outputs.Add(new TxOut(Money.Coins(1.0m), new Key()));
                funded = sfnRpc.FundRawTransaction(tx);
                Transaction signed = sfnRpc.SignRawTransaction(funded.Transaction);
                rpcClient.SendRawTransaction(signed);
            }
        }

        [Fact]
        public void CanGenerateToAddress()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                var privateKey = new Key();

                uint256[] blockHash = rpcClient.GenerateToAddress(1, privateKey.ScriptPubKey.GetDestinationAddress(rpcClient.Network));
                Block block = rpcClient.GetBlock(blockHash[0]);

                Assert.Equal(privateKey.ScriptPubKey, block.Transactions[0].Outputs[0].ScriptPubKey);
            }
        }

        [Fact]
        public void CanGetBlockFromRPC()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                BlockHeader response = rpcClient.GetBlockHeader(0);

                Assert.Equal(this.regTest.GetGenesis().Header.ToBytes(), response.ToBytes());
                Assert.Equal(this.regTest.GenesisHash, response.GetHash());

                CoreNode sfn = builder.CreateXelsPowNode(this.regTest).Start();

                RPCClient sfnRpc = sfn.CreateRPCClient();

                response = sfnRpc.GetBlockHeader(0);

                Assert.Equal(this.regTest.GetGenesis().Header.ToBytes(), response.ToBytes());
                Assert.Equal(this.regTest.GenesisHash, response.GetHash());
            }
        }

        [Fact]
        public void TryValidateAddress()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                BitcoinAddress pkh = rpcClient.GetNewAddress();
                Assert.True(rpcClient.ValidateAddress(pkh).IsValid);

                CoreNode sfn = builder.CreateXelsPowNode(this.regTest).Start();

                Assert.True(sfn.CreateRPCClient().ValidateAddress(pkh).IsValid);
            }
        }

        [Fact]
        public void TryEstimateFeeRate()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                Assert.Null(rpcClient.TryEstimateFeeRate(1));
            }
        }

        [Fact]
        public void CanGetTxOutNoneFromRPC()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: "0.18.0", useNewConfigStyle: true).Start();
                CoreNode sfn = builder.CreateXelsPowNode(this.regTest).Start();

                TestHelper.ConnectAndSync(node, sfn);

                RPCClient rpcClient = node.CreateRPCClient();
                RPCClient sfnRpc = sfn.CreateRPCClient();

                uint256 txid = rpcClient.Generate(1).Single();
                UnspentTransaction resultTxOut = rpcClient.GetTxOut(txid, 0, true);
                Assert.Null(resultTxOut);

                TestHelper.ConnectAndSync(node, sfn);

                resultTxOut = sfnRpc.GetTxOut(txid, 0, true);
                Assert.Null(resultTxOut);
            }
        }

        [Fact]
        public void CanGetTransactionBlockFromRPC()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                uint256 blockId = rpcClient.GetBestBlockHash();
                Block block = rpcClient.GetBlock(blockId);
                Assert.True(block.CheckMerkleRoot());
            }
        }

        [Fact]
        public void CanGetRawTransaction()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: "0.18.0", useNewConfigStyle: true).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                rpcClient.Generate(101);

                CoreNode sfn = builder.CreateXelsPowNode(this.regTest);
                sfn.Start();
                TestHelper.ConnectAndSync(node, sfn);

                uint256 txid = rpcClient.SendToAddress(new Key().PubKey.GetAddress(rpcClient.Network), Money.Coins(1.0m));

                TestBase.WaitLoop(() => node.CreateRPCClient().GetRawMempool().Length == 1);

                Transaction tx = rpcClient.GetRawTransaction(txid);

                RPCClient sfnRpc = sfn.CreateRPCClient();

                TestBase.WaitLoop(() => sfnRpc.GetRawMempool().Length == 1);

                Transaction tx2 = sfnRpc.GetRawTransaction(txid);

                Assert.Equal(tx.ToHex(), tx2.ToHex());
            }
        }

        [Fact]
        public void RawTransactionIsConformsToRPC()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                Transaction tx = this.testNet.GetGenesis().Transactions[0];
                Transaction tx2 = rpcClient.DecodeRawTransaction(tx.ToBytes());

                Assert.True(JToken.DeepEquals(tx.ToString(this.testNet, RawFormat.Satoshi), tx2.ToString(this.testNet, RawFormat.Satoshi)));
            }
        }

        [Fact]
        public void CanUseBatchedRequests()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                uint256[] blocks = rpcClient.Generate(10);
                Assert.Throws<InvalidOperationException>(() => rpcClient.SendBatch());
                rpcClient = rpcClient.PrepareBatch();
                var requests = new List<Task<uint256>>();
                for (int i = 1; i < 11; i++)
                {
                    requests.Add(rpcClient.GetBlockHashAsync(i));
                }
                Thread.Sleep(1000);
                foreach (Task<uint256> req in requests)
                {
                    Assert.Equal(TaskStatus.WaitingForActivation, req.Status);
                }
                rpcClient.SendBatch();
                rpcClient = rpcClient.PrepareBatch();
                int blockIndex = 0;
                foreach (Task<uint256> req in requests)
                {
                    Assert.Equal(blocks[blockIndex], req.Result);
                    Assert.Equal(TaskStatus.RanToCompletion, req.Status);
                    blockIndex++;
                }
                requests.Clear();

                requests.Add(rpcClient.GetBlockHashAsync(10));
                requests.Add(rpcClient.GetBlockHashAsync(11));
                requests.Add(rpcClient.GetBlockHashAsync(9));
                requests.Add(rpcClient.GetBlockHashAsync(8));
                rpcClient.SendBatch();
                rpcClient = rpcClient.PrepareBatch();
                Assert.Equal(TaskStatus.RanToCompletion, requests[0].Status);
                Assert.Equal(TaskStatus.Faulted, requests[1].Status);
                Assert.Equal(TaskStatus.RanToCompletion, requests[2].Status);
                Assert.Equal(TaskStatus.RanToCompletion, requests[3].Status);
                requests.Clear();

                requests.Add(rpcClient.GetBlockHashAsync(10));
                requests.Add(rpcClient.GetBlockHashAsync(11));
                rpcClient.CancelBatch();
                rpcClient = rpcClient.PrepareBatch();
                Thread.Sleep(100);
                Assert.Equal(TaskStatus.Canceled, requests[0].Status);
                Assert.Equal(TaskStatus.Canceled, requests[1].Status);
            }
        }

        [Fact]
        public void CanBackupWallet()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                string buildOutputDir = Path.GetDirectoryName(".");
                string filePath = Path.Combine(buildOutputDir, "wallet_backup.dat");
                try
                {
                    rpcClient.BackupWallet(filePath);
                    Assert.True(File.Exists(filePath));
                }
                finally
                {
                    if (File.Exists(filePath))
                        File.Delete(filePath);
                }
            }
        }

        [Fact]
        public void CanGetPrivateKeysFromAccount()
        {
            string accountName = "account";
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                var key = new Key();
                rpcClient.ImportAddress(key.PubKey.GetAddress(this.regTest), accountName, false);
                BitcoinAddress address = rpcClient.GetAccountAddress(accountName);
                BitcoinSecret secret = rpcClient.DumpPrivKey(address);
                BitcoinSecret secret2 = rpcClient.GetAccountSecret(accountName);

                Assert.Equal(secret.ToString(), secret2.ToString());
                Assert.Equal(address.ToString(), secret.GetAddress().ToString());
            }
        }

        [Fact]
        public async Task CanGetPrivateKeysFromLockedAccountAsync()
        {
            string accountName = "account";
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                var key = new Key();
                string passphrase = "password1234";
                rpcClient.SendCommand(RPCOperations.encryptwallet, passphrase);

                // Wait for recepient to process the command.
                await Task.Delay(300);

                builder.Nodes[0].Restart();
                rpcClient = node.CreateRPCClient();
                rpcClient.ImportAddress(key.PubKey.GetAddress(this.regTest), accountName, false);
                BitcoinAddress address = rpcClient.GetAccountAddress(accountName);
                rpcClient.WalletPassphrase(passphrase, 60);
                BitcoinSecret secret = rpcClient.DumpPrivKey(address);
                BitcoinSecret secret2 = rpcClient.GetAccountSecret(accountName);

                Assert.Equal(secret.ToString(), secret2.ToString());
                Assert.Equal(address.ToString(), secret.GetAddress().ToString());
            }
        }

        [Fact]
        public void CanAuthWithCookieFile()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateBitcoinCoreNode(version: BitcoinCoreVersion15, useCookieAuth: true).Start();

                RPCClient rpcClient = node.CreateRPCClient();
                rpcClient.GetBlockCount();
                node.Restart();
                rpcClient = node.CreateRPCClient();
                rpcClient.GetBlockCount();

                string invalidCookiePath = Path.Combine("Data", "invalid.cookie");
                string notFoundCookiePath = Path.Combine("Data", "not_found.cookie");
                Assert.Throws<ArgumentException>(() => new RPCClient($"cookiefile={invalidCookiePath}", new Uri("http://localhost/"), this.regTest));
                Assert.Throws<FileNotFoundException>(() => new RPCClient($"cookiefile={notFoundCookiePath}", new Uri("http://localhost/"), this.regTest));

                var uri = new Uri("http://127.0.0.1:" + this.regTest.DefaultRPCPort + "/");
                rpcClient = new RPCClient("bla:bla", uri, this.regTest);
                Assert.Equal(uri.OriginalString, rpcClient.Address.AbsoluteUri);

                rpcClient = node.CreateRPCClient();
                rpcClient = rpcClient.PrepareBatch();
                Task<int> blockCountAsync = rpcClient.GetBlockCountAsync();
                rpcClient.SendBatch();
                int blockCount = blockCountAsync.GetAwaiter().GetResult();

                node.Restart();

                rpcClient = rpcClient.PrepareBatch();
                blockCountAsync = rpcClient.GetBlockCountAsync();
                rpcClient.SendBatch();
                blockCount = blockCountAsync.GetAwaiter().GetResult();

                rpcClient = new RPCClient("bla:bla", new Uri("http://toto/"), this.regTest);
            }
        }
    }
}