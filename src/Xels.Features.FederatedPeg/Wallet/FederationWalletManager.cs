﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Policy;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;
using Xels.Features.FederatedPeg.Controllers;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.TargetChain;
using TracerAttributes;

namespace Xels.Features.FederatedPeg.Wallet
{
    /// <summary>
    /// A class that represents a flat view of the wallets history.
    /// </summary>
    public class FlatHistory
    {
        /// <summary>
        /// The address associated with this UTXO
        /// </summary>
        public MultiSigAddress Address { get; set; }

        /// <summary>
        /// The transaction representing the UTXO.
        /// </summary>
        public TransactionData Transaction { get; set; }
    }

    /// <summary>
    /// Credentials to the federation wallet.
    /// </summary>
    public class WalletSecret
    {
        /// <summary>The federation wallet's password, needed for getting the private key which is used for signing federation transactions.</summary>
        public string WalletPassword { get; set; }
    }

    /// <summary>
    /// A manager providing operations on wallets.
    /// </summary>
    public class FederationWalletManager : LockProtected, IFederationWalletManager
    {
        /// <summary>
        /// If the federation wallet tip is within this amount of blocks from the chain's tip, consider it synced.
        /// </summary>
        public const int FederationWalletTipSyncBuffer = 10;

        /// <summary>Timer for saving wallet files to the file system.</summary>
        private const int WalletSavetimeIntervalInMinutes = 5;

        /// <summary>Keep at least this many transactions in the wallet despite the
        /// max reorg age limit for spent transactions. This is so that it never
        /// looks like the wallet has become empty to the user.</summary>
        private const int MinimumRetainedTransactions = 100;

        /// <summary>The async loop we need to wait upon before we can shut down this manager.</summary>
        private IAsyncLoop asyncLoop;

        /// <summary>Factory for creating background async loop tasks.</summary>
        private readonly IAsyncProvider asyncProvider;

        /// <summary>Gets the wallet.</summary>
        public FederationWallet Wallet { get; set; }

        /// <summary>The type of coin used in this manager.</summary>
        private readonly CoinType coinType;

        /// <summary>Specification of the network the node runs on - regtest/testnet/mainnet.</summary>
        private readonly Network network;

        /// <summary>The chain of headers.</summary>
        private readonly ChainIndexer chainIndexer;

        /// <summary>Global application life cycle control - triggers when application shuts down.</summary>
        private readonly INodeLifetime nodeLifetime;

        /// <summary>The withdrawal extractor used to extract withdrawals from transactions.</summary>
        private readonly IWithdrawalExtractor withdrawalExtractor;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>An object capable of storing <see cref="FederationWallet"/>s to the file system.</summary>
        private readonly FileStorage<FederationWallet> fileStorage;

        /// <summary>Provider of time functions.</summary>
        private readonly IDateTimeProvider dateTimeProvider;

        private readonly IBlockStore blockStore;

        /// <summary>Indicates whether the federation is active.</summary>
        private bool isFederationActive;

        public uint256 WalletTipHash { get; set; }
        public int WalletTipHeight { get; set; }

        public bool ContainsWallets => throw new NotImplementedException();

        /// <summary>
        /// Credentials for the wallet. Initially unpopulated on node startup, has to be provided by the user.
        /// </summary>
        public WalletSecret Secret { get; set; }

        /// <summary>
        /// The name of the watch-only wallet as saved in the file system.
        /// </summary>
        private const string WalletFileName = "multisig_wallet.json";

        /// <summary>
        /// Creates a mapping from (TransactionData.Id, TransactionData.Index) to TransactionData.
        /// </summary>
        private Dictionary<OutPoint, TransactionData> outpointLookup => this.Wallet.MultiSigAddress.Transactions.GetOutpointLookup();

        /// <summary>
        /// Gateway settings picked up from the node config.
        /// </summary>
        private readonly IFederatedPegSettings federatedPegSettings;

        public FederationWalletManager(
            Network network,
            INodeStats nodeStats,
            ChainIndexer chainIndexer,
            DataFolder dataFolder,
            IWalletFeePolicy walletFeePolicy,
            IAsyncProvider asyncProvider,
            INodeLifetime nodeLifetime,
            IDateTimeProvider dateTimeProvider,
            IFederatedPegSettings federatedPegSettings,
            IWithdrawalExtractor withdrawalExtractor,
            IBlockStore blockStore) : base()
        {
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(chainIndexer, nameof(chainIndexer));
            Guard.NotNull(dataFolder, nameof(dataFolder));
            Guard.NotNull(walletFeePolicy, nameof(walletFeePolicy));
            Guard.NotNull(asyncProvider, nameof(asyncProvider));
            Guard.NotNull(nodeLifetime, nameof(nodeLifetime));
            Guard.NotNull(federatedPegSettings, nameof(federatedPegSettings));
            Guard.NotNull(withdrawalExtractor, nameof(withdrawalExtractor));
            Guard.NotNull(blockStore, nameof(blockStore));

            this.logger = LogManager.GetCurrentClassLogger();

            this.network = network;

            this.coinType = (CoinType)network.Consensus.CoinType;
            this.chainIndexer = chainIndexer;
            this.asyncProvider = asyncProvider;
            this.nodeLifetime = nodeLifetime;
            this.fileStorage = new FileStorage<FederationWallet>(dataFolder.WalletPath);
            this.dateTimeProvider = dateTimeProvider;
            this.federatedPegSettings = federatedPegSettings;
            this.withdrawalExtractor = withdrawalExtractor;
            this.isFederationActive = false;
            this.blockStore = blockStore;

            nodeStats.RegisterStats(this.AddComponentStats, StatsType.Component, this.GetType().Name);
            nodeStats.RegisterStats(this.AddInlineStats, StatsType.Inline, this.GetType().Name, 800);
        }

