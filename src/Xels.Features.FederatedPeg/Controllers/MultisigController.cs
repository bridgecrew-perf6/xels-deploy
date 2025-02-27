﻿using System;
using System.Linq;
using System.Net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonErrors;
using Xels.Bitcoin.Utilities.ModelStateErrors;
using Xels.Features.FederatedPeg.Models;

namespace Xels.Features.FederatedPeg.Controllers
{
    /// <summary>
    /// Controller providing operations on a wallet.
    /// </summary>
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public class MultisigController : Controller
    {
        private readonly FedMultiSigManualWithdrawalTransactionBuilder fedMultiSigManualWithdrawalTransactionBuilder;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        public MultisigController(
            FedMultiSigManualWithdrawalTransactionBuilder fedMultiSigManualWithdrawalTransactionBuilder,
            Network network)
        {
            this.fedMultiSigManualWithdrawalTransactionBuilder = fedMultiSigManualWithdrawalTransactionBuilder;
            this.network = network;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <summary>
        /// Builds a transaction and returns the hex to use when executing the transaction.
        /// </summary>
        /// <param name="request">An object containing the parameters used to build a transaction.</param>
        /// <returns>A JSON object including the transaction ID, the hex used to execute
        /// the transaction, and the transaction fee.</returns>
        /// <response code="200">Returns transaction details</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("build-transaction")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult BuildTransaction([FromBody] BuildMultisigTransactionRequest request)
        {
            Guard.NotNull(request, nameof(request));

            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                var recipients = request
                    .Recipients
                    .Select(recipientModel => new Wallet.Recipient
                    {
                        ScriptPubKey = BitcoinAddress.Create(recipientModel.DestinationAddress, this.network).ScriptPubKey,
                        Amount = recipientModel.Amount
                    })
                    .ToList();

                Key[] privateKeys = request
                    .Secrets
                    .Select(secret => new Mnemonic(secret.Mnemonic).DeriveExtKey(secret.Passphrase).PrivateKey)
                    .ToArray();

                Transaction transactionResult = this.fedMultiSigManualWithdrawalTransactionBuilder.BuildTransaction(recipients, privateKeys);

                var model = new WalletBuildTransactionModel
                {
                    Hex = transactionResult.ToHex(),
                    TransactionId = transactionResult.GetHash()
                };

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