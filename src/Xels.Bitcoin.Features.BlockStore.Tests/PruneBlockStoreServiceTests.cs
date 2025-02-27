﻿using Moq;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.BlockStore.Pruning;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Features.BlockStore.Tests
{
    public sealed class PruneBlockStoreServiceTests : LogsTestBase
    {
        private readonly IAsyncProvider asyncProvider;
        private readonly IBlockRepository blockRepository;
        private Mock<IPrunedBlockRepository> prunedBlockRepository;
        private readonly Mock<IChainState> chainState;
        private readonly INodeLifetime nodeLifetime;

        public PruneBlockStoreServiceTests() : base(new StraxMain())
        {
            this.asyncProvider = new Mock<IAsyncProvider>().Object;
            this.blockRepository = new Mock<IBlockRepository>().Object;
            this.chainState = new Mock<IChainState>();
            this.nodeLifetime = new NodeLifetime();
        }

        [Fact]
        public void PruneService_Initialize_Genesis_PrunedUpToHeader_Set()
        {
            var block = this.Network.CreateBlock();
            var genesisHeader = new ChainedHeader(block.Header, block.GetHash(), 0);
            this.chainState.Setup(c => c.BlockStoreTip).Returns(genesisHeader);

            this.prunedBlockRepository = new Mock<IPrunedBlockRepository>();
            this.prunedBlockRepository.Setup(x => x.PrunedTip).Returns(new HashHeightPair(genesisHeader));

            var storeSettings = new StoreSettings(NodeSettings.Default(this.Network))
            {
                AmountOfBlocksToKeep = 2880
            };

            var service = new PruneBlockStoreService(this.asyncProvider, this.blockRepository, this.prunedBlockRepository.Object, this.chainState.Object, this.LoggerFactory.Object, this.nodeLifetime, storeSettings);

            service.Initialize();

            Assert.Equal(genesisHeader, service.PrunedUpToHeaderTip);
        }

        [Fact]
        public void PruneService_Initialize_MidChain_PrunedUpToHeader_Set()
        {
            var chainHeaderTip = this.BuildProvenHeaderChain(10);

            this.chainState.Setup(c => c.BlockStoreTip).Returns(chainHeaderTip);

            this.prunedBlockRepository = new Mock<IPrunedBlockRepository>();
            this.prunedBlockRepository.Setup(x => x.PrunedTip).Returns(new HashHeightPair(chainHeaderTip));

            var storeSettings = new StoreSettings(NodeSettings.Default(this.Network))
            {
                AmountOfBlocksToKeep = 2880
            };

            var service = new PruneBlockStoreService(this.asyncProvider, this.blockRepository, this.prunedBlockRepository.Object, this.chainState.Object, this.LoggerFactory.Object, this.nodeLifetime, storeSettings);

            service.Initialize();

            Assert.Equal(chainHeaderTip, service.PrunedUpToHeaderTip);
        }

        [Fact]
        public void PruneService_Blockstore_Height_Below_AmountofBlockstoKeep_PruneAborted()
        {
            var block = this.Network.CreateBlock();
            var genesisHeader = new ChainedHeader(block.Header, block.GetHash(), 0);

            var blockRepository = new Mock<IBlockRepository>();
            blockRepository.Setup(x => x.TipHashAndHeight).Returns(new HashHeightPair(genesisHeader));

            this.chainState.Setup(c => c.BlockStoreTip).Returns(genesisHeader);

            this.prunedBlockRepository = new Mock<IPrunedBlockRepository>();
            this.prunedBlockRepository.Setup(x => x.PrunedTip).Returns(new HashHeightPair(genesisHeader));

            var storeSettings = new StoreSettings(NodeSettings.Default(this.Network))
            {
                AmountOfBlocksToKeep = 2880
            };

            var service = new PruneBlockStoreService(this.asyncProvider, blockRepository.Object, this.prunedBlockRepository.Object, this.chainState.Object, this.LoggerFactory.Object, this.nodeLifetime, storeSettings);
            service.Initialize();

            service.PruneBlocks();

            Assert.Equal(genesisHeader, service.PrunedUpToHeaderTip);
        }

        [Fact]
        public void PruneService_Blockstore_Height_Equals_Prunedtip_PruneAborted()
        {
            var block = this.Network.CreateBlock();
            var header = new ChainedHeader(block.Header, block.GetHash(), 2880);

            var blockRepository = new Mock<IBlockRepository>();
            blockRepository.Setup(x => x.TipHashAndHeight).Returns(new HashHeightPair(header));

            this.chainState.Setup(c => c.BlockStoreTip).Returns(header);

            this.prunedBlockRepository = new Mock<IPrunedBlockRepository>();
            this.prunedBlockRepository.Setup(x => x.PrunedTip).Returns(new HashHeightPair(header));

            var storeSettings = new StoreSettings(NodeSettings.Default(this.Network))
            {
                AmountOfBlocksToKeep = 2880
            };

            var service = new PruneBlockStoreService(this.asyncProvider, blockRepository.Object, this.prunedBlockRepository.Object, this.chainState.Object, this.LoggerFactory.Object, this.nodeLifetime, storeSettings);
            service.Initialize();

            service.PruneBlocks();

            Assert.Equal(header, service.PrunedUpToHeaderTip);
        }

        [Fact]
        public void PruneService_Blockstore_Height_Below_PrunedTip_Plus_AmountToKeep_PruneAborted()
        {
            var chain = this.BuildProvenHeaderChain(50);

            var storeTipAt25 = chain.GetAncestor(20);

            var blockRepository = new Mock<IBlockRepository>();
            blockRepository.Setup(x => x.TipHashAndHeight).Returns(new HashHeightPair(storeTipAt25));

            this.chainState.Setup(c => c.BlockStoreTip).Returns(storeTipAt25);

            var prunedUptoHeaderTipAt10 = chain.GetAncestor(10);

            this.prunedBlockRepository = new Mock<IPrunedBlockRepository>();
            this.prunedBlockRepository.Setup(x => x.PrunedTip).Returns(new HashHeightPair(prunedUptoHeaderTipAt10));

            var storeSettings = new StoreSettings(NodeSettings.Default(this.Network))
            {
                AmountOfBlocksToKeep = 20
            };

            var service = new PruneBlockStoreService(this.asyncProvider, blockRepository.Object, this.prunedBlockRepository.Object, this.chainState.Object, this.LoggerFactory.Object, this.nodeLifetime, storeSettings);
            service.Initialize();

            service.PruneBlocks();

            Assert.Equal(prunedUptoHeaderTipAt10, service.PrunedUpToHeaderTip);
        }

        [Fact]
        public void PruneService_Triggered_FromGenesis_Respect_AmountOfBlocksToKeep()
        {
            var chain = this.BuildProvenHeaderChain(50);

            var storeTipAt35 = chain.GetAncestor(35);

            var blockRepository = new Mock<IBlockRepository>();
            blockRepository.Setup(x => x.TipHashAndHeight).Returns(new HashHeightPair(storeTipAt35));

            this.chainState.Setup(c => c.BlockStoreTip).Returns(storeTipAt35);

            var prunedUptoHeaderTipAtGenesis = chain.GetAncestor(0);

            this.prunedBlockRepository = new Mock<IPrunedBlockRepository>();
            this.prunedBlockRepository.Setup(x => x.PrunedTip).Returns(new HashHeightPair(prunedUptoHeaderTipAtGenesis));

            var storeSettings = new StoreSettings(NodeSettings.Default(this.Network))
            {
                AmountOfBlocksToKeep = 20
            };

            var service = new PruneBlockStoreService(this.asyncProvider, blockRepository.Object, this.prunedBlockRepository.Object, this.chainState.Object, this.LoggerFactory.Object, this.nodeLifetime, storeSettings);
            service.Initialize();

            service.PruneBlocks();

            Assert.Equal(15, service.PrunedUpToHeaderTip.Height);
        }

        [Fact]
        public void PruneService_Triggered_MidChain_Respect_AmountOfBlocksToKeep()
        {
            var chain = this.BuildProvenHeaderChain(50);

            var storeTipAt45 = chain.GetAncestor(45);

            var blockRepository = new Mock<IBlockRepository>();
            blockRepository.Setup(x => x.TipHashAndHeight).Returns(new HashHeightPair(storeTipAt45));

            this.chainState.Setup(c => c.BlockStoreTip).Returns(storeTipAt45);

            var prunedUptoHeaderTipAt10 = chain.GetAncestor(10);

            this.prunedBlockRepository = new Mock<IPrunedBlockRepository>();
            this.prunedBlockRepository.Setup(x => x.PrunedTip).Returns(new HashHeightPair(prunedUptoHeaderTipAt10));

            var storeSettings = new StoreSettings(NodeSettings.Default(this.Network))
            {
                AmountOfBlocksToKeep = 20
            };

            var service = new PruneBlockStoreService(this.asyncProvider, blockRepository.Object, this.prunedBlockRepository.Object, this.chainState.Object, this.LoggerFactory.Object, this.nodeLifetime, storeSettings);
            service.Initialize();

            service.PruneBlocks();

            Assert.Equal(25, service.PrunedUpToHeaderTip.Height);
        }
    }
}
