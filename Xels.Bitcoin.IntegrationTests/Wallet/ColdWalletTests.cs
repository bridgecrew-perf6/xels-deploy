using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.ColdStaking;
using Xels.Bitcoin.Features.ColdStaking.Controllers;
using Xels.Bitcoin.Features.ColdStaking.Models;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.Miner.Interfaces;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Controllers;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.IntegrationTests.Common.ReadyData;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common;
using Xels.Features.SQLiteWalletRepository;
using Xunit;

namespace Xels.Bitcoin.IntegrationTests.Wallet
{
    /// <summary>
    /// Contains integration tests for the cold wallet feature.
    /// </summary>
    public class ColdWalletTests
    {
        private const string Password = "password";
        private const string WalletName = "mywallet";
        private const string Account = "account 0";

        /// <summary>
        /// Creates the transaction build context.
        /// </summary>
        /// <param name="network">The network that the context is for.</param>
        /// <param name="accountReference">The wallet account providing the funds.</param>
        /// <param name="password">the wallet password.</param>
        /// <param name="destinationScript">The destination script where we are sending the funds to.</param>
        /// <param name="amount">the amount of money to send.</param>
        /// <param name="feeType">The fee type.</param>
        /// <param name="minConfirmations">The minimum number of confirmations.</param>
        /// <returns>The transaction build context.</returns>
        private static TransactionBuildContext CreateContext(Network network, WalletAccountReference accountReference, string password,
            Script destinationScript, Money amount, FeeType feeType, int minConfirmations)
        {
            return new TransactionBuildContext(network)
            {
                AccountReference = accountReference,
                MinConfirmations = minConfirmations,
                FeeType = feeType,
                WalletPassword = password,
                Recipients = new[] { new Recipient { Amount = amount, ScriptPubKey = destinationScript } }.ToList()
            };
        }

        /// <summary>
        /// Creates a cold staking node.
        /// </summary>
        /// <param name="nodeBuilder">The node builder that will be used to build the node.</param>
        /// <param name="network">The network that the node is being built for.</param>
        /// <param name="dataDir">The data directory used by the node.</param>
        /// <param name="coldStakeNode">Set to <c>false</c> to create a normal (non-cold-staking) node.</param>
        /// <returns>The created cold staking node.</returns>
        private CoreNode CreatePowPosMiningNode(NodeBuilder nodeBuilder, Network network, string dataDir, bool coldStakeNode)
        {
            var extraParams = new NodeConfigParameters { { "datadir", dataDir } };

            var buildAction = new Action<IFullNodeBuilder>(builder =>
            {
                builder.UseBlockStore()
                 .UsePosConsensus()
                 .UseMempool();

                if (coldStakeNode)
                {
                    builder.UseColdStakingWallet();
                }
                else
                {
                    builder.UseWallet();
                }

                builder
                 .AddSQLiteWalletRepository()
                 .AddPowPosMining(true)
                 .AddRPC()
                 .UseApi()
                 .UseTestChainedHeaderTree()
                 .MockIBD();
            });

            return nodeBuilder.CreateCustomNode(buildAction, network, ProtocolVersion.PROVEN_HEADER_VERSION, configParameters: extraParams);
        }

        /// <summary>
        /// Tests whether a cold stake can be minted.
        /// </summary>
        /// <description>
        /// Sends funds from mined by a sending node to the hot wallet node. The hot wallet node creates
        /// the cold staking setup using a cold staking address obtained from the cold wallet node.
        /// Success is determined by whether the balance in the cold wallet increases.
        /// </description>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        [Trait("Unstable", "True")]
        public async Task WalletCanMineWithColdWalletCoinsAsync()
        {
            using (var builder = NodeBuilder.Create(this))
            {
                var network = new StraxRegTest();

                CoreNode xelsSender = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: false);
                CoreNode xelsHotStake = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: true);
                CoreNode xelsColdStake = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: true);

                xelsSender.WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                xelsHotStake.WithWallet().Start();
                xelsColdStake.WithWallet().Start();

