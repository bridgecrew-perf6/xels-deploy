﻿using System;
using System.Collections.Generic;
using NBitcoin;
using Xels.Bitcoin.Utilities;

namespace Xels.Bitcoin.Features.Wallet.Interfaces
{
    /// <summary>
    /// Defines the required interface of a wallet repository.
    /// </summary>
    /// <remarks>
    /// The repository should not contain any business logic other than what is implied or provided by services explicitly associated
    /// with this interface. These currently include:
    /// - Network and Consensus. Used for obtaining:
    ///   - the coin type for HdPath resolution
    ///   - human-readable addresses to be returned by some public methods
    /// - DataFolder. Used for obtaining:
    ///   - the wallet folder.
    /// - IDateTimeProvider
    ///   - used for populating CreationTime fields.
    ///   IScriptAddressReader (or IScriptDestinationReader)
    ///   - used to find the destinations of <see cref="TxOut.ScriptPubKey" /> scripts.
    /// </remarks>
    public interface IWalletRepository
    {
        /// <summary>
        /// Gets the network used by the repository.
        /// </summary>
        Network Network { get; }

        /// <summary>
        /// Initialize an existing or empty database.
        /// </summary>
        /// <param name="dbPerWallet">If set to <c>false</c> then one database will be created for all wallets.</param>
        void Initialize(bool dbPerWallet = true);

        /// <summary>
        /// Shuts the repository down.
        /// </summary>
        void Shutdown();

        /// <summary>
        /// Returns a wallet by name.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <returns>The wallet.</returns>
        Wallet GetWallet(string walletName);

        /// <summary>
        /// Updates the relevant wallets from the information contained in the transaction.
        /// </summary>
        /// <param name="walletName">The wallet name.</param>
        /// <param name="transaction">The transient transaction to process.</param>
        /// <param name="txId">Used to overrides the default transaction id.</param>
        /// <remarks>
        /// This method is intended to be idempotent - i.e. running it twice should not produce any adverse effects.
        /// If any empty wallet addresses have transactions added to them then the affected accounts should
        /// have their addresses topped up to ensure there are always a buffer of unused addresses after the last
        /// address containing transactions.
        /// </remarks>
        void ProcessTransaction(string walletName, Transaction transaction, uint256 txId = null);

        /// <summary>
        /// Updates all the wallets from the information contained in the block.
        /// </summary>
        /// <param name="block">The block to process.</param>
        /// <param name="header">The header of the block passed.</param>
        /// <param name="walletName">Set this to limit processing to the named wallet.</param>
        /// <remarks>
        /// This method is intended to be idempotent - i.e. running it twice consecutively should not produce any adverse effects.
        /// Similar to the rest of the methods it should not contain any business logic other than what may be injected externally.
        /// If any empty wallet addresses have transactions added to them then the affected accounts should
        /// have their addresses topped up to ensure there are always a buffer of unused addresses after the last
        /// address containing transactions.
        /// It's the caller's responsibility to ensure that this method is not called again when it's already executing.
        /// </remarks>
        void ProcessBlock(Block block, ChainedHeader header, string walletName = null);

        /// <summary>
        /// Updates all the wallets from the information contained in the blocks.
        /// </summary>
        /// <param name="blocks">The blocks to process.</param>
        /// <param name="walletName">Set this to limit processing to the named wallet.</param>
        /// <remarks>
        /// This method is intended to be idempotent - i.e. running it twice consecutively should not produce any adverse effects.
        /// Similar to the rest of the methods it should not contain any business logic other than what may be injected externally.
        /// If any empty wallet addresses have transactions added to them then the affected accounts should
        /// have their addresses topped up to ensure there are always a buffer of unused addresses after the last
        /// address containing transactions.
        /// It's the caller's responsibility to ensure that this method is not called again when it's already executing.
        /// </remarks>
        void ProcessBlocks(IEnumerable<(ChainedHeader header, Block block)> blocks, string walletName = null);

