using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.Consensus.Interfaces;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.MemoryPool.Interfaces;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Mining;
using Xels.Bitcoin.Utilities;

namespace Xels.Bitcoin.Features.Miner.Staking
{
    public class StraxMinting : PosMinting
    {
        public StraxMinting(
            IBlockProvider blockProvider,
            IConsensusManager consensusManager,
            ChainIndexer chainIndexer,
            Network network,
            IDateTimeProvider dateTimeProvider,
            IInitialBlockDownloadState initialBlockDownloadState,
            INodeLifetime nodeLifetime,
            ICoinView coinView,
            IStakeChain stakeChain,
            IStakeValidator stakeValidator,
            MempoolSchedulerLock mempoolLock,
            ITxMempool mempool,
            IWalletManager walletManager,
            IAsyncProvider asyncProvider,
            ITimeSyncBehaviorState timeSyncBehaviorState,
            ILoggerFactory loggerFactory,
            MinerSettings minerSettings) : base(blockProvider, consensusManager, chainIndexer, network, dateTimeProvider,
                initialBlockDownloadState, nodeLifetime, coinView, stakeChain, stakeValidator, mempoolLock, mempool,
                walletManager, asyncProvider, timeSyncBehaviorState, loggerFactory, minerSettings)
        {
        }

        public override Transaction PrepareCoinStakeTransactions(int currentChainHeight, CoinstakeContext coinstakeContext, long coinstakeOutputValue, int utxosCount, long amountStaked, long reward)
        {
            long cirrusReward = reward * StraxCoinviewRule.CirrusRewardPercentage / 100;

            coinstakeOutputValue -= cirrusReward;

            // Populate the initial coinstake with the modified overall reward amount, the outputs will be split as necessary
            base.PrepareCoinStakeTransactions(currentChainHeight, coinstakeContext, coinstakeOutputValue, utxosCount, amountStaked, reward);

            // Now add the remaining reward into an additional output on the coinstake
            var cirrusRewardOutput = new TxOut(cirrusReward, StraxCoinstakeRule.CirrusRewardScript);
            coinstakeContext.CoinstakeTx.Outputs.Add(cirrusRewardOutput);

            return coinstakeContext.CoinstakeTx;
        }
    }
}