                var senderWalletManager = xelsSender.FullNode.WalletManager() as ColdStakingManager;
                var coldWalletManager = xelsColdStake.FullNode.WalletManager() as ColdStakingManager;
                var hotWalletManager = xelsHotStake.FullNode.WalletManager() as ColdStakingManager;

                // Set up cold staking account on cold wallet.
                coldWalletManager.GetOrCreateColdStakingAccount(WalletName, true, Password, null);
                HdAddress coldWalletAddress = coldWalletManager.GetFirstUnusedColdStakingAddress(WalletName, true);

                // Set up cold staking account on hot wallet.
                hotWalletManager.GetOrCreateColdStakingAccount(WalletName, false, Password, null);
                HdAddress hotWalletAddress = hotWalletManager.GetFirstUnusedColdStakingAddress(WalletName, false);

                var walletAccountReference = new WalletAccountReference(WalletName, Account);
                long total2 = xelsSender.FullNode.WalletManager().GetSpendableTransactionsInAccount(walletAccountReference, 1).Sum(s => s.Transaction.Amount);

                // Sync all nodes
                TestHelper.ConnectAndSync(xelsHotStake, xelsSender);
                TestHelper.ConnectAndSync(xelsHotStake, xelsColdStake);
                TestHelper.Connect(xelsSender, xelsColdStake);

                // Send coins to hot wallet.
                Money amountToSend = total2 - network.Consensus.ProofOfWorkReward;
                HdAddress sendto = hotWalletManager.GetUnusedAddress(new WalletAccountReference(WalletName, Account));

                Transaction transaction1 = xelsSender.FullNode.WalletTransactionHandler().BuildTransaction(CreateContext(xelsSender.FullNode.Network, new WalletAccountReference(WalletName, Account), Password, sendto.ScriptPubKey, amountToSend, FeeType.Medium, 1));

                // Broadcast to the other node
                await xelsSender.FullNode.NodeController<WalletController>().SendTransactionAsync(new SendTransactionRequest(transaction1.ToHex()));

                // Wait for the transaction to arrive
                TestBase.WaitLoop(() => xelsHotStake.CreateRPCClient().GetRawMempool().Length > 0);
                Assert.NotNull(xelsHotStake.CreateRPCClient().GetRawTransaction(transaction1.GetHash(), null, false));
                TestBase.WaitLoop(() => xelsHotStake.FullNode.WalletManager().GetSpendableTransactionsInWallet(WalletName).Any());

                long receiveTotal = xelsHotStake.FullNode.WalletManager().GetSpendableTransactionsInWallet(WalletName).Sum(s => s.Transaction.Amount);
                Assert.Equal(amountToSend, (Money)receiveTotal);
                Assert.Null(xelsHotStake.FullNode.WalletManager().GetSpendableTransactionsInWallet(WalletName).First().Transaction.BlockHeight);

                // Setup cold staking from the hot wallet.
                Money amountToSend2 = receiveTotal - network.Consensus.ProofOfWorkReward;
                (Transaction transaction2, _) = hotWalletManager.GetColdStakingSetupTransaction(xelsHotStake.FullNode.WalletTransactionHandler(),
                    coldWalletAddress.Address, hotWalletAddress.Address, WalletName, Account, Password, amountToSend2, new Money(0.02m, MoneyUnit.BTC), false, false, 1, false);

                // Broadcast to the other node
                await xelsHotStake.FullNode.NodeController<WalletController>().SendTransactionAsync(new SendTransactionRequest(transaction2.ToHex()));