        /// <summary>
        /// The purpose of this method is to retrieve <see cref="Transaction"/> objects for each <see cref="SpendingDetails.TransactionId"/>.
        /// If any transaction can't be resolved the wallet is rewound to remove the corrupt <see cref="TransactionData"/> record containing
        /// the <see cref="SpendingDetails"/>.
        /// </summary>
        /// <param name="transactions">The <see cref="TransactionData"/> records for which to retrieve the transactions.</param>
        /// <returns>Retrieved <see cref="Transaction"/> objects for each <see cref="SpendingDetails.TransactionId"/></returns>
        private Dictionary<TransactionData, Transaction> GetSpendingTransactions(IEnumerable<TransactionData> transactions)
        {
            var res = new Dictionary<TransactionData, Transaction>();

            // Record all the transaction data spent by a given spending transaction located in a given block.
            var spendTxsByBlockId = new Dictionary<uint256, Dictionary<uint256, List<TransactionData>>>();
            foreach (TransactionData transactionData in transactions)
            {
                SpendingDetails spendingDetail = transactionData.SpendingDetails;

                // There is no transaction or its already resolved.
                if (spendingDetail?.TransactionId == null || spendingDetail.Transaction != null)
                {
                    res.Add(transactionData, spendingDetail?.Transaction);
                    continue;
                }

                // Some SpendingDetail.BlockHash values may bet set to (uint256)0, so fix that too.
                if (spendingDetail.BlockHash == 0)
                {
                    if (spendingDetail.BlockHeight == null || (spendingDetail.BlockHeight > this.chainIndexer.Tip.Height))
                        continue;

                    spendingDetail.BlockHash = this.chainIndexer[(int)spendingDetail.BlockHeight].HashBlock;
                }

                if (!spendTxsByBlockId.TryGetValue(spendingDetail.BlockHash, out Dictionary<uint256, List<TransactionData>> spentOutputsBySpendTxId))
                {
                    spentOutputsBySpendTxId = new Dictionary<uint256, List<TransactionData>>();
                    spendTxsByBlockId[spendingDetail.BlockHash] = spentOutputsBySpendTxId;
                }

                if (!spentOutputsBySpendTxId.TryGetValue(spendingDetail.TransactionId, out List<TransactionData> spentOutputs))
                {
                    spentOutputs = new List<TransactionData>();
                    spentOutputsBySpendTxId[spendingDetail.TransactionId] = spentOutputs;
                }

                spentOutputs.Add(transactionData);
            }

            // Will keep track of the height of spending details we're unable to fix.
            int firstMissingTransactionHeight = this.LastBlockSyncedHashHeight().Height + 1;

            // Find the spending transactions.
            foreach ((uint256 blockId, Dictionary<uint256, List<TransactionData>> spentOutputsBySpendTxId) in spendTxsByBlockId)
            {
                Block block = this.blockStore.GetBlock(blockId);
                Dictionary<uint256, Transaction> txIndex = block?.Transactions.ToDictionary(t => t.GetHash(), t => t);

                foreach ((uint256 spendTxId, List<TransactionData> spentOutputs) in spentOutputsBySpendTxId)
                {
                    if (txIndex != null && txIndex.TryGetValue(spendTxId, out Transaction spendTransaction))
                    {
                        foreach (TransactionData transactionData in spentOutputs)
                            res[transactionData] = spendTransaction;
                    }
                    else
                    {
                        // The spending transaction could not be found in the consensus chain.
                        // Set the firstMissingTransactionHeight to the block of the spending transaction.
                        SpendingDetails spendingDetails = spentOutputs.Select(td => td.SpendingDetails).Where(s => s.BlockHeight != null).FirstOrDefault();

                        Guard.Assert(spendingDetails != null);

                        if (spendingDetails.BlockHeight < firstMissingTransactionHeight)
                            firstMissingTransactionHeight = (int)spendingDetails.BlockHeight;
                    }
                }
            }

            // If there are unresolvable spending details then re-sync from that point onwards.
            if (firstMissingTransactionHeight <= this.LastBlockSyncedHashHeight().Height)
            {
                ChainedHeader fork = this.chainIndexer.GetHeader(Math.Min(firstMissingTransactionHeight - 1, this.chainIndexer.Height));

                this.RemoveBlocks(fork);
            }

            return res;
        }

        public void Start()
        {
            lock (this.lockObject)
            {
                // Find the wallet and load it in memory.
                if (this.fileStorage.Exists(WalletFileName))
                {
                    this.Wallet = this.fileStorage.LoadByFileName(WalletFileName);
                    this.RemoveUnconfirmedTransactionData();
                }
                else
                {
                    // Create the multisig wallet file if it doesn't exist
                    this.Wallet = this.GenerateWallet();
                    this.SaveWallet();
                }

                // find the last chain block received by the wallet manager.
                HashHeightPair hashHeightPair = this.LastBlockSyncedHashHeight();
                this.WalletTipHash = hashHeightPair.Hash;
                this.WalletTipHeight = hashHeightPair.Height;

                // save the wallets file every 5 minutes to help against crashes.
                this.asyncLoop = this.asyncProvider.CreateAndRunAsyncLoop("wallet persist job", token =>
                {
                    this.SaveWallet();
                    this.logger.LogInformation("Wallets saved to file at {0}.", this.dateTimeProvider.GetUtcNow());

                    return Task.CompletedTask;
                },
                this.nodeLifetime.ApplicationStopping,
                repeatEvery: TimeSpan.FromMinutes(WalletSavetimeIntervalInMinutes),
                startAfter: TimeSpan.FromMinutes(WalletSavetimeIntervalInMinutes));
            }
        }

        /// <inheritdoc />
        public void Stop()
        {
            lock (this.lockObject)
            {
                this.asyncLoop?.Dispose();
                this.SaveWallet();
            }
        }

        /// <inheritdoc />
        public HashHeightPair LastBlockSyncedHashHeight()
        {
            lock (this.lockObject)
            {
                if (!this.IsWalletActive())
                {
                    this.logger.LogTrace("(-)[NO_WALLET]:{0}='{1}'", nameof(this.chainIndexer.Tip), this.chainIndexer.Tip);
                    return new HashHeightPair(this.chainIndexer.Tip);
                }

                if (this.Wallet.LastBlockSyncedHash == null && this.Wallet.LastBlockSyncedHeight == null)
                {
                    this.logger.LogTrace("(-)[WALLET_SYNC_BLOCK_NOT_SET]:{0}='{1}'", nameof(this.chainIndexer.Tip), this.chainIndexer.Tip);
                    return new HashHeightPair(this.chainIndexer.Tip);
                }

                return new HashHeightPair(this.Wallet.LastBlockSyncedHash, this.Wallet.LastBlockSyncedHeight.Value);
            }
        }

        /// <inheritdoc />
        public IEnumerable<UnspentOutputReference> GetSpendableTransactionsInWallet(int confirmations = 0)
        {
            lock (this.lockObject)
            {
                if (this.Wallet == null)
                {
                    return Enumerable.Empty<UnspentOutputReference>();
                }

                return this.GetSpendableTransactions(this.chainIndexer.Tip.Height, confirmations).ToArray();
            }
        }

        /// <inheritdoc />
        public void RemoveBlocks(ChainedHeader fork)
        {
            Guard.NotNull(fork, nameof(fork));

            lock (this.lockObject)
            {
                this.logger.LogDebug("Removing blocks back to height {0} from {1}.", fork.Height, this.LastBlockSyncedHashHeight().Height);

                // Remove all the UTXO that have been reorged.
                IEnumerable<TransactionData> makeUnspendable = this.Wallet.MultiSigAddress.Transactions.Where(w => w.BlockHeight > fork.Height).ToList();
                foreach (TransactionData transactionData in makeUnspendable)
                {
                    this.logger.LogDebug("Removing reorged tx '{0}'.", transactionData.Id);
                    this.Wallet.MultiSigAddress.Transactions.Remove(transactionData);

                    if (transactionData.SpendingDetails != null)
                        this.RemoveAssociatedUnconfirmedSpentByTransaction(transactionData.SpendingDetails.TransactionId);
                }

                // Bring back all the UTXO that are now spendable after the reorg.
                IEnumerable<TransactionData> makeSpendable = this.Wallet.MultiSigAddress.Transactions.Where(w => (w.SpendingDetails != null) && (w.SpendingDetails.BlockHeight > fork.Height));
                foreach (TransactionData transactionData in makeSpendable)
                {
                    this.logger.LogDebug("Unspend transaction '{0}'.", transactionData.Id);
                    transactionData.SpendingDetails = null;
                }

                this.UpdateLastBlockSyncedHeight(fork);
            }
        }