        /// <summary>
        /// Creates a wallet without any accounts.
        /// </summary>
        /// <param name="walletName">The name of the wallet to create.</param>
        /// <param name="encryptedSeed">The encrypted seed of the wallet.</param>
        /// <param name="chainCode">The chain code of the walllet.</param>
        /// <param name="lastBlockSynced">The last block synced. Typically the current chain tip or <c>null</c> if the wallet should sync from genesis.</param>
        /// <param name="blockLocator">The block locator to set for the wallet.</param>
        /// <param name="creationTime">The creation time to set for the wallet.</param>
        /// <returns>The new wallet.</returns>
        Wallet CreateWallet(string walletName, string encryptedSeed, byte[] chainCode, HashHeightPair lastBlockSynced, BlockLocator blockLocator, long? creationTime = null);

        /// <summary>
        /// Deletes a wallet.
        /// </summary>
        /// <param name="walletName">The name of the wallet to delete.</param>
        /// <returns>Returns <c>true</c> if the wallet was deleted and <c>false</c> otherwise.</returns>
        bool DeleteWallet(string walletName);

        /// <summary>
        /// Creates a wallet account using an extended public key.
        /// </summary>
        /// <param name="walletName">The name of the wallet to create the account for.</param>
        /// <param name="accountIndex">The account index to create an account for.</param>
        /// <param name="accountName">The account name to use.</param>
        /// <param name="extPubKey">The extended public key for the account.</param>
        /// <param name="creationTime">Used to override the default creation time of the account.</param>
        /// <returns>The newly created account.</returns>
        /// <param name="addressCounts">The number of each type of address to create.</param>
        HdAccount CreateAccount(string walletName, int accountIndex, string accountName, ExtPubKey extPubKey, DateTimeOffset? creationTime = null, (int external, int change)? addressCounts = null);

        /// <summary>
        /// Gets up to the specified number of unused addresses.
        /// </summary>
        /// <param name="accountReference">The account to get unused addresses for.</param>
        /// <param name="count">The maximum number of addresses to return.</param>
        /// <param name="isChange">The type of addresses to return.</param>
        /// <returns>A list of unused addresses.</returns>
        IEnumerable<HdAddress> GetUnusedAddresses(WalletAccountReference accountReference, int count, bool isChange = false);

        /// <summary>
        /// Getss all the unused addresses in the account.
        /// </summary>
        /// <param name="accountReference">The account to get used addresses for.</param>
        /// <param name="isChange">The type of addresses to return.</param>
        /// <returns>A list of used addresses with their balances.</returns>
        IEnumerable<(HdAddress address, Money confirmed, Money total)> GetUsedAddresses(WalletAccountReference accountReference, bool isChange = false);

        /// <summary>
        /// Gets all the unused addresses in the account.
        /// </summary>
        /// <param name="accountReference">The account to get unused addresses for.</param>
        /// <param name="isChange">The type of addresses to return.</param>
        /// <returns>A list of unused addresses.</returns>
        IEnumerable<HdAddress> GetUnusedAddresses(WalletAccountReference accountReference, bool isChange = false);

        /// <summary>
        /// Returns one or more newly created addresses every time.
        /// This is in contrast to the unusedaddress(es) endpoints that will return the same set of addresses if there has been no transactional activity.
        /// <remarks>The created addresses are created without regard for the default gap limit. Care must therefore be taken when restoring the wallet if many sparsely used addresses have been created.</remarks>
        /// </summary>
        /// <param name="accountReference">A reference to the wallet and account that addresses should be created in.</param>
        /// <param name="count">The number of addresses to be created.</param>
        /// <param name="isChange">Whether the created addresses should be change addresses or not.</param>
        /// <returns>A list of the created addresses.</returns>
        IEnumerable<HdAddress> GetNewAddresses(WalletAccountReference accountReference, int count, bool isChange = false);