                // Wait for the transaction to arrive
                TestBase.WaitLoop(() => coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).Any());

                long receivetotal2 = coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).Sum(s => s.Transaction.Amount);
                Assert.Equal(amountToSend2, (Money)receivetotal2);
                Assert.Null(coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).First().Transaction.BlockHeight);

                // Allow coins to reach maturity
                int stakingMaturity = ((PosConsensusOptions)network.Consensus.Options).GetStakeMinConfirmations(0, network);
                TestHelper.MineBlocks(xelsSender, stakingMaturity, true);

                // Start staking.
                var hotMiningFeature = xelsHotStake.FullNode.NodeFeature<MiningFeature>();
                hotMiningFeature.StartStaking(WalletName, Password);

                TestBase.WaitLoop(() =>
                {
                    var stakingInfo = xelsHotStake.FullNode.NodeService<IPosMinting>().GetGetStakingInfoModel();
                    return stakingInfo.Staking;
                });

                // Wait for money from staking.
                var cancellationToken = new CancellationTokenSource(TimeSpan.FromMinutes(3)).Token;
                TestBase.WaitLoop(() =>
                {
                    // Keep mining to ensure that staking outputs reach maturity.
                    TestHelper.MineBlocks(xelsSender, 1, true);
                    return coldWalletManager.GetSpendableTransactionsInColdWallet(WalletName, true).Sum(s => s.Transaction.Amount) > receivetotal2;
                }, cancellationToken: cancellationToken);
            }
        }

        [Fact]
        [Trait("Unstable", "True")]
        public async Task CanRetrieveFilteredUtxosAsync()
        {
            using (var builder = NodeBuilder.Create(this))
            {
                var network = new StraxRegTest();

                CoreNode xelsSender = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: false);
                CoreNode xelsColdStake = CreatePowPosMiningNode(builder, network, TestBase.CreateTestDir(this), coldStakeNode: true);

                xelsSender.WithReadyBlockchainData(ReadyBlockchain.StraxRegTest150Miner).Start();
                xelsColdStake.WithWallet().Start();

                var coldWalletManager = xelsColdStake.FullNode.WalletManager() as ColdStakingManager;

                // Set up cold staking account on cold wallet.
                coldWalletManager.GetOrCreateColdStakingAccount(WalletName, true, Password, null);
                HdAddress coldWalletAddress = coldWalletManager.GetFirstUnusedColdStakingAddress(WalletName, true);

                var walletAccountReference = new WalletAccountReference(WalletName, Account);
                long total2 = xelsSender.FullNode.WalletManager().GetSpendableTransactionsInAccount(walletAccountReference, 1).Sum(s => s.Transaction.Amount);

                // Sync nodes.
                TestHelper.Connect(xelsSender, xelsColdStake);

                // Send coins to cold address.
                Money amountToSend = total2 - network.Consensus.ProofOfWorkReward;
                Transaction transaction1 = xelsSender.FullNode.WalletTransactionHandler().BuildTransaction(CreateContext(xelsSender.FullNode.Network, new WalletAccountReference(WalletName, Account), Password, coldWalletAddress.ScriptPubKey, amountToSend, FeeType.Medium, 1));

                // Broadcast to the other nodes.
                await xelsSender.FullNode.NodeController<WalletController>().SendTransactionAsync(new SendTransactionRequest(transaction1.ToHex()));

                // Wait for the transaction to arrive.
                TestBase.WaitLoop(() => xelsColdStake.CreateRPCClient().GetRawMempool().Length > 0);

                // Despite the funds being sent to an address in the cold account, the wallet does not recognise the output as funds belonging to it.
                Assert.True(xelsColdStake.FullNode.WalletManager().GetBalances(WalletName, Account).Sum(a => a.AmountUnconfirmed + a.AmountUnconfirmed) == 0);

                uint256[] mempoolTransactionId = xelsColdStake.CreateRPCClient().GetRawMempool();

                Transaction misspentTransaction = xelsColdStake.CreateRPCClient().GetRawTransaction(mempoolTransactionId[0]);

                // Now retrieve the UTXO sent to the cold address. The funds will reappear in a normal account on the cold staking node.
                xelsColdStake.FullNode.NodeController<ColdStakingController>().RetrieveFilteredUtxos(new RetrieveFilteredUtxosRequest() { WalletName = xelsColdStake.WalletName, WalletPassword = xelsColdStake.WalletPassword, Hex = misspentTransaction.ToHex(), WalletAccount = null, Broadcast = true});

                TestBase.WaitLoop(() => xelsColdStake.FullNode.WalletManager().GetBalances(WalletName, Account).Sum(a => a.AmountUnconfirmed + a.AmountUnconfirmed) > 0);
            }
        }
    }
}
