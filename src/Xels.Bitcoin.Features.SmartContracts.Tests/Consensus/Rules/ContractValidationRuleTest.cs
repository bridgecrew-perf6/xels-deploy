﻿using System.Collections.Generic;
using System.Threading.Tasks;
using NBitcoin;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Xels.Bitcoin.Features.SmartContracts.Rules;
using Xels.Bitcoin.Utilities;
using Xels.SmartContracts.CLR;
using Xels.SmartContracts.CLR.Serialization;
using Xels.SmartContracts.Networks;
using Xunit;

namespace Xels.Bitcoin.Features.SmartContracts.Tests.Consensus.Rules
{
    public class ContractValidationRuleTest
    {
        private readonly Network network;
        private readonly ICallDataSerializer callDataSerializer;

        public ContractValidationRuleTest()
        {
            this.network = new SmartContractsRegTest();
            this.callDataSerializer = new CallDataSerializer(new ContractPrimitiveSerializerV2(this.network));
        }

        private UnspentOutputSet GetMockOutputSet()
        {
            Transaction transaction = this.network.Consensus.ConsensusFactory.CreateTransaction();

            var fakeTxOut = new TxOut
            {
                Value = 100_000_000
            };

            var coins = new UnspentOutput(new OutPoint(uint256.Zero, 0), new Coins(0, fakeTxOut, false));

            var unspentOutputs = new UnspentOutputSet();
            unspentOutputs.SetCoins(new[] { coins });

            return unspentOutputs;
        }

        [Fact]
        public void SmartContractFormatRule_GasBudgetMaxDoesntOverflow()
        {
            checked
            {
#pragma warning disable CS0219 // Variable is assigned but its value is never used
                ulong test = SmartContractFormatLogic.GasPriceMaximum * SmartContractFormatLogic.GasLimitMaximum;
#pragma warning restore CS0219 // Variable is assigned but its value is never used
            }
        }

        [Fact]
        public async Task SmartContractFormatRule_SuccessAsync()
        {
            TestRulesContext testContext = TestRulesContextFactory.CreateAsync(this.network);
            ContractTransactionPartialValidationRule rule = testContext.CreateContractValidationRule();

            var context = new PowRuleContext(new ValidationContext(), testContext.DateTimeProvider.GetTimeOffset());
            context.UnspentOutputSet = GetMockOutputSet();
            context.ValidationContext.BlockToValidate = testContext.Network.Consensus.ConsensusFactory.CreateBlock();

            var gasPriceSatoshis = 20;
            var gasLimit = 4_000_000;
            var gasBudgetSatoshis = gasPriceSatoshis * gasLimit;
            var relayFeeSatoshis = 10000;

            var totalSuppliedSatoshis = gasBudgetSatoshis + relayFeeSatoshis;

            var contractTxData = new ContractTxData(1, (ulong)gasPriceSatoshis, (Xels.SmartContracts.RuntimeObserver.Gas)gasLimit, 0, "TestMethod");
            var serialized = this.callDataSerializer.Serialize(contractTxData);

            Transaction funding = new Transaction
            {
                Outputs =
                {
                    new TxOut(totalSuppliedSatoshis, new Script())
                }
            };

            var transactionBuilder = new TransactionBuilder(testContext.Network);
            transactionBuilder.AddCoins(funding);
            transactionBuilder.SendFees(relayFeeSatoshis + gasBudgetSatoshis);
            transactionBuilder.Send(new Script(serialized), 0);

            Transaction transaction = transactionBuilder.BuildTransaction(false);

            context.ValidationContext.BlockToValidate.Transactions = new List<Transaction>
            {
               transaction
            };

            await rule.RunAsync(context);
        }

        [Fact]
        public async Task SmartContractFormatRule_MultipleOutputs_SuccessAsync()
        {
            TestRulesContext testContext = TestRulesContextFactory.CreateAsync(this.network);
            ContractTransactionPartialValidationRule rule = testContext.CreateContractValidationRule();

            var context = new PowRuleContext(new ValidationContext(), testContext.DateTimeProvider.GetTimeOffset())
            {
                UnspentOutputSet = GetMockOutputSet()
            };

            context.ValidationContext.BlockToValidate = testContext.Network.Consensus.ConsensusFactory.CreateBlock();

            var gasPriceSatoshis = 20;
            var gasLimit = 4000000;
            var gasBudgetSatoshis = gasPriceSatoshis * gasLimit;
            var relayFeeSatoshis = 10000;
            var change = 200000;

            var totalSuppliedSatoshis = gasBudgetSatoshis + relayFeeSatoshis;

            var contractTxData = new ContractTxData(1, (ulong)gasPriceSatoshis, (Xels.SmartContracts.RuntimeObserver.Gas)gasLimit, 0, "TestMethod");
            var serialized = this.callDataSerializer.Serialize(contractTxData);

            Transaction funding = new Transaction
            {
                Outputs =
                {
                    new TxOut(totalSuppliedSatoshis + change, new Script())
                }
            };

            var transactionBuilder = new TransactionBuilder(testContext.Network);
            transactionBuilder.AddCoins(funding);
            transactionBuilder.SendFees(totalSuppliedSatoshis);
            transactionBuilder.Send(new Script(serialized), 0);

            // Add a change output to the transaction
            transactionBuilder.SetChange(new Script());

            Transaction transaction = transactionBuilder.BuildTransaction(false);

            context.ValidationContext.BlockToValidate.Transactions = new List<Transaction>
            {
                transaction
            };

            await rule.RunAsync(context);
        }

        /// <summary>
        /// In this test we supply a higher gas limit in our carrier than what we budgeted for in our transaction
        /// </summary>
        [Fact]
        public async Task SmartContractFormatRule_FailureAsync()
        {
            TestRulesContext testContext = TestRulesContextFactory.CreateAsync(this.network);
            ContractTransactionPartialValidationRule rule = testContext.CreateContractValidationRule();

            var context = new PowRuleContext(new ValidationContext(), testContext.DateTimeProvider.GetTimeOffset());

            context.ValidationContext.BlockToValidate = testContext.Network.Consensus.ConsensusFactory.CreateBlock();

            var gasPriceSatoshis = 20;
            var gasLimit = 4000000;
            var gasBudgetSatoshis = gasPriceSatoshis * gasLimit;
            var relayFeeSatoshis = 10000;

            var totalSuppliedSatoshis = gasBudgetSatoshis + relayFeeSatoshis;

            var higherGasLimit = gasLimit + 10000;

            var contractTxData = new ContractTxData(1, (ulong)gasPriceSatoshis, (Xels.SmartContracts.RuntimeObserver.Gas)higherGasLimit, 0, "TestMethod");
            var serialized = this.callDataSerializer.Serialize(contractTxData);

            Transaction funding = new Transaction
            {
                Outputs =
                {
                    new TxOut(totalSuppliedSatoshis, new Script())
                }
            };

            var transactionBuilder = new TransactionBuilder(testContext.Network);
            transactionBuilder.AddCoins(funding);
            transactionBuilder.SendFees(relayFeeSatoshis);
            transactionBuilder.Send(new Script(serialized), gasBudgetSatoshis);

            Transaction transaction = transactionBuilder.BuildTransaction(false);

            context.ValidationContext.BlockToValidate.Transactions = new List<Transaction>
            {
                new Transaction(), // Include an empty transaction to ensure we're checking multiple.
                transaction
            };

            await Assert.ThrowsAsync<ConsensusErrorException>(async () => await rule.RunAsync(context));
        }
    }
}