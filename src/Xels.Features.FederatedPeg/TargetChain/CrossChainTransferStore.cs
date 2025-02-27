﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DBreeze;
using DBreeze.DataTypes;
using DBreeze.Utils;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Utilities;
using Xels.Features.FederatedPeg.Events;
using Xels.Features.FederatedPeg.Exceptions;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.Models;
using Xels.Features.FederatedPeg.Wallet;
using Xels.SmartContracts.Core.State;

namespace Xels.Features.FederatedPeg.TargetChain
{
    public sealed class CrossChainTransferStore : ICrossChainTransferStore
    {
        /// <summary>
        /// Given that we can have up to 10 UTXOs going at once.
        /// </summary>
        private const int TransfersToDisplay = 10;

        /// <summary>
        /// Maximum number of partial transactions.
        /// </summary>
        public const int MaximumPartialTransactions = 100;

        /// <summary>This table contains the cross-chain transfer information.</summary>
        private const string transferTableName = "Transfers";

        /// <summary>This table keeps track of the chain tips so that we know exactly what data our transfer table contains.</summary>
        private const string commonTableName = "Common";

        // <summary>Block batch size for synchronization</summary>
        private const int SynchronizationBatchSize = 1000;

        /// <summary>This contains deposits ids indexed by block hash of the corresponding transaction.</summary>
        private readonly Dictionary<uint256, HashSet<uint256>> depositIdsByBlockHash = new Dictionary<uint256, HashSet<uint256>>();

        /// <summary>This contains the block heights by block hashes for only the blocks of interest in our chain.</summary>
        private readonly Dictionary<uint256, int> blockHeightsByBlockHash = new Dictionary<uint256, int>();

        /// <summary>This table contains deposits ids by status.</summary>
        private readonly Dictionary<CrossChainTransferStatus, HashSet<uint256>> depositsIdsByStatus = new Dictionary<CrossChainTransferStatus, HashSet<uint256>>();

        /// <inheritdoc />
        public int NextMatureDepositHeight { get; private set; }

        /// <inheritdoc />
        public ChainedHeader TipHashAndHeight { get; private set; }

        /// <summary>The key of the repository tip in the common table.</summary>
        private static readonly byte[] RepositoryTipKey = new byte[] { 0 };

        /// <summary>The key of the counter-chain last mature block tip in the common table.</summary>
        private static readonly byte[] NextMatureTipKey = new byte[] { 1 };

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Access to DBreeze database.</summary>
        private readonly DBreezeEngine DBreeze;

        private readonly IBlockRepository blockRepository;
        private readonly CancellationTokenSource cancellation;
        private readonly ChainIndexer chainIndexer;
        private readonly DBreezeSerializer dBreezeSerializer;
        private readonly IFederationWalletManager federationWalletManager;
        private readonly Network network;
        private readonly INodeStats nodeStats;
        private readonly IFederatedPegSettings settings;
        private readonly ISignals signals;
        private readonly IStateRepositoryRoot stateRepositoryRoot;
        private readonly IWithdrawalExtractor withdrawalExtractor;
        private readonly IWithdrawalHistoryProvider withdrawalHistoryProvider;
        private readonly IWithdrawalTransactionBuilder withdrawalTransactionBuilder;

        /// <summary>Provider of time functions.</summary>
        private readonly object lockObj;

        public CrossChainTransferStore(Network network, INodeStats nodeStats, DataFolder dataFolder, ChainIndexer chainIndexer, IFederatedPegSettings settings, IDateTimeProvider dateTimeProvider,
            IWithdrawalExtractor withdrawalExtractor, IWithdrawalHistoryProvider withdrawalHistoryProvider, IBlockRepository blockRepository, IFederationWalletManager federationWalletManager, IWithdrawalTransactionBuilder withdrawalTransactionBuilder,
            DBreezeSerializer dBreezeSerializer, ISignals signals, IStateRepositoryRoot stateRepositoryRoot = null)
        {
            if (!settings.IsMainChain)
            {
                Guard.NotNull(stateRepositoryRoot, nameof(stateRepositoryRoot));
            }

            Guard.NotNull(network, nameof(network));
            Guard.NotNull(dataFolder, nameof(dataFolder));
            Guard.NotNull(chainIndexer, nameof(chainIndexer));
            Guard.NotNull(settings, nameof(settings));
            Guard.NotNull(dateTimeProvider, nameof(dateTimeProvider));
            Guard.NotNull(withdrawalExtractor, nameof(withdrawalExtractor));
            Guard.NotNull(blockRepository, nameof(blockRepository));
            Guard.NotNull(federationWalletManager, nameof(federationWalletManager));
            Guard.NotNull(withdrawalTransactionBuilder, nameof(withdrawalTransactionBuilder));

            this.network = network;
            this.nodeStats = nodeStats;
            this.chainIndexer = chainIndexer;
            this.blockRepository = blockRepository;
            this.federationWalletManager = federationWalletManager;
            this.dBreezeSerializer = dBreezeSerializer;
            this.lockObj = new object();
            this.logger = LogManager.GetCurrentClassLogger();
            this.TipHashAndHeight = this.chainIndexer.GetHeader(0);
            this.NextMatureDepositHeight = 1;
            this.cancellation = new CancellationTokenSource();
            this.settings = settings;
            this.signals = signals;
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.withdrawalExtractor = withdrawalExtractor;
            this.withdrawalHistoryProvider = withdrawalHistoryProvider;
            this.withdrawalTransactionBuilder = withdrawalTransactionBuilder;

            // Future-proof store name.
            string depositStoreName = "federatedTransfers" + settings.MultiSigAddress.ToString();
            string folder = Path.Combine(dataFolder.RootPath, depositStoreName);
            Directory.CreateDirectory(folder);
            this.DBreeze = new DBreezeEngine(folder);

            // Initialize tracking deposits by status.
            foreach (object status in typeof(CrossChainTransferStatus).GetEnumValues())
                this.depositsIdsByStatus[(CrossChainTransferStatus)status] = new HashSet<uint256>();

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
        }

        /// <summary>Performs any needed initialisation for the database.</summary>
        public void Initialize()
        {
            lock (this.lockObj)
            {
                using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
                {
                    dbreezeTransaction.ValuesLazyLoadingIsOn = false;

                    this.LoadTipHashAndHeight(dbreezeTransaction);
                    this.LoadNextMatureHeight(dbreezeTransaction);

                    // Initialize the lookups.
                    foreach (Row<byte[], byte[]> transferRow in dbreezeTransaction.SelectForward<byte[], byte[]>(transferTableName))
                    {
                        var transfer = new CrossChainTransfer();
                        transfer.FromBytes(transferRow.Value, this.network.Consensus.ConsensusFactory);
                        this.depositsIdsByStatus[transfer.Status].Add(transfer.DepositTransactionId);

                        if (transfer.BlockHash != null && transfer.BlockHeight != null)
                        {
                            if (!this.depositIdsByBlockHash.TryGetValue(transfer.BlockHash, out HashSet<uint256> deposits))
                            {
                                deposits = new HashSet<uint256>();
                                this.depositIdsByBlockHash[transfer.BlockHash] = deposits;
                            }

                            deposits.Add(transfer.DepositTransactionId);

                            this.blockHeightsByBlockHash[transfer.BlockHash] = (int)transfer.BlockHeight;
                        }
                    }
                }
            }
        }

        /// <summary>Starts the cross-chain-transfer store.</summary>
        public void Start()
        {
            lock (this.lockObj)
            {
                this.federationWalletManager.Synchronous(() =>
                {
                    this.Synchronize();
                });
            }
        }

        /// <inheritdoc />
        public bool HasSuspended()
        {
            lock (this.lockObj)
            {
                return this.depositsIdsByStatus[CrossChainTransferStatus.Suspended].Count != 0;
            }
        }