        /// <summary>
        /// Gets all spendable transactions in the wallet with the given number of confirmation.
        /// </summary>
        /// <param name="accountReference">The account to get unused addresses for.</param>
        /// <param name="currentChainHeight">The chain height to use in the determination of the number of confirmations of a transaction. </param>
        /// <param name="confirmations">The minimum number of confirmations for a transactions to be regarded spendable.</param>
        /// <param name="coinBaseMaturity">Can be used to override <see cref="Network.Consensus.CoinbaseMaturity"/>.</param>
        /// <returns>The list of spendable transactions for the account.</returns>
        /// <remarks>For coinbase transactions <see cref="Network.Consensus.CoinbaseMaturity" /> will be used in addition to <paramref name="confirmations"/>.</remarks>
        IEnumerable<UnspentOutputReference> GetSpendableTransactionsInAccount(WalletAccountReference accountReference, int currentChainHeight, int confirmations = 0, int? coinBaseMaturity = null);

        /// <summary>
        /// Gets an account's total balance and confirmed balance amounts.
        /// </summary>
        /// <param name="walletAccountReference">The account to get the balances for.</param>
        /// <param name="currentChainHeight">The current chain height.</param>
        /// <param name="confirmations">The minimum number of confirmations for a transactions to be regarded spendable.</param>
        /// <param name="coinBaseMaturity">Can be used to override <see cref="Network.Consensus.CoinbaseMaturity"/>.</param>
        /// <param name="address">Filter the results to only include this address.</param>
        /// <returns>The account's total balance and confirmed balance amounts.</returns>
        /// <remarks>For coinbase transactions <see cref="Network.Consensus.CoinbaseMaturity" /> will be used in addition to <paramref name="confirmations"/>.</remarks>
        (Money totalAmount, Money confirmedAmount, Money spendableAmount) GetAccountBalance(WalletAccountReference walletAccountReference, int currentChainHeight, int confirmations = 0, int? coinBaseMaturity = null, (int, int)? address = null);

        /// <summary>
        /// Returns a history of all transactions in the wallet.
        /// </summary>
        /// <param name="account">An optional account name to limit the results to a particular account.</param>
        /// <param name="limit">Limit the result set by this amount of records (used with paging).</param>
        /// <param name="offset">Offset the result set start point by this amount of records (used with paging).</param>
        /// <param name="txId">Optional transaction filter.</param>
        /// <param name="address">An optional account address filter to limit the results to a particular address.</param>
        /// <param name="forSmartContracts">If set, gets the smart contract history.</param>
        /// <returns>A history of all transactions in the wallet.</returns>
        AccountHistory GetHistory(HdAccount account, int limit, int offset, string txId = null, string address = null, bool forSmartContracts = false);

        /// <summary>
        /// Allows an unconfirmed transaction to be removed.
        /// </summary>
        /// <param name="walletName">The name of the wallet to remove the transaction from.</param>
        /// <param name="txId">The transaction id of the transaction to remove.</param>
        /// <returns>The creation time of the transaction that was removed.</returns>
        DateTimeOffset? RemoveUnconfirmedTransaction(string walletName, uint256 txId);

        /// <summary>
        /// Removes all unconfirmed transactions from the wallet.
        /// </summary>
        /// <param name="walletName">The name of the wallet to remove the transactions from.</param>
        /// <returns>The transactions that were removed.</returns>
        IEnumerable<(uint256 txId, DateTimeOffset creationTime)> RemoveAllUnconfirmedTransactions(string walletName);

        /// <summary>
        /// Determines a block in common between the supplied chain tip and the wallet block locator.
        /// </summary>
        /// <param name="walletName">The name of the wallet to determine the fork for.</param>
        /// <param name="chainTip">The chain tip to use in determining the fork.</param>
        /// <returns>The fork or <c>null</c> if there are no blocks in common.</returns>
        ChainedHeader FindFork(string walletName, ChainedHeader chainTip);

