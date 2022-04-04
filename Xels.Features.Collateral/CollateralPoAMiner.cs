using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Consensus.Validators;
using Xels.Bitcoin.EventBus.CoreEvents;
using Xels.Bitcoin.Features.BlockStore.AddressIndexing;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Features.PoA.Voting;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Mining;
using Xels.Bitcoin.Primitives;
using Xels.Bitcoin.Utilities;
using Xels.Features.PoA.Collateral;
using Xels.Features.PoA.Collateral.CounterChain;

namespace Xels.Features.Collateral
{
    /// <summary>
    /// Collateral aware version of <see cref="PoAMiner"/>. At the block template creation it will check our own collateral at a commitment height which is
    /// calculated in a following way: <c>counter chain height - maxReorgLength - AddressIndexer.SyncBuffer</c>. Then commitment height is encoded in
    /// OP_RETURN output of a coinbase transaction.
    /// </summary>
    public class CollateralPoAMiner : PoAMiner
    {
        private readonly CollateralHeightCommitmentEncoder encoder;

        private readonly ICollateralChecker collateralChecker;

        private readonly Network counterChainNetwork;

        private readonly ChainIndexer chainIndexer;

        private readonly JoinFederationRequestMonitor joinFederationRequestMonitor;

        public CollateralPoAMiner(IConsensusManager consensusManager, IDateTimeProvider dateTimeProvider, Network network, INodeLifetime nodeLifetime, ILoggerFactory loggerFactory,
            IInitialBlockDownloadState ibdState, BlockDefinition blockDefinition, ISlotsManager slotsManager, IConnectionManager connectionManager, JoinFederationRequestMonitor joinFederationRequestMonitor,
            PoABlockHeaderValidator poaHeaderValidator, IFederationManager federationManager, IFederationHistory federationHistory, IIntegrityValidator integrityValidator, IWalletManager walletManager, ChainIndexer chainIndexer,
            INodeStats nodeStats, VotingManager votingManager, PoASettings poAMinerSettings, ICollateralChecker collateralChecker, IAsyncProvider asyncProvider, ICounterChainSettings counterChainSettings,
            IIdleFederationMembersKicker idleFederationMembersKicker,
            NodeSettings nodeSettings)
            : base(consensusManager, dateTimeProvider, network, nodeLifetime, loggerFactory, ibdState, blockDefinition, slotsManager, connectionManager,
            poaHeaderValidator, federationManager, federationHistory, integrityValidator, walletManager, nodeStats, votingManager, poAMinerSettings, asyncProvider, idleFederationMembersKicker, nodeSettings)
        {
            this.counterChainNetwork = counterChainSettings.CounterChainNetwork;
            this.collateralChecker = collateralChecker;
            this.encoder = new CollateralHeightCommitmentEncoder();
            this.chainIndexer = chainIndexer;
            this.joinFederationRequestMonitor = joinFederationRequestMonitor;
        }

        /// <inheritdoc />
        protected override void FillBlockTemplate(BlockTemplate blockTemplate, out bool dropTemplate)
        {
            OnBeforeFillBlockTemplate();

            base.FillBlockTemplate(blockTemplate, out dropTemplate);

            int counterChainHeight = this.collateralChecker.GetCounterChainConsensusHeight();
            int maxReorgLength = AddressIndexer.GetMaxReorgOrFallbackMaxReorg(this.network);

            int commitmentHeight = counterChainHeight - maxReorgLength - AddressIndexer.SyncBuffer;

            if (commitmentHeight <= 0)
            {
                dropTemplate = true;
                if (counterChainHeight != 0)
                    this.logger.LogWarning("Counter chain should first advance at least at {0}! Block can't be produced.", maxReorgLength + AddressIndexer.SyncBuffer);

                this.logger.LogTrace("(-)[LOW_COMMITMENT_HEIGHT]");
                return;
            }

            IFederationMember currentMember = this.federationManager.GetCurrentFederationMember();

            if (currentMember == null)
            {
                dropTemplate = true;
                this.logger.LogWarning("Unable to get this node's federation member!");
                this.logger.LogTrace("(-)[CANT_GET_FED_MEMBER]");
                return;
            }

            // Check our own collateral at a given commitment height.
            bool success = this.collateralChecker.CheckCollateral(currentMember, commitmentHeight);

            if (!success)
            {
                dropTemplate = true;
                this.logger.LogWarning("Failed to fulfill collateral requirement for mining!");
                this.logger.LogTrace("(-)[BAD_COLLATERAL]");
                return;
            }

            // Add height commitment.
            byte[] encodedHeight = this.encoder.EncodeCommitmentHeight(commitmentHeight);

            var heightCommitmentScript = new Script(OpcodeType.OP_RETURN, Op.GetPushOp(encodedHeight), Op.GetPushOp(this.counterChainNetwork.MagicBytes));
            blockTemplate.Block.Transactions[0].AddOutput(Money.Zero, heightCommitmentScript);
        }


        /// <summary>
        /// It is possible that this node was not a federation member at the time a pending poll was started.
        /// As such the node would not have voted up to now. We have to check if a vote should be added now.
        /// </summary>
        /// <remarks>
        /// There is another scenario catered for by this method. It's the situation where a node crashed or is
        /// stopped when it contains scheduled "add member" votes that have not yet been added to a block.
        /// </remarks>
        private void OnBeforeFillBlockTemplate()
        {
            if (!this.federationManager.IsFederationMember || !this.network.ConsensusOptions.VotingEnabled)
                return;

            try
            {
                List<Poll> pendingAddFederationMemberPolls = this.votingManager.GetPendingPolls().Where(p => p.VotingData.Key == VoteKey.AddFederationMember).ToList();

                // Filter all polls where this federation number has not voted on.
                pendingAddFederationMemberPolls = pendingAddFederationMemberPolls.Where(p => !p.PubKeysHexVotedInFavor.Any(v => v.PubKey == this.federationManager.CurrentFederationKey.PubKey.ToString())).ToList();

                if (!pendingAddFederationMemberPolls.Any())
                {
                    this.logger.LogDebug("There are no outstanding add member polls for this node to vote on.");
                    return;
                }

                foreach (Poll poll in pendingAddFederationMemberPolls)
                {
                    this.logger.LogDebug($"Attempting to cast outstanding vote on poll '{poll.Id}'.");

                    ChainedHeader pollStartHeader = this.chainIndexer.GetHeader(poll.PollStartBlockData.Hash);
                    ChainedHeader votingRequestHeader = pollStartHeader.Previous;

                    ChainedHeaderBlock blockData = this.consensusManager.GetBlockData(votingRequestHeader.HashBlock);

                    this.joinFederationRequestMonitor.OnBlockConnected(new BlockConnected(new ChainedHeaderBlock(blockData.Block, votingRequestHeader)));
                }

                return;
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return;
            }
        }
    }
}