        /// <summary>
        /// Partial or fully signed transfers should have their source UTXO's recorded by an up-to-date wallet.
        /// Sets transfers to <see cref="CrossChainTransferStatus.Suspended"/> if their UTXO's are not reserved
        /// within the wallet.
        /// </summary>
        /// <param name="crossChainTransfers">The transfers to check. If not supplied then all partial and fully signed transfers are checked.</param>
        /// <returns>Returns the list of transfers, possible with updated statuses.</returns>
        private ICrossChainTransfer[] ValidateCrossChainTransfers(ICrossChainTransfer[] crossChainTransfers = null)
        {
            if (crossChainTransfers == null)
            {
                crossChainTransfers = this.Get(
                    this.depositsIdsByStatus[CrossChainTransferStatus.Partial].Union(
                        this.depositsIdsByStatus[CrossChainTransferStatus.FullySigned]).ToArray());
            }

            var tracker = new StatusChangeTracker();
            int newChainATip = this.NextMatureDepositHeight;

            foreach (ICrossChainTransfer partialTransfer in crossChainTransfers)
            {
                if (partialTransfer == null)
                    continue;

                if (partialTransfer.Status != CrossChainTransferStatus.Partial && partialTransfer.Status != CrossChainTransferStatus.FullySigned)
                    continue;

                List<(Transaction transaction, IWithdrawal withdrawal)> walletData = this.federationWalletManager.FindWithdrawalTransactions(partialTransfer.DepositTransactionId);

                this.logger.LogTrace("DepositTransactionId:{0}; {1}:{2}", partialTransfer.DepositTransactionId, nameof(walletData), walletData.Count);

                if (walletData.Count == 1 && this.ValidateTransaction(walletData[0].transaction))
                {
                    Transaction walletTran = walletData[0].transaction;
                    if (walletTran.GetHash() == partialTransfer.PartialTransaction.GetHash())
                        continue;

                    if (SigningUtils.TemplatesMatch(this.network, walletTran, partialTransfer.PartialTransaction))
                    {
                        partialTransfer.SetPartialTransaction(walletTran);

                        if (walletData[0].withdrawal.BlockNumber != 0)
                            tracker.SetTransferStatus(partialTransfer, CrossChainTransferStatus.SeenInBlock, walletData[0].withdrawal.BlockHash, (int)walletData[0].withdrawal.BlockNumber);
                        else if (this.ValidateTransaction(walletTran, true))
                            tracker.SetTransferStatus(partialTransfer, CrossChainTransferStatus.FullySigned);
                        else
                            tracker.SetTransferStatus(partialTransfer, CrossChainTransferStatus.Partial);

                        continue;
                    }

                    this.logger.LogDebug("Templates don't match for {0} and {1}.", walletTran.GetHash(), partialTransfer.PartialTransaction.GetHash());
                }

                // The chain may have been rewound so that this transaction or its UTXO's have been lost.
                // Rewind our recorded chain A tip to ensure the transaction is re-built once UTXO's become available.
                if (partialTransfer.DepositHeight < newChainATip)
                    newChainATip = partialTransfer.DepositHeight ?? newChainATip;

                this.logger.LogDebug("Setting DepositId {0} to Suspended", partialTransfer.DepositTransactionId);

                tracker.SetTransferStatus(partialTransfer, CrossChainTransferStatus.Suspended);
            }

            if (tracker.Count == 0)
            {
                this.logger.LogTrace("(-)[NO_CHANGES_IN_TRACKER]");
                return crossChainTransfers;
            }

            using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
            {
                dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);

                int oldChainATip = this.NextMatureDepositHeight;

                try
                {
                    foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
                    {
                        this.PutTransfer(dbreezeTransaction, kv.Key);
                    }

                    this.SaveNextMatureHeight(dbreezeTransaction, newChainATip);
                    dbreezeTransaction.Commit();
                    this.UpdateLookups(tracker);

                    // Remove any remnants of suspended transactions from the wallet.
                    foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
                    {
                        if (kv.Key.Status == CrossChainTransferStatus.Suspended)
                        {
                            this.federationWalletManager.RemoveWithdrawalTransactions(kv.Key.DepositTransactionId);
                        }
                    }

                    this.federationWalletManager.SaveWallet();

                    return crossChainTransfers;
                }
                catch (Exception err)
                {
                    // Restore expected store state in case the calling code retries / continues using the store.
                    this.NextMatureDepositHeight = oldChainATip;

                    this.RollbackAndThrowTransactionError(dbreezeTransaction, err, "SANITY_ERROR");

                    // Dummy return as the above method throws. Avoids compiler error.
                    return null;
                }
            }
        }

        /// <summary>Rolls back the database if an operation running in the context of a database transaction fails.</summary>
        /// <param name="dbreezeTransaction">Database transaction to roll back.</param>
        /// <param name="exception">Exception to report and re-raise.</param>
        /// <param name="reason">Short reason/context code of failure.</param>
        private void RollbackAndThrowTransactionError(DBreeze.Transactions.Transaction dbreezeTransaction, Exception exception, string reason = "FAILED_TRANSACTION")
        {
            this.logger.LogError("Error during database update: {0}, reason: {1}", exception.Message, reason);

            dbreezeTransaction.Rollback();
            throw exception;
        }

