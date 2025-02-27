﻿using System;
using System.Threading.Tasks;
using NBitcoin;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xunit;
using static NBitcoin.Transaction;

namespace Xels.Bitcoin.Features.Consensus.Tests.Rules.CommonRules
{
    public class SetActivationDeploymentsRuleTest : TestConsensusRulesUnitTestBase
    {
        public SetActivationDeploymentsRuleTest()
        {
            this.ChainIndexer = GenerateChainWithHeight(5, this.network);
            this.consensusRules = this.InitializeConsensusRules();
        }

        [Fact]
        public async Task RunAsync_ValidBlock_SetsConsensusFlagsAsync()
        {
            this.nodeDeployments = new NodeDeployments(this.network, this.ChainIndexer);
            this.consensusRules = this.InitializeConsensusRules();

            Block block = this.network.CreateBlock();
            block.AddTransaction(this.network.CreateTransaction());
            block.UpdateMerkleRoot();
            block.Header.BlockTime = new DateTimeOffset(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(5));
            block.Header.HashPrevBlock = this.ChainIndexer.Tip.HashBlock;
            block.Header.Nonce = RandomUtils.GetUInt32();

            this.ruleContext.ValidationContext.BlockToValidate = block;
            this.ruleContext.ValidationContext.ChainedHeaderToValidate = this.ChainIndexer.Tip;

            await this.consensusRules.RegisterRule<SetActivationDeploymentsPartialValidationRule>().RunAsync(this.ruleContext);

            Assert.NotNull(this.ruleContext.Flags);
            Assert.True(this.ruleContext.Flags.EnforceBIP30);
            Assert.False(this.ruleContext.Flags.EnforceBIP34);
            Assert.Equal(LockTimeFlags.None, this.ruleContext.Flags.LockTimeFlags);
            Assert.Equal(ScriptVerify.Mandatory, this.ruleContext.Flags.ScriptFlags);
        }
    }
}