        /// <summary>
        /// Determines if the wallet is active.
        /// The wallet will only become active after <see cref="FederationWallet.LastBlockSyncedHeight"/>.
        /// </summary>
        /// <param name="height">The height at which to test if the wallet should be active. Defaults to the chain indexer height.</param>
        /// <returns><c>true</c> if the wallet is active.</returns>
        private bool IsWalletActive(int? height = null)
        {
            if (this.Wallet == null)
                return false;

            if (this.Wallet.LastBlockSyncedHash != null)
                return true;

            if (this.Wallet.LastBlockSyncedHeight >= (height ?? this.chainIndexer.Height))
                return false;

            return true;
        }

        /// <inheritdoc />
        public void ProcessBlock(Block block, ChainedHeader chainedHeader)
        {
            Guard.NotNull(block, nameof(block));
            Guard.NotNull(chainedHeader, nameof(chainedHeader));

            lock (this.lockObject)
            {
                // If there is no wallet yet, update the wallet tip hash and do nothing else.
                if (!this.IsWalletActive(chainedHeader.Height))
                {
                    this.WalletTipHash = chainedHeader.HashBlock;
                    this.logger.LogTrace("(-)[NO_WALLET]");
                    return;
                }

                // Is this the next block.
                if (chainedHeader.Header.HashPrevBlock != this.WalletTipHash)
                {
                    this.logger.LogDebug("New block's previous hash '{0}' does not match current wallet's tip hash '{1}'.", chainedHeader.Header.HashPrevBlock, this.WalletTipHash);

                    // The block coming in to the wallet should never be ahead of the wallet.
                    // If the block is behind, let it pass.
                    if (chainedHeader.Height > this.WalletTipHeight)
                    {
                        this.logger.LogTrace("(-)[BLOCK_TOO_FAR]");
                        throw new WalletException("block too far in the future has arrived to the wallet");
                    }
                }

                bool walletUpdated = false;
                foreach (Transaction transaction in block.Transactions.Where(t => !(t.IsCoinBase && t.TotalOut == Money.Zero)))
                {
                    bool trxFound = this.ProcessTransaction(transaction, chainedHeader.Height, chainedHeader.HashBlock, block);
                    if (trxFound)
                        walletUpdated = true;
                }

                // Update the wallets with the last processed block height.
                // It's important that updating the height happens after the block processing is complete,
                // as if the node is stopped, on re-opening it will start updating from the previous height.
                this.UpdateLastBlockSyncedHeight(chainedHeader);

                if (walletUpdated)
                    this.SaveWallet();
            }
        }

        /// <inheritdoc />
        public bool ProcessTransaction(Transaction transaction, int? blockHeight = null, uint256 blockHash = null, Block block = null)
        {
            Guard.NotNull(transaction, nameof(transaction));
            Guard.Assert(blockHash == (blockHash ?? block?.GetHash()));

            lock (this.lockObject)
            {
                if (!this.IsWalletActive())
                {
                    this.logger.LogTrace("(-)");
                    return false;
                }

                bool foundReceivingTrx = false, foundSendingTrx = false;

                // Check if we're trying to spend a utxo twice
                foreach (TxIn input in transaction.Inputs)
                {
                    if (!this.outpointLookup.TryGetValue(input.PrevOut, out TransactionData tTx))
                    {
                        continue;
                    }

                    // If we're trying to spend an input that is already spent, and it's not coming in a new block, don't reserve the transaction.
                    // This would be the case when blocks are synced in between CrossChainTransferStore calling
                    // FederationWalletTransactionHandler.BuildTransaction and FederationWalletManager.ProcessTransaction.
                    if (blockHeight == null && tTx.SpendingDetails?.BlockHeight != null)
                    {
                        return false;
                    }
                }

                // Extract the withdrawal from the transaction (if any).
                IWithdrawal withdrawal = this.withdrawalExtractor.ExtractWithdrawalFromTransaction(transaction, blockHash, blockHeight ?? 0);
                if (withdrawal != null)
                {
                    // Exit if already present and included in a block.
                    List<(Transaction transaction, IWithdrawal withdrawal)> walletData = this.FindWithdrawalTransactions(withdrawal.DepositId);
                    if ((walletData.Count == 1) && (walletData[0].withdrawal.BlockNumber != 0))
                    {
                        this.logger.LogDebug("Deposit '{0}' already included in block.", withdrawal.DepositId);
                        return false;
                    }

                    // Remove this to prevent duplicates if the transaction hash has changed.
                    if (walletData.Count != 0)
                    {
                        this.logger.LogDebug("Removing duplicates for '{0}'.", withdrawal.DepositId);
                        this.RemoveWithdrawalTransactions(withdrawal.DepositId);
                    }
                }

                // Check the outputs.
                foreach (TxOut utxo in transaction.Outputs)
                {
                    // Check if the outputs contain one of our addresses.
                    if (this.Wallet.MultiSigAddress.ScriptPubKey == utxo.ScriptPubKey)
                    {
                        this.AddTransactionToWallet(transaction, utxo, blockHeight, blockHash, block);
                        foundReceivingTrx = true;
                    }
                }

                // Check the inputs - include those that have a reference to a transaction containing one of our scripts and the same index.
                foreach (TxIn input in transaction.Inputs)
                {
                    if (!this.outpointLookup.TryGetValue(input.PrevOut, out TransactionData tTx))
                    {
                        continue;
                    }

                    // Get the details of the outputs paid out.
                    IEnumerable<TxOut> paidOutTo = transaction.Outputs.Where(o =>
                    {
                        // If script is empty ignore it.
                        if (o.IsEmpty)
                            return false;

                        // Check if the destination script is one of the wallet's.
                        // TODO fix this
                        bool found = this.Wallet.MultiSigAddress.ScriptPubKey == o.ScriptPubKey;

                        // Include the keys not included in our wallets (external payees).
                        if (!found)
                            return true;

                        // Include the keys that are in the wallet but that are for receiving
                        // addresses (which would mean the user paid itself).
                        // We also exclude the keys involved in a staking transaction.
                        //return !addr.IsChangeAddress() && !transaction.IsCoinStake;
                        return true;
                    });

                    this.AddSpendingTransactionToWalletLocked(transaction, paidOutTo, tTx.Id, tTx.Index, blockHeight, blockHash, block, withdrawal);
                    foundSendingTrx = true;
                }

                return foundSendingTrx || foundReceivingTrx;
            }
        }

