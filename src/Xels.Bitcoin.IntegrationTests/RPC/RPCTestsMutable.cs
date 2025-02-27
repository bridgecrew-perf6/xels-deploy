﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using NBitcoin;
using NBitcoin.DataEncoders;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.IntegrationTests.Common.ReadyData;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace Xels.Bitcoin.IntegrationTests.RPC
{
    /// <summary>
    /// RPC Integration tests that require their own node(s) for each test because they change node state.
    /// </summary>
    public class RPCTestsMutable
    {
        [Fact]
        public void TestRpcGetBalanceIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var network = new BitcoinRegTest();
                CoreNode node = builder.CreateXelsPowNode(network).WithWallet().Start();

                RPCClient rpcClient = node.CreateRPCClient();

                int maturity = (int)network.Consensus.CoinbaseMaturity;

                TestHelper.MineBlocks(node, maturity);
                Assert.Equal(Money.Zero, rpcClient.GetBalance()); // test with defaults.

                TestHelper.MineBlocks(node, 1);
                Assert.Equal(Money.Coins(50), rpcClient.GetBalance(0, false)); // test with parameters.
            }
        }

        [Fact]
        public void TestRpcGetTransactionIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateXelsPowNode(new BitcoinRegTest()).AlwaysFlushBlocks().WithWallet().Start();

                RPCClient rpc = node.CreateRPCClient();
                uint256 blockHash = rpc.Generate(1)[0];
                Block block = rpc.GetBlock(blockHash);

                TestBase.WaitLoop(() => TestHelper.IsNodeSynced(node));

                RPCResponse walletTx = rpc.SendCommand(RPCOperations.gettransaction, block.Transactions[0].GetHash().ToString());
                walletTx.ThrowIfError();
            }
        }

        [Fact]
        public void TestRpcImportPubkeyIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateXelsPowNode(new BitcoinRegTest()).AlwaysFlushBlocks().WithWallet().Start();
                CoreNode node2 = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest10Miner).Start();

                TestHelper.ConnectAndSync(node, node2);

                UnspentOutputReference tx = node2.FullNode.WalletManager().GetUnspentTransactionsInWallet("mywallet", 0, Features.Wallet.Wallet.NormalAccounts).First();
                
                RPCClient rpc = node.CreateRPCClient();

                PubKey pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(tx.Address.Pubkey);
                PubKey pubKey2 = new Key().PubKey;

                uint256 blockHash = rpc.GenerateToAddress(1, pubKey2.GetAddress(rpc.Network)).First();
                Block block = rpc.GetBlock(blockHash);
                uint256 tx2 = block.Transactions.First().GetHash();

                Assert.Throws<RPCException>(() => rpc.SendCommand(RPCOperations.gettransaction, tx.Transaction.Id.ToString(), true));
                Assert.Throws<RPCException>(() => rpc.SendCommand(RPCOperations.gettransaction, tx2.ToString(), true));;

                // Test that adding the same pubkey twice doesn't throw.
                rpc.ImportPubKey(pubKey.ToHex());
                rpc.ImportPubKey(pubKey.ToHex());

                // Add a second pubkey and ensure it doesn't throw.
                rpc.ImportPubKey(pubKey2.ToHex());

                // Add an arbitrary pubkey and ensure it doesn't throw.
                rpc.ImportPubKey(new Key().PubKey.ToHex());
                
                TestBase.WaitLoop(() => node.FullNode.WalletManager().WalletTipHeight == node2.FullNode.WalletManager().WalletTipHeight);

                TestBase.WaitLoop(() =>
                {
                    try
                    {
                        // Check if gettransaction can now find the transactions in the watch only account.
                        RPCResponse walletTx = rpc.SendCommand(RPCOperations.gettransaction, tx.Transaction.Id.ToString(), true);
                        RPCResponse walletTx2 = rpc.SendCommand(RPCOperations.gettransaction, tx2.ToString(), true);

                        return walletTx != null && walletTx2 != null;
                    }
                    catch (RPCException)
                    {
                        return false;
                    }
                });

                // Check that when include_watchonly is not set, the watched addresses' transactions cannot be located in the normal wallet accounts.
                Assert.Throws<RPCException>(() => rpc.SendCommand(RPCOperations.gettransaction, tx.Transaction.Id.ToString(), false));
                Assert.Throws<RPCException>(() => rpc.SendCommand(RPCOperations.gettransaction, tx2.ToString(), false));
            }
        }

        [Fact]
        public void TestRpcListUnspentForWatchOnlyIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                CoreNode node = builder.CreateXelsPowNode(new BitcoinRegTest()).AlwaysFlushBlocks().WithWallet().Start();
                CoreNode node2 = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest10Miner).Start();

                TestHelper.ConnectAndSync(node, node2);

                UnspentOutputReference tx = node2.FullNode.WalletManager().GetUnspentTransactionsInWallet("mywallet", 0, Features.Wallet.Wallet.NormalAccounts).First();

                RPCClient rpc = node.CreateRPCClient();

                PubKey pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(tx.Address.Pubkey);

                Assert.Throws<RPCException>(() => rpc.SendCommand(RPCOperations.gettransaction, tx.Transaction.Id.ToString(), true));

                rpc.ImportPubKey(pubKey.ToHex());

                // ListUnspent will not regard the outputs as spendable if they are not sufficiently mature.
                rpc.Generate((int)node.FullNode.Network.Consensus.CoinbaseMaturity);

                TestBase.WaitLoop(() => node.FullNode.WalletManager().WalletTipHeight == node2.FullNode.WalletManager().WalletTipHeight);

                TestBase.WaitLoop(() =>
                {
                    try
                    {
                        // Wait until gettransaction can find the transaction in the watch only account.
                        RPCResponse walletTx = rpc.SendCommand(RPCOperations.gettransaction, tx.Transaction.Id.ToString(), true);

                        return walletTx != null;
                    }
                    catch (RPCException)
                    {
                        return false;
                    }
                });

                UnspentCoin[] unspent = rpc.ListUnspent(1, 9999999);

                bool found = false;

                foreach (UnspentCoin coin in unspent)
                {
                    if (coin.OutPoint == tx.ToOutPoint())
                        found = true;

                    Assert.Equal(coin.Account, Features.Wallet.Wallet.WatchOnlyAccountName);
                }

                Assert.True(found);
            }
        }

        [Fact]
        public void TestRpcGetBlockWithValidHashIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest100Miner).Start();

                RPCClient rpcClient = node.CreateRPCClient();
                uint256 blockId = rpcClient.GetBestBlockHash();
                Block block = rpcClient.GetBlock(blockId);
                Assert.True(block.CheckMerkleRoot());
            }
        }

        [Fact]
        public void TestRpcListUnspentWithDefaultsIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();

                RPCClient rpcClient = node.CreateRPCClient();
                var coins = rpcClient.ListUnspent();
                coins.Should().NotBeNull();

                // Maturity is 100 and we have 150 coins in the current chain.
                coins.Length.Should().Be(50);
            }
        }

        [Fact]
        public void TestRpcListUnspentWithParametersIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(network).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();
                var minerAddress = BitcoinAddress.Create(ReadyBlockchain.BitcoinRegTestMinerAddress, network);

                RPCClient rpcClient = node.CreateRPCClient();

                // Validate existing address and minconf.
                var coins = rpcClient.ListUnspent((int)network.Consensus.CoinbaseMaturity + 2, 99999, minerAddress);
                coins.Should().NotBeNull();

                // Maturity is 100 and we have 150 coins in the current chain.
                coins.Length.Should().Be(50 - 1);

                // Validate unknown address.
                var unknownAddress = new Key().GetBitcoinSecret(network).GetAddress();
                coins = rpcClient.ListUnspent(1, 99999, unknownAddress);
                coins.Should().NotBeNull();
                coins.Should().BeEmpty();

                // Test just min conf.
                var response = rpcClient.SendCommand(RPCOperations.listunspent, (int)network.Consensus.CoinbaseMaturity + 2);
                var result = response.ResultString;
                result.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void TestRpcSendToAddressIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(network).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                var alice = new Key().GetBitcoinSecret(network);
                var aliceAddress = alice.GetAddress();
                rpcClient.WalletPassphrase("password", 60);
                var txid = rpcClient.SendToAddress(aliceAddress, Money.Coins(1.0m));
                rpcClient.SendCommand(RPCOperations.walletlock);

                // Check the hash calculated correctly.
                var tx = rpcClient.GetRawTransaction(txid);
                Assert.Equal(txid, tx.GetHash());

                // Check the output is the right amount.
                var coin = tx.Outputs.AsCoins().First(c => c.ScriptPubKey == aliceAddress.ScriptPubKey);
                Assert.Equal(coin.Amount, Money.Coins(1.0m));
            }
        }

        [Fact]
        public void TestRpcSendToAddressCantSpendWhenLocked()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(network).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();
                var aliceAddress = new Key().GetBitcoinSecret(network).GetAddress();

                RPCClient rpcClient = node.CreateRPCClient();

                // Not unlocked case.
                Action action = () => rpcClient.SendToAddress(aliceAddress, Money.Coins(1.0m));
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED);

                // Unlock and lock case.
                rpcClient.WalletPassphrase("password", 60);
                rpcClient.SendCommand(RPCOperations.walletlock);
                action = () => rpcClient.SendToAddress(aliceAddress, Money.Coins(1.0m));
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED);

                // Unlock timesout case.
                rpcClient.WalletPassphrase("password", 5);
                Thread.Sleep(10 * 1000); // 10 seconds.
                action = () => rpcClient.SendToAddress(aliceAddress, Money.Coins(1.0m));
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED);
            }
        }

        [Fact]
        public void TestRpcSendManyWithLockedWalletFails()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();

                RPCClient rpcClient = node.CreateRPCClient();

                var addresses = new Dictionary<string, decimal>
                {
                    { new Key().GetBitcoinSecret(network).GetAddress().ToString(), 1.0m },
                    { new Key().GetBitcoinSecret(network).GetAddress().ToString(), 2.0m }
                };

                var addressesJson = JsonConvert.SerializeObject(addresses);
                Action action = () => rpcClient.SendCommand(RPCOperations.sendmany, string.Empty, addressesJson);
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED);
            }
        }

        [Fact]
        public void TestRpcSendManyWithInvalidParametersFails()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(network).WithWallet().Start();

                RPCClient rpcClient = node.CreateRPCClient();
                rpcClient.WalletPassphrase("password", 60);

                // Test with empty list of addresses
                var addresses = new Dictionary<string, decimal>();
                var addressesJson = JsonConvert.SerializeObject(addresses);
                Action action = () => rpcClient.SendCommand(RPCOperations.sendmany, string.Empty, addressesJson);
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_INVALID_PARAMETER);

                // Test with malformed Json.
                addressesJson = "[\"address\"";
                action = () => rpcClient.SendCommand(RPCOperations.sendmany, string.Empty, addressesJson);
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_PARSE_ERROR);
            }
        }

        [Fact]
        public void TestRpcSendManyWithValidDataIsSuccessful()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();

                var aliceAddress = new Key().GetBitcoinSecret(network).GetAddress();
                var bobAddress = new Key().GetBitcoinSecret(network).GetAddress();

                RPCClient rpcClient = node.CreateRPCClient();
                rpcClient.WalletPassphrase("password", 60);

                // Test with just defaults.
                const decimal coinsForAlice = 1.0m;
                const decimal coinsForBob = 2.0m;
                var addresses = new Dictionary<string, decimal>
                {
                    { aliceAddress.ToString(), coinsForAlice },
                    { bobAddress.ToString(), coinsForBob }
                };

                var addressesJson = JsonConvert.SerializeObject(addresses);
                var response = rpcClient.SendCommand(RPCOperations.sendmany, string.Empty, addressesJson);
                var txid = new uint256(response.ResultString);

                // Check the hash calculated correctly.
                var tx = rpcClient.GetRawTransaction(txid);
                txid.Should().Be(tx.GetHash());

                // Check the output is the right amount.
                var aliceCoins = tx.Outputs.AsCoins().First(c => c.ScriptPubKey == aliceAddress.ScriptPubKey);
                aliceCoins.Amount.Should().Be(Money.Coins(coinsForAlice));

                var bobCoins = tx.Outputs.AsCoins().First(c => c.ScriptPubKey == bobAddress.ScriptPubKey);
                bobCoins.Amount.Should().Be(Money.Coins(coinsForBob));

                // TODO: Enable test below once WalletTransactionHandler supports recepients with SubtractFeeFromAmount=true
                // Test option to subtract fees from outputs.
                //var subtractFeeAddresses = new[] { aliceAddress.ToString(), bobAddress.ToString() };
                //response = rpcClient.SendCommand(RPCOperations.sendmany, string.Empty, addressesJson, 0, string.Empty, subtractFeeAddresses);
                //txid = new uint256(response.ResultString);

                // Check the hash calculated correctly.
                //tx = rpcClient.GetRawTransaction(txid);
                //txid.Should().Be(tx.GetHash());

                // Check the output is the right amount.
                //aliceCoins = tx.Outputs.AsCoins().First(c => c.ScriptPubKey == aliceAddress.ScriptPubKey);
                //aliceCoins.Amount.Should().NotBeSameAs(Money.Coins(coinsForAlice));

                //bobCoins = tx.Outputs.AsCoins().First(c => c.ScriptPubKey == bobAddress.ScriptPubKey);
                //bobCoins.Amount.Should().NotBeSameAs(Money.Coins(coinsForBob));
            }
        }

        [Fact]
        public void TestRpcSendToAddressSpendRefreshesTimeout()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();
                var aliceAddress = new Key().GetBitcoinSecret(network).GetAddress();

                RPCClient rpcClient = node.CreateRPCClient();

                // Unlock for 20 seconds.
                rpcClient.WalletPassphrase("password", 20);

                // Send a transaction.
                rpcClient.SendToAddress(aliceAddress, Money.Coins(1.0m)).Should().NotBeNull();

                // Wait 10 seconds and then send another transaction, should still be unlocked.
                Thread.Sleep(10 * 1000);

                var bobAddress = new Key().GetBitcoinSecret(network).GetAddress();
                rpcClient.SendToAddress(bobAddress, Money.Coins(1.0m)).Should().NotBeNull();

                // Now wait 10 seconds so the wallet should be back locked and a transaction should fail.
                Thread.Sleep(10 * 1000);
                Action action = () => rpcClient.SendToAddress(aliceAddress, Money.Coins(1.0m));
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_WALLET_UNLOCK_NEEDED);
            }
        }

        [Fact]
        public void TestRpcSendToAddressInsufficientFunds()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();
                var aliceAddress = new Key().GetBitcoinSecret(network).GetAddress();

                RPCClient rpcClient = node.CreateRPCClient();

                rpcClient.WalletPassphrase("password", 30);
                Action action = () => rpcClient.SendToAddress(aliceAddress, Money.Coins(10000.0m));
                action.Should().Throw<RPCException>().Which.RPCCode.Should().Be(RPCErrorCode.RPC_WALLET_ERROR);
            }
        }

        [Fact]
        public void TestRpcGetBlockHeaderForBestBlockHashSuccessfull()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();

                RPCClient rpcClient = node.CreateRPCClient();
                var hash = rpcClient.GetBestBlockHash();

                // get hex representation of block header
                RPCResponse resp = rpcClient.SendCommand("getblockheader", hash.ToString(), false);

                // load header from hex representation
                var header = rpcClient.Network.Consensus.ConsensusFactory.CreateBlockHeader();
                var bytes = Encoders.Hex.DecodeData(resp.Result.Value<string>());
                header.FromBytes(bytes);

                // validate header has same hash as best block
                header.GetHash().Should().Be(hash);
            }
        }

        [Fact]
        public void TestRpcGetBlockchainInfo()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();
                RPCClient rpcClient = node.CreateRPCClient();
                var response = rpcClient.SendCommand(RPCOperations.getblockchaininfo);
                response.ResultString.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public void TestRpcGetNetworkInfo()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(new BitcoinRegTest()).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest150Miner).Start();
                RPCClient rpcClient = node.CreateRPCClient();
                var response = rpcClient.SendCommand(RPCOperations.getnetworkinfo);
                response.ResultString.Should().NotBeNullOrEmpty();
            }
        }

        [Fact]
        public async Task TestScanRPCCapabilitiesOnXelsNetworkAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new StraxRegTest();
                var node = builder.CreateXelsPosNode(network).WithReadyBlockchainData(ReadyBlockchain.StraxRegTest10Miner).Start();
                RPCClient rpcClient = node.CreateRPCClient();

                RPCCapabilities capabilities = await rpcClient.ScanRPCCapabilitiesAsync();

                capabilities.SupportEstimateSmartFee.Should().BeFalse();
                capabilities.SupportGetNetworkInfo.Should().BeTrue();
                capabilities.SupportScanUTXOSet.Should().BeFalse();
                capabilities.SupportSignRawTransactionWith.Should().BeFalse();
                capabilities.SupportSegwit.Should().BeTrue();
                capabilities.SupportGenerateToAddress.Should().BeTrue();
            }
        }

        [Fact]
        public async Task TestScanRPCCapabilitiesOnBitcoinNetworkAsync()
        {
            using (NodeBuilder builder = NodeBuilder.Create(this))
            {
                Network network = new BitcoinRegTest();
                var node = builder.CreateXelsPowNode(network).WithReadyBlockchainData(ReadyBlockchain.BitcoinRegTest10Miner).Start();
                RPCClient rpcClient = node.CreateRPCClient();

                RPCCapabilities capabilities = await rpcClient.ScanRPCCapabilitiesAsync();

                capabilities.SupportEstimateSmartFee.Should().BeFalse();
                capabilities.SupportGetNetworkInfo.Should().BeTrue();
                capabilities.SupportScanUTXOSet.Should().BeFalse();
                capabilities.SupportSignRawTransactionWith.Should().BeFalse();
                capabilities.SupportSegwit.Should().BeTrue();
                capabilities.SupportGenerateToAddress.Should().BeTrue();
            }
        }
    }
}
