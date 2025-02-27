﻿using System;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Features.Consensus.Tests
{
    public class InitialBlockDownloadTest
    {
        private readonly ConsensusSettings consensusSettings;
        private readonly Checkpoints checkpoints;
        private readonly ChainState chainState;
        private readonly Network network;
        private readonly Mock<ILoggerFactory> loggerFactory;

        public InitialBlockDownloadTest()
        {
            this.network = KnownNetworks.Main;
            this.consensusSettings = new ConsensusSettings(new NodeSettings(this.network));
            this.checkpoints = new Checkpoints(this.network, this.consensusSettings);
            this.chainState = new ChainState();
            this.loggerFactory = new Mock<ILoggerFactory>();
            this.loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(new Mock<ILogger>().Object);
        }

        [Fact]
        public void InIBDIfChainTipIsNull()
        {
            this.chainState.ConsensusTip = null;
            var blockDownloadState = new InitialBlockDownloadState(this.chainState, this.network, this.consensusSettings, this.checkpoints, DateTimeProvider.Default);
            Assert.True(blockDownloadState.IsInitialBlockDownload());
        }

        [Fact]
        public void InIBDIfBehindCheckpoint()
        {
            BlockHeader blockHeader = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
            this.chainState.ConsensusTip = new ChainedHeader(blockHeader, blockHeader.GetHash(), 1000);
            var blockDownloadState = new InitialBlockDownloadState(this.chainState, this.network, this.consensusSettings, this.checkpoints, DateTimeProvider.Default);
            Assert.True(blockDownloadState.IsInitialBlockDownload());
        }

        [Fact]
        public void InIBDIfChainWorkIsLessThanMinimum()
        {
            BlockHeader blockHeader = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
            this.chainState.ConsensusTip = new ChainedHeader(blockHeader, blockHeader.GetHash(), this.checkpoints.GetLastCheckpointHeight() + 1);
            var blockDownloadState = new InitialBlockDownloadState(this.chainState, this.network, this.consensusSettings, this.checkpoints, DateTimeProvider.Default);
            Assert.True(blockDownloadState.IsInitialBlockDownload());
        }

        [Fact]
        public void InIBDIfTipIsOlderThanMaxAge()
        {
            BlockHeader blockHeader = this.network.Consensus.ConsensusFactory.CreateBlockHeader();

            // Enough work to get us past the chain work check.
            blockHeader.Bits = new Target(new uint256(uint.MaxValue));

            // Block has a time sufficiently in the past that it can't be the tip.
            blockHeader.Time = ((uint) DateTimeOffset.Now.ToUnixTimeSeconds()) - (uint) this.network.MaxTipAge - 1;

            this.chainState.ConsensusTip = new ChainedHeader(blockHeader, blockHeader.GetHash(), this.checkpoints.GetLastCheckpointHeight() + 1);
            var blockDownloadState = new InitialBlockDownloadState(this.chainState, this.network, this.consensusSettings, this.checkpoints, DateTimeProvider.Default);
            Assert.True(blockDownloadState.IsInitialBlockDownload());
        }
    }
}