﻿using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.MemoryPool.Interfaces;
using Xels.Bitcoin.Features.SmartContracts.ReflectionExecutor.Consensus.Rules;
using Xels.Bitcoin.Features.SmartContracts.Rules;
using Xels.SmartContracts.CLR;

namespace Xels.Bitcoin.Features.SmartContracts.MempoolRules
{
    /// <summary>
    /// Validates that a smart contract transaction can be deserialized correctly, and that it conforms to gas
    /// price and gas limit rules.
    /// </summary>
    public class SmartContractFormatLogicMempoolRule : MempoolRule
    {
        private readonly ICallDataSerializer callDataSerializer;

        public SmartContractFormatLogicMempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings mempoolSettings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory,
            ICallDataSerializer callDataSerializer) : base(network, mempool, mempoolSettings, chainIndexer, loggerFactory)
        {
            this.callDataSerializer = callDataSerializer;
        }

        /// <inheritdoc/>
        public override void CheckTransaction(MempoolValidationContext context)
        {
            TxOut scTxOut = context.Transaction.TryGetSmartContractTxOut();

            if (scTxOut == null)
            {
                // No SC output to validate.
                return;
            }

            ContractTxData txData = ContractTransactionChecker.GetContractTxData(this.callDataSerializer, scTxOut);

            SmartContractFormatLogic.Check(txData, context.Fees);
        }
    }
}