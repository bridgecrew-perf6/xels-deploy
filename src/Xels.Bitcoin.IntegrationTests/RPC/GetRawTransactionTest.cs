﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Xels.Bitcoin.Controllers.Models;
using Xels.Bitcoin.Features.BlockStore.Models;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.IntegrationTests.Common.ReadyData;
using Xels.Bitcoin.Networks;
using Xunit;

namespace Xels.Bitcoin.IntegrationTests.RPC
{
    public class GetRawTransactionTest
    {
        private readonly Network network;

        public GetRawTransactionTest()
        {
            this.network = new StraxRegTest();
        }

        [Fact]
        public void GetRawTransactionDoesntExistInMempool()
        {
            string txId = "7922666d8f88e3af37cfc88ff410da82f02de913a75891258a808c387ebdee54";

            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                RPCClient rpc = node.CreateRPCClient();

                Func<Task> getTransaction = async () => { await rpc.SendCommandAsync(RPCOperations.getrawtransaction, txId); };
                RPCException exception = getTransaction.Should().Throw<RPCException>().Which;
                exception.RPCCode.Should().Be(RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY);
                exception.Message.Should().Be("No such mempool transaction. Use -txindex to enable blockchain transaction queries.");
            }
        }

        [Fact]
        public async Task GetRawTransactionDoesntExistInBlockAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                RPCClient rpc = node.CreateRPCClient();

