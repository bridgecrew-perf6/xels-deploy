﻿using System.Collections.Generic;
using NBitcoin;
using NBitcoin.Policy;

namespace Xels.Bitcoin.Features.SmartContracts
{
    public class SmartContractTransactionPolicy : StandardTransactionPolicy
    {
        public SmartContractTransactionPolicy(Network network) : base(network)
        {
            // Allow bigger fees to be sent as we include gas as well.
            this.MaxTxFee = new FeeRate(Money.Coins(10));
        }

        /// <inheritdoc />
        protected override void CheckPubKey(Transaction transaction, List<TransactionPolicyError> errors)
        {
            if (this.CheckScriptPubKey)
            {
                foreach (Coin txout in transaction.Outputs.AsCoins())
                {
                    ScriptTemplate template = this.Network.StandardScriptsRegistry.GetTemplateFromScriptPubKey(txout.ScriptPubKey);

                    if (template == null && !txout.ScriptPubKey.IsSmartContractExec())
                        errors.Add(new OutputPolicyError("Non-Standard scriptPubKey", (int)txout.Outpoint.N));
                }
            }
        }

        /// <inheritdoc />
        protected override void CheckMinRelayTxFee(Transaction transaction, List<TransactionPolicyError> errors)
        {
            if (this.MinRelayTxFee != null)
            {
                foreach (TxOut output in transaction.Outputs)
                {
                    byte[] bytes = output.ScriptPubKey.ToBytes(true);

                    if (output.IsDust(this.MinRelayTxFee) && !IsOpReturn(bytes) && !output.ScriptPubKey.IsSmartContractExec())
                        errors.Add(new DustPolicyError(output.Value, output.GetDustThreshold(this.MinRelayTxFee)));
                }
            }
        }
    }
}