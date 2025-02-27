using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using CSharpFunctionalExtensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Newtonsoft.Json;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Controllers;
using Xels.Bitcoin.Features.SmartContracts.Models;
using Xels.Bitcoin.Features.SmartContracts.Wallet;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Broadcasting;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities.JsonErrors;
using Xels.Bitcoin.Utilities.ModelStateErrors;
using Stratis.SmartContracts;
using Xels.SmartContracts.CLR;
using Xels.SmartContracts.CLR.Caching;
using Xels.SmartContracts.CLR.Compilation;
using Xels.SmartContracts.CLR.Decompilation;
using Xels.SmartContracts.CLR.Local;
using Xels.SmartContracts.CLR.Serialization;
using Xels.SmartContracts.Core;
using Xels.SmartContracts.Core.Receipts;
using Xels.SmartContracts.Core.State;

namespace Xels.Bitcoin.Features.SmartContracts.ReflectionExecutor.Controllers
{
    [ApiVersion("1")]
    public class SmartContractsController : FeatureController
    {
        /// <summary>
        /// For consistency in retrieval of balances, and to ensure that smart contract transaction
        /// creation always works, as the retrieved transactions have always already been included in a block.
        /// </summary>
        private const int MinConfirmationsAllChecks = 1;

        private readonly IBroadcasterManager broadcasterManager;
        private readonly ChainIndexer chainIndexer;
        private readonly CSharpContractDecompiler contractDecompiler;
        private readonly ILogger logger;
        private readonly Network network;
        private readonly IStateRepositoryRoot stateRoot;
        private readonly IWalletManager walletManager;
        private readonly ISerializer serializer;
        private readonly IContractPrimitiveSerializer primitiveSerializer;
        private readonly IReceiptRepository receiptRepository;
        private readonly ILocalExecutor localExecutor;
        private readonly ISmartContractTransactionService smartContractTransactionService;
        private readonly IConnectionManager connectionManager;
        private readonly IContractAssemblyCache contractAssemblyCache;
        private readonly NodeSettings nodeSettings;

        public SmartContractsController(IBroadcasterManager broadcasterManager,
            IBlockStore blockStore,
            ChainIndexer chainIndexer,
            CSharpContractDecompiler contractDecompiler,
            ILoggerFactory loggerFactory,
            Network network,
            IStateRepositoryRoot stateRoot,
            IWalletManager walletManager,
            ISerializer serializer,
            IContractPrimitiveSerializer primitiveSerializer,
            IReceiptRepository receiptRepository,
            ILocalExecutor localExecutor,
            ISmartContractTransactionService smartContractTransactionService,
            IConnectionManager connectionManager,
            IContractAssemblyCache contractAssemblyCache,
            NodeSettings nodeSettings)
        {
            this.stateRoot = stateRoot;
            this.contractDecompiler = contractDecompiler;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.network = network;
            this.chainIndexer = chainIndexer;
            this.walletManager = walletManager;
            this.broadcasterManager = broadcasterManager;
            this.serializer = serializer;
            this.primitiveSerializer = primitiveSerializer;
            this.receiptRepository = receiptRepository;
            this.localExecutor = localExecutor;
            this.smartContractTransactionService = smartContractTransactionService;
            this.connectionManager = connectionManager;
            this.contractAssemblyCache = contractAssemblyCache;
            this.nodeSettings = nodeSettings;
        }

