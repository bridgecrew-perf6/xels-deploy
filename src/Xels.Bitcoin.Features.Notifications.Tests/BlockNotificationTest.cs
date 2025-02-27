﻿// TODO: The BlockNotification class is not currently being used. Restore this test class when it does get used.
/*
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.EventBus.CoreEvents;
using Xels.Bitcoin.Primitives;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Features.Notifications.Tests
{
    public class BlockNotificationTest : LogsTestBase
    {
        private const string RevisitWhenBlockNotificationFixed = "Revisit these tests when BlockNotification is fixed.";

        private readonly NodeLifetime lifetime;
        private readonly Mock<ISignals> signals;
        private readonly Mock<IConsensusManager> consensusManager;
        private ChainIndexer chainIndexer;

        public BlockNotificationTest()
        {
            this.lifetime = new NodeLifetime();
            this.signals = new Mock<ISignals>();
            this.consensusManager = new Mock<IConsensusManager>();
            this.chainIndexer = new ChainIndexer(this.Network);
        }

        /// <summary>
        /// Tests that <see cref="BlockNotification.Notify(System.Threading.CancellationToken)"/> exits due
        /// to <see cref="BlockNotification.StartHash"/> being null and no blocks were signaled.
        /// </summary>
        [Fact]
        public void Notify_Completes_StartHashNotSet()
        {
            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object, this.signals.Object, new AsyncProvider(new LoggerFactory(), this.signals.Object, this.lifetime), this.lifetime);
            notification.Notify(this.lifetime.ApplicationStopping);

            this.signals.Verify(s => s.Publish(It.IsAny<BlockConnected>()), Times.Exactly(0));
        }

        /// <summary>
        /// Tests that <see cref="BlockNotification.Notify(System.Threading.CancellationToken)"/> exits due
        /// to <see cref="BlockNotification.StartHash"/> not being on the chain and no blocks were signaled.
        /// </summary>
        [Fact]
        public void Notify_Completes_StartHashNotOnChain()
        {
            var startBlockId = new uint256(156);
            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object, this.signals.Object, new AsyncProvider(new LoggerFactory(), this.signals.Object, this.lifetime), this.lifetime);
            notification.SyncFrom(startBlockId);
            notification.Notify(this.lifetime.ApplicationStopping);

            this.signals.Verify(s => s.Publish(It.IsAny<BlockConnected>()), Times.Exactly(0));
        }

        /// <summary>
        /// Ensures that <see cref="ISignals.SignalBlock(Block)" /> was called twice
        /// as 2 blocks were made available by the puller to be signaled.
        /// </summary>
        [Fact(Skip = RevisitWhenBlockNotificationFixed)]
        public void Notify_WithSync_RunsAndBroadcastsBlocks()
        {
            List<Block> blocks = this.CreateBlocks(2);

            this.chainIndexer = new ChainIndexer(this.Network, new ChainedHeader(blocks[0].Header, blocks[0].GetHash(), 0));
            this.AppendBlocksToChain(this.chainIndexer, blocks.Skip(1).Take(1));

            var notification = new Mock<BlockNotification>(this.LoggerFactory.Object, this.chainIndexer, this.signals.Object, new AsyncProvider(new LoggerFactory(), this.signals.Object, this.lifetime), this.lifetime);
            notification.SetupGet(s => s.StartHash).Returns(blocks[0].GetHash());

            notification.SetupSequence(s => s.ReSync)
                .Returns(false)
                .Returns(false)
                .Returns(true);

            notification.Object.Notify(this.lifetime.ApplicationStopping);

            this.signals.Verify(s => s.Publish(It.IsAny<BlockConnected>()), Times.Exactly(2));
        }

        [Fact(Skip = RevisitWhenBlockNotificationFixed)]
        public void Notify_Reorg_PushesPullerBackToForkPoint_SignalsNewLookaheadResult()
        {
            List<Block> blocks = this.CreateBlocks(3);

            this.chainIndexer = new ChainIndexer(this.Network, new ChainedHeader(blocks[0].Header, blocks[0].GetHash(), 0));
            this.AppendBlocksToChain(this.chainIndexer, blocks.Skip(1));

            var source = new CancellationTokenSource();
            CancellationToken token = source.Token;
            this.signals.Setup(s => s.Publish(It.Is<BlockConnected>(b => b.ConnectedBlock.Block.GetHash() == blocks[0].GetHash())))
                .Callback(() =>
                {
                    source.Cancel();
                }).Verifiable();

            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object,
                this.signals.Object, new AsyncProvider(new LoggerFactory(), this.signals.Object, this.lifetime), this.lifetime);

            try
            {
                notification.SyncFrom(blocks[0].GetHash());
                notification.Notify(token);
            }
            catch (OperationCanceledException)
            {
            }

            this.signals.Verify();
        }

        /// <summary>
        /// Ensures that <see cref="BlockNotification.StartHash" /> gets updated
        /// every time <see cref="BlockNotification.SyncFrom(uint256)"/> gets called.
        /// </summary>
        [Fact]
        public void CallingSyncFromUpdatesStartHashAccordingly()
        {
            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object,
                this.signals.Object, new AsyncProvider(new LoggerFactory(), this.signals.Object, this.lifetime), this.lifetime);

            var blockId1 = new uint256(150);
            var blockId2 = new uint256(151);

            Assert.Null(notification.StartHash);
            notification.SyncFrom(blockId1);

            Assert.NotNull(notification.StartHash);
            Assert.Equal(blockId1, notification.StartHash);

            notification.SyncFrom(blockId2);
            Assert.Equal(blockId2, notification.StartHash);
        }

        [Fact]
        public void SyncFrom_StartHashIsNull_SetsStartHashToBlockNotification()
        {
            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object,
                this.signals.Object, new AsyncProvider(new LoggerFactory(), this.signals.Object, this.lifetime), this.lifetime);

            notification.SyncFrom(null);

            Assert.Null(notification.StartHash);
        }

        [Fact]
        public void SyncFrom_StartHashIsNotNull_GetsBlockBasedOnStartHash_SetsPullerAndTipToPreviousBlock()
        {
            List<Block> blocks = this.CreateBlocks(3);

            this.chainIndexer = new ChainIndexer(this.Network);
            this.AppendBlocksToChain(this.chainIndexer, blocks.Skip(1));

            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object, this.signals.Object, new AsyncProvider(new LoggerFactory(), this.signals.Object, this.lifetime), this.lifetime);

            notification.SyncFrom(blocks[0].GetHash());
            notification.SyncFrom(blocks[2].GetHash());

            Assert.Equal(notification.StartHash, blocks[2].GetHash());
        }

        [Fact]
        public void Start_RunsAsyncLoop()
        {
            var asyncProvider = new Mock<IAsyncProvider>();

            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object, this.signals.Object, asyncProvider.Object, this.lifetime);

            notification.Start();

            asyncProvider.Verify(a => a.CreateAndRunAsyncLoop("Notify", It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>(), null, null));
        }

        [Fact]
        public void Stop_DisposesAsyncLoop()
        {
            var asyncLoop = new Mock<IAsyncLoop>();
            var asyncProvider = new Mock<IAsyncProvider>();
            asyncProvider.Setup(a => a.CreateAndRunAsyncLoop("Notify", It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>(), null, null))
                .Returns(asyncLoop.Object);

            var notification = new BlockNotification(this.LoggerFactory.Object, this.chainIndexer, this.consensusManager.Object, this.signals.Object, asyncProvider.Object, this.lifetime);

            notification.Start();
            notification.Stop();

            asyncLoop.Verify(a => a.Dispose());
        }
    }
}
*/