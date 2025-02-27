﻿using Moq;
using NBitcoin;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.SmartContracts.Rules;
using Xels.SmartContracts.Core.State;
using Xels.SmartContracts.Networks;
using Xunit;

namespace Xels.Bitcoin.Features.SmartContracts.Tests.Consensus.Rules
{
    public class P2PKHNotContractRuleTests
    {
        private readonly Network network;

        public P2PKHNotContractRuleTests()
        {
            this.network = new SmartContractsRegTest();
        }

        [Fact]
        public void SendTo_NotAContract_Success()
        {
            var walletAddress = new uint160(321);

            var state = new Mock<IStateRepositoryRoot>();
            state.Setup(x => x.GetAccountState(walletAddress)).Returns<AccountState>(null);

            Transaction transaction = this.network.CreateTransaction();
            transaction.Outputs.Add(new TxOut(100, PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(new KeyId(walletAddress))));
            P2PKHNotContractRule.CheckTransaction(state.Object, transaction);
        }

        [Fact]
        public void SendTo_Contract_Fails()
        {
            var contractAddress = new uint160(123);

            var state = new Mock<IStateRepositoryRoot>();
            state.Setup(x => x.GetAccountState(contractAddress)).Returns(new AccountState()); // not null
            
            Transaction transaction = this.network.CreateTransaction();
            transaction.Outputs.Add(new TxOut(100, PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(new KeyId(contractAddress))));
            Assert.Throws<ConsensusErrorException>(() => P2PKHNotContractRule.CheckTransaction(state.Object, transaction));
        }
    }
}
