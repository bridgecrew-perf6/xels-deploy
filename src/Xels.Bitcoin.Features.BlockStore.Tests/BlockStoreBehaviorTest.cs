using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Networks;
using Xunit;

namespace Xels.Bitcoin.Features.BlockStore.Tests
{
    public class BlockStoreBehaviorTest
    {
        private readonly BlockStoreBehavior behavior;
        private readonly Mock<IChainState> chainState;
        private readonly ChainIndexer chainIndexer;
        private readonly ILoggerFactory loggerFactory;
        private readonly Mock<IConsensusManager> consensusManager;
        private readonly Mock<IBlockStoreQueue> blockStore;

        public BlockStoreBehaviorTest()
        {
            this.loggerFactory = new LoggerFactory();
            this.chainIndexer = new ChainIndexer(new StraxMain());
            this.chainState = new Mock<IChainState>();
            this.consensusManager = new Mock<IConsensusManager>();
            this.blockStore = new Mock<IBlockStoreQueue>();

            this.behavior = new BlockStoreBehavior(this.chainIndexer, this.chainState.Object, this.loggerFactory, this.consensusManager.Object, this.blockStore.Object);
        }

        [Fact]
        public void AnnounceBlocksWithoutBlocksReturns()
        {
            var blocks = new List<ChainedHeader>();

            Task task = this.behavior.AnnounceBlocksAsync(blocks);

            Assert.Equal(TaskStatus.RanToCompletion, task.Status);
            Assert.Null(this.behavior.AttachedPeer);
        }
    }
}