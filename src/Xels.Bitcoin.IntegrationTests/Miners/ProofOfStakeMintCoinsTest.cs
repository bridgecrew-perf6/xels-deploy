﻿using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NBitcoin;
using Xels.Bitcoin.Features.Miner.Interfaces;
using Xels.Bitcoin.Features.Miner.Staking;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace Xels.Bitcoin.IntegrationTests.Miners
{
    public sealed class ProofOfStakeMintCoinsTest
    {
        private readonly HashSet<uint256> transactionsBeforeStaking = new HashSet<uint256>();
        private readonly ConcurrentDictionary<uint256, TransactionData> transactionLookup = new ConcurrentDictionary<uint256, TransactionData>();

        [Fact]
        public void Staking_Wallet_Can_Mint_New_Coins()
        {
            using (var builder = NodeBuilder.Create(this))
            {
                var configParameters = new NodeConfigParameters { { "txindex", "1" } };
                var network = new StraxRegTest();

                var minerA = builder.CreateXelsPosNode(network, "stake-1-minerA", configParameters: configParameters).OverrideDateTimeProvider().WithWallet().Start();

                var addressUsed = TestHelper.MineBlocks(minerA, (int)network.Consensus.PremineHeight).AddressUsed;

                //Since the pre - mine will not be immediately spendable, the transactions have to be counted directly from the address.
                addressUsed.Transactions.Count().Should().Be((int)network.Consensus.PremineHeight);

                addressUsed.Transactions.Sum(s => s.Amount).Should().Be(network.Consensus.PremineReward + network.Consensus.ProofOfWorkReward);

                // Mine blocks to maturity
                TestHelper.MineBlocks(minerA, (int)network.Consensus.CoinbaseMaturity + 10);

                // Get set of transaction IDs present in wallet before staking is started.
                this.transactionsBeforeStaking.Clear();
                foreach (TransactionData transactionData in this.GetTransactionsSnapshot(minerA))
                {
                    this.transactionsBeforeStaking.Add(transactionData.Id);
                }

                // Start staking on the node.
                var minter = minerA.FullNode.NodeService<IPosMinting>();
                minter.Stake(new List<WalletSecret>()
                {
                    new WalletSecret()
                    {
                        WalletName = "mywallet", WalletPassword = "password"
                    }
                });

                // If new transactions are appearing in the wallet, staking has been successful. Due to coin maturity settings the
                // spendable balance of the wallet actually drops after staking, so the wallet balance should not be used to
                // determine whether staking occurred.
                TestBase.WaitLoop(() =>
                {
                    List<TransactionData> transactions = this.GetTransactionsSnapshot(minerA);

                    foreach (TransactionData transactionData in transactions)
                    {
                        if (!this.transactionsBeforeStaking.Contains(transactionData.Id) && (transactionData.IsCoinStake ?? false))
                        {
                            return true;
                        }
                    }

                    return false;
                });

                // build a dictionary of coinstake tx's indexed by tx id.
                foreach (var tx in this.GetTransactionsSnapshot(minerA))
                {
                    this.transactionLookup[tx.Id] = tx;
                }

                TestBase.WaitLoop(() =>
                {
                    List<TransactionData> transactions = this.GetTransactionsSnapshot(minerA);

                    foreach (TransactionData transactionData in transactions)
                    {
                        if (!this.transactionsBeforeStaking.Contains(transactionData.Id) && (transactionData.IsCoinStake ?? false))
                        {
                            Transaction coinstakeTransaction = minerA.FullNode.BlockStore().GetTransactionById(transactionData.Id);
                            var balance = new Money(0);

                            // Add coinstake outputs to balance.
                            foreach (TxOut output in coinstakeTransaction.Outputs)
                            {
                                balance += output.Value;
                            }

                            //Subtract coinstake inputs from balance.
                            foreach (TxIn input in coinstakeTransaction.Inputs)
                            {
                                this.transactionLookup.TryGetValue(input.PrevOut.Hash, out TransactionData prevTransactionData);

                                if (prevTransactionData == null)
                                    continue;

                                Transaction prevTransaction = minerA.FullNode.BlockStore().GetTransactionById(prevTransactionData.Id);

                                balance -= prevTransaction.Outputs[input.PrevOut.N].Value;
                            }

                            Assert.Equal(minerA.FullNode.Network.Consensus.ProofOfStakeReward, balance);

                            return true;
                        }
                    }

                    return false;
                });
            }
        }

        /// <summary>
        /// Returns a snapshot of the current transactions by coin type in the first wallet.
        /// </summary>
        /// <param name="node">See <see cref="CoreNode"/>.</param>
        /// <returns>A list of TransactionData.</returns>
        private List<TransactionData> GetTransactionsSnapshot(CoreNode node)
        {
            // Enumerate to a list otherwise the enumerable can change during enumeration as new transactions are added to the wallet.
            return node.FullNode.WalletManager().Wallets.First().GetAllTransactions().ToList();
        }
    }
}