        /// <inheritdoc />
        public bool CleanTransactionsPastMaxReorg(int crossChainTransferStoreTip)
        {
            bool walletUpdated = false;

            if (this.network.Consensus.MaxReorgLength == 0 || this.Wallet.MultiSigAddress.Transactions.Count <= MinimumRetainedTransactions)
            {
                this.logger.LogDebug("Skipping clean up of federation wallet. {0}={1};{2}={3}", nameof(this.network.Consensus.MaxReorgLength), this.network.Consensus.MaxReorgLength, nameof(this.Wallet.MultiSigAddress.Transactions), this.Wallet.MultiSigAddress.Transactions.Count);
                return walletUpdated;
            }

            int heightToCleanFrom = crossChainTransferStoreTip - (int)this.network.Consensus.MaxReorgLength;
            var transactionsPastMaxReorg = new List<TransactionData>();

            // Only want to remove transactions that are spent, and the spend must have passed max reorg too
            foreach ((_, List<TransactionData> txList) in this.Wallet.MultiSigAddress.Transactions.SpentTransactionsBeforeHeight(heightToCleanFrom))
            {
                foreach (TransactionData transactionData in txList)
                {
                    transactionsPastMaxReorg.Add(transactionData);
                }
            }

            foreach (TransactionData transactionData in transactionsPastMaxReorg)
            {
                this.Wallet.MultiSigAddress.Transactions.Remove(transactionData);
                walletUpdated = true;

                if (this.Wallet.MultiSigAddress.Transactions.Count <= MinimumRetainedTransactions)
                    break;
            }

            this.logger.LogDebug("Cleaned {0} transactions older than the CCTS tip less max reorg of {1}.", transactionsPastMaxReorg.Count, crossChainTransferStoreTip);

            return walletUpdated;
        }

        private bool RemoveTransaction(Transaction transaction)
        {
            Guard.NotNull(transaction, nameof(transaction));
            uint256 hash = transaction.GetHash();

            bool updatedWallet = false;

            // Check the inputs - include those that have a reference to a transaction containing one of our scripts and the same index.
            foreach (TxIn input in transaction.Inputs)
            {
                if (!this.outpointLookup.TryGetValue(input.PrevOut, out TransactionData spentTransaction))
                {
                    continue;
                }

                if (spentTransaction.SpendingDetails != null)
                {
                    // Get the transaction being spent and unspend it.
                    this.logger.LogDebug("Unspending transaction {0}-{1}.", spentTransaction.Id, spentTransaction.Index);
                    this.RemoveAssociatedUnconfirmedSpentByTransaction(spentTransaction.SpendingDetails.TransactionId);
                    spentTransaction.SpendingDetails = null;
                    updatedWallet = true;
                }
            }

            foreach (TxOut utxo in transaction.Outputs)
            {
                // Check if the outputs contain one of our addresses.
                if (this.Wallet.MultiSigAddress.ScriptPubKey == utxo.ScriptPubKey)
                {
                    int index = transaction.Outputs.IndexOf(utxo);

                    // Remove any UTXO's that were provided by this transaction from wallet.
                    if (this.Wallet.MultiSigAddress.Transactions.TryGetTransaction(hash, index, out TransactionData foundTransaction))
                    {
                        this.logger.LogDebug("Removing transaction {0}-{1}.", foundTransaction.Id, foundTransaction.Index);
                        this.Wallet.MultiSigAddress.Transactions.Remove(foundTransaction);
                        updatedWallet = true;
                    }
                }
            }

            return updatedWallet;
        }

        /// <summary>
        /// If the transaction we are trying to remove has spending details, it's associated unconfirmed transaction should also be removed.
        /// </summary>
        /// <param name="transactionId">The transaction to process.</param>
        private void RemoveAssociatedUnconfirmedSpentByTransaction(uint256 transactionId)
        {
            if (!this.Wallet.MultiSigAddress.Transactions.TryGetTransaction(transactionId, 0, out TransactionData transactionData))
            {
                this.logger.LogDebug("Spending transaction '{0}' does not exist.", transactionId);
                return;
            }

            if (transactionData.IsConfirmed())
            {
                this.logger.LogDebug("Spending transaction '{0}' was not removed as it is already confirmed.", transactionId);
                return;
            }

            this.Wallet.MultiSigAddress.Transactions.Remove(transactionData);
            this.logger.LogDebug("'{0}' was removed.", transactionId);
        }

        /// <inheritdoc />
        public HashSet<(uint256, DateTimeOffset)> RemoveAllTransactions()
        {
            var removedTransactions = new HashSet<(uint256, DateTimeOffset)>();

            if (!this.IsWalletActive())
                return removedTransactions;

            lock (this.lockObject)
            {
                removedTransactions = this.Wallet.MultiSigAddress.Transactions.Select(t => (t.Id, t.CreationTime)).ToHashSet();
                this.Wallet.MultiSigAddress.Transactions.Clear();

                if (removedTransactions.Any())
                {
                    this.SaveWallet();
                }

                return removedTransactions;
            }
        }

        /// <summary>
        /// Adds a transaction that credits the wallet with new coins.
        /// This method is can be called many times for the same transaction (idempotent).
        /// </summary>
        /// <param name="transaction">The transaction from which details are added.</param>
        /// <param name="utxo">The unspent output to add to the wallet.</param>
        /// <param name="blockHeight">Height of the block.</param>
        /// <param name="blockHash">Hash of the block.</param>
        /// <param name="block">The block containing the transaction to add.</param>
        private void AddTransactionToWallet(Transaction transaction, TxOut utxo, int? blockHeight = null, uint256 blockHash = null, Block block = null)
        {
            Guard.NotNull(transaction, nameof(transaction));
            Guard.NotNull(utxo, nameof(utxo));
            Guard.Assert(blockHash == (blockHash ?? block?.GetHash()));

            // Check if a similar UTXO exists or not (same transaction ID and same index).
            // New UTXOs are added, existing ones are updated.
            uint256 transactionHash = transaction.GetHash();
            int index = transaction.Outputs.IndexOf(utxo);
            if (!this.Wallet.MultiSigAddress.Transactions.TryGetTransaction(transactionHash, index, out TransactionData foundTransaction))
            {
                this.logger.LogDebug("Transaction '{0}-{1}' not found, creating. BlockHeight={2}, BlockHash={3}", transactionHash, index, blockHeight, blockHash);

                TransactionData newTransaction = new TransactionData
                {
                    Amount = utxo.Value,
                    BlockHeight = blockHeight,
                    BlockHash = blockHash,
                    Id = transactionHash,
                    CreationTime = DateTimeOffset.FromUnixTimeSeconds(block?.Header.Time ?? this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp()),
                    Index = index,
                    ScriptPubKey = utxo.ScriptPubKey
                };

                this.Wallet.MultiSigAddress.Transactions.Add(newTransaction);
            }
            else
            {
                this.logger.LogDebug("Transaction '{0}-{1}' found, updating. BlockHeight={2}, BlockHash={3}.", transactionHash, index, blockHeight, blockHash);

                // Update the block height and block hash.
                if ((foundTransaction.BlockHeight == null) && (blockHeight != null))
                {
                    foundTransaction.BlockHeight = blockHeight;
                    foundTransaction.BlockHash = blockHash;
                }

                // Update the block time.
                if (block != null)
                {
                    foundTransaction.CreationTime = DateTimeOffset.FromUnixTimeSeconds(block.Header.Time);
                }
            }
        }