                Func<Task> getTransaction = async () => { await rpc.SendCommandAsync(RPCOperations.getrawtransaction, uint256.Zero.ToString(), false, lastBlockHash); };
                RPCException exception = getTransaction.Should().Throw<RPCException>().Which;
                exception.RPCCode.Should().Be(RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY);
                exception.Message.Should().Be("No such transaction found in the provided block.");
            }
        }

        [Fact]
        public void GetRawTransactionWhenBlockNotFound()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Act.
                RPCClient rpc = node.CreateRPCClient();
                Func<Task> getTransaction = async () => { await rpc.SendCommandAsync(RPCOperations.getrawtransaction, uint256.Zero.ToString(), false, uint256.Zero.ToString()); };

                // Assert.
                RPCException exception = getTransaction.Should().Throw<RPCException>().Which;
                exception.RPCCode.Should().Be(RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY);
                exception.Message.Should().Be("Block hash not found.");
            }
        }

        [Fact]
        public async Task GetRawTransactionWithGenesisTransactionAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the genesis block.
                BlockModel block = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("blockstore/block")
                    .SetQueryParams(new { hash = "0x77283cca51b83fe3bda9ce8966248613036b0dc55a707ce76ca7b79aaa9962e4", outputJson = true })
                    .GetJsonAsync<BlockModel>();

                RPCClient rpc = node.CreateRPCClient();

                Func<Task> getTransaction = async () => { await rpc.SendCommandAsync(RPCOperations.getrawtransaction, block.MerkleRoot, false, block.Hash); };
                RPCException exception = getTransaction.Should().Throw<RPCException>().Which;
                exception.RPCCode.Should().Be(RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY);
                exception.Message.Should().Be("The genesis block coinbase is not considered an ordinary transaction and cannot be retrieved.");
            }
        }

        [Fact]
        public async Task GetRawTransactionWithTransactionIndexedInBlockchainVerboseAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                BlockModel tip = await $"http://localhost:{node.ApiPort}/api"
                                .AppendPathSegment("blockstore/block")
                                .SetQueryParams(new { hash = lastBlockHash, outputJson = true })
                                .GetJsonAsync<BlockModel>();

                // Act.
                RPCClient rpc = node.CreateRPCClient();
                RPCResponse response = await rpc.SendCommandAsync(RPCOperations.getrawtransaction, tip.Transactions.First(), true);

                // Assert.
                TransactionVerboseModel transaction = response.Result.ToObject<TransactionVerboseModel>();
                transaction.TxId.Should().Be((string)tip.Transactions.First());
                transaction.VOut.First().ScriptPubKey.Addresses.Count.Should().Be(1);
            }
        }

        [Fact]
        public async Task GetRawTransactionWithTransactionIndexedInBlockchainNotVerboseAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                BlockTransactionDetailsModel tip = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("blockstore/block")
                    .SetQueryParams(new { hash = lastBlockHash, outputJson = true, showtransactiondetails = true })
                    .GetJsonAsync<BlockTransactionDetailsModel>();

                // Act.
                RPCClient rpc = node.CreateRPCClient();
                RPCResponse response = await rpc.SendCommandAsync(RPCOperations.getrawtransaction, tip.Transactions.First().TxId);

                // Assert.
                response.ResultString.Should().Be(tip.Transactions.First().Hex);
            }
        }

        [Fact]
        public async Task GetRawTransactionWithTransactionInMempoolAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                //Arrange.
                // Create a sending and a receiving node.
                CoreNode sendingNode = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                CoreNode receivingNode = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Listener).Start();

                TestHelper.ConnectAndSync(sendingNode, receivingNode);

                // Get an address to send to.
                IEnumerable<string> unusedaddresses = await $"http://localhost:{receivingNode.ApiPort}/api"
                    .AppendPathSegment("wallet/unusedAddresses")
                    .SetQueryParams(new { walletName = "mywallet", accountName = "account 0", count = 1 })
                    .GetJsonAsync<IEnumerable<string>>();

                // Build and send a transaction.
                WalletBuildTransactionModel buildTransactionModel = await $"http://localhost:{sendingNode.ApiPort}/api"
                    .AppendPathSegment("wallet/build-transaction")
                    .PostJsonAsync(new BuildTransactionRequest
                    {
                        WalletName = "mywallet",
                        AccountName = "account 0",
                        FeeType = "low",
                        Password = "password",
                        ShuffleOutputs = true,
                        AllowUnconfirmed = true,
                        Recipients = unusedaddresses.Select(address => new RecipientModel
                        {
                            DestinationAddress = address,
                            Amount = "1"
                        }).ToList(),
                    })
                    .ReceiveJson<WalletBuildTransactionModel>();

                await $"http://localhost:{sendingNode.ApiPort}/api"
                    .AppendPathSegment("wallet/send-transaction")
                    .PostJsonAsync(new SendTransactionRequest
                    {
                        Hex = buildTransactionModel.Hex
                    })
                    .ReceiveJson<WalletSendTransactionModel>();

                uint256 txId = buildTransactionModel.TransactionId;

                // Act.
                RPCClient rpc = sendingNode.CreateRPCClient();
                RPCResponse response = await rpc.SendCommandAsync(RPCOperations.getrawtransaction, txId.ToString());

                // Assert.
                response.ResultString.Should().Be(buildTransactionModel.Hex);
            }
        }

        [Fact]
        public async Task GetRawTransactionWithBlockHashVerboseAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                BlockModel tip = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("blockstore/block")
                    .SetQueryParams(new { hash = lastBlockHash, outputJson = true })
                    .GetJsonAsync<BlockModel>();

                // Act.
                RPCClient rpc = node.CreateRPCClient();
                RPCResponse response = await rpc.SendCommandAsync(RPCOperations.getrawtransaction, tip.Transactions.First(), true, tip.Hash);

                // Assert.
                TransactionVerboseModel transaction = response.Result.ToObject<TransactionVerboseModel>();
                transaction.TxId.Should().Be((string)tip.Transactions.First());
            }
        }

        [Fact]
        public async Task GetRawTransactionWithBlockHashNonVerboseAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                BlockTransactionDetailsModel tip = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("blockstore/block")
                    .SetQueryParams(new { hash = lastBlockHash, outputJson = true, showtransactiondetails = true })
                    .GetJsonAsync<BlockTransactionDetailsModel>();

                // Act.
                RPCClient rpc = node.CreateRPCClient();
                RPCResponse response = await rpc.SendCommandAsync(RPCOperations.getrawtransaction, tip.Transactions.First().TxId, false, tip.Hash);

                // Assert.
                response.ResultString.Should().Be(tip.Transactions.First().Hex);
            }
        }

        [Fact]
        public async Task GetRawTransactionWithTransactionAndBlockHashInBlockchainAndNotIndexedAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var configParameters = new NodeConfigParameters { { "txindex", "0" } };

                CoreNode node = builder.CreateXelsCustomPowNode(new BitcoinRegTest(), configParameters).WithWallet().Start();
                TestHelper.MineBlocks(node, 5);

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                BlockTransactionDetailsModel tip = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("blockstore/block")
                    .SetQueryParams(new { hash = lastBlockHash, outputJson = true, showtransactiondetails = true })
                    .GetJsonAsync<BlockTransactionDetailsModel>();

                string txId = tip.Transactions.First().TxId;

                RPCClient rpc = node.CreateRPCClient();
                Func<Task> getTransaction = async () => { await rpc.SendCommandAsync(RPCOperations.getrawtransaction, txId); };
                RPCException exception = getTransaction.Should().Throw<RPCException>().Which;
                exception.RPCCode.Should().Be(RPCErrorCode.RPC_INVALID_ADDRESS_OR_KEY);
                exception.Message.Should().Be("No such mempool transaction. Use -txindex to enable blockchain transaction queries.");
            }
        }

        [Fact]
        public async Task GetRawTransactionWithNonZeroIntegerParameterReperesenting()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                BlockModel tip = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("blockstore/block")
                    .SetQueryParams(new { hash = lastBlockHash, outputJson = true })
                    .GetJsonAsync<BlockModel>();

                // Act.
                RPCClient rpc = node.CreateRPCClient();
                RPCResponse response = await rpc.SendCommandAsync(RPCOperations.getrawtransaction, tip.Transactions.First(), 1);

                // Assert.
                TransactionVerboseModel transaction = response.Result.ToObject<TransactionVerboseModel>();
                transaction.TxId.Should().Be((string)tip.Transactions.First());
                transaction.VOut.First().ScriptPubKey.Addresses.Count.Should().Be(1);
            }
        }

        [Fact]
        public async Task GetRawTransactionWithZeroIntegerParameterReperesenting()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                // Arrange.
                CoreNode node = builder.CreateXelsPosNode(this.network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();

                // Get the last block we have.
                string lastBlockHash = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("consensus/getbestblockhash")
                    .GetJsonAsync<string>();

                BlockTransactionDetailsModel tip = await $"http://localhost:{node.ApiPort}/api"
                    .AppendPathSegment("blockstore/block")
                    .SetQueryParams(new { hash = lastBlockHash, outputJson = true, showtransactiondetails = true })
                    .GetJsonAsync<BlockTransactionDetailsModel>();

                // Act.
                RPCClient rpc = node.CreateRPCClient();
                RPCResponse response = await rpc.SendCommandAsync(RPCOperations.getrawtransaction, tip.Transactions.First().TxId, 0, tip.Hash);

                // Assert.
                response.ResultString.Should().Be(tip.Transactions.First().Hex);
            }
        }
    }
}