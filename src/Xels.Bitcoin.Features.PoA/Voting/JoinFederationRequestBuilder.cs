﻿using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.Policy;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.PoA.Features.Voting;
using Xels.Bitcoin.Utilities;

namespace Xels.Bitcoin.Features.PoA.Voting
{
    public static class JoinFederationRequestBuilder
    {
        public const decimal VotingRequestTransferAmount = 500m;
        public const int VotingRequestExpectedInputCount = 1;
        public const int VotingRequestExpectedOutputCount = 2;

        public static JoinFederationRequestResult BuildTransaction(IWalletTransactionHandler walletTransactionHandler, Network network, JoinFederationRequest request, JoinFederationRequestEncoder encoder, string walletName, string walletAccount, string walletPassword)
        {
            byte[] encodedVotingRequest = encoder.Encode(request);
            var votingOutputScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(encodedVotingRequest));

            var context = new TransactionBuildContext(network)
            {
                AccountReference = new WalletAccountReference(walletName, walletAccount),
                MinConfirmations = 0,
                FeeType = FeeType.High,
                WalletPassword = walletPassword,
                Recipients = new[] { new Recipient { Amount = new Money(VotingRequestTransferAmount, MoneyUnit.BTC), ScriptPubKey = votingOutputScript } }.ToList()
            };

            Transaction transaction = walletTransactionHandler.BuildTransaction(context);

            Guard.Assert(IsVotingRequestTransaction(transaction, encoder));

            if (context.TransactionBuilder.Verify(transaction, out TransactionPolicyError[] errors))
                return new JoinFederationRequestResult() { Transaction = transaction };

            return new JoinFederationRequestResult() { Errors = string.Join(" - ", errors.Select(s => s.ToString())) };
        }

        public static JoinFederationRequest Deconstruct(Transaction trx, JoinFederationRequestEncoder encoder)
        {
            if (trx.Outputs.Count != VotingRequestExpectedOutputCount)
                return null;

            IList<Op> ops = trx.Outputs[1].ScriptPubKey.ToOps();

            if (ops[0].Code != OpcodeType.OP_RETURN)
                return null;

            if (trx.Outputs[1].Value.ToDecimal(MoneyUnit.BTC) != VotingRequestTransferAmount)
                return null;

            return encoder.Decode(ops[1].PushData);
        }

        public static bool IsVotingRequestTransaction(Transaction trx, JoinFederationRequestEncoder encoder)
        {
            return Deconstruct(trx, encoder) != null;
        }
    }

    public sealed class JoinFederationRequestResult
    {
        public Transaction Transaction { get; set; }
        public string Errors { get; set; }
    }
}