        /// <summary>
        /// Mark an output as spent, the credit of the output will not be used to calculate the balance.
        /// The output will remain in the wallet for history (and reorg).
        /// </summary>
        /// <param name="transaction">The transaction from which details are added.</param>
        /// <param name="paidToOutputs">A list of payments made out</param>
        /// <param name="spendingTransactionId">The id of the transaction containing the output being spent, if this is a spending transaction.</param>
        /// <param name="spendingTransactionIndex">The index of the output in the transaction being referenced, if this is a spending transaction.</param>
        /// <param name="blockHeight">Height of the block.</param>
        /// <param name="blockHash">Hash of the block.</param>
        /// <param name="block">The block containing the transaction to add.</param>
        /// <param name="withdrawal">The withdrawal (if any), otherwise <c>null</c>.</param>
        private void AddSpendingTransactionToWalletLocked(Transaction transaction,
            IEnumerable<TxOut> paidToOutputs,
            uint256 spendingTransactionId,
            int spendingTransactionIndex,
            int? blockHeight = null,
            uint256 blockHash = null,
            Block block = null,
            IWithdrawal withdrawal = null)
        {
            Guard.NotNull(transaction, nameof(transaction));
            Guard.NotNull(paidToOutputs, nameof(paidToOutputs));
            Guard.Assert(blockHash == (blockHash ?? block?.GetHash()));

            // Get the transaction being spent.
            if (!this.Wallet.MultiSigAddress.Transactions.TryGetTransaction(spendingTransactionId, spendingTransactionIndex, out TransactionData spendingTransaction))
            {
                // Strange, why would it be null?
                this.logger.LogTrace("(-)[TX_NULL]");
                return;
            }

            if (spendingTransaction.SpendingDetails?.BlockHeight != null && blockHeight == null)
            {
                // If the spending tx's spending details are confirmed and this is coming in unconfirmed, ignore.
                // This is probably an unlucky concurrency issues, e.g. tx from mempool coming in after confirmed in a block.
                this.logger.LogDebug("Unconfirmed spending UTXO '{0}-{1}' is being ignored as it is already confirmed in block {2}", spendingTransactionId, spendingTransactionIndex, spendingTransaction.SpendingDetails.BlockHeight);
                return;
            }

            // If spending details is null, always set new spending details.
            if (spendingTransaction.SpendingDetails == null)
                this.logger.LogDebug("Spending UTXO '{0}-{1}' is new at height {2}, spending with tx '{3}'.", spendingTransactionId, spendingTransactionIndex, blockHeight, transaction.GetHash());

            // If there are unconfirmed existing spending details, always overwrite with new one. 
            // Could be a "more" signed tx, a FullySigned mempool tx or a confirmed block tx.
            if (spendingTransaction.SpendingDetails != null && spendingTransaction.SpendingDetails.BlockHeight == null)
            {
                this.logger.LogDebug("Spending UTXO '{0}-{1}' has unconfirmed spending details at height {2}, spending with tx '{3}'.", spendingTransactionId, spendingTransactionIndex, blockHeight, transaction.GetHash());

                //If we are overwriting existing spending details, remove the associated transaction as well.
                this.RemoveAssociatedUnconfirmedSpentByTransaction(spendingTransaction.SpendingDetails.TransactionId);
            }

            // If the spending details are confirmed and this is also coming in confirmed, then update the spending details.
            if (spendingTransaction.SpendingDetails != null && spendingTransaction.SpendingDetails.BlockHeight != null && blockHeight != null)
                this.logger.LogDebug("Spending UTXO '{0}-{1}' has confirmed spending details height {2}, spending with tx '{3}'.", spendingTransactionId, spendingTransactionIndex, blockHeight, transaction.GetHash());

            spendingTransaction.SpendingDetails = this.BuildSpendingDetails(transaction, paidToOutputs, blockHeight, blockHash, block, withdrawal);
        }

        /// <summary>
        /// Creates a <see cref="SpendingDetails"/> object for a spending transaction to be associated with a <see cref="TransactionData"/> object.
        /// The information includes selected transaction outputs as <see cref="PaymentDetails"/> entries. Adds the block information if it has been included in a block.
        /// </summary>
        /// <param name="transaction">The transaction providing the outputs.</param>
        /// <param name="paidToOutputs">A filtered list of the transaction outputs.</param>
        /// <param name="blockHeight">The block height of the transaction if its included in a block.</param>
        /// <param name="blockHash">The block hash of the transaction if its included in a block.</param>
        /// <param name="block">The block of the transaction if its included in a block.</param>
        /// <param name="withdrawal">The withdrawal details if its a withdrawal transaction, otherwise <c>null</c>.</param>
        /// <returns>See <see cref="SpendingDetails">.</returns>
        private SpendingDetails BuildSpendingDetails(Transaction transaction,
            IEnumerable<TxOut> paidToOutputs,
            int? blockHeight = null,
            uint256 blockHash = null,
            Block block = null,
            IWithdrawal withdrawal = null)
        {
            List<PaymentDetails> payments = new List<PaymentDetails>();
            foreach (TxOut paidToOutput in paidToOutputs)
            {
                // TODO: Use the ScriptAddressReader here?
                // Figure out how to retrieve the destination address.
                string destinationAddress = string.Empty;
                ScriptTemplate scriptTemplate = paidToOutput.ScriptPubKey.FindTemplate(this.network);
                switch (scriptTemplate.Type)
                {
                    // Pay to PubKey can be found in outputs of staking transactions.
                    case TxOutType.TX_PUBKEY:
                        PubKey pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(paidToOutput.ScriptPubKey);
                        destinationAddress = pubKey.GetAddress(this.network).ToString();
                        break;
                    // Pay to PubKey hash is the regular, most common type of output.
                    case TxOutType.TX_PUBKEYHASH:
                        destinationAddress = paidToOutput.ScriptPubKey.GetDestinationAddress(this.network).ToString();
                        break;
                    case TxOutType.TX_NONSTANDARD:
                    case TxOutType.TX_SCRIPTHASH:
                        destinationAddress = paidToOutput.ScriptPubKey.GetDestinationAddress(this.network).ToString();
                        break;
                    case TxOutType.TX_MULTISIG:
                    case TxOutType.TX_NULL_DATA:
                    case TxOutType.TX_SEGWIT:
                    case TxOutType.TX_COLDSTAKE:
                        break;
                }

                payments.Add(new PaymentDetails
                {
                    DestinationScriptPubKey = paidToOutput.ScriptPubKey,
                    DestinationAddress = destinationAddress,
                    Amount = paidToOutput.Value
                });
            }

            SpendingDetails spendingDetails = new SpendingDetails
            {
                TransactionId = transaction.GetHash(),
                Payments = payments,
                CreationTime = DateTimeOffset.FromUnixTimeSeconds(block?.Header.Time ?? this.dateTimeProvider.GetAdjustedTimeAsUnixTimestamp()),
                BlockHeight = blockHeight,
                BlockHash = blockHash,
                // Only recording this if it can't be found in a block.
                Transaction = (blockHeight > 0) ? null : transaction,
                IsCoinStake = transaction.IsCoinStake == false ? (bool?)null : true
            };

            if (withdrawal != null)
            {
                spendingDetails.WithdrawalDetails = new WithdrawalDetails
                {
                    Amount = withdrawal.Amount,
                    MatchingDepositId = withdrawal.DepositId,
                    TargetAddress = withdrawal.TargetAddress
                };
            }

            return spendingDetails;
        }

