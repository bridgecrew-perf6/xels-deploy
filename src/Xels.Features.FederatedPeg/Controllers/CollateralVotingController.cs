﻿using System;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Features.PoA.Voting;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonErrors;
using Xels.Bitcoin.Utilities.ModelStateErrors;

namespace Xels.Features.FederatedPeg.Controllers
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class CollateralVotingController : Controller
    {
        protected readonly IFederationManager fedManager;

        protected readonly VotingManager votingManager;

        protected readonly Network network;

        protected readonly ILogger logger;

        public CollateralVotingController(IFederationManager fedManager, VotingManager votingManager, Network network)
        {
            this.fedManager = fedManager;
            this.votingManager = votingManager;
            this.network = network;

            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>Schedules a vote to kick a federation member.</summary>
        /// <param name="request">See <see cref="CollateralFederationMemberModel"/>.</param>
        /// <response code="400">Not yet implemented</response>
        /// <response code="500">Request is null</response>
        /// <returns>See <see cref="cref="IActionResult"/>.</returns>
        [Route("schedulevote-kickfedmember")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult VoteKickFedMember([FromBody] CollateralFederationMemberModel request)
        {
            return this.VoteAddKickFedMember(request, false);
        }

        private IActionResult VoteAddKickFedMember(CollateralFederationMemberModel request, bool addMember)
        {
            Guard.NotNull(request, nameof(request));

            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            if (!this.fedManager.IsFederationMember)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Only federation members can vote", string.Empty);

            try
            {
                var key = new PubKey(request.PubKeyHex);

                if (this.fedManager.IsMultisigMember(key))
                    return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Multisig members can't be voted on", string.Empty);

                IFederationMember federationMember = new CollateralFederationMember(key, false, new Money(request.CollateralAmountSatoshis), request.CollateralMainchainAddress);

                byte[] fedMemberBytes = (this.network.Consensus.ConsensusFactory as PoAConsensusFactory).SerializeFederationMember(federationMember);

                this.votingManager.ScheduleVote(new VotingData()
                {
                    Key = addMember ? VoteKey.AddFederationMember : VoteKey.KickFederationMember,
                    Data = fedMemberBytes
                });

                return this.Ok();
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "There was a problem executing a command.", e.ToString());
            }
        }
    }
}
