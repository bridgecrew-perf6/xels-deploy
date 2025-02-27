﻿using Xels.Bitcoin.Consensus;

namespace Xels.Bitcoin.Features.PoA
{
    /// <summary>Rules that might be thrown by consensus rules that are specific to PoA consensus.</summary>
    public static class PoAConsensusErrors
    {
        public static ConsensusError InvalidHeaderBits => new ConsensusError("invalid-header-bits", "invalid header bits");

        public static ConsensusError InvalidHeaderTimestamp => new ConsensusError("invalid-header-timestamp", "invalid header timestamp");

        public static ConsensusError InvalidHeaderSignature => new ConsensusError("invalid-header-signature", "invalid header signature");

        public static ConsensusError InvalidBlockSignature => new ConsensusError("invalid-block-signature", "invalid block signature");

        // Voting related errors.
        public static ConsensusError BlockMissingVotes => new ConsensusError("missing-block-votes", "missing block votes");

        public static ConsensusError TooManyVotingOutputs => new ConsensusError("too-many-voting-outputs", "there could be only 1 voting output");

        public static ConsensusError VotingDataInvalidFormat => new ConsensusError("invalid-voting-data-format", "voting data format is invalid");

        public static ConsensusError VotingRequestInvalidFormat => new ConsensusError("invalid-voting-request-format", "voting request format is invalid");

        public static ConsensusError InvalidVotingOnMultiSig => new ConsensusError("invalid-voting-on-multisig", "invalid voting on multisig member");

        public static ConsensusError VotingRequestInvalidCollateralReuse => new ConsensusError("invalid-voting-request-collateral", "invalid voting request collateral re-use");

        // Collateral related errors.
        public static ConsensusError InvalidCollateralAmount => new ConsensusError("invalid-collateral-amount", "collateral requirement is not fulfilled");

        public static ConsensusError InvalidCollateralRequirement => new ConsensusError("invalid-collateral-requirement", "collateral requirement is invalid");

        public static ConsensusError CollateralCommitmentHeightMissing => new ConsensusError("collateral-commitment-height-missing", "collateral commitment height missing");

        public static ConsensusError InvalidCollateralAmountCommitmentTooNew => new ConsensusError("collateral-commitment-too-new", "collateral commitment too new");
    }
}
