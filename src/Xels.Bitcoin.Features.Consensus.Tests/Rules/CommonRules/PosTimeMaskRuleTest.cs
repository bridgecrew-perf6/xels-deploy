﻿using System.Threading.Tasks;
using NBitcoin;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xunit;

namespace Xels.Bitcoin.Features.Consensus.Tests.Rules.CommonRules
{
    public class PosTimeMaskRuleTest : PosConsensusRuleUnitTestBase
    {
        private const int MaxFutureDriftBeforeHardFork = 128 * 60 * 60;
        private const int MaxFutureDriftAfterHardFork = 15;

        public PosTimeMaskRuleTest()
        {
            AddBlocksToChain(this.ChainIndexer, 5);
        }

        [Fact]
        public void RunAsync_HeaderVersionBelowMinimalHeaderVersion_ThrowsBadVersionConsensusError()
        {
            var rule = this.CreateRule<XelsHeaderVersionRule>();

            int MinimalHeaderVersion = 7;
            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.GetHeader(1);
            this.ruleContext.ValidationContext.ChainedHeaderToValidate.Header.Version = MinimalHeaderVersion - 1;

            ConsensusErrorException exception = Assert.Throws<ConsensusErrorException>(() => rule.Run(this.ruleContext));

            Assert.Equal(ConsensusErrors.BadVersion, exception.ConsensusError);
        }

        [Fact]
        public async Task RunAsync_ProofOfWorkTooHigh_ThrowsProofOfWorkTooHighConsensusErrorAsync()
        {
            var rule = this.CreateRule<PosTimeMaskRule>();

            this.SetBlockStake();
            this.network.Consensus.LastPOWBlock = 2;
            this.ruleContext.ValidationContext = new ValidationContext();
            this.ruleContext.ValidationContext.BlockToValidate = this.network.Consensus.ConsensusFactory.CreateBlock();
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());
            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.GetHeader(3);

            ConsensusErrorException exception = await Assert.ThrowsAsync<ConsensusErrorException>(() => rule.RunAsync(this.ruleContext));

            Assert.Equal(ConsensusErrors.ProofOfWorkTooHigh, exception.ConsensusError);
        }

        [Fact]
        public async Task RunAsync_StakeTimestampInvalid_BlockTimeNotTransactionTime_ThrowsStakeTimeViolationConsensusErrorAsync()
        {
            var rule = this.CreateRule<PosTimeMaskRule>();

            this.SetBlockStake(BlockFlag.BLOCK_PROOF_OF_STAKE);
            this.ruleContext.ValidationContext = new ValidationContext();
            this.ruleContext.ValidationContext.BlockToValidate = this.network.Consensus.ConsensusFactory.CreateBlock();
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());

            // create a stake trx
            this.ruleContext.ValidationContext.BlockToValidate.Transactions[1].Inputs.Add(new TxIn(new OutPoint(uint256.One, 0)));
            this.ruleContext.ValidationContext.BlockToValidate.Transactions[1].Outputs.Add(new TxOut(Money.Zero, new Script()));
            this.ruleContext.ValidationContext.BlockToValidate.Transactions[1].Outputs.Add(new TxOut(Money.Zero, new Script()));

            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.GetHeader(3);

            this.network.Consensus.LastPOWBlock = 12500;
            this.ruleContext.ValidationContext.ChainedHeaderToValidate.Header.Time = this.ruleContext.ValidationContext.BlockToValidate.Header.Time + MaxFutureDriftAfterHardFork;

            rule.FutureDriftRule = new XelsBugFixPosFutureDriftRule();

            ConsensusErrorException exception = await Assert.ThrowsAsync<ConsensusErrorException>(() => rule.RunAsync(this.ruleContext));

