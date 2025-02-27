﻿using System;
using System.Collections.Generic;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.Base.Deployments.Models;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Controllers;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonErrors;

namespace Xels.Bitcoin.Features.Consensus
{
    /// <summary>
    /// A <see cref="FeatureController"/> that provides API and RPC methods from the consensus loop.
    /// </summary>
    [ApiVersion("1")]
    public class ConsensusController : FeatureController
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        public ConsensusController(
            ILoggerFactory loggerFactory,
            IChainState chainState,
            IConsensusManager consensusManager,
            ChainIndexer chainIndexer)
            : base(chainState: chainState, consensusManager: consensusManager, chainIndexer: chainIndexer)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(chainIndexer, nameof(chainIndexer));
            Guard.NotNull(chainState, nameof(chainState));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <summary>
        /// Implements the getbestblockhash RPC call.
        /// </summary>
        /// <returns>A <see cref="uint256"/> hash of the block at the consensus tip.</returns>
        [ActionName("getbestblockhash")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Get the hash of the block at the consensus tip.")]
        public uint256 GetBestBlockHashRPC()
        {
            return this.ChainState.ConsensusTip?.HashBlock;
        }

        /// <summary>
        /// Get the threshold states of softforks currently being deployed.
        /// Allowable states are: Defined, Started, LockedIn, Failed, Active.
        /// </summary>
        /// <returns>A <see cref="JsonResult"/> object derived from a list of
        /// <see cref="ThresholdStateModel"/> objects - one per deployment.
        /// Returns an <see cref="ErrorResult"/> if the method fails.</returns>
        /// <response code="200">Returns the list of deployment flags</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("api/[controller]/deploymentflags")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult DeploymentFlags()
        {
            try
            {
                ConsensusRuleEngine ruleEngine = this.ConsensusManager.ConsensusRules as ConsensusRuleEngine;

                // Ensure threshold conditions cached.
                ThresholdState[] thresholdStates = ruleEngine.NodeDeployments.BIP9.GetStates(this.ChainState.ConsensusTip.Previous);

                List<ThresholdStateModel> metrics = ruleEngine.NodeDeployments.BIP9.GetThresholdStateMetrics(this.ChainState.ConsensusTip.Previous, thresholdStates);

                return this.Json(metrics);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Gets the hash of the block at the consensus tip.
        /// </summary>
        /// <returns>Json formatted <see cref="uint256"/> hash of the block at the consensus tip. Returns <see cref="IActionResult"/> formatted error if fails.</returns>
        /// <remarks>This is an API implementation of an RPC call.</remarks>
        /// <response code="200">Returns the block hash</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("api/[controller]/getbestblockhash")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetBestBlockHashAPI()
        {
            try
            {
                return this.Json(this.GetBestBlockHashRPC());
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Implements the getblockhash RPC call.
        /// </summary>
        /// <param name="height">The requested block height.</param>
        /// <returns>A <see cref="uint256"/> hash of the block at the given height. <c>Null</c> if block not found.</returns>
        [ActionName("getblockhash")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Gets the hash of the block at the given height.")]
        public uint256 GetBlockHashRPC(int height)
        {
            this.logger.LogDebug("GetBlockHash {0}", height);

            uint256 bestBlockHash = this.ConsensusManager.Tip?.HashBlock;
            ChainedHeader bestBlock = bestBlockHash == null ? null : this.ChainIndexer.GetHeader(bestBlockHash);
            if (bestBlock == null)
                return null;
            ChainedHeader block = this.ChainIndexer.GetHeader(height);
            uint256 hash = block == null || block.Height > bestBlock.Height ? null : block.HashBlock;

            if (hash == null)
                throw new BlockNotFoundException($"No block found at height {height}");

            return hash;
        }

        /// <summary>
        /// Gets the hash of the block at a given height.
        /// </summary>
        /// <param name="height">The height of the block to get the hash for.</param>
        /// <returns>Json formatted <see cref="uint256"/> hash of the block at the given height. <c>Null</c> if block not found. Returns <see cref="IActionResult"/> formatted error if fails.</returns>
        /// <remarks>This is an API implementation of an RPC call.</remarks>
        /// <response code="200">Returns the block hash</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("api/[controller]/getblockhash")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetBlockHashAPI([FromQuery] int height)
        {
            try
            {
                return this.Json(this.GetBlockHashRPC(height));
            }
            catch (Exception e)
            {
                this.logger.LogTrace("(-)[EXCEPTION]");
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Returns the current tip of consensus.
        /// </summary>
        /// <returns>Json formatted <see cref="uint256"/> hash and height of the block at the consensus tip. Returns <see cref="IActionResult"/> formatted error if fails.</returns>
        /// <response code="200">Returns the tip hash and height.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route("api/[controller]/tip")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult ConsensusTip()
        {
            try
            {
                if (this.ConsensusManager.Tip == null)
                    return this.Json("Consensus is not initialized.");

                var tip = this.ConsensusManager.Tip;

                return this.Json(new { TipHash = tip.HashBlock, TipHeight = tip.Height });
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
