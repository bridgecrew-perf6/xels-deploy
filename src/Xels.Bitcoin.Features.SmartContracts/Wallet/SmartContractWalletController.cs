﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Features.SmartContracts.Models;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Broadcasting;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonErrors;
using Xels.Bitcoin.Utilities.ModelStateErrors;
using Xels.SmartContracts.CLR;
using Xels.SmartContracts.Core.Receipts;

namespace Xels.Bitcoin.Features.SmartContracts.Wallet
{
    [ApiVersion("1")]
    [Route("api/[controller]")]
    public sealed class SmartContractWalletController : Controller
    {
        private readonly IBroadcasterManager broadcasterManager;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly IConnectionManager connectionManager;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IReceiptRepository receiptRepository;
        private readonly IWalletManager walletManager;
        private readonly ISmartContractTransactionService smartContractTransactionService;

        public SmartContractWalletController(
            IBroadcasterManager broadcasterManager,
            ICallDataSerializer callDataSerializer,
            IConnectionManager connectionManager,
            ILoggerFactory loggerFactory,
            Network network,
            IReceiptRepository receiptRepository,
            IWalletManager walletManager,
            ISmartContractTransactionService smartContractTransactionService)
        {
            this.broadcasterManager = broadcasterManager;
            this.callDataSerializer = callDataSerializer;
            this.connectionManager = connectionManager;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.receiptRepository = receiptRepository;
            this.walletManager = walletManager;
            this.smartContractTransactionService = smartContractTransactionService;
        }