        /// <inheritdoc />
        public void SaveWallet()
        {
            lock (this.lockObject)
            {
                if (this.Wallet != null)
                {
                    lock (this.lockObject)
                    {
                        this.fileStorage.SaveToFile(this.Wallet, WalletFileName);
                    }
                }
            }
        }

        /// <inheritdoc />
        public bool RemoveUnconfirmedTransactionData()
        {
            lock (this.lockObject)
            {
                bool walletUpdated = false;

                foreach (TransactionData transactionData in this.Wallet.MultiSigAddress.Transactions.ToList())
                {
                    // Change for unconfirmed transaction?
                    if (transactionData.BlockHeight == null)
                    {
                        this.Wallet.MultiSigAddress.Transactions.Remove(transactionData);
                    }
                    // Spend by unconfirmed transaction?
                    else if (transactionData.SpendingDetails != null && transactionData.SpendingDetails.BlockHeight == null)
                    {
                        transactionData.SpendingDetails = null;
                    }
                    else
                    {
                        continue;
                    }

                    walletUpdated = true;
                }

                return walletUpdated;
            }
        }

        /// <inheritdoc />
        public bool RemoveWithdrawalTransactions(uint256 depositId)
        {
            this.logger.LogDebug("Removing transient transactions for depositId '{0}'.", depositId);

            lock (this.lockObject)
            {
                // Remove transient transactions not seen in a block yet.
                bool walletUpdated = false;

                foreach ((Transaction transaction, IWithdrawal withdrawal) in this.FindWithdrawalTransactions(depositId))
                {
                    walletUpdated |= this.RemoveTransaction(transaction);
                }

                return walletUpdated;
            }
        }

        private OutPoint EarliestOutput(Transaction transaction)
        {
            var comparer = Comparer<OutPoint>.Create((x, y) => this.CompareOutpoints(x, y));
            return transaction.Inputs.Select(i => i.PrevOut).OrderBy(t => t, comparer).FirstOrDefault();
        }

        /// <inheritdoc />
        public List<(Transaction, IWithdrawal)> FindWithdrawalTransactions(uint256 depositId = null, bool sort = false)
        {
            lock (this.lockObject)
            {
                var withdrawals = new List<(Transaction transaction, IWithdrawal withdrawal)>();

                var txList = new List<TransactionData>();
                foreach ((uint256 _, List<TransactionData> txListDeposit) in this.Wallet.MultiSigAddress.Transactions.GetSpendingTransactionsByDepositId(depositId))
                {
                    txList.AddRange(txListDeposit);
                }

                Dictionary<TransactionData, Transaction> spendingTransactions = this.GetSpendingTransactions(txList);

                foreach (TransactionData txData in txList)
                {
                    SpendingDetails spendingDetail = txData.SpendingDetails;

                    // Multiple UTXOs may be spent by the one withdrawal, so if it's already added then no need to add it again.
                    if (withdrawals.Any(w => w.withdrawal.Id == spendingDetail.TransactionId))
                        continue;

                    if (spendingDetail.WithdrawalDetails == null)
                    {
                        this.logger.LogError($"Spending detail with txId '{spendingDetail.TransactionId}' has null withdrawal details, deposit id '{depositId}'");
                    }

                    var withdrawal = new Withdrawal(
                        spendingDetail.WithdrawalDetails.MatchingDepositId,
                        spendingDetail.TransactionId,
                        spendingDetail.WithdrawalDetails.Amount,
                        spendingDetail.WithdrawalDetails.TargetAddress,
                        spendingDetail.BlockHeight ?? 0,
                        spendingDetail.BlockHash);

                    Transaction transaction = spendingTransactions[txData];

                    withdrawals.Add((transaction, withdrawal));
                }

                if (sort)
                {
                    return withdrawals
                        .OrderBy(w => this.EarliestOutput(w.Item1), Comparer<OutPoint>.Create((x, y) => this.CompareOutpoints(x, y)))
                        .ToList();
                }

                return withdrawals;
            }
        }

        /// <summary>
        /// Checks if a transaction has valid UTXOs that are spent by it.
        /// </summary>
        /// <param name="transaction">The transaction to check.</param>
        /// <param name="coins">Returns the coins found if this parameter supplies an empty coin list.</param>
        /// <returns><c>True</c> if UTXO's are valid and <c>false</c> otherwise.</returns>
        private bool TransactionHasValidUTXOs(Transaction transaction, List<Coin> coins = null)
        {
            // All the input UTXO's should be present in spending details of the multi-sig address.
            foreach (TxIn input in transaction.Inputs)
            {
                if (!this.outpointLookup.TryGetValue(input.PrevOut, out TransactionData transactionData))
                    return false;

                if (transactionData.SpendingDetails?.TransactionId != transaction.GetHash())
                    return false;

                coins?.Add(new Coin(transactionData.Id, (uint)transactionData.Index, transactionData.Amount, transactionData.ScriptPubKey));
            }

            return true;
        }

        /// <summary>
        /// Checks if a transaction is consuming unspent UTXOs that exist in the wallet.
        /// </summary>
        /// <param name="transaction">The transaction to check.</param>
        /// <param name="coins">Returns the coins found if this parameter supplies an empty coin list.</param>
        /// <returns><c>True</c> if UTXO's are valid and <c>false</c> otherwise.</returns>
        private bool TransactionIsSpendingUnspentUTXOs(Transaction transaction, List<Coin> coins = null)
        {
            // All the input UTXO's should be present but not be spent by anything yet.
            foreach (TxIn input in transaction.Inputs)
            {
                if (!this.outpointLookup.TryGetValue(input.PrevOut, out TransactionData transactionData))
                    return false;

                if (transactionData.SpendingDetails != null)
                    return false;

                coins?.Add(new Coin(transactionData.Id, (uint)transactionData.Index, transactionData.Amount, transactionData.ScriptPubKey));
            }

            return true;
        }