        /// <summary>
        /// Gets the bytecode for a smart contract as a hexadecimal string. The bytecode is decompiled to
        /// C# source, which is returned as well. Be aware, it is the bytecode which is being executed,
        /// so this is the "source of truth".
        /// </summary>
        ///
        /// <param name="address">The address of the smart contract to retrieve as bytecode and C# source.</param>
        ///
        /// <returns>A response object containing the bytecode and the decompiled C# code.</returns>
        /// <response code="200">Returns code response (may be unsuccessful)</response>
        [Route("api/[controller]/code")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public IActionResult GetCode([FromQuery] string address)
        {
            uint160 addressNumeric = address.ToUint160(this.network);
            byte[] contractCode = this.stateRoot.GetCode(addressNumeric);

            if (contractCode == null || !contractCode.Any())
            {
                return this.Json(new GetCodeResponse
                {
                    Message = string.Format("No contract execution code exists at {0}", address)
                });
            }

            string typeName = this.stateRoot.GetContractType(addressNumeric);

            Result<string> sourceResult = this.contractDecompiler.GetSource(contractCode);

            return this.Json(new GetCodeResponse
            {
                Message = string.Format("Contract execution code retrieved at {0}", address),
                Bytecode = contractCode.ToHexString(),
                Type = typeName,
                CSharp = sourceResult.IsSuccess ? sourceResult.Value : sourceResult.Error // Show the source, or the reason why the source couldn't be retrieved.
            });
        }

        /// <summary>
        /// Gets the balance of a smart contract in STRAT (or the sidechain coin). This method only works for smart contract addresses. 
        /// </summary>
        /// 
        /// <param name="address">The address of the smart contract to retrieve the balance for.</param>
        /// 
        /// <returns>The balance of a smart contract in STRAT (or the sidechain coin).</returns>
        /// <response code="200">Returns balance</response>
        [Route("api/[controller]/balance")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public IActionResult GetBalance([FromQuery] string address)
        {
            uint160 addressNumeric = address.ToUint160(this.network);
            ulong balance = this.stateRoot.GetCurrentBalance(addressNumeric);
            Money moneyBalance = Money.Satoshis(balance);
            return this.Json(moneyBalance.ToString(false));
        }

        /// <summary>
        /// Gets a single piece of smart contract data, which was stored as a key–value pair using the
        /// SmartContract.PersistentState property. 
        /// The method performs a lookup in the smart contract
        /// state database for the supplied smart contract address and key.
        /// The value associated with the given key, deserialized for the specified data type, is returned.
        /// If the key does not exist or deserialization fails, the method returns the default value for
        /// the specified type.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to perform a retrieve stored data request.</param>
        ///
        /// <returns>A single piece of stored smart contract data.</returns>
        /// <response code="200">Returns data response (may be unsuccessful)</response>
        /// <response code="400">Invalid request</response>
        [Route("api/[controller]/storage")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetStorage([FromQuery] GetStorageRequest request)
        {
            if (!this.ModelState.IsValid)
            {
                this.logger.LogTrace("(-)[MODELSTATE_INVALID]");
                return ModelStateErrors.BuildErrorResponse(this.ModelState);
            }

            var height = request.BlockHeight.HasValue ? request.BlockHeight.Value : (ulong)this.chainIndexer.Height;

            ChainedHeader chainedHeader = this.chainIndexer.GetHeader((int)height);

            var scHeader = chainedHeader?.Header as ISmartContractBlockHeader;

            IStateRepositoryRoot stateAtHeight = this.stateRoot.GetSnapshotTo(scHeader.HashStateRoot.ToBytes());

            uint160 addressNumeric = request.ContractAddress.ToUint160(this.network);
            byte[] storageValue = stateAtHeight.GetStorageValue(addressNumeric, Encoding.UTF8.GetBytes(request.StorageKey));

            if (storageValue == null)
            {
                return this.NotFound(new
                {
                    Message = string.Format("No data at storage with key {0}", request.StorageKey)
                });
            }

            // Interpret the storage bytes as an object of the given type
            object interpretedStorageValue = this.InterpretStorageValue(request.DataType, storageValue);

            // Use MethodParamStringSerializer to serialize the interpreted object to a string
            string serialized = MethodParameterStringSerializer.Serialize(interpretedStorageValue, this.network);
            return this.Json(serialized);
        }

        [ActionName("getreceipt")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Gets the receipt for this transaction hash.")]
        public ReceiptResponse GetReceiptRPC(string txHash)
        {
            uint256 txHashNum = new uint256(txHash);
            Receipt receipt = this.receiptRepository.Retrieve(txHashNum);

            if (receipt == null)
            {
                return null;
            }

            uint160 address = receipt.NewContractAddress ?? receipt.To;

            if (!receipt.Logs.Any())
            {
                return new ReceiptResponse(receipt, new List<LogResponse>(), this.network);
            }

            var deserializer = new ApiLogDeserializer(this.primitiveSerializer, this.network, this.stateRoot, this.contractAssemblyCache);

            List<LogResponse> logResponses = deserializer.MapLogResponses(receipt.Logs);

            return new ReceiptResponse(receipt, logResponses, this.network);
        }

        /// <summary>
        /// Gets a smart contract transaction receipt. Receipts contain information about how a smart contract transaction was executed.
        /// This includes the value returned from a smart contract call and how much gas was used.  
        /// </summary>
        /// 
        /// <param name="txHash">A hash of the smart contract transaction (the transaction ID).</param>
        /// 
        /// <returns>The receipt for the smart contract.</returns> 
        /// <response code="200">Returns transaction receipt</response>
        /// <response code="400">Transaction not found</response>
        [Route("api/[controller]/receipt")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult GetReceiptAPI([FromQuery] string txHash)
        {
            ReceiptResponse receiptResponse = this.GetReceiptRPC(txHash);

            if (receiptResponse == null)
            {
                this.logger.LogTrace("(-)[RECEIPT_NOT_FOUND]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest,
                    "The receipt was not found.",
                    "No stored transaction could be found for the supplied hash.");
            }

            return this.Json(receiptResponse);
        }

        /// <summary>
        /// Searches for receipts that match the given filter criteria. Filter criteria are ANDed together.
        /// </summary>
        /// <param name="contractAddress">The contract address from which events were raised.</param>
        /// <param name="eventName">The name of the event raised.</param>
        /// <param name="topics">The topics to search. All specified topics must be present.</param>
        /// <param name="fromBlock">The block number from which to start searching.</param>
        /// <param name="toBlock">The block number where searching finishes.</param>
        /// <returns>A list of all matching receipts.</returns>
        [ActionName("searchreceipts")]
        [ApiExplorerSettings(IgnoreApi = true)]
        [ActionDescription("Searches for receipts matching the filter criteria.")]
        public List<ReceiptResponse> ReceiptSearch(string contractAddress, string eventName, List<string> topics = null, int fromBlock = 0, int? toBlock = null)
        {
            return this.smartContractTransactionService.ReceiptSearch(contractAddress, eventName, topics, fromBlock, toBlock);
        }

        // Note: We may not know exactly how to best structure "receipt search" queries until we start building 
        // a web3-like library. For now the following method serves as a very basic example of how we can query the block
        // bloom filters to retrieve events.

        /// <summary>
        /// Searches a smart contract's receipts for those which match a specific event. The SmartContract.Log() function
        /// is capable of storing C# structs, and structs are used to store information about different events occurring 
        /// on the smart contract. For example, a "TransferLog" struct could contain "From" and "To" fields and be used to log
        /// when a smart contract makes a transfer of funds from one wallet to another. The log entries are held inside the smart contract,
        /// indexed using the name of the struct, and are linked to individual transaction receipts.
        /// Therefore, it is possible to return a smart contract's transaction receipts
        /// which match a specific event (as defined by the struct name).  
        /// </summary>
        /// 
        /// <param name="contractAddress">The contract address from which events were raised.</param>
        /// <param name="eventName">The name of the event raised.</param>
        /// <param name="topics">The topics to search. All specified topics must be present.</param>
        /// <param name="fromBlock">The block number from which to start searching.</param>
        /// <param name="toBlock">The block number where searching finishes.</param>
        /// 
        /// <returns>A list of receipts for transactions relating to a specific smart contract and a specific event in that smart contract.</returns>
        [Route("api/[controller]/receipt-search")]
        [HttpGet]
        public Task<IActionResult> ReceiptSearchAPI([FromQuery] string contractAddress, [FromQuery] string eventName, [FromQuery] List<string> topics = null, [FromQuery] int fromBlock = 0, [FromQuery] int? toBlock = null)
        {
            List<ReceiptResponse> result = this.smartContractTransactionService.ReceiptSearch(contractAddress, eventName, topics, fromBlock, toBlock);

            if (result == null)
            {
                return Task.FromResult<IActionResult>(ErrorHelpers.BuildErrorResponse(HttpStatusCode.InternalServerError, "No code exists", $"No contract execution code exists at {contractAddress}"));
            }

            return Task.FromResult<IActionResult>(this.Json(result));
        }

        /// <summary>
        /// Builds a transaction to create a smart contract. Although the transaction is created, the smart contract is not
        /// deployed on the network, and no gas or fees are consumed.
        /// Instead the created transaction is returned as a hexadecimal string within a JSON object.
        /// Transactions built using this method can be deployed using /api/SmartContractWallet/send-transaction.
        /// However, unless there is a need to closely examine the transaction before deploying it, you should use
        /// api/SmartContracts/build-and-send-create.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param>
        /// 
        /// <returns>A transaction ready to create a smart contract.</returns>
        /// <response code="200">Returns create contract response</response>
        /// <response code="400">Invalid request or failed to build transaction</response>
        [Route("api/[controller]/build-create")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult BuildCreateSmartContractTransaction([FromBody] BuildCreateContractTransactionRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            BuildCreateContractTransactionResponse response = this.smartContractTransactionService.BuildCreateTx(request);

            if (!response.Success)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, response.Message, string.Empty);

            return this.Json(response);
        }

        /// <summary>
        /// Builds a transaction to call a smart contract method. Although the transaction is created, the
        /// call is not made, and no gas or fees are consumed.
        /// Instead the created transaction is returned as a JSON object.
        /// Transactions built using this method can be deployed using /api/SmartContractWallet/send-transaction
        /// However, unless there is a need to closely examine the transaction before deploying it, you should use
        /// api/SmartContracts/build-and-send-call.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param>
        /// 
        /// <returns>A transaction ready to call a method on a smart contract.</returns>
        /// <response code="200">Returns call contract response</response>
        /// <response code="400">Invalid request or failed to build transaction</response>
        [Route("api/[controller]/build-call")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult BuildCallSmartContractTransaction([FromBody] BuildCallContractTransactionRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            BuildCallContractTransactionResponse response = this.smartContractTransactionService.BuildCallTx(request);

            if (!response.Success)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, response.Message, string.Empty);

            return this.Json(response);
        }

        /// <summary>
        /// Builds a transaction to transfer funds on a smart contract network.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param>
        /// 
        /// <returns>The build transaction hex.</returns>
        /// <response code="200">Returns transaction response</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        [Route("api/[controller]/build-transaction")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult BuildTransaction([FromBody] BuildContractTransactionRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                BuildContractTransactionResult result = this.smartContractTransactionService.BuildTx(request);

                return this.Json(result.Response);
            }
            catch (Exception e)
            {
                this.logger.LogError("Exception occurred: {0}", e.ToString());
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, e.Message, e.ToString());
            }
        }

        /// <summary>
        /// Gets a fee estimate for a specific smart contract account-based transfer transaction.
        /// This differs from fee estimation on standard networks due to the way inputs must be selected for account-based transfers.
        /// </summary>
        /// <param name="request">An object containing the parameters used to build the the fee estimation transaction.</param>
        /// <returns>The estimated fee for the transaction.</returns>
        /// <response code="200">Returns estimated fee</response>
        /// <response code="400">Invalid request or unexpected exception occurred</response>
        [Route("api/[controller]/estimate-fee")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        public IActionResult EstimateFee([FromBody] ScTxFeeEstimateRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            try
            {
                EstimateFeeResult result = this.smartContractTransactionService.EstimateFee(request);

                return this.Json(result.Fee);
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
        /// <returns>The transaction used to create the smart contract. The result of the transaction broadcast is not returned
        /// and you should check for a transaction receipt to see if it was successful.</returns>
        /// <response code="200">Returns create transaction response</response>
        /// <response code="400">Invalid request, failed to build transaction, or cannot broadcast transaction</response>
        /// <response code="403">No connected peers</response>
        [Route("api/[controller]/build-and-send-create")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.Forbidden)]
        public async Task<IActionResult> BuildAndSendCreateSmartContractTransactionAsync([FromBody] BuildCreateContractTransactionRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            // Ignore this check if the node is running dev mode.
            if (this.nodeSettings.DevMode == null && !this.connectionManager.ConnectedPeers.Any())
            {
                this.logger.LogTrace("(-)[NO_CONNECTED_PEERS]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.Forbidden, "Can't send transaction as the node requires at least one connection.", string.Empty);
            }

            BuildCreateContractTransactionResponse response = this.smartContractTransactionService.BuildCreateTx(request);

            if (!response.Success)
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, response.Message, string.Empty);

            Transaction transaction = this.network.CreateTransaction(response.Hex);

            await this.broadcasterManager.BroadcastTransactionAsync(transaction);

            // Check if transaction was actually added to a mempool.
            TransactionBroadcastEntry transactionBroadCastEntry = this.broadcasterManager.GetTransaction(transaction.GetHash());

            if (transactionBroadCastEntry?.TransactionBroadcastState == Features.Wallet.Broadcasting.TransactionBroadcastState.CantBroadcast)
            {
                this.logger.LogError("Exception occurred: {0}", transactionBroadCastEntry.ErrorMessage);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, transactionBroadCastEntry.ErrorMessage, "Transaction Exception");
            }

            response.Message = "Your CREATE contract transaction was successfully built and sent. Check the receipt using the transaction ID once it has been included in a new block.";

            return this.Json(response);
        }

        /// <summary>
        /// Builds a transaction to call a smart contract method and then broadcasts the transaction to the network.
        /// If the call is successful, any changes to the smart contract balance or persistent data are propagated
        /// across the network.
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param>
        ///
        /// <returns>The transaction used to call a smart contract method. The result of the transaction broadcast is not returned
        /// and you should check for a transaction receipt to see if it was successful.</returns>
        /// <response code="200">Returns call transaction response</response>
        /// <response code="400">Invalid request or cannot broadcast transaction</response>
        /// <response code="403">No connected peers</response>
        [Route("api/[controller]/build-and-send-call")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.Forbidden)]
        public async Task<IActionResult> BuildAndSendCallSmartContractTransactionAsync([FromBody] BuildCallContractTransactionRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            // Ignore this check if the node is running dev mode.
            if (this.nodeSettings.DevMode == null && !this.connectionManager.ConnectedPeers.Any())
            {
                this.logger.LogTrace("(-)[NO_CONNECTED_PEERS]");
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.Forbidden, "Can't send transaction as the node requires at least one connection.", string.Empty);
            }

            BuildCallContractTransactionResponse response = this.smartContractTransactionService.BuildCallTx(request);
            if (!response.Success)
                return this.Json(response);

            Transaction transaction = this.network.CreateTransaction(response.Hex);

            await this.broadcasterManager.BroadcastTransactionAsync(transaction);

            // Check if transaction was actually added to a mempool.
            TransactionBroadcastEntry transactionBroadCastEntry = this.broadcasterManager.GetTransaction(transaction.GetHash());

            if (transactionBroadCastEntry?.TransactionBroadcastState == Features.Wallet.Broadcasting.TransactionBroadcastState.CantBroadcast)
            {
                this.logger.LogError("Exception occurred: {0}", transactionBroadCastEntry.ErrorMessage);
                return ErrorHelpers.BuildErrorResponse(HttpStatusCode.BadRequest, transactionBroadCastEntry.ErrorMessage, "Transaction Exception");
            }

            response.Message = $"Your CALL method {request.MethodName} transaction was successfully built and sent. Check the receipt using the transaction ID once it has been included in a new block.";

            return this.Json(response);
        }

        /// <summary>
        /// Makes a local call to a method on a smart contract that has been successfully deployed. A transaction 
        /// is not created as the call is never propagated across the network. All persistent data held by the   
        /// smart contract is copied before the call is made. Only this copy is altered by the call
        /// and the actual data is unaffected. Even if an amount of funds are specified to send with the call,
        /// no funds are in fact sent.
        /// The purpose of this function is to query and test methods. 
        /// </summary>
        /// 
        /// <param name="request">An object containing the necessary parameters to build the transaction.</param>
        /// 
        /// <results>The result of the local call to the smart contract method.</results>
        /// <response code="200">Returns call response</response>
        /// <response code="400">Invalid request</response>
        /// <response code="500">Unable to deserialize method parameters</response>
        [Route("api/[controller]/local-call")]
        [HttpPost]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        [ProducesResponseType((int)HttpStatusCode.BadRequest)]
        [ProducesResponseType((int)HttpStatusCode.InternalServerError)]
        public IActionResult LocalCallSmartContractTransaction([FromBody] LocalCallContractRequest request)
        {
            if (!this.ModelState.IsValid)
                return ModelStateErrors.BuildErrorResponse(this.ModelState);

            // Rewrite the method name to a property name
            this.RewritePropertyGetterName(request);

            try
            {
                ContractTxData txData = this.smartContractTransactionService.BuildLocalCallTxData(request);

                var height = request.BlockHeight.HasValue ? request.BlockHeight.Value : (ulong)this.chainIndexer.Height;

                ILocalExecutionResult result = this.localExecutor.Execute(
                    height,
                    request.Sender?.ToUint160(this.network) ?? new uint160(),
                    !string.IsNullOrWhiteSpace(request.Amount) ? (Money)request.Amount : 0,
                    txData);

                var deserializer = new ApiLogDeserializer(this.primitiveSerializer, this.network, result.StateRoot, this.contractAssemblyCache);

                var response = new LocalExecutionResponse
                {
                    InternalTransfers = deserializer.MapTransferInfo(result.InternalTransfers.ToArray()),
                    Logs = deserializer.MapLogResponses(result.Logs.ToArray()),
                    GasConsumed = result.GasConsumed,
                    Revert = result.Revert,
                    ErrorMessage = result.ErrorMessage,
                    Return = result.Return // All return values should be primitives, let default serializer handle.
                };

                return this.Json(response, new JsonSerializerSettings
                {
                    ContractResolver = new ContractParametersContractResolver(this.network)
                });
            }
            catch (MethodParameterStringSerializerException e)
            {
                return this.Json(ErrorHelpers.BuildErrorResponse(HttpStatusCode.InternalServerError, e.Message,
                    "Error deserializing method parameters"));
            }
        }

        /// <summary>
        /// If the call is to a property, rewrites the method name to the getter method's name.
        /// </summary>
        private void RewritePropertyGetterName(LocalCallContractRequest request)
        {
            // Don't rewrite if there are params
            if (request.Parameters != null && request.Parameters.Any())
                return;

            byte[] contractCode = this.stateRoot.GetCode(request.ContractAddress.ToUint160(this.network));

            string contractType = this.stateRoot.GetContractType(request.ContractAddress.ToUint160(this.network));

            Result<IContractModuleDefinition> readResult = ContractDecompiler.GetModuleDefinition(contractCode);

            if (readResult.IsSuccess)
            {
                IContractModuleDefinition contractModule = readResult.Value;
                string propertyGetterName = contractModule.GetPropertyGetterMethodName(contractType, request.MethodName);

                if (propertyGetterName != null)
                {
                    request.MethodName = propertyGetterName;
                }
            }
        }

        /// <summary>
        /// Gets all addresses owned by a wallet which have a balance associated with them. This
        /// method effectively returns the balance of all the UTXOs associated with a wallet.
        /// In a case where multiple UTXOs are associated with one address, the amounts
        /// are tallied to give a total for that address.
        /// </summary>
        ///
        /// <param name="walletName">The name of the wallet to retrieve the addresses from.</param>
        /// 
        /// <returns>The addresses owned by a wallet which have a balance associated with them.</returns>
        /// <response code="200">Returns address balances</response>
        [Route("api/[controller]/address-balances")]
        [HttpGet]
        [ProducesResponseType((int)HttpStatusCode.OK)]
        public IActionResult GetAddressesWithBalances([FromQuery] string walletName)
        {
            IEnumerable<IGrouping<HdAddress, UnspentOutputReference>> allSpendable = this.walletManager.GetSpendableTransactionsInWallet(walletName, MinConfirmationsAllChecks).GroupBy(x => x.Address);
            var result = new List<object>();
            foreach (IGrouping<HdAddress, UnspentOutputReference> grouping in allSpendable)
            {
                result.Add(new
                {
                    grouping.Key.Address,
                    Sum = grouping.Sum(x => x.Transaction.GetUnspentAmount(false))
                });
            }

            return this.Json(result);
        }

        private object InterpretStorageValue(MethodParameterDataType dataType, byte[] bytes)
        {
            switch (dataType)
            {
                case MethodParameterDataType.Bool:
                    return this.serializer.ToBool(bytes);
                case MethodParameterDataType.Byte:
                    return bytes[0];
                case MethodParameterDataType.Char:
                    return this.serializer.ToChar(bytes);
                case MethodParameterDataType.String:
                    return this.serializer.ToString(bytes);
                case MethodParameterDataType.UInt:
                    return this.serializer.ToUInt32(bytes);
                case MethodParameterDataType.Int:
                    return this.serializer.ToInt32(bytes);
                case MethodParameterDataType.ULong:
                    return this.serializer.ToUInt64(bytes);
                case MethodParameterDataType.Long:
                    return this.serializer.ToInt64(bytes);
                case MethodParameterDataType.Address:
                    return this.serializer.ToAddress(bytes);
                case MethodParameterDataType.ByteArray:
                    return bytes.ToHexString();
                case MethodParameterDataType.UInt128:
                    return this.serializer.ToUInt128(bytes);
                case MethodParameterDataType.UInt256:
                    return this.serializer.ToUInt256(bytes);
            }

            return null;
        }
    }
}