        /// <summary>
        /// Gets a smart contract account address.
        /// This is a single address to use for all smart contract interactions.
        /// Smart contracts send funds to and store data at this address. For example, an ERC-20 token
        /// would store tokens allocated to a user at this address, although the actual data
        /// could, in fact, be anything. The address stores a history of smart contract create/call transactions.   
        /// It also holds a UTXO list/balance based on UTXOs sent to it from smart contracts or user wallets.
        /// Once a smart contract has written data to this address, you need to use the address to
        /// provide gas and fees for smart contract calls involving that stored data (for that smart contract deployment).
        /// In the case of specific ERC-20 tokens allocated to you, using this address would be
        /// a requirement if you were to, for example, send some of the tokens to an exchange.  
        /// It is therefore recommended that in order to keep an intact history and avoid complications,
        /// you use the single smart contract address provided by this function for all interactions with smart contracts.
        /// In addition, a smart contract address can be used to identify a contract deployer.
        /// Some methods, such as a withdrawal method on an escrow smart contract, should only be executed
        /// by the deployer, and in this case, it is the smart contract account address that identifies the deployer.
        ///  
        /// Note that this account differs from "account 0", which is the "default
        /// holder of multiple addresses". Other address holding accounts can be created,
        /// but they should not be confused with the smart contract account, which is represented
        /// by a single address.
        /// </summary>
        /// 
        /// <param name="walletName">The name of the wallet to retrieve a smart contract account address for.</param>
        /// 
        /// <returns>A smart contract account address to use for the wallet.</returns>
        /// <response code="200">Returns account addresses</response>
        /// <response code="400">Wallet name not provided or unexpected exception occurred</response>
        [Route("account-addresses")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetAccountAddresses(string walletName)
        {
            if (string.IsNullOrWhiteSpace(walletName))
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "No wallet name", "No wallet name provided");

            try
            {
                IEnumerable<string> addresses = this.walletManager.GetAccountAddressesWithBalance(walletName)
                    .Select(a => a.Address);

                if (!addresses.Any())
                {
                    HdAccount account = this.walletManager.GetAccounts(walletName).First();

                    var walletAccountReference = new WalletAccountReference(walletName, account.Name);

                    HdAddress nextAddress = this.walletManager.GetUnusedAddress(walletAccountReference);

                    return this.Json(new[] { nextAddress.Address });
                }

                return this.Json(addresses);
            }
            catch (WalletException e)
            {
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Gets the balance at a specific wallet address in STRAT (or the sidechain coin).
        /// This method gets the UTXOs at the address that the wallet can spend.
        /// The function can be used to query the balance at a smart contract account address
        /// supplied by /api/SmartContractWallet/account-addresses.
        /// </summary>
        /// <param name="address">The address at which to retrieve the balance.</param>
        /// <returns>The balance at a specific wallet address in STRAT (or the sidechain coin).</returns>
        /// <response code="200">Returns address balance</response>
        [Route("address-balance")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public IActionResult GetAddressBalance(string address)
        {
            AddressBalance balance = this.walletManager.GetAddressBalance(address);

            return this.Json(balance.AmountConfirmed.ToUnit(MoneyUnit.Satoshi));
        }

        /// <summary>
        /// Gets the history of a specific wallet address.
        /// This includes the smart contract create and call transactions
        /// This method can be used to query the balance at a smart contract account address
        /// supplied by /api/SmartContractWallet/account-addresses. Indeed,
        /// it is advisable to use /api/SmartContractWallet/account-addresses
        /// to generate an address for all smart contract interactions.
        /// If this has been done, and that address is supplied to this method,
        /// a list of all smart contract interactions for a wallet will be returned.
        /// </summary>
        ///
        /// <param name="request">See <see cref="GetHistoryRequest"/>.</param>
        /// <returns>A list of smart contract create and call transaction items as well as transaction items at a specific wallet address.</returns>
        /// <response code="200">Returns transaction history</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        /// <response code="500">Request is null</response>
        [Route("history")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult GetHistory(GetHistoryRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.WalletName))
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "No wallet name", "No wallet name provided");

            if (string.IsNullOrWhiteSpace(request.Address))
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, "No address", "No address provided");

            try
            {
                var transactionItems = new List<ContractTransactionItem>();

                HdAccount account = this.walletManager.GetAccounts(request.WalletName).First();

                // Get a list of all the transactions found in an account (or in a wallet if no account is specified), with the addresses associated with them.
                IEnumerable<AccountHistory> accountsHistory = this.walletManager.GetHistory(request.WalletName, account.Name, null, offset: request.Skip ?? 0, limit: request.Take ?? int.MaxValue, accountAddress: request.Address, forSmartContracts: true);

                // Wallet manager returns only 1 when an account name is specified.
                AccountHistory accountHistory = accountsHistory.First();

                var scTransactions = accountHistory.History.Select(h => new
                {
                    TransactionId = uint256.Parse(h.Id),
                    Fee = h.Fee,
                    SendToScriptPubKey = Script.FromHex(h.SendToScriptPubkey),
                    OutputAmount = h.Amount,
                    BlockHeight = h.BlockHeight
                }).ToList();

                // Get all receipts in one transaction
                IList<Receipt> receipts = this.receiptRepository.RetrieveMany(scTransactions.Select(x => x.TransactionId).ToList());

                for (int i = 0; i < scTransactions.Count; i++)
                {
                    var scTransaction = scTransactions[i];
                    Receipt receipt = receipts[i];

                    // This will always give us a value - the transaction has to be serializable to get past consensus.
                    Result<ContractTxData> txDataResult = this.callDataSerializer.Deserialize(scTransaction.SendToScriptPubKey.ToBytes());
                    ContractTxData txData = txDataResult.Value;

                    // If the receipt is not available yet, we don't know how much gas was consumed so use the full gas budget.
                    ulong gasFee = receipt != null
                        ? receipt.GasUsed * receipt.GasPrice
                        : txData.GasCostBudget;

                    long totalFees = scTransaction.Fee;
                    Money transactionFee = Money.FromUnit(totalFees, MoneyUnit.Satoshi) - Money.FromUnit(txData.GasCostBudget, MoneyUnit.Satoshi);

                    var result = new ContractTransactionItem
                    {
                        Amount = new Money(scTransaction.OutputAmount).ToUnit(MoneyUnit.Satoshi),
                        BlockHeight = scTransaction.BlockHeight,
                        Hash = scTransaction.TransactionId,
                        TransactionFee = transactionFee.ToUnit(MoneyUnit.Satoshi),
                        GasFee = gasFee
                    };

                    if (scTransaction.SendToScriptPubKey.IsSmartContractCreate())
                    {
                        result.Type = ContractTransactionItemType.ContractCreate;
                        result.To = receipt?.NewContractAddress?.ToBase58Address(this.network) ?? string.Empty;
                    }
                    else if (scTransaction.SendToScriptPubKey.IsSmartContractCall())
                    {
                        result.Type = ContractTransactionItemType.ContractCall;
                        result.To = txData.ContractAddress.ToBase58Address(this.network);
                    }

                    transactionItems.Add(result);
                }

                return this.Json(transactionItems.OrderByDescending(x => x.BlockHeight ?? int.MaxValue));
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Builds a transaction to create a smart contract and then broadcasts the transaction to the network.
        /// If the deployment is successful, methods on the smart contract can be subsequently called.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param>
        /// 
        /// <returns>A hash of the transaction used to create the smart contract. The result of the transaction broadcast is not returned,
        /// and you should check for a transaction receipt to see if it was successful.</returns>
        /// <response code="200">Returns build transaction response</response>
        /// <response code="400">Invalid request, failed to build transaction, or could not broadcast transaction</response>
        [Route("create")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult Create([FromBody] BuildCreateContractTransactionRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            BuildCreateContractTransactionResponse response = this.smartContractTransactionService.BuildCreateTx(request);

            if (!response.Success)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, response.Message, string.Empty);

            Transaction transaction = this.network.CreateTransaction(response.Hex);

            this.broadcasterManager.BroadcastTransactionAsync(transaction).GetAwaiter().GetResult();

            // Check if transaction was actually added to a mempool.
            TransactionBroadcastEntry transactionBroadCastEntry = this.broadcasterManager.GetTransaction(transaction.GetHash());

            if (transactionBroadCastEntry?.TransactionBroadcastState == TransactionBroadcastState.CantBroadcast)
            {
                this.logger.LogError("Exception occurred: {0}", transactionBroadCastEntry.ErrorMessage);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, transactionBroadCastEntry.ErrorMessage, "Transaction Exception");
            }

            return this.Json(response.TransactionId);
        }

        /// <summary>
        /// Builds a transaction to call a smart contract method and then broadcasts the transaction to the network.
        /// If the call is successful, any changes to the smart contract balance or persistent data are propagated
        /// across the network.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param>
        ///
        /// <returns>The transaction used to call a smart contract method. The result of the transaction broadcast is not returned,
        /// and you should check for a transaction receipt to see if it was successful.</returns>
        /// <response code="200">Returns build transaction response</response>
        /// <response code="400">Invalid request, failed to build transaction, or could not broadcast transaction</response>
        [Route("call")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult Call([FromBody] BuildCallContractTransactionRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            BuildCallContractTransactionResponse response = this.smartContractTransactionService.BuildCallTx(request);

            if (!response.Success)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, response.Message, string.Empty);

            Transaction transaction = this.network.CreateTransaction(response.Hex);

            this.broadcasterManager.BroadcastTransactionAsync(transaction).GetAwaiter().GetResult();

            // Check if transaction was actually added to a mempool.
            TransactionBroadcastEntry transactionBroadCastEntry = this.broadcasterManager.GetTransaction(transaction.GetHash());

            if (transactionBroadCastEntry?.TransactionBroadcastState == TransactionBroadcastState.CantBroadcast)
            {
                this.logger.LogError("Exception occurred: {0}", transactionBroadCastEntry.ErrorMessage);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, transactionBroadCastEntry.ErrorMessage, "Transaction Exception");
            }

            return this.Json(response);
        }

