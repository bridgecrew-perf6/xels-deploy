﻿using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace Xels.Bitcoin.Features.PoA.Tests
{
    public class SlotsManagerTests
    {
        private ISlotsManager slotsManager;
        private TestPoANetwork network;
        private readonly PoAConsensusOptions consensusOptions;
        private readonly IFederationManager federationManager;
        private readonly IFederationHistory federationHistory;
        private Mock<ChainIndexer> chainIndexer;

        public SlotsManagerTests()
        {
            this.network = new TestPoANetwork();
            this.consensusOptions = this.network.ConsensusOptions;

            (this.federationManager, this.federationHistory) = PoATestsBase.CreateFederationManager(this);
            this.chainIndexer = new Mock<ChainIndexer>();
            this.slotsManager = new SlotsManager(this.network, this.federationManager, this.federationHistory, this.chainIndexer.Object);
        }

        [Fact]
        public void IsValidTimestamp()
        {
            uint targetSpacing = this.consensusOptions.TargetSpacingSeconds;

            Assert.True(this.slotsManager.IsValidTimestamp(targetSpacing));
            Assert.True(this.slotsManager.IsValidTimestamp(targetSpacing * 100));
            Assert.False(this.slotsManager.IsValidTimestamp(targetSpacing * 10 + 1));
            Assert.False(this.slotsManager.IsValidTimestamp(targetSpacing + 2));
        }

        [Fact]
        public void GetMiningTimestamp()
        {
            var tool = new KeyTool(new DataFolder(string.Empty));
            Key key = tool.GeneratePrivateKey();
            this.network = new TestPoANetwork(new List<PubKey>() { tool.GeneratePrivateKey().PubKey, key.PubKey, tool.GeneratePrivateKey().PubKey });

            (IFederationManager fedManager, IFederationHistory federationHistory) = PoATestsBase.CreateFederationManager(this, this.network, new ExtendedLoggerFactory(), new Signals.Signals(new LoggerFactory(), null));
            var header = new BlockHeader();
            this.chainIndexer.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));
            this.slotsManager = new SlotsManager(this.network, fedManager, federationHistory, this.chainIndexer.Object);

            List<IFederationMember> federationMembers = fedManager.GetFederationMembers();
            uint roundStart = this.consensusOptions.TargetSpacingSeconds * (uint)federationMembers.Count * 5;

            fedManager.SetPrivatePropertyValue(typeof(FederationManager), nameof(IFederationManager.CurrentFederationKey), key);
            fedManager.SetPrivatePropertyValue(typeof(FederationManager), nameof(this.federationManager.IsFederationMember), true);

            Assert.Equal(roundStart + this.consensusOptions.TargetSpacingSeconds, this.slotsManager.GetMiningTimestamp(roundStart));
            Assert.Equal(roundStart + this.consensusOptions.TargetSpacingSeconds, this.slotsManager.GetMiningTimestamp(roundStart + 4));

            roundStart += this.consensusOptions.TargetSpacingSeconds * (uint)federationMembers.Count;
            Assert.Equal(roundStart + this.consensusOptions.TargetSpacingSeconds, this.slotsManager.GetMiningTimestamp(roundStart - 5));
            Assert.Equal(roundStart + this.consensusOptions.TargetSpacingSeconds, this.slotsManager.GetMiningTimestamp(roundStart - this.consensusOptions.TargetSpacingSeconds + 1));

            Assert.True(this.slotsManager.IsValidTimestamp(this.slotsManager.GetMiningTimestamp(roundStart - 5)));

            uint thisTurnTimestamp = roundStart + this.consensusOptions.TargetSpacingSeconds;
            uint nextTurnTimestamp = thisTurnTimestamp + this.consensusOptions.TargetSpacingSeconds * (uint)federationMembers.Count;

            // If we are past our last timestamp's turn, always give us the NEXT timestamp.
            uint justPastOurTurnTime = thisTurnTimestamp + (this.consensusOptions.TargetSpacingSeconds / 2) + 1;
            Assert.Equal(nextTurnTimestamp, this.slotsManager.GetMiningTimestamp(justPastOurTurnTime));

            // If we are only just past our last timestamp, but still in the "range" and we haven't mined a block yet, get THIS turn's timestamp.
            Assert.Equal(thisTurnTimestamp, this.slotsManager.GetMiningTimestamp(thisTurnTimestamp + 1));

            // If we are only just past our last timestamp, but we've already mined a block there, then get the NEXT turn's timestamp.
            header = new BlockHeader
            {
                Time = thisTurnTimestamp
            };

            Mock.Get(federationHistory).Setup(x => x.GetFederationMemberForBlock(It.IsAny<ChainedHeader>())).Returns<ChainedHeader>((chainedHeader) =>
            {
                return federationMembers[1];
            });

            this.chainIndexer.Setup(x => x.Tip).Returns(new ChainedHeader(header, header.GetHash(), 0));
            this.slotsManager = new SlotsManager(this.network, fedManager, federationHistory, this.chainIndexer.Object);
            Assert.Equal(nextTurnTimestamp, this.slotsManager.GetMiningTimestamp(thisTurnTimestamp + 1));

        }
    }
}
