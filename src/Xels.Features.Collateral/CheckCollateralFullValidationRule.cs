﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Consensus.Rules;
using Xels.Bitcoin.Features.BlockStore.AddressIndexing;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;
using Xels.Features.PoA.Collateral;

namespace Xels.Features.Collateral
{
    /// <summary>Ensures that collateral requirement on counterpart chain is fulfilled for the federation member that produced a block.</summary>
    /// <remarks>Ignored in IBD.</remarks>
    public class CheckCollateralFullValidationRule : FullValidationConsensusRule
    {
        private readonly IInitialBlockDownloadState ibdState;

        private readonly ICollateralChecker collateralChecker;

        private readonly IDateTimeProvider dateTime;

        private readonly Network network;

        /// <summary>For how many seconds the block should be banned in case collateral check failed.</summary>
        private readonly int collateralCheckBanDurationSeconds;

        private readonly IFederationHistory federationHistory;

        public CheckCollateralFullValidationRule(IInitialBlockDownloadState ibdState, ICollateralChecker collateralChecker,
            IDateTimeProvider dateTime, Network network, IFederationHistory federationHistory)
        {
            this.network = network;
            this.ibdState = ibdState;
            this.collateralChecker = collateralChecker;
            this.dateTime = dateTime;
            this.federationHistory = federationHistory;

            this.collateralCheckBanDurationSeconds = (int)(this.network.Consensus.Options as PoAConsensusOptions).TargetSpacingSeconds / 2;
        }

        public override Task RunAsync(RuleContext context)
        {
            if (this.ibdState.IsInitialBlockDownload())
            {
                this.Logger.LogTrace("(-)[SKIPPED_IN_IBD]");
                return Task.CompletedTask;
            }

            var commitmentHeightEncoder = new CollateralHeightCommitmentEncoder();
            (int? commitmentHeight, uint? commitmentNetworkMagic) = commitmentHeightEncoder.DecodeCommitmentHeight(context.ValidationContext.BlockToValidate.Transactions.First());
            if (commitmentHeight == null)
            {
                // We return here as it is CheckCollateralCommitmentHeightRule's responsibility to perform this check.
                this.Logger.LogTrace("(-)SKIPPED_AS_COLLATERAL_COMMITMENT_HEIGHT_MISSING]");
                return Task.CompletedTask;
            }

            this.Logger.LogDebug("Commitment is: {0}. Magic is: {1}", commitmentHeight, commitmentNetworkMagic);

            // TODO: Both this and CollateralPoAMiner are using this chain's MaxReorg instead of the Counter chain's MaxReorg. Beware: fixing requires fork.
            int counterChainHeight = this.collateralChecker.GetCounterChainConsensusHeight();
            int maxReorgLength = AddressIndexer.GetMaxReorgOrFallbackMaxReorg(this.network);

            // Check if commitment height is less than `mainchain consensus tip height - MaxReorg`.
            if (commitmentHeight > counterChainHeight - maxReorgLength)
            {
                // Temporary reject the block since it's possible that due to network connectivity problem counter chain is out of sync and
                // we are relying on chain state old data. It is possible that when we advance on counter chain commitment height will be
                // sufficiently old.
                context.ValidationContext.RejectUntil = this.dateTime.GetUtcNow() + TimeSpan.FromSeconds(this.collateralCheckBanDurationSeconds);

                this.Logger.LogDebug("commitmentHeight is {0}, counterChainHeight is {1}.", commitmentHeight, counterChainHeight);

                this.Logger.LogTrace("(-)[COMMITMENT_TOO_NEW]");
                PoAConsensusErrors.InvalidCollateralAmountCommitmentTooNew.Throw();
            }

            IFederationMember federationMember = this.federationHistory.GetFederationMemberForBlock(context.ValidationContext.ChainedHeaderToValidate);
            if (!this.collateralChecker.CheckCollateral(federationMember, commitmentHeight.Value))
            {
                // By setting rejectUntil we avoid banning a peer that provided a block.
                context.ValidationContext.RejectUntil = this.dateTime.GetUtcNow() + TimeSpan.FromSeconds(this.collateralCheckBanDurationSeconds);

                this.Logger.LogTrace("(-)[BAD_COLLATERAL]");
                PoAConsensusErrors.InvalidCollateralAmount.Throw();
            }

            return Task.CompletedTask;
        }
    }
}
