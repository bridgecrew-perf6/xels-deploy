﻿using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Configuration.Logging;
using Xels.SmartContracts.CLR.ResultProcessors;
using Xels.SmartContracts.Networks;
using Xunit;

namespace Xels.SmartContracts.CLR.Tests
{
    public sealed class ContractRefundProcessorTests
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly Network network;
        private readonly IContractRefundProcessor refundProcessor;

        public ContractRefundProcessorTests()
        {
            this.loggerFactory = ExtendedLoggerFactory.Create();
            this.network = new SmartContractsRegTest();
            this.refundProcessor = new ContractRefundProcessor(this.loggerFactory);
        }

        [Fact]
        public void ContractExecutionResult_RefundDue_AdjustFee()
        {
            var contractAddress = new uint160(1);

            var contractTxData = new ContractTxData(1, 1, (RuntimeObserver.Gas)5000, contractAddress, "ThrowException");
            var sender = new uint160(2);

            (Money fee, TxOut refund) = this.refundProcessor.Process(contractTxData, new Money(10500), sender, (RuntimeObserver.Gas)950, false);

            Assert.Equal(6450, fee);
            Assert.Equal(sender.ToBytes(), refund.ScriptPubKey.GetDestination(this.network).ToBytes());
            Assert.Equal(4050, refund.Value);
        }

        [Fact]
        public void ContractExecutionResult_NoRefundDue_NoFeeAdjustment()
        {
            var contractAddress = new uint160(1);

            var contractTxData = new ContractTxData(1, 1, (RuntimeObserver.Gas)5000, contractAddress, "ThrowException");
            var sender = new uint160(2);

            (Money fee, TxOut refund) = this.refundProcessor.Process(contractTxData, new Money(10500), sender, (RuntimeObserver.Gas)5000, false);

            Assert.Equal(10500, fee);
            Assert.Null(refund);
        }

        [Fact]
        public void ContractExecutionResult_OutOfGasException_NoRefundDue_NoFeeAdjustment()
        {
            var contractAddress = new uint160(1);

            var contractTxData = new ContractTxData(1, 1, (RuntimeObserver.Gas)5000, contractAddress, "ThrowException");
            var sender = new uint160(2);

            (Money fee, TxOut refund) = this.refundProcessor.Process(contractTxData, new Money(10500), sender, (RuntimeObserver.Gas)5000, true);

            Assert.Equal(10500, fee);
            Assert.Null(refund);
        }
    }
}