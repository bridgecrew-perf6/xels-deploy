﻿using System;
using System.Collections.Generic;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Features.Consensus.Tests.Rules.CommonRules
{
    /// <summary>
    /// These tests only cover the first part of BIP68 and not the MaxSigOps, coinview update or scripts verify or calculate block rewards
    /// </summary>
    public class PowCoinViewRuleTests
    {
        private Exception caughtExecption;
        private readonly Network network;
        private Mock<ILogger> logger;
        private const int HeightOfBlockchain = 1;
        private RuleContext ruleContext;
        private UnspentOutputSet coinView;
        private Transaction transactionWithCoinbaseFromPreviousBlock;
        private readonly CoinViewRule rule;

        public PowCoinViewRuleTests()
        {
            this.network = KnownNetworks.RegTest;
            this.rule = new PowCoinviewRule();
        }

        [Fact]
        public void RunAsync_ValidatingATransactionThatIsNotCoinBaseButStillHasUnspentOutputsWithoutInput_ThrowsBadTransactionMissingInput()
        {
            this.GivenACoinbaseTransactionFromAPreviousBlock();
            this.AndARuleContext();
            this.AndSomeUnspentOutputs();
            this.AndATransactionWithNoUnspentOutputsAsInput();
            this.WhenExecutingTheRule(this.rule, this.ruleContext);
            this.ThenExceptionThrownIs(ConsensusErrors.BadTransactionMissingInput);
        }

        [Fact]
        //NOTE: This is not full coverage of BIP68 bad transaction non final as a block earlier than allowable timestamp is also not allowable under BIP68.
        public void RunAsync_ValidatingABlockHeightLowerThanBIP86Allows_ThrowsBadTransactionNonFinal()
        {
            this.GivenACoinbaseTransactionFromAPreviousBlock();
            this.AndARuleContext();
            this.AndSomeUnspentOutputs();
            this.AndATransactionBlockHeightLowerThanBip68Allows();
            this.WhenExecutingTheRule(this.rule, this.ruleContext);
            this.ThenExceptionThrownIs(ConsensusErrors.BadTransactionNonFinal);
        }

        private void AndSomeUnspentOutputs()
        {
            this.coinView = new UnspentOutputSet();
            this.coinView.SetCoins(new UnspentOutput[0]);
            (this.ruleContext as UtxoRuleContext).UnspentOutputSet = this.coinView;
            this.coinView.Update(this.network, this.transactionWithCoinbaseFromPreviousBlock, 0);
        }

        private void AndARuleContext()
        {
            this.ruleContext = new PowRuleContext { };
            this.ruleContext.ValidationContext = new ValidationContext();
            BlockHeader blockHeader = this.network.Consensus.ConsensusFactory.CreateBlockHeader();
            this.ruleContext.ValidationContext.ChainedHeaderToValidate = new ChainedHeader(blockHeader, blockHeader.GetHash(), HeightOfBlockchain);

            Block block = this.network.CreateBlock();
            block.Transactions = new List<Transaction>();
            this.ruleContext.ValidationContext.BlockToValidate = block;
        }

        protected void WhenExecutingTheRule(ConsensusRuleBase rule, RuleContext ruleContext)
        {
            try
            {
                this.logger = new Mock<ILogger>();
                rule.Logger = this.logger.Object;

                var loggerFactory = ExtendedLoggerFactory.Create();

                var dateTimeProvider = new DateTimeProvider();

                rule.Parent = new PowConsensusRuleEngine(
                    KnownNetworks.RegTest,
                    new Mock<ILoggerFactory>().Object,
                    new Mock<IDateTimeProvider>().Object,
                    new ChainIndexer(this.network),
                    new NodeDeployments(KnownNetworks.RegTest, new ChainIndexer(this.network)),
                    new ConsensusSettings(NodeSettings.Default(KnownNetworks.RegTest)), new Mock<ICheckpoints>().Object, new Mock<ICoinView>().Object, new Mock<IChainState>().Object,
                    new InvalidBlockHashStore(dateTimeProvider),
                    new NodeStats(dateTimeProvider, NodeSettings.Default(this.network), new Mock<IVersionProvider>().Object),
                    new AsyncProvider(loggerFactory, new Mock<ISignals>().Object),
                    new ConsensusRulesContainer());

                rule.Initialize();

                (rule as AsyncConsensusRule).RunAsync(ruleContext).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                this.caughtExecption = e;
            }
        }

        private void ThenExceptionThrownIs(ConsensusError consensusErrorType)
        {
            this.caughtExecption.Should().NotBeNull();
            this.caughtExecption.Should().BeOfType<ConsensusErrorException>();
            var consensusErrorException = (ConsensusErrorException)this.caughtExecption;
            consensusErrorException.ConsensusError.Should().Be(consensusErrorType);
        }

        private void AndATransactionBlockHeightLowerThanBip68Allows()
        {
            var transaction = new Transaction
            {
                Inputs = { new TxIn()
                {
                    PrevOut = new OutPoint(this.transactionWithCoinbaseFromPreviousBlock, 0),
                    Sequence = HeightOfBlockchain + 1, //this sequence being higher triggers the ThrowsBadTransactionNonFinal
                } },
                Outputs = { new TxOut() },
                Version = 2, // So that sequence locks considered (BIP68)
            };

            this.ruleContext.Flags = new DeploymentFlags() { LockTimeFlags = Transaction.LockTimeFlags.VerifySequence };
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(transaction);
        }

        private void GivenACoinbaseTransactionFromAPreviousBlock()
        {
            this.transactionWithCoinbaseFromPreviousBlock = new Transaction();
            var txIn = new TxIn { PrevOut = new OutPoint() };
            this.transactionWithCoinbaseFromPreviousBlock.AddInput(txIn);
            this.transactionWithCoinbaseFromPreviousBlock.AddOutput(new TxOut());
        }

        private void AndATransactionWithNoUnspentOutputsAsInput()
        {
            this.ruleContext.ValidationContext.BlockToValidate.Transactions.Add(new Transaction { Inputs = { new TxIn() } });
        }
    }
}