        /// <summary>
        /// Broadcasts a transaction, which either creates a smart contract or calls a method on a smart contract.
        /// If the contract deployment or method call are successful gas and fees are consumed.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to send the transaction.</param>
        /// 
        /// <returns>A model of the transaction which the Broadcast Manager broadcasts. The result of the transaction broadcast is not returned,
        /// and you should check for a transaction receipt to see if it was successful.</returns>
        /// <response code="200">Returns the broadcast transaction</response>
        /// <response code="400">Invalid request, failed to broadcast transaction or unexpected exception occurred</response>
        /// <response code="500">Request is null, or no peers are connected</response>
        [Route("send-transaction")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult SendTransaction([FromBody] SendTransactionRequest request)
        {
            Guard.NotNull(request, nameof(request));

            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            if (!this.connectionManager.ConnectedPeers.Any())
                throw new WalletException("Can't send transaction: sending transaction requires at least one connection!");

            try
            {
                Transaction transaction = this.network.CreateTransaction(request.Hex);

                var model = new WalletSendTransactionModel
                {
                    TransactionId = transaction.GetHash(),
                    Outputs = new List<TransactionOutputModel>()
                };

                foreach (TxOut output in transaction.Outputs)
                {
                    bool isUnspendable = output.ScriptPubKey.IsUnspendable;

                    string address = this.GetAddressFromScriptPubKey(output);
                    model.Outputs.Add(new TransactionOutputModel
                    {
                        Address = address,
                        Amount = output.Value,
                        OpReturnData = isUnspendable ? Encoding.UTF8.GetString(output.ScriptPubKey.ToOps().Last().PushData) : null
                    });
                }

                this.broadcasterManager.BroadcastTransactionAsync(transaction).GetAwaiter().GetResult();

                TransactionBroadcastEntry transactionBroadCastEntry = this.broadcasterManager.GetTransaction(transaction.GetHash());
                if (!string.IsNullOrEmpty(transactionBroadCastEntry?.ErrorMessage))
                {
                    this.logger.LogError("Exception occurred: {0}", transactionBroadCastEntry.ErrorMessage);
                    return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, transactionBroadCastEntry.ErrorMessage, "Transaction Exception");
                }

                return this.Json(model);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Retrieves a string that represents the receiving address for an output.For smart contract transactions,
        /// returns the opcode that was sent i.e.OP_CALL or OP_CREATE
        /// </summary>
        private string GetAddressFromScriptPubKey(TxOut output)
        {
            if (output.ScriptPubKey.IsSmartContractExec())
                return output.ScriptPubKey.ToOps().First().Code.ToString();

            if (!output.ScriptPubKey.IsUnspendable)
                return output.ScriptPubKey.GetDestinationAddress(this.network).ToString();

            return null;
        }
    }
}