        /// <inheritdoc />
        public Task SaveCurrentTipAsync()
        {
            return Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
                    {
                        dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);
                        this.SaveNextMatureHeight(dbreezeTransaction, this.NextMatureDepositHeight);
                        dbreezeTransaction.Commit();
                    }
                }
            });
        }

        /// <inheritdoc />
        public void RejectTransfer(ICrossChainTransfer crossChainTransfer)
        {
            Guard.Assert(crossChainTransfer.Status == CrossChainTransferStatus.FullySigned);

            lock (this.lockObj)
            {
                var tracker = new StatusChangeTracker();

                tracker.SetTransferStatus(crossChainTransfer, CrossChainTransferStatus.Rejected);

                this.federationWalletManager.Synchronous(() =>
                {
                    if (!this.Synchronize())
                        return;

                    using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
                    {
                        try
                        {
                            dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);

                            // Update new or modified transfers.
                            foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
                                this.PutTransfer(dbreezeTransaction, kv.Key);

                            dbreezeTransaction.Commit();
                            this.UpdateLookups(tracker);

                            foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
                                this.federationWalletManager.RemoveWithdrawalTransactions(kv.Key.DepositTransactionId);

                            this.federationWalletManager.SaveWallet();
                        }
                        catch (Exception err)
                        {
                            this.logger.LogError("An error occurred when processing deposits {0}", err);

                            this.RollbackAndThrowTransactionError(dbreezeTransaction, err, "REJECT_ERROR");
                        }
                    }
                });
            }
        }

        /// <inheritdoc />
        public Task<RecordLatestMatureDepositsResult> RecordLatestMatureDepositsAsync(IList<MaturedBlockDepositsModel> maturedBlockDeposits)
        {
            Guard.NotNull(maturedBlockDeposits, nameof(maturedBlockDeposits));

            return Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    int originalDepositHeight = this.NextMatureDepositHeight;

                    // Sanitize and sort the list.
                    maturedBlockDeposits = maturedBlockDeposits
                        .OrderBy(a => a.BlockInfo.BlockHeight)
                        .SkipWhile(m => m.BlockInfo.BlockHeight < this.NextMatureDepositHeight).ToArray();

                    if (maturedBlockDeposits.Count == 0 || maturedBlockDeposits.First().BlockInfo.BlockHeight != this.NextMatureDepositHeight)
                    {
                        this.logger.LogDebug($"No viable blocks to process; {nameof(maturedBlockDeposits)}={maturedBlockDeposits.Count};{nameof(this.NextMatureDepositHeight)}={this.NextMatureDepositHeight}");
                        return new RecordLatestMatureDepositsResult().Succeeded();
                    }

                    if (maturedBlockDeposits.Last().BlockInfo.BlockHeight != this.NextMatureDepositHeight + maturedBlockDeposits.Count - 1)
                    {
                        this.logger.LogDebug("(-)[DUPLICATE_BLOCKS]:true");
                        return new RecordLatestMatureDepositsResult().Succeeded();
                    }

                    // Paying to our own multisig is a null operation and not supported.
                    bool depositFilter(IDeposit d) => d.TargetAddress != this.settings.MultiSigAddress.ToString();

                    if (!maturedBlockDeposits.Any(md => md.Deposits.Any(depositFilter)))
                    {
                        this.NextMatureDepositHeight += maturedBlockDeposits.Count;

                        this.logger.LogDebug("(-)[NO_DEPOSITS]:true");
                        return new RecordLatestMatureDepositsResult().Succeeded();
                    }

                    var recordDepositResult = new RecordLatestMatureDepositsResult();

                    this.federationWalletManager.Synchronous(() =>
                    {
                        if (!this.Synchronize())
                            return;

                        this.logger.LogInformation($"{maturedBlockDeposits.Count} blocks received, containing a total of {maturedBlockDeposits.SelectMany(d => d.Deposits).Where(a => a.Amount > 0).Count()} deposits.");
                        this.logger.LogInformation($"Block Range : {maturedBlockDeposits.Min(a => a.BlockInfo.BlockHeight)} to {maturedBlockDeposits.Max(a => a.BlockInfo.BlockHeight)}.");

                        foreach (MaturedBlockDepositsModel maturedDeposit in maturedBlockDeposits)
                        {
                            if (maturedDeposit.BlockInfo.BlockHeight != this.NextMatureDepositHeight)
                                continue;

                            IReadOnlyList<IDeposit> deposits = maturedDeposit.Deposits.Where(depositFilter).ToList();
                            if (deposits.Count == 0)
                            {
                                this.NextMatureDepositHeight++;
                                continue;
                            }

                            if (!this.federationWalletManager.IsFederationWalletActive())
                            {
                                this.logger.LogError("The store can't persist mature deposits while the federation is inactive.");
                                continue;
                            }

                            ICrossChainTransfer[] transfers = this.ValidateCrossChainTransfers(this.Get(deposits.Select(d => d.Id).ToArray()));

                            var tracker = new StatusChangeTracker();
                            bool walletUpdated = false;

                            // Deposits are assumed to be in order of occurrence on the source chain.
                            // If we fail to build a transaction the transfer and subsequent transfers
                            // in the ordered list will be set to suspended.
                            bool haveSuspendedTransfers = false;

                            for (int i = 0; i < deposits.Count; i++)
                            {
                                if (transfers[i] != null && transfers[i].Status != CrossChainTransferStatus.Suspended)
                                    continue;

                                IDeposit deposit = deposits[i];
                                Transaction transaction = null;
                                CrossChainTransferStatus status = CrossChainTransferStatus.Suspended;
                                Script scriptPubKey = BitcoinAddress.Create(deposit.TargetAddress, this.network).ScriptPubKey;

                                if (!haveSuspendedTransfers)
                                {
                                    var recipient = new Recipient
                                    {
                                        Amount = deposit.Amount,
                                        ScriptPubKey = scriptPubKey
                                    };

                                    // Can't send funds to known contract address.
                                    bool invalidRecipient = false;

                                    if (this.stateRepositoryRoot != null)
                                    {
                                        KeyId p2pkhParams = PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(recipient.ScriptPubKey);

                                        if (p2pkhParams != null && this.stateRepositoryRoot.GetAccountState(new uint160(p2pkhParams.ToBytes())) != null)
                                        {
                                            invalidRecipient = true;
                                        }
                                    }

                                    if (invalidRecipient)
                                    {
                                        this.logger.LogInformation("Invalid recipient.");

                                        status = CrossChainTransferStatus.Rejected;
                                    }
                                    else if ((tracker.Count(t => t.Value == CrossChainTransferStatus.Partial) + this.depositsIdsByStatus[CrossChainTransferStatus.Partial].Count) >= this.settings.MaximumPartialTransactionThreshold)
                                    {
                                        haveSuspendedTransfers = true;
                                        this.logger.LogWarning($"Partial transaction limit of {this.settings.MaximumPartialTransactionThreshold} reached, processing of deposits will continue once the partial transaction count falls below this value.");
                                    }
                                    else
                                    {
                                        transaction = this.withdrawalTransactionBuilder.BuildWithdrawalTransaction(maturedDeposit.BlockInfo.BlockHeight, deposit.Id, maturedDeposit.BlockInfo.BlockTime, recipient);

                                        if (transaction != null)
                                        {
                                            // Reserve the UTXOs before building the next transaction.
                                            walletUpdated |= this.federationWalletManager.ProcessTransaction(transaction);

                                            if (!this.ValidateTransaction(transaction))
                                            {
                                                this.logger.LogInformation("Suspending transfer for deposit '{0}' to retry invalid transaction later.", deposit.Id);

                                                this.federationWalletManager.RemoveWithdrawalTransactions(deposit.Id);
                                                haveSuspendedTransfers = true;
                                                transaction = null;
                                            }
                                            else
                                            {
                                                status = CrossChainTransferStatus.Partial;
                                                recordDepositResult.WithdrawalTransactions.Add(transaction);
                                            }
                                        }
                                        else
                                        {
                                            this.logger.LogInformation("Unable to build withdrawal transaction, suspending.");
                                            haveSuspendedTransfers = true;
                                        }
                                    }
                                }
                                else
                                {
                                    this.logger.LogInformation("Suspended flag set: '{0}'", deposit);
                                }

                                if (transfers[i] == null || transaction == null)
                                {
                                    transfers[i] = new CrossChainTransfer(status, deposit.Id, scriptPubKey, deposit.Amount, maturedDeposit.BlockInfo.BlockHeight, transaction, null, null);
                                    tracker.SetTransferStatus(transfers[i]);
                                    this.logger.LogDebug("Set {0} to {1}.", transfers[i]?.DepositTransactionId, status);
                                }
                                else
                                {
                                    transfers[i].SetPartialTransaction(transaction);
                                    tracker.SetTransferStatus(transfers[i], CrossChainTransferStatus.Partial);
                                    this.logger.LogDebug("Set {0} to Partial.", transfers[i]?.DepositTransactionId);
                                }
                            }

                            using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
                            {
                                dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);

                                int currentDepositHeight = this.NextMatureDepositHeight;

                                try
                                {
                                    if (walletUpdated)
                                    {
                                        this.federationWalletManager.SaveWallet();
                                    }

                                    // Update new or modified transfers.
                                    foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
                                    {
                                        this.PutTransfer(dbreezeTransaction, kv.Key);
                                    }

                                    // Ensure we get called for a retry by NOT advancing the chain A tip if the block
                                    // contained any suspended transfers.
                                    if (!haveSuspendedTransfers)
                                        this.SaveNextMatureHeight(dbreezeTransaction, this.NextMatureDepositHeight + 1);

                                    dbreezeTransaction.Commit();
                                    this.UpdateLookups(tracker);
                                }
                                catch (Exception err)
                                {
                                    this.logger.LogError("An error occurred when processing deposits {0}", err);

                                    // Undo reserved UTXO's.
                                    if (walletUpdated)
                                    {
                                        foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
                                        {
                                            if (kv.Value == CrossChainTransferStatus.Partial)
                                            {
                                                this.federationWalletManager.RemoveWithdrawalTransactions(kv.Key.DepositTransactionId);
                                            }
                                        }

                                        this.federationWalletManager.SaveWallet();
                                    }

                                    // Restore expected store state in case the calling code retries / continues using the store.
                                    this.NextMatureDepositHeight = currentDepositHeight;
                                    this.RollbackAndThrowTransactionError(dbreezeTransaction, err, "DEPOSIT_ERROR");
                                }
                            }
                        }
                    });

                    // If progress was made we will check for more blocks.
                    if (this.NextMatureDepositHeight != originalDepositHeight)
                        return recordDepositResult.Succeeded();

                    return recordDepositResult;
                }
            });
        }

        /// <inheritdoc />
        public Transaction MergeTransactionSignatures(uint256 depositId, Transaction[] partialTransactions)
        {
            Guard.NotNull(depositId, nameof(depositId));
            Guard.NotNull(partialTransactions, nameof(partialTransactions));

            lock (this.lockObj)
            {
                return this.federationWalletManager.Synchronous(() =>
                {
                    if (!this.Synchronize())
                        return null;

                    ICrossChainTransfer transfer = this.ValidateCrossChainTransfers(this.Get(new[] { depositId })).FirstOrDefault();

                    if (transfer == null)
                    {
                        this.logger.LogDebug("(-)[MERGE_NOT_FOUND]:null");
                        return null;
                    }

                    if (transfer.Status != CrossChainTransferStatus.Partial)
                    {
                        this.logger.LogDebug("(-)[MERGE_BAD_STATUS]:{0}={1}", nameof(transfer.Status), transfer.Status);
                        return transfer.PartialTransaction;
                    }

                    try
                    {

                        this.logger.LogDebug("Partial Transaction inputs:{0}", partialTransactions[0].Inputs.Count);
                        this.logger.LogDebug("Partial Transaction outputs:{0}", partialTransactions[0].Outputs.Count);

                        for (int i = 0; i < partialTransactions[0].Inputs.Count; i++)
                        {
                            TxIn input = partialTransactions[0].Inputs[i];
                            this.logger.LogDebug("Partial Transaction Input N:{0} : Hash:{1}", input.PrevOut.N, input.PrevOut.Hash);
                        }

                        for (int i = 0; i < partialTransactions[0].Outputs.Count; i++)
                        {
                            TxOut output = partialTransactions[0].Outputs[i];
                            this.logger.LogDebug("Partial Transaction Output Value:{0} : ScriptPubKey:{1}", output.Value, output.ScriptPubKey);
                        }

                        this.logger.LogDebug("Transfer Partial Transaction inputs:{0}", transfer.PartialTransaction.Inputs.Count);
                        this.logger.LogDebug("Transfer Partial Transaction outputs:{0}", transfer.PartialTransaction.Outputs.Count);

                        for (int i = 0; i < transfer.PartialTransaction.Inputs.Count; i++)
                        {
                            TxIn transferInput = transfer.PartialTransaction.Inputs[i];
                            this.logger.LogDebug("Transfer Partial Transaction Input N:{0} : Hash:{1}", transferInput.PrevOut.N, transferInput.PrevOut.Hash);
                        }

                        for (int i = 0; i < transfer.PartialTransaction.Outputs.Count; i++)
                        {
                            TxOut transferOutput = transfer.PartialTransaction.Outputs[i];
                            this.logger.LogDebug("Transfer Partial Transaction Output Value:{0} : ScriptPubKey:{1}", transferOutput.Value, transferOutput.ScriptPubKey);
                        }
                    }
                    catch (Exception err)
                    {
                        this.logger.LogDebug("Failed to log transactions: {0}.", err.Message);
                    }

                    this.logger.LogDebug("Merging signatures for deposit : {0}", depositId);

                    var builder = new TransactionBuilder(this.network);
                    Transaction oldTransaction = transfer.PartialTransaction;

                    transfer.CombineSignatures(builder, partialTransactions);

                    if (transfer.PartialTransaction.GetHash() == oldTransaction.GetHash())
                    {
                        // We will finish dealing with the request here if an invalid signature is sent.
                        // The incoming partial transaction will not have the same inputs / outputs as what our node has generated
                        // so would have failed CrossChainTransfer.TemplatesMatch() and leave through here.
                        this.logger.LogDebug("(-)[MERGE_UNCHANGED_TX_HASHES_MATCH]");
                        return transfer.PartialTransaction;
                    }

                    using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
                    {
                        try
                        {
                            dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);

                            if (this.federationWalletManager.ProcessTransaction(transfer.PartialTransaction))
                                this.federationWalletManager.SaveWallet();

                            if (this.ValidateTransaction(transfer.PartialTransaction, true))
                            {
                                this.logger.LogDebug("Deposit: {0} collected enough signatures and is FullySigned", transfer.DepositTransactionId);
                                transfer.SetStatus(CrossChainTransferStatus.FullySigned);
                                this.signals.Publish(new CrossChainTransferTransactionFullySigned(transfer));
                            }
                            else
                            {
                                this.logger.LogDebug("Deposit: {0} did not collect enough signatures and is Partial", transfer.DepositTransactionId);
                            }

                            this.PutTransfer(dbreezeTransaction, transfer);
                            dbreezeTransaction.Commit();

                            // Do this last to maintain DB integrity. We are assuming that this won't throw.
                            // This will remove the transaction from the Partial dictionary, and re-insert it, either as Partial again or FullySigned dependent on what happened above.
                            this.TransferStatusUpdated(transfer, CrossChainTransferStatus.Partial);
                        }
                        catch (Exception err)
                        {
                            this.logger.LogError("Error: {0} ", err);

                            // Restore expected store state in case the calling code retries / continues using the store.
                            transfer.SetPartialTransaction(oldTransaction);

                            if (this.federationWalletManager.ProcessTransaction(oldTransaction))
                                this.federationWalletManager.SaveWallet();

                            this.RollbackAndThrowTransactionError(dbreezeTransaction, err, "MERGE_ERROR");
                        }

                        return transfer.PartialTransaction;
                    }
                });
            }
        }

        /// <summary>
        /// Uses the information contained in our chain's blocks to update the store.
        /// Sets the <see cref="CrossChainTransferStatus.SeenInBlock"/> status for transfers
        /// identified in the blocks.
        /// </summary>
        /// <param name="blocks">The blocks used to update the store. Must be sorted by ascending height leading up to the new tip.</param>
        /// <param name="chainedHeadersSnapshot">The chained headers corresponding to the blocks.</param>
        private void Put(List<Block> blocks, Dictionary<uint256, ChainedHeader> chainedHeadersSnapshot)
        {
            if (blocks.Count == 0)
                this.logger.LogTrace("(-)[NO_BLOCKS]:0");

            Dictionary<uint256, ICrossChainTransfer> transferLookup;
            Dictionary<uint256, IWithdrawal[]> allWithdrawals;

            int blockHeight = this.TipHashAndHeight.Height + 1;
            var allDepositIds = new HashSet<uint256>();

            allWithdrawals = new Dictionary<uint256, IWithdrawal[]>();
            foreach (Block block in blocks)
            {
                IReadOnlyList<IWithdrawal> blockWithdrawals = this.withdrawalExtractor.ExtractWithdrawalsFromBlock(block, blockHeight++);
                allDepositIds.UnionWith(blockWithdrawals.Select(d => d.DepositId).ToArray());
                allWithdrawals[block.GetHash()] = blockWithdrawals.ToArray();
            }

            // Nothing to do?
            if (allDepositIds.Count == 0)
            {
                // Exiting here and saving the tip after the sync.
                this.TipHashAndHeight = chainedHeadersSnapshot[blocks.Last().GetHash()];

                this.logger.LogTrace("(-)[NO_DEPOSIT_IDS]");
                return;
            }

            // Create transfer lookup by deposit Id.
            uint256[] uniqueDepositIds = allDepositIds.ToArray();
            ICrossChainTransfer[] uniqueTransfers = this.Get(uniqueDepositIds);
            transferLookup = new Dictionary<uint256, ICrossChainTransfer>();
            for (int i = 0; i < uniqueDepositIds.Length; i++)
                transferLookup[uniqueDepositIds[i]] = uniqueTransfers[i];

            // Only create a transaction if there is important work to do.
            using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
            {
                dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);

                ChainedHeader prevTip = this.TipHashAndHeight;

                try
                {
                    var tracker = new StatusChangeTracker();

                    // Find transfer transactions in blocks
                    foreach (Block block in blocks)
                    {
                        // First check the database to see if we already know about these deposits.
                        IWithdrawal[] withdrawals = allWithdrawals[block.GetHash()].ToArray();
                        ICrossChainTransfer[] crossChainTransfers = withdrawals.Select(d => transferLookup[d.DepositId]).ToArray();

                        // Update the information about these deposits or record their status.
                        for (int i = 0; i < crossChainTransfers.Length; i++)
                        {
                            IWithdrawal withdrawal = withdrawals[i];
                            Transaction transaction = block.Transactions.Single(t => t.GetHash() == withdrawal.Id);

                            // Ensure that the wallet is in step.
                            if (this.federationWalletManager.ProcessTransaction(transaction, withdrawal.BlockNumber, withdrawal.BlockHash, block))
                                this.federationWalletManager.SaveWallet();

                            if (crossChainTransfers[i] == null)
                            {
                                Script scriptPubKey = BitcoinAddress.Create(withdrawal.TargetAddress, this.network).ScriptPubKey;

                                crossChainTransfers[i] = new CrossChainTransfer(CrossChainTransferStatus.SeenInBlock, withdrawal.DepositId,
                                    scriptPubKey, withdrawal.Amount, null, transaction, withdrawal.BlockHash, withdrawal.BlockNumber);

                                tracker.SetTransferStatus(crossChainTransfers[i]);
                            }
                            else
                            {
                                crossChainTransfers[i].SetPartialTransaction(transaction);

                                tracker.SetTransferStatus(crossChainTransfers[i], CrossChainTransferStatus.SeenInBlock, withdrawal.BlockHash, withdrawal.BlockNumber);
                            }
                        }
                    }

                    // Write transfers.
                    this.PutTransfers(dbreezeTransaction, tracker.Keys.ToArray());

                    // Commit additions
                    ChainedHeader newTip = chainedHeadersSnapshot[blocks.Last().GetHash()];
                    this.SaveTipHashAndHeight(dbreezeTransaction, newTip);
                    dbreezeTransaction.Commit();

                    // Update the lookups last to ensure store integrity.
                    this.UpdateLookups(tracker);
                }
                catch (Exception err)
                {
                    // Restore expected store state in case the calling code retries / continues using the store.
                    this.TipHashAndHeight = prevTip;
                    this.RollbackAndThrowTransactionError(dbreezeTransaction, err, "PUT_ERROR");
                }
            }
        }

        /// <summary>
        /// Used to handle reorg (if required) and revert status from <see cref="CrossChainTransferStatus.SeenInBlock"/> to
        /// <see cref="CrossChainTransferStatus.FullySigned"/>. Also returns a flag to indicate whether we are behind the current tip.
        /// </summary>
        private void RewindIfRequiredLocked()
        {
            if (this.TipHashAndHeight == null)
            {
                this.logger.LogTrace("(-)[CCTS_TIP_NOT_SET]");
                return;
            }

            HashHeightPair tipToChase = this.federationWalletManager.LastBlockSyncedHashHeight();

            // Indicates that the CCTS is synchronized with the Federation Wallet.
            if (this.TipHashAndHeight.HashBlock == tipToChase.Hash)
            {
                this.logger.LogTrace("(-)[SYNCHRONIZED]");
                return;
            }

            // If the Federation Wallet's tip is not on chain, rewind.
            if (this.chainIndexer.GetHeader(tipToChase.Hash) == null)
            {
                var blocks = this.federationWalletManager.GetWallet().BlockLocator.ToList();
                ChainedHeader fork = this.chainIndexer.FindFork(new BlockLocator { Blocks = blocks });

                this.federationWalletManager.RemoveBlocks(fork);

                // Re-set the tip to chase to the federation wallet's new tip.
                tipToChase = this.federationWalletManager.LastBlockSyncedHashHeight();
            }

            // If the CCTS's tip is higher than the federation wallet's tip 
            // OR
            // the CCTS's tip is not on chain, then rewind.
            if (this.TipHashAndHeight.Height > tipToChase.Height || this.chainIndexer.GetHeader(this.TipHashAndHeight.HashBlock)?.Height != this.TipHashAndHeight.Height)
            {
                // We are ahead of the current chain or on the wrong chain.
                ChainedHeader fork = this.chainIndexer.FindFork(this.TipHashAndHeight.GetLocator()) ?? this.chainIndexer.GetHeader(0);

                // Must not exceed wallet height otherwise transaction validations may fail.
                while (fork.Height > tipToChase.Height)
                    fork = fork.Previous;

                using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
                {
                    dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);
                    dbreezeTransaction.ValuesLazyLoadingIsOn = false;

                    ChainedHeader prevTip = this.TipHashAndHeight;
                    int prevDepositHeight = this.NextMatureDepositHeight;

                    try
                    {
                        StatusChangeTracker tracker = this.OnDeleteBlocks(dbreezeTransaction, fork.Height);

                        int newDepositHeight = Math.Min(prevDepositHeight, tracker
                            .Select(kv => kv.Key)
                            .Where(transfer => transfer.Status == CrossChainTransferStatus.Suspended)
                            .Min(transfer => transfer.DepositHeight) ?? int.MaxValue);

                        this.SaveNextMatureHeight(dbreezeTransaction, newDepositHeight);
                        this.SaveTipHashAndHeight(dbreezeTransaction, fork);

                        dbreezeTransaction.Commit();

                        // Remove any remnants of suspended transactions from the wallet.
                        bool walletUpdated = false;
                        foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
                            if (kv.Key.Status == CrossChainTransferStatus.Suspended)
                                walletUpdated |= this.federationWalletManager.RemoveWithdrawalTransactions(kv.Key.DepositTransactionId);

                        if (walletUpdated)
                            this.federationWalletManager.SaveWallet();

                        this.UndoLookups(tracker);
                    }
                    catch (Exception err)
                    {
                        // Restore expected store state in case the calling code retries / continues using the store.
                        this.TipHashAndHeight = prevTip;
                        this.NextMatureDepositHeight = prevDepositHeight;
                        this.RollbackAndThrowTransactionError(dbreezeTransaction, err, "REWIND_ERROR");
                    }
                }

                this.ValidateCrossChainTransfers();
            }
        }

        /// <summary>
        /// Attempts to synchronizes the store with the chain.
        /// <para>
        /// If the synchronization did not happen due to the federation wallet tip not being on chain,
        /// we exit and the caller needs to stop further execution.
        /// </para>
        /// </summary>
        /// <returns>Returns <c>true</c> if the store is in sync or <c>false</c> otherwise.</returns>
        private bool Synchronize()
        {
            lock (this.lockObj)
            {
                if (this.TipHashAndHeight == null)
                {
                    this.logger.LogError("Synchronization failed as the store's tip is null.");
                    this.logger.LogTrace("(-)[CCTS_TIP_NOT_SET]:false");
                    return false;
                }

                HashHeightPair federationWalletTip = this.federationWalletManager.LastBlockSyncedHashHeight();

                // Check if the federation wallet's tip is on chain, if not exit.
                if (this.chainIndexer.GetHeader(federationWalletTip.Hash) == null)
                {
                    this.logger.LogDebug("Synchronization failed as the federation wallet tip is not on chain; {0}='{1}', {2}='{3}'", nameof(this.chainIndexer.Tip), this.chainIndexer.Tip, nameof(federationWalletTip), federationWalletTip);
                    this.logger.LogTrace("(-)[FED_WALLET_TIP_NOT_ONCHAIN]:false");
                    return false;
                }

                // If the federation wallet tip matches the store's tip, exit.
                if (federationWalletTip.Hash == this.TipHashAndHeight.HashBlock)
                {
                    this.logger.LogTrace("(-)[SYNCHRONIZED]:true");
                    return true;
                }

                while (!this.cancellation.IsCancellationRequested)
                {
                    if (this.HasSuspended())
                    {
                        try
                        {
                            ICrossChainTransfer[] transfers = this.Get(this.depositsIdsByStatus[CrossChainTransferStatus.Suspended].ToArray());
                            this.NextMatureDepositHeight = transfers.Min(t => t.DepositHeight) ?? this.NextMatureDepositHeight;
                        }
                        catch (Exception ex)
                        {
                            this.logger.LogError($"An error occurred whilst synchronizing the store: {ex}.");
                            throw ex;
                        }
                    }

                    this.RewindIfRequiredLocked();

                    try
                    {
                        if (this.SynchronizeBatch())
                        {
                            using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
                            {
                                dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);

                                this.SaveTipHashAndHeight(dbreezeTransaction, this.TipHashAndHeight);

                                dbreezeTransaction.Commit();
                            }

                            // As the CCTS syncs from the federation wallet, it needs to be
                            // responsible for cleaning transactions past max reorg.
                            // Doing this from the federation wallet manager could mean transactions
                            // are cleaned before they are processed by the CCTS (which means they will
                            // be wrongly added back).
                            if (this.federationWalletManager.IsSyncedWithChain() && this.federationWalletManager.CleanTransactionsPastMaxReorg(this.TipHashAndHeight.Height))
                                this.federationWalletManager.SaveWallet();

                            return true;
                        }
                    }
                    catch (FederationWalletTipNotOnChainException)
                    {
                        return false;
                    }

                }

                return false;
            }
        }

        /// <summary>Synchronize with a batch of blocks.</summary>
        /// <returns>Returns <c>true</c> if we match the chain tip and <c>false</c> if we are behind the tip.</returns>
        private bool SynchronizeBatch()
        {
            // Get a batch of blocks.
            int batchSize = 0;

            HashHeightPair federationWalletTip = this.federationWalletManager.LastBlockSyncedHashHeight();

            if (this.chainIndexer.GetHeader(federationWalletTip.Hash) == null)
            {
                // If the federation tip is found to be not on chain, we need to throw an
                // exception to ensure that we exit the synchronization process.
                this.logger.LogTrace("(-)[FEDERATION_WALLET_TIP_NOT_ON CHAIN]:{0}='{1}', {2}='{3}'", nameof(this.chainIndexer.Tip), this.chainIndexer.Tip, nameof(federationWalletTip), federationWalletTip);
                throw new FederationWalletTipNotOnChainException();
            }

            var chainedHeadersSnapshot = new Dictionary<uint256, ChainedHeader>();

            foreach (ChainedHeader header in this.chainIndexer.EnumerateToTip(this.TipHashAndHeight.HashBlock).Skip(1))
            {
                if (this.chainIndexer.GetHeader(header.HashBlock) == null)
                    break;

                if (header.Height > federationWalletTip.Height)
                    break;

                chainedHeadersSnapshot.Add(header.HashBlock, header);

                if (++batchSize >= SynchronizationBatchSize)
                    break;
            }

            List<Block> blocks = this.blockRepository.GetBlocks(chainedHeadersSnapshot.Select(c => c.Key).ToList());
            int availableBlocks = blocks.FindIndex(b => (b == null));
            if (availableBlocks < 0)
                availableBlocks = blocks.Count;

            if (availableBlocks > 0)
            {
                Block lastBlock = blocks[availableBlocks - 1];
                this.Put(blocks.GetRange(0, availableBlocks), chainedHeadersSnapshot);
                this.logger.LogInformation("Synchronized {0} blocks with cross-chain store to advance tip to block {1}", availableBlocks, this.TipHashAndHeight?.Height);
            }

            bool done = availableBlocks < SynchronizationBatchSize;

            return done;
        }

        /// <summary>Loads the tip and hash height.</summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <returns>The hash and height pair.</returns>
        private ChainedHeader LoadTipHashAndHeight(DBreeze.Transactions.Transaction dbreezeTransaction)
        {
            var blockLocator = new BlockLocator();
            try
            {
                Row<byte[], byte[]> row = dbreezeTransaction.Select<byte[], byte[]>(commonTableName, RepositoryTipKey);
                Guard.Assert(row.Exists);
                blockLocator.FromBytes(row.Value);
            }
            catch (Exception)
            {
                blockLocator.Blocks = new List<uint256> { this.network.GenesisHash };
            }

            this.TipHashAndHeight = this.chainIndexer.GetHeader(blockLocator.Blocks[0]) ?? this.chainIndexer.FindFork(blockLocator);
            return this.TipHashAndHeight;
        }

        /// <summary>Saves the tip and hash height.</summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <param name="newTip">The new tip to persist.</param>
        private void SaveTipHashAndHeight(DBreeze.Transactions.Transaction dbreezeTransaction, ChainedHeader newTip)
        {
            BlockLocator locator = this.chainIndexer.Tip.GetLocator();
            this.TipHashAndHeight = newTip;
            dbreezeTransaction.Insert<byte[], byte[]>(commonTableName, RepositoryTipKey, locator.ToBytes());
        }

        /// <summary>Loads the counter-chain next mature block height.</summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <returns>The hash and height pair.</returns>
        private int LoadNextMatureHeight(DBreeze.Transactions.Transaction dbreezeTransaction)
        {
            Row<byte[], int> row = dbreezeTransaction.Select<byte[], int>(commonTableName, NextMatureTipKey);

            if (row.Exists)
                this.NextMatureDepositHeight = row.Value;

            // We only want to sync deposits from a certain block number if the main chain is very long.
            this.NextMatureDepositHeight = Math.Max(this.settings.CounterChainDepositStartBlock, this.NextMatureDepositHeight);

            return this.NextMatureDepositHeight;
        }

        /// <summary>Saves the counter-chain next mature block height.</summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <param name="newTip">The next mature block height on the counter-chain.</param>
        private void SaveNextMatureHeight(DBreeze.Transactions.Transaction dbreezeTransaction, int newTip)
        {
            this.NextMatureDepositHeight = newTip;
            dbreezeTransaction.Insert<byte[], int>(commonTableName, NextMatureTipKey, this.NextMatureDepositHeight);
        }

        /// <inheritdoc />
        public Task<ICrossChainTransfer[]> GetAsync(uint256[] depositIds, bool validate = true)
        {
            return Task.Run(() =>
            {
                lock (this.lockObj)
                {
                    return this.federationWalletManager.Synchronous(() =>
                    {
                        if (!this.Synchronize())
                            return null;

                        ICrossChainTransfer[] transfers = this.Get(depositIds);

                        if (validate)
                            transfers = this.ValidateCrossChainTransfers(transfers);

                        return transfers;
                    });
                }
            });
        }

        private ICrossChainTransfer[] Get(uint256[] depositId)
        {
            using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
            {
                dbreezeTransaction.ValuesLazyLoadingIsOn = false;

                return this.Get(dbreezeTransaction, depositId);
            }
        }

        private CrossChainTransfer[] Get(DBreeze.Transactions.Transaction transaction, uint256[] depositId)
        {
            Guard.NotNull(depositId, nameof(depositId));

            // To boost performance we will access the deposits sorted by deposit id.
            var depositDict = new Dictionary<uint256, int>();
            for (int i = 0; i < depositId.Length; i++)
                depositDict[depositId[i]] = i;

            var byteListComparer = new ByteListComparer();
            List<KeyValuePair<uint256, int>> depositList = depositDict.ToList();
            depositList.Sort((pair1, pair2) => byteListComparer.Compare(pair1.Key.ToBytes(), pair2.Key.ToBytes()));

            var res = new CrossChainTransfer[depositId.Length];

            foreach (KeyValuePair<uint256, int> kv in depositList)
            {
                Row<byte[], byte[]> transferRow = transaction.Select<byte[], byte[]>(transferTableName, kv.Key.ToBytes());

                if (transferRow.Exists)
                {
                    var crossChainTransfer = new CrossChainTransfer();
                    crossChainTransfer.FromBytes(transferRow.Value, this.network.Consensus.ConsensusFactory);
                    res[kv.Value] = crossChainTransfer;
                }
            }

            return res;
        }

        private OutPoint EarliestOutput(Transaction transaction)
        {
            var comparer = Comparer<OutPoint>.Create((x, y) => ((FederationWalletManager)this.federationWalletManager).CompareOutpoints(x, y));
            return transaction.Inputs.Select(i => i.PrevOut).OrderBy(t => t, comparer).FirstOrDefault();
        }

        /// <inheritdoc />
        public ICrossChainTransfer[] GetTransfersByStatus(CrossChainTransferStatus[] statuses, bool sort = false, bool validate = true)
        {
            lock (this.lockObj)
            {
                return this.federationWalletManager.Synchronous(() =>
                {
                    if (!this.Synchronize())
                        return new ICrossChainTransfer[] { };

                    var depositIds = new HashSet<uint256>();
                    foreach (CrossChainTransferStatus status in statuses)
                        depositIds.UnionWith(this.depositsIdsByStatus[status]);

                    uint256[] partialTransferHashes = depositIds.ToArray();
                    ICrossChainTransfer[] partialTransfers = this.Get(partialTransferHashes).Where(t => t != null).ToArray();

                    if (validate)
                    {
                        this.ValidateCrossChainTransfers(partialTransfers);
                        partialTransfers = partialTransfers.Where(t => statuses.Contains(t.Status)).ToArray();
                    }

                    if (!sort)
                    {
                        return partialTransfers;
                    }

                    // When sorting, Suspended transactions will have null PartialTransactions. Always put them last in the order they're in.
                    IEnumerable<ICrossChainTransfer> unsortable = partialTransfers.Where(x => x.Status == CrossChainTransferStatus.Suspended || x.Status == CrossChainTransferStatus.Rejected);
                    IEnumerable<ICrossChainTransfer> sortable = partialTransfers.Where(x => x.Status != CrossChainTransferStatus.Suspended && x.Status != CrossChainTransferStatus.Rejected);

                    return sortable.OrderBy(t => this.EarliestOutput(t.PartialTransaction), Comparer<OutPoint>.Create((x, y) =>
                            ((FederationWalletManager)this.federationWalletManager).CompareOutpoints(x, y)))
                        .Concat(unsortable)
                        .ToArray();
                });
            }
        }

        /// <inheritdoc />
        public int GetTransferCountByStatus(CrossChainTransferStatus status)
        {
            return this.depositsIdsByStatus[status].Count;
        }

        /// <summary>Persist the cross-chain transfer information into the database.</summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <param name="crossChainTransfer">Cross-chain transfer information to be inserted.</param>
        private void PutTransfer(DBreeze.Transactions.Transaction dbreezeTransaction, ICrossChainTransfer crossChainTransfer)
        {
            Guard.NotNull(crossChainTransfer, nameof(crossChainTransfer));

            byte[] crossChainTransferBytes = this.dBreezeSerializer.Serialize(crossChainTransfer);

            dbreezeTransaction.Insert<byte[], byte[]>(transferTableName, crossChainTransfer.DepositTransactionId.ToBytes(), crossChainTransferBytes);
        }

        /// <summary>Persist multiple cross-chain transfer information into the database.</summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <param name="crossChainTransfers">Cross-chain transfers to be inserted.</param>
        private void PutTransfers(DBreeze.Transactions.Transaction dbreezeTransaction, ICrossChainTransfer[] crossChainTransfers)
        {
            Guard.NotNull(crossChainTransfers, nameof(crossChainTransfers));

            // Optimal ordering for DB consumption.
            var byteListComparer = new ByteListComparer();
            List<ICrossChainTransfer> orderedTransfers = crossChainTransfers.ToList();
            orderedTransfers.Sort((pair1, pair2) => byteListComparer.Compare(pair1.DepositTransactionId.ToBytes(), pair2.DepositTransactionId.ToBytes()));

            // Write each transfer in order.
            foreach (ICrossChainTransfer transfer in orderedTransfers)
            {
                byte[] transferBytes = this.dBreezeSerializer.Serialize(transfer);
                dbreezeTransaction.Insert<byte[], byte[]>(transferTableName, transfer.DepositTransactionId.ToBytes(), transferBytes);
            }
        }

        /// <summary>Deletes the cross-chain transfer information from the database</summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <param name="crossChainTransfer">Cross-chain transfer information to be deleted.</param>
        private void DeleteTransfer(DBreeze.Transactions.Transaction dbreezeTransaction, ICrossChainTransfer crossChainTransfer)
        {
            Guard.NotNull(crossChainTransfer, nameof(crossChainTransfer));

            dbreezeTransaction.RemoveKey<byte[]>(transferTableName, crossChainTransfer.DepositTransactionId.ToBytes());
        }

        /// <summary>
        /// Forgets transfer information for the blocks being removed and returns information for updating the transient lookups.
        /// </summary>
        /// <param name="dbreezeTransaction">The DBreeze transaction context to use.</param>
        /// <param name="lastBlockHeight">The last block to retain.</param>
        /// <returns>A tracker with all the cross chain transfers that were affected.</returns>
        private StatusChangeTracker OnDeleteBlocks(DBreeze.Transactions.Transaction dbreezeTransaction, int lastBlockHeight)
        {
            // Gather all the deposit ids that may have had transactions in the blocks being deleted.
            var depositIds = new HashSet<uint256>();
            uint256[] blocksToRemove = this.blockHeightsByBlockHash.Where(a => a.Value > lastBlockHeight).Select(a => a.Key).ToArray();

            foreach (HashSet<uint256> deposits in blocksToRemove.Select(a => this.depositIdsByBlockHash[a]))
            {
                depositIds.UnionWith(deposits);
            }

            // Find the transfers related to these deposit ids in the database.
            var tracker = new StatusChangeTracker();
            CrossChainTransfer[] crossChainTransfers = this.Get(dbreezeTransaction, depositIds.ToArray());

            foreach (CrossChainTransfer transfer in crossChainTransfers)
            {
                // Transfers that only exist in the DB due to having been seen in a block should be removed completely.
                if (transfer.DepositHeight == null)
                {
                    // Trigger deletion from the status lookup.
                    tracker.SetTransferStatus(transfer);

                    // Delete the transfer completely.
                    this.DeleteTransfer(dbreezeTransaction, transfer);
                }
                else
                {
                    // Transaction is no longer seen and the FederationWalletManager is going to remove the transaction anyhow
                    // So don't prolong - just set to Suspended now.
                    this.logger.LogDebug("Setting DepositId {0} to Suspended", transfer.DepositTransactionId);
                    tracker.SetTransferStatus(transfer, CrossChainTransferStatus.Suspended);

                    // Write the transfer status to the database.
                    this.PutTransfer(dbreezeTransaction, transfer);
                }
            }

            return tracker;
        }

        /// <summary>Updates the status lookup based on a transfer and its previous status.</summary>
        /// <param name="transfer">The cross-chain transfer that was update.</param>
        /// <param name="oldStatus">The old status.</param>
        private void TransferStatusUpdated(ICrossChainTransfer transfer, CrossChainTransferStatus? oldStatus)
        {
            if (oldStatus != null)
            {
                this.depositsIdsByStatus[(CrossChainTransferStatus)oldStatus].Remove(transfer.DepositTransactionId);
            }

            this.depositsIdsByStatus[transfer.Status].Add(transfer.DepositTransactionId);
        }

        /// <summary>Update the transient lookups after changes have been committed to the store.</summary>
        /// <param name="tracker">Information about how to update the lookups.</param>
        private void UpdateLookups(StatusChangeTracker tracker)
        {
            foreach (uint256 hash in tracker.UniqueBlockHashes())
            {
                this.depositIdsByBlockHash[hash] = new HashSet<uint256>();
            }

            foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
            {
                this.TransferStatusUpdated(kv.Key, kv.Value);

                if (kv.Key.BlockHash != null && kv.Key.BlockHeight != null)
                {
                    if (!this.depositIdsByBlockHash[kv.Key.BlockHash].Contains(kv.Key.DepositTransactionId))
                        this.depositIdsByBlockHash[kv.Key.BlockHash].Add(kv.Key.DepositTransactionId);
                    this.blockHeightsByBlockHash[kv.Key.BlockHash] = (int)kv.Key.BlockHeight;
                }
            }
        }

        /// <summary>Undoes the transient lookups after block removals have been committed to the store.</summary>
        /// <param name="tracker">Information about how to undo the lookups.</param>
        private void UndoLookups(StatusChangeTracker tracker)
        {
            foreach (KeyValuePair<ICrossChainTransfer, CrossChainTransferStatus?> kv in tracker)
            {
                if (kv.Value == null)
                {
                    this.depositsIdsByStatus[kv.Key.Status].Remove(kv.Key.DepositTransactionId);
                }

                this.TransferStatusUpdated(kv.Key, kv.Value);
            }

            foreach (uint256 hash in tracker.UniqueBlockHashes())
            {
                this.depositIdsByBlockHash.Remove(hash);
                this.blockHeightsByBlockHash.Remove(hash);
            }
        }

        public bool ValidateTransaction(Transaction transaction, bool checkSignature = false)
        {
            return this.federationWalletManager.ValidateTransaction(transaction, checkSignature).IsValid;
        }

        /// <inheritdoc />
        public List<Transaction> GetCompletedWithdrawalsForTransactions(IEnumerable<Transaction> transactionsToCheck)
        {
            var res = new List<Transaction>();

            lock (this.lockObj)
            {
                HashSet<uint256> inProgress = this.depositsIdsByStatus[CrossChainTransferStatus.Partial].Union(
                    this.depositsIdsByStatus[CrossChainTransferStatus.FullySigned].Union(
                    this.depositsIdsByStatus[CrossChainTransferStatus.Suspended])).ToHashSet();

                foreach (Transaction tx in transactionsToCheck)
                {
                    IWithdrawal withdrawal = this.withdrawalExtractor.ExtractWithdrawalFromTransaction(tx, null, 0);

                    // Transactions containing withdrawals that are not in progress.
                    if (withdrawal != null && !inProgress.Contains(withdrawal.DepositId))
                    {
                        res.Add(tx);
                    }
                }
            }

            return res;
        }

        /// <summary>
        /// Determines if a mempool error would be recoverable by waiting or rebuilding the transaction.
        /// </summary>
        /// <param name="mempoolError">The error to evaluate.</param>
        /// <returns><c>True</c> if its recoverble or <c>false</c> otherwise.</returns>
        public static bool IsMempoolErrorRecoverable(MempoolError mempoolError)
        {
            switch (mempoolError.RejectCode)
            {
                // Can recover from inputs already spent.
                case MempoolErrors.RejectDuplicate:
                    return true;

                // Duplicates should not make the transfer fail.
                case MempoolErrors.RejectAlreadyKnown:
                    return true;

                default:
                    return mempoolError.ConsensusError == null;
            }
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Cross Chain Transfer Store");
            benchLog.AppendLine("Height".PadRight(LoggingConfiguration.ColumnLength) + $": {this.TipHashAndHeight.Height} [{this.TipHashAndHeight.HashBlock}]");
            benchLog.AppendLine("NextDepositHeight".PadRight(LoggingConfiguration.ColumnLength) + $": {this.NextMatureDepositHeight}");
            benchLog.AppendLine("Partial Txs".PadRight(LoggingConfiguration.ColumnLength) + $": {GetTransferCountByStatus(CrossChainTransferStatus.Partial)}");
            benchLog.AppendLine("Suspended Txs".PadRight(LoggingConfiguration.ColumnLength) + $": {GetTransferCountByStatus(CrossChainTransferStatus.Suspended)}");
            benchLog.AppendLine();

            var depositIds = new HashSet<uint256>();
            ICrossChainTransfer[] transfers;

            try
            {
                foreach (CrossChainTransferStatus status in new[] { CrossChainTransferStatus.FullySigned, CrossChainTransferStatus.Partial })
                    depositIds.UnionWith(this.depositsIdsByStatus[status]);

                transfers = this.Get(depositIds.ToArray()).Where(t => t != null).ToArray();

                // When sorting, Suspended transactions will have null PartialTransactions. Always put them last in the order they're in.
                IEnumerable<ICrossChainTransfer> inprogress = transfers.Where(x => x.Status != CrossChainTransferStatus.Suspended && x.Status != CrossChainTransferStatus.Rejected);
                IEnumerable<ICrossChainTransfer> suspended = transfers.Where(x => x.Status == CrossChainTransferStatus.Suspended || x.Status == CrossChainTransferStatus.Rejected);

                IEnumerable<WithdrawalModel> pendingWithdrawals = this.withdrawalHistoryProvider.GetPendingWithdrawals(inprogress.Concat(suspended)).OrderByDescending(p => p.SignatureCount);

                if (pendingWithdrawals.Count() > 0)
                {
                    benchLog.AppendLine("--- Pending Withdrawals ---");
                    foreach (WithdrawalModel withdrawal in pendingWithdrawals.Take(TransfersToDisplay))
                        benchLog.AppendLine(withdrawal.ToString());

                    if (pendingWithdrawals.Count() > TransfersToDisplay)
                        benchLog.AppendLine($"and {pendingWithdrawals.Count() - TransfersToDisplay} more...");

                    benchLog.AppendLine();
                }
            }
            catch (Exception exception)
            {
                benchLog.AppendLine("--- Pending Withdrawals ---");
                benchLog.AppendLine("Failed to retrieve data");
                this.logger.LogError("Exception occurred while getting pending withdrawals: '{0}'.", exception.ToString());
            }
        }

        /// <inheritdoc />
        public List<WithdrawalModel> GetCompletedWithdrawals(int transfersToDisplay)
        {
            HashSet<uint256> depositIds = this.depositsIdsByStatus[CrossChainTransferStatus.SeenInBlock];
            ICrossChainTransfer[] transfers = this.Get(depositIds.ToArray()).Where(t => t != null).ToArray();
            return this.withdrawalHistoryProvider.GetHistory(transfers, transfersToDisplay);
        }

        /// <inheritdoc />
        public int DeleteSuspendedTransfers()
        {
            HashSet<uint256> depositIds = this.depositsIdsByStatus[CrossChainTransferStatus.Suspended];
            ICrossChainTransfer[] transfers = this.Get(depositIds.ToArray()).Where(t => t != null).ToArray();

            using (DBreeze.Transactions.Transaction dbreezeTransaction = this.DBreeze.GetTransaction())
            {
                dbreezeTransaction.SynchronizeTables(transferTableName, commonTableName);
                dbreezeTransaction.ValuesLazyLoadingIsOn = false;

                foreach (ICrossChainTransfer transfer in transfers)
                {
                    this.DeleteTransfer(dbreezeTransaction, transfer);
                    this.depositsIdsByStatus[CrossChainTransferStatus.Suspended].Clear();
                    this.logger.LogDebug($"Suspended transfer with deposit id '{transfer.DepositTransactionId}' deleted.");
                }

                dbreezeTransaction.Commit();
            }

            return transfers.Count();
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.SaveCurrentTipAsync().GetAwaiter().GetResult();
            this.cancellation.Cancel();
            this.DBreeze.Dispose();
        }
    }
}
