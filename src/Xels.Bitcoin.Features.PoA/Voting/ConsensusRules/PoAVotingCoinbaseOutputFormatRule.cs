﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Consensus.Rules;
using TracerAttributes;

namespace Xels.Bitcoin.Features.PoA.Voting.ConsensusRules
{
    /// <summary>Validates <see cref="VotingData"/> collection format if voting output is present in the coinbase transaction.</summary>
    public class PoAVotingCoinbaseOutputFormatRule : PartialValidationConsensusRule
    {
        private VotingDataEncoder votingDataEncoder;

        [NoTrace]
        public override void Initialize()
        {
            this.votingDataEncoder = new VotingDataEncoder();

            base.Initialize();
        }

        public override Task RunAsync(RuleContext context)
        {
            Transaction coinbase = context.ValidationContext.BlockToValidate.Transactions[0];

            byte[] votingDataBytes = this.votingDataEncoder.ExtractRawVotingData(coinbase);

            if (votingDataBytes == null)
            {
                this.Logger.LogTrace("(-)[NO_VOTING_DATA]");
                return Task.CompletedTask;
            }

            List<VotingData> votingDataList = this.votingDataEncoder.Decode(votingDataBytes);

            if (votingDataList.Count == 0)
            {
                this.Logger.LogTrace("(-)[EMPTY]");
                PoAConsensusErrors.VotingDataInvalidFormat.Throw();
            }

            return Task.CompletedTask;
        }
    }
}