        /// <summary>
        /// Only keep wallet transactions up to and including the specified block.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="lastBlockSynced">The last block synced to set.</param>
        /// <returns>A flag indicating success and a list of transactions removed from the wallet.</returns>
        /// <remarks>The value of lastBlockSynced must match a block that was conceivably processed by the wallet (or be null).</remarks>
        (bool RewindExecuted, IEnumerable<(uint256, DateTimeOffset)> RemovedTransactions) RewindWallet(string walletName, ChainedHeader lastBlockSynced);

        /// <summary>
        /// Allows multiple interface calls to be grouped into a transaction.
        /// </summary>
        /// <param name="walletName">The wallet the transaction is for.</param>
        /// <returns>A transaction context providing <see cref="ITransactionContext.Commit"/> and <see cref="ITransactionContext.Rollback"/> methods.</returns>
        ITransactionContext BeginTransaction(string walletName);

        /// <summary>
        /// Gets the wallet's <see cref="TransactionData"/> records. Records are sorted by transaction creation time and output index.
        /// </summary>
        /// <param name="hdAddress">The address for which to retrieve transactions.</param>
        /// <param name="limit">The maximum number of records to return.</param>
        /// <param name="prev">The record preceding the first record to be returned. Can be <c>null</c> to return the first record.</param>
        /// <param name="descending">The default is descending. Set to <c>false</c> to return records in ascending order.</param>
        /// <returns>The wallet's <see cref="TransactionData"/> records.</returns>
        /// <remarks>Spending details are not included.</remarks>
        IEnumerable<TransactionData> GetAllTransactions(HdAddress hdAddress, int limit = int.MaxValue, TransactionData prev = null, bool descending = true);

        /// <summary>
        /// Returns <see cref="TransactionData"/> records in the wallet acting as inputs to the given transaction.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="transactionTime">The transaction creation time.</param>
        /// <param name="transactionId">The transaction id.</param>
        /// <param name="includePayments">Set to <c>true</c> to include payment details.</param>
        /// <returns><see cref="TransactionData"/> records in the wallet acting as inputs to the given transaction.</returns>
        IEnumerable<TransactionData> GetTransactionInputs(HdAccount account, DateTimeOffset? transactionTime, uint256 transactionId, bool includePayments = false);

        /// <summary>
        /// Returns <see cref="TransactionData"/> records in the wallet acting as outputs to the given transaction.
        /// </summary>
        /// <param name="account">The account.</param>
        /// <param name="transactionTime">The transaction creation time.</param>
        /// <param name="transactionId">The transaction id.</param>
        /// <param name="includePayments">Set to <c>true</c> to include payment details.</param>
        /// <returns><see cref="TransactionData"/> records in the wallet acting as inputs to the given transaction.</returns>
        IEnumerable<TransactionData> GetTransactionOutputs(HdAccount account, DateTimeOffset? transactionTime, uint256 transactionId, bool includePayments = false);

        /// <summary>
        /// Determines address groupings.
        /// </summary>
        /// <param name="walletName">The wallet name.</param>
        /// <returns>Returns an enumeration of grouped addresses.</returns>
        IEnumerable<IEnumerable<string>> GetAddressGroupings(string walletName);

        /// <summary>
        /// Get the accounts in the wallet.
        /// </summary>
        /// <param name="hdWallet">The wallet to get the accounts for.</param>
        /// <param name="accountName">Specifies a specific account to return.</param>
        /// <returns>The accounts in the wallet.</returns>
        IEnumerable<HdAccount> GetAccounts(Wallet hdWallet, string accountName = null);

        /// <summary>
        /// Get the names of the wallets in the repository.
        /// </summary>
        /// <returns>The names of the wallets in the repository.</returns>
        List<string> GetWalletNames();

        /// <summary>
        /// Gets a lookup that can identify where in a wallet an address belongs.
        /// </summary>
        /// <param name="walletName">Name of the wallet.</param>
        /// <returns>The lookup.</returns>
        IWalletAddressReadOnlyLookup GetWalletAddressLookup(string walletName);