            Assert.Equal(ConsensusErrors.StakeTimeViolation, exception.ConsensusError);
        }

        [Fact]
        public async Task RunAsync_StakeTimestampInvalid_TransactionTimeDoesNotIncludeStakeTimestampMask_ThrowsStakeTimeViolationConsensusErrorAsync()
        {
            var rule = this.CreateRule<PosTimeMaskRule>();

            this.SetBlockStake(BlockFlag.BLOCK_PROOF_OF_STAKE);
            this.ruleContext.ValidationContext = new ValidationContext();
            this.ruleContext.ValidationContext.BlockToValidate = this.network.Consensus.ConsensusFactory.CreateBlock();
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());

            // create a stake trx
            this.ruleContext.ValidationContext.BlockToValidate.Transactions[1].Inputs.Add(new TxIn(new OutPoint(uint256.One, 0)));
            this.ruleContext.ValidationContext.BlockToValidate.Transactions[1].Outputs.Add(new TxOut(Money.Zero, new Script()));
            this.ruleContext.ValidationContext.BlockToValidate.Transactions[1].Outputs.Add(new TxOut(Money.Zero, new Script()));

            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.GetHeader(3);
            this.network.Consensus.LastPOWBlock = 12500;
            this.ruleContext.ValidationContext.ChainedHeaderToValidate.Header.Time = this.ruleContext.ValidationContext.BlockToValidate.Header.Time + MaxFutureDriftAfterHardFork;

            rule.FutureDriftRule = new XelsBugFixPosFutureDriftRule();

            ConsensusErrorException exception = await Assert.ThrowsAsync<ConsensusErrorException>(() => rule.RunAsync(this.ruleContext));

            Assert.Equal(ConsensusErrors.StakeTimeViolation, exception.ConsensusError);
        }

        [Fact]
        public void RunAsync_BlockTimestampSameAsPrevious_ThrowsBlockTimestampTooEarlyConsensusError()
        {
            var rule = this.CreateRule<HeaderTimeChecksPosRule>();

            this.SetBlockStake(BlockFlag.BLOCK_PROOF_OF_STAKE);
            this.ruleContext.ValidationContext = new ValidationContext();
            this.ruleContext.ValidationContext.BlockToValidate = this.network.Consensus.ConsensusFactory.CreateBlock();
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());

            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.GetHeader(3);
            this.network.Consensus.LastPOWBlock = 12500;

            // time same as previous block.
            uint previousBlockHeaderTime = this.ruleContext.ValidationContext.ChainedHeaderToValidate.Previous.Header.Time;
            this.ruleContext.ValidationContext.ChainedHeaderToValidate.Header.Time = previousBlockHeaderTime;

            ConsensusErrorException exception = Assert.Throws<ConsensusErrorException>(() => rule.Run(this.ruleContext));

            Assert.Equal(ConsensusErrors.BlockTimestampTooEarly, exception.ConsensusError);
        }

        [Fact]
        public async Task RunAsync_ValidRuleContext_DoesNotThrowExceptionAsync()
        {
            var rule = this.CreateRule<PosTimeMaskRule>();

            this.SetBlockStake(BlockFlag.BLOCK_PROOF_OF_STAKE);
            this.ruleContext.ValidationContext = new ValidationContext();
            this.ruleContext.ValidationContext.BlockToValidate = this.network.Consensus.ConsensusFactory.CreateBlock();
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(this.network.CreateTransaction());

            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.GetHeader(3);
            this.network.Consensus.LastPOWBlock = 12500;

            // time after previous block.
            uint previousBlockHeaderTime = this.ruleContext.ValidationContext.ChainedHeaderToValidate.Previous.Header.Time;
            this.ruleContext.ValidationContext.ChainedHeaderToValidate.Header.Time = previousBlockHeaderTime + 64;

            rule.FutureDriftRule = new XelsBugFixPosFutureDriftRule();

            await rule.RunAsync(this.ruleContext);
        }

        private void SetBlockStake(BlockFlag flg)
        {
            (this.ruleContext as PosRuleContext).BlockStake = new BlockStake()
            {
                Flags = flg
            };
        }

        private void SetBlockStake()
        {
            (this.ruleContext as PosRuleContext).BlockStake = new BlockStake();
        }

    }
}
