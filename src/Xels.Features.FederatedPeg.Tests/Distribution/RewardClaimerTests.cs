using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NSubstitute;
using Xels.Bitcoin;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Persistence.KeyValueStores;
using Xels.Bitcoin.Primitives;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xels.Features.FederatedPeg.Conversion;
using Xels.Features.FederatedPeg.Distribution;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.SourceChain;
using Xels.Features.FederatedPeg.Tests.Utils;
using Xels.Sidechains.Networks;
using Xunit;

namespace Xels.Features.FederatedPeg.Tests.Distribution
{
    public sealed class RewardClaimerTests
    {
        private readonly MultisigAddressHelper addressHelper;
        private List<ChainedHeaderBlock> blocks;
        private readonly IBroadcasterManager broadCasterManager;
        private readonly ChainIndexer chainIndexer;
        private readonly IConsensusManager consensusManager;
        private readonly DBreezeSerializer dbreezeSerializer;
        private readonly IConversionRequestRepository conversionRequestRepository;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILoggerFactory loggerFactory;
        private readonly StraxRegTest network;
        private readonly IOpReturnDataReader opReturnDataReader;
        private readonly Signals signals;
        private readonly IInitialBlockDownloadState initialBlockDownloadState;

        public RewardClaimerTests()
        {
            this.network = new StraxRegTest
            {
                RewardClaimerBatchActivationHeight = 40,
                RewardClaimerBlockInterval = 10
            };

            this.addressHelper = new MultisigAddressHelper(this.network, new CirrusRegTest());
            this.broadCasterManager = Substitute.For<IBroadcasterManager>();
            this.chainIndexer = new ChainIndexer(this.network);
            this.consensusManager = Substitute.For<IConsensusManager>();
            this.dbreezeSerializer = new DBreezeSerializer(this.network.Consensus.ConsensusFactory);
            this.conversionRequestRepository = Substitute.For<IConversionRequestRepository>();

            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.signals = new Signals(this.loggerFactory, null);

            this.initialBlockDownloadState = Substitute.For<IInitialBlockDownloadState>();
            this.initialBlockDownloadState.IsInitialBlockDownload().Returns(false);

            this.opReturnDataReader = new OpReturnDataReader(new CirrusRegTest());

            this.federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            this.federatedPegSettings.MultiSigRedeemScript.Returns(this.addressHelper.PayToMultiSig);

            this.federatedPegSettings.MinimumConfirmationsSmallDeposits.Returns(5);
            this.federatedPegSettings.MinimumConfirmationsNormalDeposits.Returns(10);
            this.federatedPegSettings.MinimumConfirmationsLargeDeposits.Returns(20);

            this.federatedPegSettings.SmallDepositThresholdAmount.Returns(Money.Coins(10));
            this.federatedPegSettings.NormalDepositThresholdAmount.Returns(Money.Coins(100));
        }

        /// <summary>
        /// Scenario 1
        /// 
        /// Tip                         = 30
        /// Distribution Deposits from  = 11 to 15
        /// </summary>
        [Fact]
        public async Task RewardClaimer_RetrieveSingleDepositsAsync()
        {
            DataFolder dataFolder = TestBase.CreateDataFolder(this);
            var keyValueRepository = new LevelDbKeyValueRepository(dataFolder, this.dbreezeSerializer);

            // Create a "chain" of 30 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, network: this.network, chainIndexer: this.chainIndexer, withCoinbaseAndCoinStake: true, createCirrusReward: true);
            using (var rewardClaimer = new RewardClaimer(this.broadCasterManager, this.chainIndexer, this.consensusManager, this.initialBlockDownloadState, keyValueRepository, this.network, this.signals))
            {
                var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);

                // Add 5 distribution deposits from block 11 through to 15.
                for (int i = 11; i <= 15; i++)
                {
                    Transaction rewardTransaction = rewardClaimer.BuildRewardTransaction(false);
                    IDeposit deposit = await depositExtractor.ExtractDepositFromTransaction(rewardTransaction, i, this.blocks[i].Block.GetHash());
                    Assert.NotNull(deposit);
                }
            }
        }

        /// <summary>
        /// Scenario 1
        /// 
        /// Tip                         = 30
        /// Distribution Deposits from  = 11 to 15
        /// </summary>
        [Fact]
        public async Task RewardClaimer_RetrieveBatchedDepositsAsync()
        {
            DataFolder dataFolder = TestBase.CreateDataFolder(this);
            var keyValueRepository = new LevelDbKeyValueRepository(dataFolder, this.dbreezeSerializer);

            // Create a "chain" of 30 blocks.
            this.blocks = ChainedHeadersHelper.CreateConsecutiveHeadersAndBlocks(30, true, network: this.network, chainIndexer: this.chainIndexer, withCoinbaseAndCoinStake: true, createCirrusReward: true);

            // The reward claimer should look at block 10 to 20.
            using (var rewardClaimer = new RewardClaimer(this.broadCasterManager, this.chainIndexer, this.consensusManager, this.initialBlockDownloadState, keyValueRepository, this.network, this.signals))
            {
                Transaction rewardTransaction = rewardClaimer.BuildRewardTransaction(true);

                Assert.Equal(10, rewardTransaction.Inputs.Count);
                Assert.Equal(2, rewardTransaction.Outputs.Count);
                Assert.Equal(Money.Coins(90), rewardTransaction.TotalOut);

                var depositExtractor = new DepositExtractor(this.conversionRequestRepository, this.federatedPegSettings, this.network, this.opReturnDataReader);
                IDeposit deposit = await depositExtractor.ExtractDepositFromTransaction(rewardTransaction, 30, this.blocks[30].Block.GetHash());
                Assert.Equal(Money.Coins(90), deposit.Amount);
            }
        }
    }
}