        /// <summary>
        /// Gets a lookup that can identify where in a wallet a transaction belongs.
        /// </summary>
        /// <param name="walletName">Name of the wallet.</param>
        /// <returns>The lookup.</returns>
        IWalletTransactionReadOnlyLookup GetWalletTransactionLookup(string walletName);

        int GetWalletId(string walletName);

        /// <summary>
        /// Gets a structure representing the HD Path of the provided parameters.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="accountName">The account name.</param>
        /// <param name="addressType">The address type.</param>
        /// <param name="addressIndex">The address index.</param>
        /// <returns>A a structure representing the HD Path of the provided parameters.</returns>
        AddressIdentifier GetAddressIdentifier(string walletName, string accountName = null, int? addressType = null, int? addressIndex = null);

        /// <summary>
        /// Gets the addresses associated with an account.
        /// </summary>
        /// <param name="accountReference">The account to get the balances for.</param>
        /// <param name="addressType">The type of address (change = 1, external = 0).</param>
        /// <param name="count">The maximum number of addresses to return.</param>
        /// <returns>The addresses associated with an account.</returns>
        IEnumerable<HdAddress> GetAccountAddresses(WalletAccountReference accountReference, int addressType, int count);

        /// <summary>
        /// Gets the payment details of a transaction.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="transactionData">The transaction to get the payment details for.</param>
        /// <param name="isChange">Whether to get payment or change details.</param>
        /// <returns>Returns the payment or change details.</returns>
        IEnumerable<PaymentDetails> GetPaymentDetails(string walletName, TransactionData transactionData, bool isChange);

        IEnumerable<PaymentDetails> GetPaymentDetails(string walletName, string transactionId);

        /// <summary>
        /// Adds watch-only addresses.
        /// </summary>
        /// <param name="walletName">Name of the wallet to add the addresses to.</param>
        /// <param name="accountName">The account to add the addresses to.</param>
        /// <param name="addressType">The type of the addresses being added (0=external, 1=change).</param>
        /// <param name="addresses">The addresses being added.</param>
        /// <param name="force">Adds the addresses even if this is not a watch-only wallet. The caller must ensure the addresses are valid.</param>
        void AddWatchOnlyAddresses(string walletName, string accountName, int addressType, List<HdAddress> addresses, bool force = false);

        /// <summary>
        /// Adds watch-only transactions data.
        /// </summary>
        /// <param name="walletName">Name of the wallet to add the transaction data for.</param>
        /// <param name="accountName">The account to add the transaction data for.</param>
        /// <param name="address">The address for which transaction data is being added.</param>
        /// <param name="transactions">The transaction data to add to the address.</param>
        /// <param name="force">Adds the transactions even if this is not a watch-only wallet. The caller must ensure the transactions are valid.</param>
        void AddWatchOnlyTransactions(string walletName, string accountName, HdAddress address, ICollection<TransactionData> transactions, bool force = false);

        /// <summary>
        /// Provides a default for the "force" flag when calling <see cref="AddWatchOnlyAddresses"/> or <see cref="AddWatchOnlyTransactions"/>.
        /// </summary>
        bool TestMode { get; set; }

        /// <summary>
        /// Returns the Transaction Count for the specified Wallet and Account
        /// </summary>
        /// <param name="walletName">The Wallet Name to Query</param>
        /// <param name="accountName">The Account Name to Query</param>
        /// <returns>The Transaction Count</returns>
        int GetTransactionCount(string walletName, string accountName = null);

        /// <summary>
        /// Returns transaction data and address based on a transaction id.
        /// </summary>
        /// <param name="walletName">The wallet to query</param>
        /// <param name="transactionId">The id of the transaction to find.</param>
        /// <returns>The requested transaction data as well as the address it relates to.</returns>
        IEnumerable<(HdAddress, IEnumerable<TransactionData>)> GetTransactionsById(string walletName, uint256 transactionId);

        Func<string, string> Bech32AddressFunc { get; set; }
    }
}
