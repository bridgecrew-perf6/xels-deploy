﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonErrors;
using Xels.Bitcoin.Utilities.ModelStateErrors;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.Models;
using Xels.Features.FederatedPeg.Wallet;

namespace Xels.Features.FederatedPeg.Controllers
{
    public static class FederationWalletRouteEndPoint
    {
        public const string GeneralInfo = "general-info";
        public const string Balance = "balance";
        public const string History = "history";
        public const string Sync = "sync";
        public const string EnableFederation = "enable-federation";
        public const string RemoveTransactions = "remove-transactions";
    }

    /// <summary>
    /// Controller providing operations on a wallet.
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class FederationWalletController : Controller
    {
        private readonly ICrossChainTransferStore crossChainTransferStore;
        private readonly IFederationWalletManager federationWalletManager;
        private readonly Network network;
        private readonly IFederationWalletSyncManager walletSyncManager;
        private readonly IConnectionManager connectionManager;
        private readonly ChainIndexer chainIndexer;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        public FederationWalletController(
            IFederationWalletManager walletManager,
            IFederationWalletSyncManager walletSyncManager,
            IConnectionManager connectionManager,
            Network network,
            ChainIndexer chainIndexer,
            ICrossChainTransferStore crossChainTransferStore)
        {
            this.connectionManager = connectionManager;
            this.crossChainTransferStore = crossChainTransferStore;
            this.chainIndexer = chainIndexer;
            this.federationWalletManager = walletManager;
            this.network = network;
            this.logger = LogManager.GetCurrentClassLogger();
            this.walletSyncManager = walletSyncManager;
        }

        /// <summary>
        /// Retrieves general information about the wallet
        /// </summary>
        /// <returns>HTTP response</returns>
        /// <response code="200">Returns wallet information</response>
        /// <response code="400">Unexpected exception occurred</response>
        /// <response code="404">Wallet does not exist</response>
        [Route(FederationWalletRouteEndPoint.GeneralInfo)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public IActionResult GetGeneralInfo()
        {
            try
            {
                FederationWallet wallet = this.federationWalletManager.GetWallet();

                if (wallet == null)
                {
                    return this.NotFound("No federation wallet found.");
                }

                var model = new WalletGeneralInfoModel
                {
                    Network = wallet.Network.Name,
                    CreationTime = wallet.CreationTime,
                    LastBlockSyncedHeight = wallet.LastBlockSyncedHeight,
                    ConnectedNodes = this.connectionManager.ConnectedPeers.Count(),
                    ChainTip = this.chainIndexer.Tip.Height,
                    IsChainSynced = this.chainIndexer.IsDownloaded(),
                    IsDecrypted = true
                };

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves wallet balances
        /// </summary>
        /// <returns>HTTP response</returns>
        /// <response code="200">Returns wallet balances</response>
        /// <response code="400">Unexpected exception occurred</response>
        /// <response code="404">Wallet does not exist</response>
        [Route(FederationWalletRouteEndPoint.Balance)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public IActionResult GetBalance()
        {
            try
            {
                FederationWallet wallet = this.federationWalletManager.GetWallet();
                if (wallet == null)
                {
                    return this.NotFound("No federation wallet found.");
                }

                (Money ConfirmedAmount, Money UnConfirmedAmount) = this.federationWalletManager.GetSpendableAmount();

                var balance = new AccountBalanceModel
                {
                    CoinType = (CoinType)this.network.Consensus.CoinType,
                    AmountConfirmed = ConfirmedAmount,
                    AmountUnconfirmed = UnConfirmedAmount,
                };

                var model = new WalletBalanceModel();
                model.AccountsBalances.Add(balance);

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves withdrawal history for the wallet
        /// </summary>
        /// <param name="maxEntriesToReturn">The maximum number of history items to return.</param>
        /// <returns>HTTP response</returns>
        /// <response code="200">Returns wallet history</response>
        /// <response code="400">Unexpected exception occurred</response>
        /// <response code="404">Wallet does not exist</response>
        [Route(FederationWalletRouteEndPoint.History)]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        public IActionResult GetHistory([FromQuery] int maxEntriesToReturn)
        {
            try
            {
                FederationWallet wallet = this.federationWalletManager.GetWallet();
                if (wallet == null)
                    return this.NotFound("No federation wallet found.");

                List<WithdrawalModel> result = this.crossChainTransferStore.GetCompletedWithdrawals(maxEntriesToReturn);

                return this.Json(result);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Starts sending block to the wallet for synchronisation.
        /// This is for demo and testing use only.
        /// </summary>
        /// <param name="model">The hash of the block from which to start syncing.</param>
        /// <returns>HTTP response</returns>
        /// <response code="200">Syncronisation started</response>
        /// <response code="400">Invalid request, or block not found</response>
        [HttpPost]
        [Route(FederationWalletRouteEndPoint.Sync)]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult Sync([FromBody] HashModel model)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            ChainedHeader block = this.chainIndexer.GetHeader(uint256.Parse(model.Hash));

            if (block == null)
            {
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, $"Block with hash {model.Hash} was not found on the blockchain.", string.Empty);
            }

            this.walletSyncManager.SyncFromHeight(block.Height);
            return this.Ok();
        }

        /// <summary>
        /// Provide the federation wallet's credentials so that it can sign transactions.
        /// </summary>
        /// <param name="request">The password of the federation wallet.</param>
        /// <returns>HTTP response</returns>
        /// <response code="200">Wallet enabled</response>
        /// <response code="400">Invalid request, or unexpected exception occurred</response>
        /// <response code="404">Wallet not found before timeout</response>
        /// <response code="500">Request is null</response>
        [Route(FederationWalletRouteEndPoint.EnableFederation)]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.NotFound)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult EnableFederation([FromBody] EnableFederationRequest request)
        {
            Guard.NotNull(request, nameof(request));

            try
            {
                if (!this.ModelState.IsValid)
                {
                    IEnumerable<string> errors = this.ModelState.Values.SelectMany(e => e.Errors.Select(m => m.ErrorMessage));
                    return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "Formatting error", string.Join(Environment.NewLine, errors));
                }

                // Enabling the federation wallet requires the federation wallet.
                for (int timeOutSeconds = request.TimeoutSeconds ?? 0; timeOutSeconds >= 0; timeOutSeconds--)
                {
                    if (this.federationWalletManager.GetWallet() != null)
                    {
                        this.federationWalletManager.EnableFederationWallet(request.Password, request.Mnemonic, request.Passphrase);

                        return this.Ok();
                    }

                    Thread.Sleep(1000);
                }

                return this.NotFound("No federation wallet found.");
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Remove all transactions from the wallet.
        /// </summary>
        /// <param name="request">Transactions to remove</param>
        /// <returns>HTTP response</returns>
        /// <response code="200">Returns removed transaction list</response>
        /// <response code="400">Invalid request, or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route(FederationWalletRouteEndPoint.RemoveTransactions)]
        [HttpDelete]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult RemoveTransactions([FromQuery] RemoveFederationTransactionsModel request)
        {
            Guard.NotNull(request, nameof(request));

            // Checks the request is valid.
            if (!this.ModelState.IsValid)
            {
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            try
            {
                HashSet<(uint256 transactionId, DateTimeOffset creationTime)> result;

                result = this.federationWalletManager.RemoveAllTransactions();

                // If the user chose to resync the wallet after removing transactions.
                if (result.Any() && request.ReSync)
                {
                    // From the list of removed transactions, check which one is the oldest and retrieve the block right before that time.
                    DateTimeOffset earliestDate = result.Min(r => r.creationTime);
                    ChainedHeader chainedHeader = this.chainIndexer.GetHeader(this.chainIndexer.GetHeightAtTime(earliestDate.DateTime));

                    // Update the wallet and save it to the file system.
                    FederationWallet federationWallet = this.federationWalletManager.GetWallet();
                    federationWallet.LastBlockSyncedHeight = chainedHeader.Height;
                    federationWallet.LastBlockSyncedHash = chainedHeader.HashBlock;
                    this.federationWalletManager.SaveWallet();

                    // Initialize the syncing process from the block before the earliest transaction was seen.
                    this.walletSyncManager.SyncFromHeight(chainedHeader.Height - 1);
                }

                IEnumerable<RemovedTransactionModel> model = result.Select(r => new RemovedTransactionModel
                {
                    TransactionId = r.transactionId,
                    CreationTime = r.creationTime
                });

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }
    }
}