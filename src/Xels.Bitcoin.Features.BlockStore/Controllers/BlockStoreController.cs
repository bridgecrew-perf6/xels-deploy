﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Controllers.Models;
using Xels.Bitcoin.Features.BlockStore.AddressIndexing;
using Xels.Bitcoin.Features.BlockStore.Models;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonErrors;
using Xels.Bitcoin.Utilities.ModelStateErrors;

namespace Xels.Bitcoin.Features.BlockStore.Controllers
{
    public static class BlockStoreRouteEndPoint
    {
        public const string GetAddressesBalances = "getaddressesbalances";
        public const string GetVerboseAddressesBalances = "getverboseaddressesbalances";
        public const string GetAddressIndexerTip = "addressindexertip";
        public const string GetBlock = "block";
        public const string GetBlockCount = "getblockcount";
        public const string GetUtxoSet = "getutxoset";
        public const string GetUtxoSetForAddress = "getutxosetforaddress";
        public const string GetLastBalanceDecreaseTransaction = "getlastbalanceupdatetransaction";
    }

    /// <summary>Controller providing operations on a blockstore.</summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class BlockStoreController : Controller
    {
        private readonly IAddressIndexer addressIndexer;

        private readonly IUtxoIndexer utxoIndexer;

        /// <summary>Provides access to the block store on disk.</summary>
        private readonly IBlockStore blockStore;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>An interface that provides information about the chain and validation.</summary>
        private readonly IChainState chainState;

        /// <summary>The chain.</summary>
        private readonly ChainIndexer chainIndexer;

        /// <summary>Current network for the active controller instance.</summary>
        private readonly Network network;

        private readonly IStakeChain stakeChain;

        private readonly IScriptAddressReader scriptAddressReader;

        public BlockStoreController(
            Network network,
            ILoggerFactory loggerFactory,
            IBlockStore blockStore,
            IChainState chainState,
            ChainIndexer chainIndexer,
            IAddressIndexer addressIndexer,
            IUtxoIndexer utxoIndexer,
            IScriptAddressReader scriptAddressReader,
            IStakeChain stakeChain = null)
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(chainState, nameof(chainState));
            Guard.NotNull(addressIndexer, nameof(addressIndexer));
            Guard.NotNull(utxoIndexer, nameof(utxoIndexer));

            this.addressIndexer = addressIndexer;
            this.network = network;
            this.blockStore = blockStore;
            this.chainState = chainState;
            this.chainIndexer = chainIndexer;
            this.utxoIndexer = utxoIndexer;
            this.scriptAddressReader = scriptAddressReader;
            this.stakeChain = stakeChain;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <summary>
        /// Retrieves the <see cref="addressIndexer"/>'s tip.
        /// </summary>
        /// <returns>An instance of <see cref="AddressIndexerTipModel"/> containing the tip's hash and height.</returns>
        /// <response code="200">Returns the address indexer tip</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetAddressIndexerTip)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetAddressIndexerTip()
        {
            try
            {
                ChainedHeader addressIndexerTip = this.addressIndexer.IndexerTip;
                return this.Json(new AddressIndexerTipModel() { TipHash = addressIndexerTip?.HashBlock, TipHeight = addressIndexerTip?.Height });
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves the block which matches the supplied block hash.
        /// </summary>
        /// <param name="query">An object containing the necessary parameters to search for a block.</param>
        /// <returns><see cref="BlockModel"/> if block is found, <see cref="NotFoundObjectResult"/> if not found. Returns <see cref="IActionResult"/> with error information if exception thrown.</returns>
        /// <response code="200">Returns data about the block or block not found message</response>
        /// <response code="400">Block hash invalid, or an unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetBlock)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public IActionResult GetBlock([FromQuery] SearchByHashRequest query)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                uint256 blockId = uint256.Parse(query.Hash);

                ChainedHeader chainedHeader = this.chainIndexer.GetHeader(blockId);

                if (chainedHeader == null)
                    return this.NotFound("Block not found");

                Block block = chainedHeader.Block ?? this.blockStore.GetBlock(blockId);

                // In rare occasions a block that is found in the
                // indexer may not have been pushed to the store yet. 
                if (block == null)
                    return this.NotFound("Block not found");

                if (!query.OutputJson)
                {
                    return this.Json(block);
                }

                BlockModel blockModel = query.ShowTransactionDetails
                    ? new BlockTransactionDetailsModel(block, chainedHeader, this.chainIndexer.Tip, this.network)
                    : new BlockModel(block, chainedHeader, this.chainIndexer.Tip, this.network);

                if (this.network.Consensus.IsProofOfStake)
                {
                    var posBlock = block as PosBlock;

                    blockModel.PosBlockSignature = posBlock.BlockSignature.ToHex(this.network);
                    blockModel.PosBlockTrust = new Target(chainedHeader.GetBlockTarget()).ToUInt256().ToString();
                    blockModel.PosChainTrust = chainedHeader.ChainWork.ToString(); // this should be similar to ChainWork

                    if (this.stakeChain != null)
                    {
                        BlockStake blockStake = this.stakeChain.Get(blockId);

                        blockModel.PosModifierv2 = blockStake?.StakeModifierV2.ToString();
                        blockModel.PosFlags = blockStake?.Flags == BlockFlag.BLOCK_PROOF_OF_STAKE ? "proof-of-stake" : "proof-of-work";
                        blockModel.PosHashProof = blockStake?.HashProof?.ToString();
                    }
                }

                return this.Json(blockModel);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Gets the current consensus tip height.
        /// </summary>
        /// <remarks>This is an API implementation of an RPC call.</remarks>
        /// <returns>The current tip height. Returns <c>null</c> if fails. Returns <see cref="IActionResult"/> with error information if exception thrown.</returns>
        /// <response code="200">Returns the block count</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetBlockCount)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetBlockCount()
        {
            try
            {
                return this.Json(this.chainState.ConsensusTip.Height);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>Provides balance of the given addresses confirmed with at least <paramref name="minConfirmations"/> confirmations.</summary>
        /// <param name="addresses">A comma delimited set of addresses that will be queried.</param>
        /// <param name="minConfirmations">Only blocks below consensus tip less this parameter will be considered.</param>
        /// <returns>A result object containing the balance for each requested address and if so, a message stating why the indexer is not queryable.</returns>
        /// <response code="200">Returns balances for the requested addresses</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetAddressesBalances)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetAddressesBalances(string addresses, int minConfirmations)
        {
            try
            {
                string[] addressesArray = addresses.Split(',');

                this.logger.LogDebug("Asking data for {0} addresses.", addressesArray.Length);

                AddressBalancesResult result = this.addressIndexer.GetAddressBalances(addressesArray, minConfirmations);

                this.logger.LogDebug("Sending data for {0} addresses.", result.Balances.Count);

                return this.Json(result);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }


        /// <summary>Provides verbose balance data of the given addresses.</summary>
        /// <param name="addresses">A comma delimited set of addresses that will be queried.</param>
        /// <returns>A result object containing the balance for each requested address and if so, a message stating why the indexer is not queryable.</returns>
        /// <response code="200">Returns balances for the requested addresses</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetVerboseAddressesBalances)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetVerboseAddressesBalancesData(string addresses)
        {
            try
            {
                string[] addressesArray = addresses?.Split(',') ?? new string[] { };

                this.logger.LogDebug("Asking data for {0} addresses.", addressesArray.Length);

                VerboseAddressBalancesResult result = this.addressIndexer.GetAddressIndexerState(addressesArray);

                return this.Json(result);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>Returns every UTXO as of a given block height. This may take some time for large chains.</summary>
        /// <param name="atBlockHeight">Only process blocks up to this height for the purposes of constructing the UTXO set.</param>
        /// <returns>A result object containing the UTXOs.</returns>
        /// <response code="200">Returns the UTXO set.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetUtxoSet)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetUtxoSet(int atBlockHeight)
        {
            try
            {
                ReconstructedCoinviewContext coinView = this.utxoIndexer.GetCoinviewAtHeight(atBlockHeight);

                var outputs = new List<UtxoModel>();

                foreach (OutPoint outPoint in coinView.UnspentOutputs)
                {
                    TxOut txOut = coinView.Transactions[outPoint.Hash].Outputs[outPoint.N];
                    var utxo = new UtxoModel() { TxId = outPoint.Hash, Index = outPoint.N, ScriptPubKey = txOut.ScriptPubKey, Value = txOut.Value };

                    outputs.Add(utxo);
                }

                return this.Json(outputs);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>Provides verbose balance data of the given addresses.</summary>
        /// <param name="address">The address to query for unspent outputs.</param>
        /// <returns>A result object containing the UTXOs.</returns>
        /// <response code="200">Returns the UTXO set for a particular address at the current chain tip height.</response>
        /// <response code="400">Unexpected exception occurred</response>
        [Route(BlockStoreRouteEndPoint.GetUtxoSetForAddress)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetUtxoSetForAddress(string address)
        {
            // Get coinview at current height (SLOW)
            var coinView = this.utxoIndexer.GetCoinviewAtHeight(this.chainIndexer.Height);

            var utxos = new ConcurrentBag<UtxoModel>();

            // Get utxos for this address
            try
            {
                // Fine to do this in parallel because we don't care about the order
                Parallel.ForEach(coinView.UnspentOutputs, (utxo) =>
                {
                    var tx = coinView.Transactions[utxo.Hash];

                    // Get the actual output
                    var output = tx.Outputs[utxo.N];

                    var destinationAddress = this.scriptAddressReader.GetAddressFromScriptPubKey(this.network, output.ScriptPubKey);

                    if (destinationAddress != address)
                        return;

                    var utxoModel = new UtxoModel()
                    {
                        TxId = utxo.Hash,
                        Index = utxo.N,
                        ScriptPubKey = output.ScriptPubKey,
                        Value = output.Value
                    };

                    utxos.Add(utxoModel);
                });

                var balance = new Money(utxos.Sum(u => u.Value)).ToUnit(MoneyUnit.BTC);

                return this.Json(new
                {
                    balance,
                    utxos
                });
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        [Route(BlockStoreRouteEndPoint.GetLastBalanceDecreaseTransaction)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetLastBalanceUpdateTransaction(string address)
        {
            try
            {
                LastBalanceDecreaseTransactionModel result = this.addressIndexer.GetLastBalanceDecreaseTransaction(address);

                return this.Json(result);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}