        /// <summary>
        /// Compares two outpoints to see which occurs earlier.
        /// </summary>
        /// <param name="outPoint1">The first outpoint to compare.</param>
        /// <param name="outPoint2">The second outpoint to compare.</param>
        /// <returns><c>-1</c> if the <paramref name="outPoint1"/> occurs first and <c>1</c> otherwise.</returns>
        internal int CompareOutpoints(OutPoint outPoint1, OutPoint outPoint2)
        {
            TransactionData transactionData1 = this.outpointLookup[outPoint1];
            TransactionData transactionData2 = this.outpointLookup[outPoint2];

            return DeterministicCoinOrdering.CompareTransactionData(transactionData1, transactionData2);
        }

        /// <inheritdoc />
        public ValidateTransactionResult ValidateTransaction(Transaction transaction, bool checkSignature = false)
        {
            lock (this.lockObject)
            {
                // All the input UTXO's should be present in spending details of the multi-sig address.
                List<Coin> coins = checkSignature ? new List<Coin>() : null;

                // Verify that the transaction has valid UTXOs.
                if (!this.TransactionHasValidUTXOs(transaction, coins))
                {
                    this.logger.LogError($"Transaction '{transaction.GetHash()}' does not have valid UTXOs.");
                    return ValidateTransactionResult.Failed("Transaction does not have valid UTXOs.");
                }

                // Verify that there are no earlier unspent UTXOs.
                var comparer = Comparer<TransactionData>.Create(DeterministicCoinOrdering.CompareTransactionData);
                TransactionData earliestUnspent = this.Wallet.MultiSigAddress.Transactions.GetUnspentTransactions().FirstOrDefault();
                if (earliestUnspent != null)
                {
                    TransactionData oldestInput = transaction.Inputs
                                                             .Where(i => this.outpointLookup.ContainsKey(i.PrevOut))
                                                             .Select(i => this.outpointLookup[i.PrevOut])
                                                             .OrderByDescending(t => t, comparer)
                                                             .FirstOrDefault();
                    if (oldestInput != null && DeterministicCoinOrdering.CompareTransactionData(earliestUnspent, oldestInput) < 0)
                    {
                        this.logger.LogError($"Earlier unspent UTXOs exist for tx '{transaction.GetHash()}'");
                        return ValidateTransactionResult.Failed("Earlier unspent UTXOs exist.");
                    }
                }

                // Verify that all inputs are signed.
                if (checkSignature)
                {
                    TransactionBuilder builder = new TransactionBuilder(this.Wallet.Network).AddCoins(coins);

                    var verifyResult = builder.Verify(transaction, transaction.GetFee(coins.ToArray()), out TransactionPolicyError[] errors);
                    if (verifyResult)
                        return ValidateTransactionResult.Valid();

                    // Ignore any fee related errors here as the BuildTransaction method in FederationWalletTransactionHandler would have already
                    // verified the transaction's fee. Fee errors could occur here as the signatures (secrets) aren't added to the builder when calling
                    // the verify method.
                    IEnumerable<TransactionPolicyError> filteredErrors = errors.Where(a => a.GetType() != typeof(FeeTooLowPolicyError));
                    if (!filteredErrors.Any())
                        return ValidateTransactionResult.Valid();

                    var errorList = new List<string>();

                    // Trace the reason validation failed. Note that failure here doesn't mean an error necessarily. Just that the transaction is not fully signed.
                    foreach (TransactionPolicyError transactionPolicyError in filteredErrors)
                    {
                        this.logger.LogDebug("{0} FAILED - {1}", nameof(TransactionBuilder.Verify), transactionPolicyError.ToString());
                        errorList.Add(transactionPolicyError.ToString());
                    }

                    return ValidateTransactionResult.Failed(errorList.ToArray());

                }

                return ValidateTransactionResult.Valid();
            }
        }

        /// <inheritdoc />
        public bool ValidateConsolidatingTransaction(Transaction transaction, bool checkSignature = false)
        {
            lock (this.lockObject)
            {
                List<Coin> coins = checkSignature ? new List<Coin>() : null;

                // Verify that the transaction's UTXOs aren't used yet.
                if (!this.TransactionIsSpendingUnspentUTXOs(transaction, coins))
                    return false;

                // TODO: Check the inputs are in order?

                // Verify that all inputs are signed.
                if (checkSignature)
                {
                    TransactionBuilder builder = new TransactionBuilder(this.Wallet.Network).AddCoins(coins);

                    if (!builder.Verify(transaction, FederatedPegSettings.ConsolidationFee, out TransactionPolicyError[] errors))
                    {
                        // Trace the reason validation failed. Note that failure here doesn't mean an error necessarily. Just that the transaction is not fully signed.
                        foreach (TransactionPolicyError transactionPolicyError in errors)
                        {
                            this.logger.LogInformation("{0} FAILED - {1}", nameof(TransactionBuilder.Verify), transactionPolicyError.ToString());
                        }

                        return false;
                    }
                }

                return true;
            }
        }

        /// <inheritdoc />
        public void UpdateLastBlockSyncedHeight(ChainedHeader chainedHeader)
        {
            Guard.NotNull(chainedHeader, nameof(chainedHeader));

            if (this.IsWalletActive(chainedHeader.Height))
            {
                lock (this.lockObject)
                {
                    // The block locator will help when the wallet
                    // needs to rewind this will be used to find the fork.
                    this.Wallet.BlockLocator = chainedHeader.GetLocator().Blocks;

                    // Update the wallets with the last processed block height.
                    this.Wallet.LastBlockSyncedHeight = chainedHeader.Height;
                    this.Wallet.LastBlockSyncedHash = chainedHeader.HashBlock;
                    this.WalletTipHash = chainedHeader.HashBlock;
                    this.WalletTipHeight = chainedHeader.Height;
                }
            }
        }

