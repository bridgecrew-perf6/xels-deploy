﻿using System;
using Moq;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.PoA.BasePoAFeatureConsensusRules;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.Extensions;
using Xunit;

namespace Xels.Bitcoin.Features.PoA.Tests.Rules
{
    public class HeaderTimeChecksPoARuleTests : PoATestsBase
    {
        private readonly HeaderTimeChecksPoARule timeChecksRule;

        public HeaderTimeChecksPoARuleTests()
        {
            this.timeChecksRule = new HeaderTimeChecksPoARule();
            this.InitRule(this.timeChecksRule);
        }

        [Fact]
        public void EnsureTimestampOfNextBlockIsGreaterThanPrevBlock()
        {
            var validationContext = new ValidationContext() { ChainedHeaderToValidate = this.currentHeader };
            var ruleContext = new RuleContext(validationContext, DateTimeOffset.Now);

            ChainedHeader prevHeader = this.currentHeader.Previous;

            // New block has smaller timestamp.
            this.currentHeader.Header.Time = this.consensusOptions.TargetSpacingSeconds;
            prevHeader.Header.Time = this.currentHeader.Header.Time + this.consensusOptions.TargetSpacingSeconds;

            Assert.Throws<ConsensusErrorException>(() => this.timeChecksRule.Run(ruleContext));

            try
            {
                this.timeChecksRule.Run(ruleContext);
            }
            catch (ConsensusErrorException exception)
            {
                Assert.Equal(ConsensusErrors.TimeTooOld, exception.ConsensusError);
            }

            // New block has equal timestamp.
            prevHeader.Header.Time = this.currentHeader.Header.Time;
            Assert.Throws<ConsensusErrorException>(() => this.timeChecksRule.Run(ruleContext));

            // New block has greater timestamp.
            prevHeader.Header.Time = this.currentHeader.Header.Time - this.consensusOptions.TargetSpacingSeconds;
            this.timeChecksRule.Run(ruleContext);
        }

        [Fact]
        public void EnsureTimestampIsNotTooNew()
        {
            long timestamp = new DateTimeProvider().GetUtcNow().ToUnixTimestamp() / this.consensusOptions.TargetSpacingSeconds * this.consensusOptions.TargetSpacingSeconds;
            DateTime time = DateTimeOffset.FromUnixTimeSeconds(timestamp).DateTime;

            // Pretend we receive the next block right on its timestamp
            var provider = new Mock<IDateTimeProvider>();
            provider.Setup(x => x.GetAdjustedTime()).Returns(time + TimeSpan.FromSeconds(this.consensusOptions.TargetSpacingSeconds));

            this.rulesEngine = new PoAConsensusRuleEngine(this.network, this.loggerFactory, provider.Object, this.ChainIndexer, new NodeDeployments(this.network, this.ChainIndexer),
                this.consensusSettings, new Checkpoints(this.network, this.consensusSettings), new Mock<ICoinView>().Object, new ChainState(), new InvalidBlockHashStore(provider.Object),
                new NodeStats(provider.Object, NodeSettings.Default(this.network), new Mock<IVersionProvider>().Object), this.slotsManager, this.poaHeaderValidator, this.votingManager, this.federationManager, this.asyncProvider,
                new ConsensusRulesContainer(), null);

            this.timeChecksRule.Parent = this.rulesEngine;
            this.timeChecksRule.Initialize();

            var validationContext = new ValidationContext() { ChainedHeaderToValidate = this.currentHeader };
            var ruleContext = new RuleContext(validationContext, time);

            ChainedHeader prevHeader = this.currentHeader.Previous;

            prevHeader.Header.BlockTime = time;

            // There is no "valid future offset" as the time is restricted to be accurate within 1 target spacing.
            this.currentHeader.Header.BlockTime = prevHeader.Header.BlockTime + TimeSpan.FromSeconds(this.consensusOptions.TargetSpacingSeconds);
            this.timeChecksRule.Run(ruleContext);

            // Send a block too far into the future, more than a targetspacing away
            this.currentHeader.Header.BlockTime = this.currentHeader.Header.BlockTime + TimeSpan.FromSeconds(this.consensusOptions.TargetSpacingSeconds);
            Assert.Throws<ConsensusErrorException>(() => this.timeChecksRule.Run(ruleContext));

            try
            {
                this.timeChecksRule.Run(ruleContext);
            }
            catch (ConsensusErrorException exception)
            {
                Assert.Equal(ConsensusErrors.TimeTooNew, exception.ConsensusError);
            }
        }
    }
}
