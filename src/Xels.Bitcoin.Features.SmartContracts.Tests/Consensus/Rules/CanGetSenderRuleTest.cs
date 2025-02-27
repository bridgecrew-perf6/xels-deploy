﻿using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.Consensus.Rules;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.MemoryPool.Interfaces;
using Xels.Bitcoin.Features.SmartContracts.MempoolRules;
using Xels.Bitcoin.Features.SmartContracts.Rules;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xels.SmartContracts.Core.Util;
using Xels.SmartContracts.Networks;
using Xunit;
using Block = NBitcoin.Block;

namespace Xels.Bitcoin.Features.SmartContracts.Tests.Consensus.Rules
{
    public class CanGetSenderRuleTest
    {
        private readonly Network network;
        private readonly CanGetSenderRule rule;
        private readonly CanGetSenderMempoolRule mempoolRule;
        private readonly Mock<ISenderRetriever> senderRetriever;

        public CanGetSenderRuleTest()
        {
            this.network = new SmartContractsRegTest();
            this.senderRetriever = new Mock<ISenderRetriever>();
            this.rule = new CanGetSenderRule(this.senderRetriever.Object);
            this.mempoolRule = new CanGetSenderMempoolRule(this.network, new Mock<ITxMempool>().Object, new MempoolSettings(new NodeSettings(this.network)), new ChainIndexer(this.network), this.senderRetriever.Object, new Mock<ILoggerFactory>().Object);
            this.rule.Parent = new PowConsensusRuleEngine(
                this.network,
                new Mock<ILoggerFactory>().Object,
                new Mock<IDateTimeProvider>().Object,
                new ChainIndexer(this.network),
                new NodeDeployments(KnownNetworks.RegTest, new ChainIndexer(this.network)),
                new ConsensusSettings(NodeSettings.Default(this.network)), new Mock<ICheckpoints>().Object, new Mock<ICoinView>().Object, new Mock<IChainState>().Object,
                new InvalidBlockHashStore(null),
                new NodeStats(DateTimeProvider.Default, NodeSettings.Default(network), new Mock<IVersionProvider>().Object),
                new AsyncProvider(new Mock<ILoggerFactory>().Object, new Mock<ISignals>().Object),
                new ConsensusRulesContainer());

            this.rule.Initialize();
        }

        [Fact]
        public void P2PKH_GetSender_Passes()
        {
            var successResult = GetSenderResult.CreateSuccess(new uint160(0));
            this.senderRetriever.Setup(x => x.GetSender(It.IsAny<Transaction>(), It.IsAny<MempoolCoinView>()))
                .Returns(successResult);
            this.senderRetriever.Setup(x => x.GetSender(It.IsAny<Transaction>(), It.IsAny<ICoinView>(), It.IsAny<IList<Transaction>>()))
                .Returns(successResult);

            Transaction transaction = this.network.CreateTransaction();
            transaction.Outputs.Add(new TxOut(100, new Script(new byte[] { (byte)ScOpcodeType.OP_CREATECONTRACT })));

            // Mempool check works
            this.mempoolRule.CheckTransaction(new MempoolValidationContext(transaction, new MempoolValidationState(false)));

            // Block validation check works
            Block block = this.network.CreateBlock();
            block.AddTransaction(transaction);
            this.rule.RunAsync(new RuleContext(new ValidationContext { BlockToValidate = block }, DateTimeOffset.Now));
        }

        [Fact]
        public void P2PKH_GetSender_Fails()
        {
            var failResult = GetSenderResult.CreateFailure("String error");
            this.senderRetriever.Setup(x => x.GetSender(It.IsAny<Transaction>(), It.IsAny<MempoolCoinView>()))
                .Returns(failResult);
            this.senderRetriever.Setup(x => x.GetSender(It.IsAny<Transaction>(), It.IsAny<ICoinView>(), It.IsAny<IList<Transaction>>()))
                .Returns(failResult);

            Transaction transaction = this.network.CreateTransaction();
            transaction.Outputs.Add(new TxOut(100, new Script(new byte[] { (byte)ScOpcodeType.OP_CREATECONTRACT })));

            // Mempool check fails
            Assert.ThrowsAny<ConsensusErrorException>(() => this.mempoolRule.CheckTransaction(new MempoolValidationContext(transaction, new MempoolValidationState(false))));

            // Block validation check fails
            Block block = this.network.CreateBlock();
            block.AddTransaction(transaction);
            Assert.ThrowsAnyAsync<ConsensusErrorException>(() => this.rule.RunAsync(new RuleContext(new ValidationContext { BlockToValidate = block }, DateTimeOffset.Now)));
        }
    }
}