        /// <summary>
        /// Generates the wallet file.
        /// </summary>
        /// <returns>The wallet object that was saved into the file system.</returns>
        /// <exception cref="WalletException">Thrown if wallet cannot be created.</exception>
        private FederationWallet GenerateWallet()
        {
            this.logger.LogDebug("Generating the federation wallet file.");

            int lastBlockSyncedHeight = Math.Max(0, this.federatedPegSettings.WalletSyncFromHeight - 1);
            uint256 lastBlockSyncedHash = (lastBlockSyncedHeight <= this.chainIndexer.Height) ? this.chainIndexer[lastBlockSyncedHeight].HashBlock : null;

            var wallet = new FederationWallet
            {
                CreationTime = this.dateTimeProvider.GetTimeOffset(),
                Network = this.network,
                CoinType = this.coinType,
                LastBlockSyncedHeight = lastBlockSyncedHeight,
                LastBlockSyncedHash = lastBlockSyncedHash,
                MultiSigAddress = new MultiSigAddress
                {
                    Address = this.federatedPegSettings.MultiSigAddress.ToString(),
                    M = this.federatedPegSettings.MultiSigM,
                    ScriptPubKey = this.federatedPegSettings.MultiSigAddress.ScriptPubKey,
                    Pubkeys = this.federatedPegSettings.FederationPublicKeys.Select(k => k.ToHex()).ToArray(),
                    RedeemScript = this.federatedPegSettings.MultiSigRedeemScript,
                    Transactions = new MultiSigTransactions()
                }
            };

            this.logger.LogTrace("(-)");
            return wallet;
        }

        /// <inheritdoc />
        public void EnableFederationWallet(string password, string mnemonic = null, string passphrase = null)
        {
            Guard.NotEmpty(password, nameof(password));

            lock (this.lockObject)
            {
                // Protect against de-activation if the federation is already active.
                if (this.isFederationActive)
                {
                    this.logger.LogWarning("(-):[FEDERATION_ALREADY_ACTIVE]");
                    return;
                }

                // Get the key and encrypted seed.
                Key key = null;
                string encryptedSeed = this.Wallet.EncryptedSeed;

                if (!string.IsNullOrEmpty(mnemonic))
                {
                    ExtKey extendedKey;
                    try
                    {
                        extendedKey = HdOperations.GetExtendedKey(mnemonic, passphrase);
                    }
                    catch (NotSupportedException ex)
                    {
                        this.logger.LogDebug("Exception occurred: {0}", ex.ToString());
                        this.logger.LogTrace("(-)[EXCEPTION]");

                        if (ex.Message == "Unknown")
                            throw new WalletException("Please make sure you enter valid mnemonic words.");

                        throw;
                    }

                    // Create a wallet file.
                    key = extendedKey.PrivateKey;
                    encryptedSeed = key.GetEncryptedBitcoinSecret(password, this.network).ToWif();
                }

                try
                {
                    if (key == null)
                        key = Key.Parse(encryptedSeed, password, this.Wallet.Network);

                    bool isValidKey = key.PubKey.ToHex() == this.federatedPegSettings.PublicKey;

                    if (!isValidKey)
                        throw new WalletException($"The wallet public key {key.PubKey.ToHex()} does not match the federation member's public key {this.federatedPegSettings.PublicKey}");

                    this.Secret = new WalletSecret() { WalletPassword = password };
                    this.Wallet.EncryptedSeed = encryptedSeed;
                    this.SaveWallet();

                    this.isFederationActive = isValidKey;
                }
                catch (Exception ex)
                {
                    throw new SecurityException(ex.Message);
                }
            }
        }

        public bool IsFederationWalletActive()
        {
            return this.isFederationActive;
        }

        [NoTrace]
        public FederationWallet GetWallet()
        {
            return this.Wallet;
        }

        /// <summary>
        /// Lists all spendable transactions in the current wallet.
        /// </summary>
        /// <param name="currentChainHeight">The current height of the chain. Used for calculating the number of confirmations a transaction has.</param>
        /// <param name="confirmations">The minimum number of confirmations required for transactions to be considered.</param>
        /// <returns>A collection of spendable outputs that belong to the given account.</returns>
        private IEnumerable<UnspentOutputReference> GetSpendableTransactions(int currentChainHeight, int confirmations = 0)
        {
            // A block that is at the tip has 1 confirmation.
            // When calculating the confirmations the tip must be advanced by one.

            int countFrom = currentChainHeight + 1;
            foreach (TransactionData transactionData in this.Wallet.MultiSigAddress.Transactions.GetUnspentTransactions())
            {
                int? confirmationCount = 0;
                if (transactionData.BlockHeight != null)
                    confirmationCount = countFrom >= transactionData.BlockHeight ? countFrom - transactionData.BlockHeight : 0;

                if (confirmationCount >= confirmations)
                {
                    yield return new UnspentOutputReference
                    {
                        Transaction = transactionData,
                    };
                }
            }
        }

        /// <inheritdoc />
        public (Money ConfirmedAmount, Money UnConfirmedAmount) GetSpendableAmount()
        {
            lock (this.lockObject)
            {
                IEnumerable<TransactionData> transactions = this.Wallet.MultiSigAddress.Transactions.GetUnspentTransactions();

                long confirmed = transactions.Sum(t => t.SpendableAmount(true));
                long total = transactions.Sum(t => t.SpendableAmount(false));

                return (confirmed, total - confirmed);
            }
        }

        /// <inheritdoc />
        public bool IsSyncedWithChain()
        {
            return this.WalletTipHeight >= this.chainIndexer.Tip.Height - FederationWalletTipSyncBuffer;
        }

        private void AddInlineStats(StringBuilder benchLogs)
        {
            string hash = this.Wallet?.LastBlockSyncedHash == null ? "N/A" : this.Wallet.LastBlockSyncedHash.ToString();
            string height = this.Wallet?.LastBlockSyncedHeight == null ? "N/A" : this.Wallet.LastBlockSyncedHeight.ToString();

            benchLogs.AppendLine("Fed.Wallet.Height".PadRight(LoggingConfiguration.ColumnLength) + $": {height}".PadRight(10) + $"(Hash: {hash})");
        }

        private void AddComponentStats(StringBuilder benchLog)
        {
            benchLog.AppendLine(">> Federation Wallet");

            (Money ConfirmedAmount, Money UnConfirmedAmount) = GetSpendableAmount();

            benchLog.AppendLine("Federation Wallet".PadRight(LoggingConfiguration.ColumnLength)
                                + ": Confirmed balance: " + ConfirmedAmount.ToString().PadRight(LoggingConfiguration.ColumnLength)
                                + " Reserved for withdrawals: " + UnConfirmedAmount.ToString().PadRight(LoggingConfiguration.ColumnLength)
                                + " Federation Status: " + (this.isFederationActive ? "Active" : "Inactive"));
            benchLog.AppendLine();

            if (!this.isFederationActive)
            {
                benchLog.AppendLine("".PadRight(59, '=') + " W A R N I N G " + "".PadRight(59, '='));
                benchLog.AppendLine();
                benchLog.AppendLine("This federation node is not enabled. You will not be able to store or participate in signing of transactions until you enable it.");
                benchLog.AppendLine("If not done previously, please enable your federation node using " + $"/api/FederationWallet/{FederationWalletRouteEndPoint.EnableFederation}.");
                benchLog.AppendLine();
                benchLog.AppendLine("".PadRight(133, '='));
                benchLog.AppendLine();
            }
        }
    }
}