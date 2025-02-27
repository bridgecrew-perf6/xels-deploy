﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using ConcurrentCollections;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.DataEncoders;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Events;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Utilities;
using Xels.Features.SQLiteWalletRepository.External;
using Xels.Features.SQLiteWalletRepository.Tables;
using Script = NBitcoin.Script;

[assembly: InternalsVisibleTo("Xels.Features.SQLiteWalletRepository.Tests")]

namespace Xels.Features.SQLiteWalletRepository
{
    /// <summary>
    /// Implements an SQLite wallet repository.
    /// </summary>
    /// <remarks>
    /// <para>This repository is basically implemented as a collection of public keys plus the transactions corresponding to those
    /// public keys. The only significant business logic being used is injected from external via the <see cref="IScriptAddressReader"/>
    /// or <see cref="IScriptDestinationReader" /> interfaces. Those interfaces bring <see cref="TxOut.ScriptPubKey" /> scripts into
    /// the world of raw public key hash (script) matching. The intention is that this will provide persistence for smart contract
    /// wallets, cold staking wallets, federation wallets and legacy wallets without any modifications to this code base.</para>
    /// <para>Federation wallets are further supported by the ability to provide a custom tx id to <see cref="ProcessTransaction" />
    /// (used only for unconfirmed transactions). In this case the custom tx id would be set to the deposit id when creating
    /// transient transactions via the <see cref="ProcessTransaction" /> call. It is expected that everything should then work
    /// as intended with confirmed transactions (via <see cref="ProcessBlock" />) taking precedence over non-confirmed transactions.</para>
    /// </remarks>
    public class SQLiteWalletRepository : IWalletRepository, IDisposable
    {
        public bool DatabasePerWallet { get; private set; }
        public bool WriteMetricsToFile { get; set; }
        public Network Network { get; private set; }

        /// <summary>Set this to true to allow adding addresses or transactions to non-watch-only wallets for testing purposes.</summary>
        public bool TestMode { get; set; }

        internal DataFolder DataFolder { get; private set; }
        internal IScriptAddressReader ScriptAddressReader { get; private set; }
        internal ConcurrentDictionary<string, WalletContainer> Wallets;
        internal string DBPath
        {
            get
            {
                return Path.Combine(this.DataFolder.WalletPath, "wallets");
            }
        }

        internal readonly ILogger logger;
        private readonly IDateTimeProvider dateTimeProvider;
        private ProcessBlocksInfo processBlocksInfo;
        private object lockObj;
        private readonly ISignals signals;
        internal const int MaxBatchDurationSeconds = 10;
        internal const int MaxDataRowsProcessed = 10000;

        // Metrics.
        internal Metrics Metrics;

        public Func<string, string> Bech32AddressFunc { get; set; } = null;

        public SQLiteWalletRepository(ILoggerFactory loggerFactory, DataFolder dataFolder, Network network, IDateTimeProvider dateTimeProvider, IScriptAddressReader scriptAddressReader, ISignals signals = null)
        {
            this.TestMode = false;
            this.Network = network;
            this.DataFolder = dataFolder;
            this.dateTimeProvider = dateTimeProvider;
            this.ScriptAddressReader = scriptAddressReader;
            this.WriteMetricsToFile = false;
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.lockObj = new object();
            this.signals = signals;

            Reset();
        }

        private void Reset()
        {
            this.Metrics = new Metrics(this.DBPath);
            this.Wallets = new ConcurrentDictionary<string, WalletContainer>();
            this.processBlocksInfo = null;
        }

        public void Dispose()
        {
        }

        private DBConnection GetConnection(string walletName = null)
        {
            if (this.DatabasePerWallet)
                Guard.NotNull(walletName, nameof(walletName));
            else if (this.processBlocksInfo != null)
            {
                this.logger.LogDebug("Re-using shared database connection");
                return this.processBlocksInfo.Conn;
            }

            if (walletName != null && this.Wallets.ContainsKey(walletName))
            {
                this.logger.LogDebug("Re-using existing database connection to wallet '{0}'", walletName);
                return this.Wallets[walletName].Conn;
            }

            if (this.DatabasePerWallet)
                this.logger.LogDebug("Creating database connection to wallet database '{0}.db'", walletName);
            else
                this.logger.LogDebug("Creating database connection to shared database `Wallet.db`");

            var conn = new DBConnection(this, this.DatabasePerWallet ? $"{walletName}.db" : "Wallet.db", this.dateTimeProvider);

            this.logger.LogDebug("Creating database structure.");

            conn.Execute("PRAGMA temp_store = MEMORY");
            conn.Execute("PRAGMA cache_size = 100000");

            conn.BeginTransaction();
            conn.CreateDBStructure();
            conn.Commit();

            return conn;
        }

        /// <inheritdoc />
        public void Initialize(bool databasePerWallet = true)
        {
            Reset();

            Directory.CreateDirectory(this.DBPath);

            this.DatabasePerWallet = databasePerWallet;

            this.logger.LogInformation("Initializing wallets at location '{0}', '{1}'={2}.", this.DBPath, nameof(this.DatabasePerWallet), this.DatabasePerWallet);

            if (this.DatabasePerWallet)
            {
                var walletNames = Directory.EnumerateFiles(this.DBPath, "*.db").Select(p => p.Substring(this.DBPath.Length + 1).Split('.')[0]);
                this.logger.LogInformation("Found {0} HD Wallets at location '{1}'.", walletNames.Count(), this.DBPath);

                foreach (string walletName in walletNames)
                {
                    var connection = GetConnection(walletName);
                    HDWallet wallet = connection.GetWalletByName(walletName);
                    AddWalletToContainer(connection, wallet, new ProcessBlocksInfo(connection, null, wallet));
                }
            }
            else
            {
                var connection = GetConnection();

                this.processBlocksInfo = new ProcessBlocksInfo(connection, null);

                var hdWallets = HDWallet.GetAll(connection);
                this.logger.LogInformation("Found {0} HD Wallets.", hdWallets.Count());

                foreach (HDWallet wallet in hdWallets)
                {
                    AddWalletToContainer(connection, wallet, this.processBlocksInfo);
                }
            }
        }

        private void AddWalletToContainer(DBConnection conn, HDWallet wallet, ProcessBlocksInfo processBlocksInfo)
        {
            var walletContainer = new WalletContainer(conn, wallet, processBlocksInfo);

            // Remove all unconfirmed transactions.
            conn.BeginTransaction();
            try
            {
                this.logger.LogDebug("Removing all unconfirmed transactions from wallet '{0}'.", wallet.Name);
                conn.RemoveAllUnconfirmedTransactions(wallet.WalletId);
                conn.Commit();
            }
            catch (Exception ex)
            {
                this.logger.LogError("An exception occurred while removing unconfirmed transactions for wallet '{0}'.", wallet.Name);
                this.logger.LogError(ex.ToString());
                conn.Rollback();
                throw;
            }

            walletContainer.AddressesOfInterest.AddAll(wallet.WalletId);
            walletContainer.TransactionsOfInterest.AddSpendableTransactions(wallet.WalletId);
            this.Wallets[wallet.Name] = walletContainer;
            this.logger.LogDebug("Wallet '{0}' added to collection.", wallet.Name);
        }

        /// <inheritdoc />
        public void Shutdown()
        {
            if (this.DatabasePerWallet)
            {
                foreach (string walletName in Directory.EnumerateFiles(this.DBPath, "*.db")
                    .Select(p => p.Substring(this.DBPath.Length + 1).Split('.')[0]))
                {
                    DBConnection conn = GetConnection(walletName);
                    if (conn != null)
                        conn.SQLiteConnection.Dispose();
                }
            }
            else
            {
                DBConnection conn = this.processBlocksInfo?.Conn;
                if (conn != null)
                    conn.SQLiteConnection.Dispose();
            }
        }

        /// <inheritdoc />
        public List<string> GetWalletNames()
        {
            var wallets = new List<string>();
            foreach (var wallet in this.Wallets)
            {
                wallets.Add(wallet.Key);
            }

            return wallets;
        }

        /// <inheritdoc />
        public Wallet GetWallet(string walletName)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            var res = new Wallet(this)
            {
                Name = walletName,
                EncryptedSeed = wallet.EncryptedSeed,
                ChainCode = (wallet.ChainCode == null) ? null : Convert.FromBase64String(wallet.ChainCode),
                BlockLocator = wallet.BlockLocator.Split(',').Where(s => !string.IsNullOrEmpty(s)).Select(strHash => uint256.Parse(strHash)).ToList(),
                CreationTime = DateTimeOffset.FromUnixTimeSeconds(wallet.CreationTime)
            };

            res.AccountsRoot = new List<AccountRoot>();
            res.AccountsRoot.Add(new AccountRoot(res)
            {
                LastBlockSyncedHeight = wallet.LastBlockSyncedHeight,
                LastBlockSyncedHash = (wallet.LastBlockSyncedHash == null) ? null : uint256.Parse(wallet.LastBlockSyncedHash),
                CoinType = (CoinType)this.Network.Consensus.CoinType
            });

            return res;
        }

        /// <inheritdoc />
        public ChainedHeader FindFork(string walletName, ChainedHeader chainTip)
        {
            return this.GetWalletContainer(walletName).Wallet.GetFork(chainTip);
        }

        /// <inheritdoc />
        public (bool RewindExecuted, IEnumerable<(uint256, DateTimeOffset)> RemovedTransactions) RewindWallet(string walletName, ChainedHeader lastBlockSynced)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);

            HDWallet wallet = walletContainer.Wallet;

            // If nothing to do then exit. Can't rewind any further.
            if (wallet.LastBlockSyncedHeight < 0)
                return (false, new List<(uint256, DateTimeOffset)>());

            // If nothing to do then exit. Tips match.
            if (lastBlockSynced?.HashBlock?.ToString() == wallet.LastBlockSyncedHash &&
                lastBlockSynced?.Height == wallet.LastBlockSyncedHeight)
                return (false, new List<(uint256, DateTimeOffset)>());

            // If the rewind location is not conceivably "within" the wallet then "rewinding" may actually advance the wallet
            // and lead to integrity issues. Prevent that from happening.
            if (!wallet.WalletContainsBlock(lastBlockSynced))
                return (false, new List<(uint256, DateTimeOffset)>());

            // Ok seems safe. Adjust the tip and rewind relevant transactions.
            walletContainer.WriteLockWait();

            bool notifyWalletUI = false;

            DBConnection conn = walletContainer.Conn;
            try
            {
                conn.BeginTransaction();

                // Add the UTXOs being freed up. If rewinding to start there will be nothing to add.
                if (lastBlockSynced != null)
                    walletContainer.TransactionsOfInterest.AddSpendableTransactions(wallet.WalletId, fromBlock: lastBlockSynced.Height);

                IEnumerable<(string txId, long creationTime)> res = conn.SetLastBlockSynced(wallet, lastBlockSynced).ToList();
                conn.Commit();

                if (lastBlockSynced == null)
                    this.logger.LogDebug("Wallet '{0}' rewound to start.", walletName);
                else
                    this.logger.LogDebug("Wallet '{0}' rewound to height '{1}'.", walletName, lastBlockSynced);

                notifyWalletUI = true;

                return (true, res.Select(i => (uint256.Parse(i.txId), DateTimeOffset.FromUnixTimeSeconds(i.creationTime))).ToList());
            }
            catch (Exception ex)
            {
                if (lastBlockSynced == null)
                    this.logger.LogError("An error occurred rewinding wallet '{0}' to start.", walletName);
                else
                    this.logger.LogError("An error occurred rewinding wallet '{0}' to '{1}'.", walletName, lastBlockSynced);

                this.logger.LogError(ex.ToString());

                conn.Rollback();

                throw;
            }
            finally
            {
                walletContainer.WriteLockRelease();

                if (notifyWalletUI)
                {
                    // It is possible that a re-org occurred of a block that the wallet just had an interest in
                    // (e.g. it staked a block and subsequently re-org).
                    // We therefore also need to notify the UI that it needs to get the balance and history again.
                    this.signals?.Publish(new WalletProcessedTransactionOfInterestEvent());
                }
            }
        }

        public Wallet CreateWallet(string walletName, string encryptedSeed = null, byte[] chainCode = null, HashHeightPair lastBlockSynced = null, BlockLocator blockLocator = null, long? creationTime = null)
        {
            this.logger.LogDebug("Creating wallet '{0}'.", walletName);

            lock (this.Wallets)
            {
                if (this.Wallets.Any(w => w.Value.Wallet?.Name == walletName))
                    throw new WalletException($"Wallet with name '{walletName}' already exists.");

                if (encryptedSeed != null)
                    if (this.Wallets.Any(w => w.Value.Wallet?.EncryptedSeed == encryptedSeed))
                        throw new WalletException("Cannot create this wallet as a wallet with the same private key already exists.");

                // If in this.BeginTransaction scope then a locked container will already exist (which will unlock external to this code).
                bool iCreatedContainer = !this.Wallets.TryGetValue(walletName, out WalletContainer walletContainer);
                if (iCreatedContainer)
                {
                    if (this.DatabasePerWallet)
                        walletContainer = new WalletContainer(this.GetConnection(walletName), null);
                    else
                        walletContainer = new WalletContainer(this.GetConnection(walletName), null, this.processBlocksInfo);

                    // No need to lock the container. We will add it to the collection after a successful commit.
                    iCreatedContainer = true;
                }

                DBConnection conn = walletContainer.Conn;

                conn.BeginTransaction();

                try
                {
                    var wallet = new HDWallet()
                    {
                        Name = walletName,
                        EncryptedSeed = encryptedSeed,
                        ChainCode = (chainCode == null) ? null : Convert.ToBase64String(chainCode),
                        CreationTime = creationTime ?? (int)this.Network.GenesisTime
                    };

                    wallet.SetLastBlockSynced(lastBlockSynced, blockLocator, this.Network);

                    wallet.CreateWallet(conn);

                    conn.Commit();

                    this.logger.LogDebug("Adding wallet '{0}' to wallet collection.", walletName);

                    walletContainer.Wallet = wallet;
                    this.Wallets[wallet.Name] = walletContainer;
                }
                catch (Exception)
                {
                    conn.Rollback();

                    if (iCreatedContainer)
                    {
                        if (this.DatabasePerWallet)
                        {
                            walletContainer.Conn.SQLiteConnection.Dispose();
                            File.Delete(Path.Combine(this.DBPath, $"{walletName}.db"));
                        }
                    }

                    throw;
                }
            }

            return GetWallet(walletName);
        }

        /// <inheritdoc />
        public bool DeleteWallet(string walletName)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            walletContainer.WriteLockWait();

            try
            {
                this.logger.LogDebug("Deleting wallet '{0}'.", walletName);

                bool isInTransaction = conn.IsInTransaction;

                if (!this.DatabasePerWallet)
                {
                    int walletId = walletContainer.Wallet.WalletId;
                    conn.BeginTransaction();

                    this.RewindWallet(walletName, null);

                    conn.Delete<HDWallet>(walletId);

                    conn.Execute($@"
                        DELETE  FROM HDAddress
                        WHERE   WalletId = ?",
                        walletId);

                    conn.Execute($@"
                        DELETE  FROM HDAccount
                        WHERE   WalletId = ?",
                        walletId);

                    conn.Commit();
                }
                else
                {
                    conn.Close();

                    if (isInTransaction)
                        File.Move(Path.Combine(this.DBPath, $"{walletName}.db"), Path.Combine(this.DBPath, $"{walletName}.bak"));
                    else
                        File.Delete(Path.Combine(this.DBPath, $"{walletName}.db"));
                }

                if (isInTransaction)
                {
                    conn.AddRollbackAction(new
                    {
                        walletContainer = this.Wallets[walletName]
                    }, (dynamic rollBackData) =>
                    {
                        string name = rollBackData.walletContainer.Wallet.Name;

                        this.Wallets[name] = rollBackData.walletContainer;

                        if (this.DatabasePerWallet)
                        {
                            File.Move(Path.Combine(this.DBPath, $"{name}.bak"), Path.Combine(this.DBPath, $"{name}.db"));
                            walletContainer.Conn = this.GetConnection(name);
                        }
                    });

                    conn.AddCommitAction(new
                    {
                        walletContainer = this.Wallets[walletName]
                    }, (dynamic rollBackData) =>
                    {
                        if (this.DatabasePerWallet)
                        {
                            string name = rollBackData.walletContainer.Wallet.Name;
                            File.Delete(Path.Combine(this.DBPath, $"{name}.bak"));
                        }
                    });
                }

                return this.Wallets.TryRemove(walletName, out _); ;
            }
            finally
            {
                walletContainer.WriteLockRelease();
            }
        }

        /// <inheritdoc />
        public HdAccount CreateAccount(string walletName, int accountIndex, string accountName, ExtPubKey extPubKey, DateTimeOffset? creationTime = null, (int external, int change)? addressCounts = null)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            walletContainer.WriteLockWait();

            try
            {
                conn.BeginTransaction();

                IEnumerable<HDAccount> accounts = conn.GetAccounts(wallet.WalletId);

                if (accounts.Any(a => a.ExtPubKey != null && ExtPubKey.Parse(a.ExtPubKey) == extPubKey))
                    throw new WalletException($"There is already an account in this wallet with this xpubkey: " + extPubKey.ToString(this.Network));

                if (accounts.Any(a => a.AccountIndex == accountIndex))
                    throw new WalletException($"There is already an account in this wallet with index: { accountIndex }");

                var account = conn.CreateAccount(wallet.WalletId, accountIndex, accountName, extPubKey?.ToString(this.Network), (creationTime ?? this.dateTimeProvider.GetTimeOffset()).ToUnixTimeSeconds());

                // Add the standard number of addresses if this is not a watch-only wallet.
                if (extPubKey != null)
                {
                    conn.CreateAddresses(account, HDAddress.Internal, addressCounts?.change ?? 20);
                    conn.CreateAddresses(account, HDAddress.External, addressCounts?.external ?? 20);
                }

                conn.Commit();

                walletContainer.AddressesOfInterest.AddAll(wallet.WalletId, accountIndex);

                return this.ToHdAccount(account);
            }
            catch (Exception)
            {
                conn.Rollback();
                throw;
            }
            finally
            {
                walletContainer.WriteLockRelease();
            }
        }

        internal HDAddress CreateAddress(HDAccount account, int addressType, int addressIndex, PubKey pubKey = null)
        {
            if (account.ExtPubKey != null)
            {
                if (pubKey != null && !this.TestMode)
                    throw new NotSupportedException($"The '{nameof(pubKey)} argument is only supported with a watchonly accounts.");

                // Retrieve the pubkey associated with the private key of this address index.
                var keyPath = new KeyPath($"{addressType}/{addressIndex}");
                ExtPubKey extPubKey = account.GetExtPubKey(this.Network).Derive(keyPath);
                pubKey = extPubKey.PubKey;
            }
            else
            {
                if (pubKey == null)
                    throw new NotSupportedException($"The '{nameof(pubKey)} argument is required for watchonly accounts.");
            }

            Script pubKeyScript = pubKey.ScriptPubKey;
            Script scriptPubKey = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(pubKey);
            Script bech32ScriptPubKey = PayToWitPubKeyHashTemplate.Instance.GenerateScriptPubKey(pubKey);

            // Add the new address details to the list of addresses.
            return new HDAddress()
            {
                WalletId = account.WalletId,
                AccountIndex = account.AccountIndex,
                AddressType = addressType,
                AddressIndex = addressIndex,
                PubKey = pubKeyScript?.ToHex(),
                ScriptPubKey = scriptPubKey?.ToHex(),
                Address = scriptPubKey?.GetDestinationAddress(this.Network).ToString() ?? "",
                Bech32Address = bech32ScriptPubKey?.GetDestinationAddress(this.Network).ToString() ?? ""
            };
        }

        /// <inheritdoc />
        public void AddWatchOnlyTransactions(string walletName, string accountName, HdAddress address, ICollection<TransactionData> transactions, bool force = false)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            DBConnection conn = walletContainer.Conn;

            walletContainer.WriteLockWait();
            try
            {
                conn.BeginTransaction();

                HDAccount account = conn.GetAccountByName(walletName, accountName);
                if (!force && !this.TestMode && account.ExtPubKey != null)
                    throw new Exception("Transactions can only be added to watch-only addresses.");

                conn.AddTransactions(account, address, transactions);
                conn.Commit();

                walletContainer.TransactionsOfInterest.AddSpendableTransactions(account.WalletId, account.AccountIndex);
            }
            catch (Exception)
            {
                conn.Rollback();

                throw;
            }
            finally
            {
                walletContainer.WriteLockRelease();
            }
        }

        /// <inheritdoc />
        public void AddWatchOnlyAddresses(string walletName, string accountName, int addressType, List<HdAddress> addresses, bool force = false)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            DBConnection conn = walletContainer.Conn;

            walletContainer.WriteLockWait();
            conn.BeginTransaction();
            try
            {
                HDAccount account = conn.GetAccountByName(walletName, accountName);
                if (!force && !this.TestMode && account.ExtPubKey != null)
                    throw new Exception("Addresses can only be added to watch-only accounts.");

                conn.CreateWatchOnlyAddresses(new WalletAccountReference(walletName, accountName), account, addressType, addresses, force);
                conn.Commit();

                walletContainer.AddressesOfInterest.AddAll(account.WalletId, account.AccountIndex);
                walletContainer.WriteLockRelease();
            }
            catch (Exception)
            {
                walletContainer.WriteLockRelease();
                conn.Rollback();
                throw;
            }
        }

        /// <inheritdoc />
        public IEnumerable<HdAccount> GetAccounts(Wallet hdWallet, string accountName = null)
        {
            WalletContainer walletContainer = this.GetWalletContainer(hdWallet.Name);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            foreach (HDAccount account in conn.GetAccounts(wallet.WalletId, accountName))
            {
                var res = this.ToHdAccount(account);
                res.WalletAccounts = hdWallet.AccountsRoot.First().Accounts;

                yield return res;
            }
        }

        private WalletContainer GetWalletContainer(string walletName, bool throwError = true)
        {
            if (!this.Wallets.TryGetValue(walletName, out WalletContainer walletContainer) && throwError)
                throw new WalletException($"No wallet with name '{walletName}' could be found.");

            return walletContainer;
        }

        private HDAddress CreateAddress(AddressIdentifier addressId)
        {
            var pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(Script.FromHex(addressId.PubKeyScript));
            Script witScriptPubKey = PayToWitPubKeyHashTemplate.Instance.GenerateScriptPubKey(pubKey);

            return new HDAddress()
            {
                WalletId = addressId.WalletId,
                AccountIndex = (int)addressId.AccountIndex,
                AddressType = (int)addressId.AddressType,
                AddressIndex = (int)addressId.AddressIndex,
                PubKey = addressId.PubKeyScript,
                ScriptPubKey = addressId.ScriptPubKey,
                Address = Script.FromHex(addressId.ScriptPubKey).GetDestinationAddress(this.Network).ToString(),
                Bech32Address = witScriptPubKey.GetDestinationAddress(this.Network).ToString()
            };
        }

        /// <inheritdoc />
        public IEnumerable<HdAddress> GetUnusedAddresses(WalletAccountReference accountReference, int count, bool isChange = false)
        {
            WalletContainer walletContainer = GetWalletContainer(accountReference.WalletName);

            DBConnection conn = walletContainer.Conn;

            walletContainer.WriteLockWait();

            try
            {
                HDAccount account = conn.GetAccountByName(accountReference.WalletName, accountReference.AccountName);

                if (account == null)
                    throw new WalletException($"Account '{accountReference.AccountName}' of wallet '{accountReference.WalletName}' does not exist.");

                List<HDAddress> addresses = conn.GetUnusedAddresses(account.WalletId, account.AccountIndex, isChange ? 1 : 0, count).ToList();
                if (addresses.Count < count)
                {
                    conn.BeginTransaction();
                    try
                    {
                        var tracker = new TopUpTracker(conn, account.WalletId, account.AccountIndex, isChange ? 1 : 0);
                        tracker.ReadAccount();

                        while (addresses.Count < count)
                        {
                            AddressIdentifier addressIdentifier = tracker.CreateAddress();
                            HDAddress address = this.CreateAddress(account, (int)addressIdentifier.AddressType, (int)addressIdentifier.AddressIndex);
                            conn.Insert(address);
                            addresses.Add(address);
                        }

                        walletContainer.AddressesOfInterest.AddAll(account.WalletId, account.AccountIndex, isChange ? 1 : 0);

                        conn.Commit();
                    }
                    catch (Exception)
                    {
                        conn.Rollback();
                        throw;
                    }
                }

                return addresses.Select(a => this.ToHdAddress(a, this.Network));
            }
            finally
            {
                walletContainer.WriteLockRelease();
            }
        }

        public IEnumerable<HdAddress> GetNewAddresses(WalletAccountReference accountReference, int count, bool isChange = false)
        {
            WalletContainer walletContainer = GetWalletContainer(accountReference.WalletName);

            DBConnection conn = walletContainer.Conn;

            const int minAddressesPerSecond = 20;

            walletContainer.WriteLockWait(true, 120 + count / minAddressesPerSecond);

            try
            {
                HDAccount account = conn.GetAccountByName(accountReference.WalletName, accountReference.AccountName);

                if (account == null)
                    throw new WalletException($"Account '{accountReference.AccountName}' of wallet '{accountReference.WalletName}' does not exist.");

                var addresses = new List<HDAddress>();

                conn.BeginTransaction();
                try
                {
                    var tracker = new TopUpTracker(conn, account.WalletId, account.AccountIndex, isChange ? 1 : 0);
                    tracker.ReadAccount();

                    for (int i = 0; i < count; i++)
                    {
                        AddressIdentifier addressIdentifier = tracker.CreateAddress();
                        HDAddress address = this.CreateAddress(account, (int)addressIdentifier.AddressType, (int)addressIdentifier.AddressIndex);
                        conn.Insert(address);
                        addresses.Add(address);
                    }

                    walletContainer.AddressesOfInterest.AddAll(account.WalletId, account.AccountIndex, isChange ? 1 : 0);

                    conn.Commit();
                }
                catch (Exception)
                {
                    conn.Rollback();
                    throw;
                }

                return addresses.Select(a => this.ToHdAddress(a, this.Network));
            }
            finally
            {
                walletContainer.WriteLockRelease();
            }
        }

        /// <inheritdoc />
        public IEnumerable<(HdAddress address, Money confirmed, Money total)> GetUsedAddresses(WalletAccountReference accountReference, bool isChange = false)
        {
            WalletContainer walletContainer = this.GetWalletContainer(accountReference.WalletName);
            DBConnection conn = walletContainer.Conn;
            HDAccount account = conn.GetAccountByName(accountReference.WalletName, accountReference.AccountName);

            if (account == null)
                throw new WalletException($"No account with the name '{accountReference.AccountName}' could be found.");

            return conn.GetUsedAddresses(account.WalletId, account.AccountIndex, isChange ? 1 : 0, int.MaxValue).Select(a =>
                (this.ToHdAddress(a, this.Network), new Money(a.ConfirmedAmount), new Money(a.TotalAmount)));
        }

        /// <inheritdoc />
        public IEnumerable<HdAddress> GetUnusedAddresses(WalletAccountReference accountReference, bool isChange = false)
        {
            WalletContainer walletContainer = this.GetWalletContainer(accountReference.WalletName);
            DBConnection conn = walletContainer.Conn;
            HDAccount account = conn.GetAccountByName(accountReference.WalletName, accountReference.AccountName);

            if (account == null)
                throw new WalletException($"No account with the name '{accountReference.AccountName}' could be found.");

            return conn.GetUnusedAddresses(account.WalletId, account.AccountIndex, isChange ? 1 : 0, int.MaxValue).Select(a => this.ToHdAddress(a, this.Network));
        }

        /// <inheritdoc />
        public IEnumerable<HdAddress> GetAccountAddresses(WalletAccountReference accountReference, int addressType, int count)
        {
            WalletContainer walletContainer = this.GetWalletContainer(accountReference.WalletName);
            DBConnection conn = walletContainer.Conn;

            int walletId = this.GetWalletId(accountReference.WalletName);

            HDAccount account = conn.GetAccountByName(accountReference.WalletName, accountReference.AccountName);

            foreach (HDAddress address in HDAddress.GetAccountAddresses(conn, walletId, account.AccountIndex, addressType, count))
            {
                yield return this.ToHdAddress(address, this.Network);
            }
        }

        /// <inheritdoc />
        public ITransactionContext BeginTransaction(string walletName)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName, false);

            if (walletContainer == null)
            {
                if (this.DatabasePerWallet)
                    walletContainer = new WalletContainer(this.GetConnection(walletName), null);
                else
                    walletContainer = new WalletContainer(this.GetConnection(walletName), null, this.processBlocksInfo);

                walletContainer.WriteLockWait();

                this.Wallets[walletName] = walletContainer;
            }
            else
            {
                walletContainer.WriteLockWait();
            }

            return new TransactionContext(walletContainer);
        }

        /// <inheritdoc />
        public void ProcessBlock(Block block, ChainedHeader header, string walletName = null)
        {
            ProcessBlocks(new[] { (header, block) }, walletName);
        }

        /// <inheritdoc />
        public void ProcessBlocks(IEnumerable<(ChainedHeader header, Block block)> blocks, string walletName = null)
        {
            if (this.Wallets.Count == 0 || !blocks.Any())
                return;

            if (this.DatabasePerWallet && walletName == null)
            {
                List<WalletContainer> rounds = this.Wallets.Values.Where(round => this.StartBatch(round, blocks.First().header)).ToList();

                if (rounds.Count > 0)
                {
                    foreach ((ChainedHeader chainedHeader, Block block) in blocks.Append((null, null)))
                    {
                        bool done = false;

                        this.logger.LogDebug("[WALLET_NAME_NULL]:Processing '{0}'.", chainedHeader);

                        Parallel.ForEach(rounds, round =>
                        {
                            if (!ParallelProcessBlock(round, block, chainedHeader, out bool _))
                                done = true;
                        });

                        if (done)
                            break;
                    }
                }
            }
            else
            {
                ProcessBlocksInfo round = (walletName != null) ? this.Wallets[walletName] : this.processBlocksInfo;

                if (this.StartBatch(round, blocks.First().header))
                {
                    bool signalUI = false;

                    foreach ((ChainedHeader chainedHeader, Block block) in blocks.Append((null, null)))
                    {
                        this.logger.LogDebug("Processing '{0}'.", chainedHeader);

                        if (!ParallelProcessBlock(round, block, chainedHeader, out bool transactionOfInterestProcessed))
                            break;

                        if (transactionOfInterestProcessed)
                            signalUI = true;
                    }

                    if (signalUI)
                    {
                        // We only want to raise events for the UI (via SignalR) to query the wallet balance if a transaction pertaining to the wallet 
                        // was processed.
                        this.signals?.Publish(new WalletProcessedTransactionOfInterestEvent() { Source = "processblock" });
                    }
                }
            }
        }

        private bool ParallelProcessBlock(ProcessBlocksInfo round, Block block, ChainedHeader chainedHeader, out bool transactionOfInterestProcessed)
        {
            transactionOfInterestProcessed = false;

            try
            {
                HDWallet wallet = round.Wallet;
                DBConnection conn = round.Conn;

                if (round.NewTip != null || chainedHeader == null)
                {
                    // Flush when new wallets are joining. This ensures that PrevTip will match all wallets requiring updating and advancing.
                    bool walletsJoining;
                    string lastBlockSyncedHash = (chainedHeader == null) ? null : (chainedHeader.Previous?.HashBlock ?? (uint256)0).ToString();
                    if (round.Wallet == null && !this.DatabasePerWallet)
                        walletsJoining = this.Wallets.Any(c => c.Value.Wallet.LastBlockSyncedHash == lastBlockSyncedHash);
                    else
                        walletsJoining = round.Wallet.LastBlockSyncedHash == lastBlockSyncedHash;

                    // See if other threads are waiting to update any of the wallets.
                    bool threadsWaiting = round.LockProcessBlocks.WaitingThreads != 0 && round.ParticipatingWallets.Any(name => this.Wallets[name].HaveWaitingThreads);
                    if (threadsWaiting || ((round.Outputs.Count + round.PrevOuts.Count) >= MaxDataRowsProcessed) || chainedHeader == null || walletsJoining || DateTime.Now.Ticks >= round.BatchDeadline)
                    {
                        if (chainedHeader == null)
                            this.logger.LogDebug("Ending batch due to end-of-data.");
                        else if (walletsJoining)
                            this.logger.LogDebug("Ending batch due to other wallets joining.");
                        else if (threadsWaiting)
                            this.logger.LogDebug("Ending batch due to other threads waiting to update a wallet.");
                        else if ((round.Outputs.Count + round.PrevOuts.Count) >= MaxDataRowsProcessed)
                            this.logger.LogDebug("Ending batch due to memory restrictions.");
                        else if (DateTime.Now.Ticks >= round.BatchDeadline)
                            this.logger.LogDebug("Ending batch due to time constraint.");

                        if (round.NewTip != null)
                        {
                            long flagFall = DateTime.Now.Ticks;

                            conn.BeginTransaction();
                            try
                            {
                                if (round.Outputs.Count != 0 || round.PrevOuts.Count != 0)
                                {
                                    this.logger.LogDebug("Batch-commit transactions extracted from blocks {0} to {1} to wallet repository.",
                                        (round.PrevTip?.Height ?? -1) + 1, round.NewTip.Height);

                                    IEnumerable<IEnumerable<string>> blockToScript = (new[] { round.Outputs, round.PrevOuts }).Select(list => list.CreateScript());

                                    // Ensure that any new addresses are present in the database before accessing the HDAddress table.
                                    foreach (AddressIdentifier addressIdentifier in round.AddressesOfInterest.GetTentative())
                                    {
                                        Guard.Assert(!string.IsNullOrEmpty(addressIdentifier.ScriptPubKey));
                                        Guard.Assert(!string.IsNullOrEmpty(addressIdentifier.PubKeyScript));

                                        conn.InsertOrReplace(this.CreateAddress(addressIdentifier));
                                    }

                                    conn.ProcessTransactions(blockToScript, wallet, round.NewTip, round.PrevTip?.Hash ?? 0);

                                    round.Outputs.Clear();
                                    round.PrevOuts.Clear();

                                    round.AddressesOfInterest.Confirm();
                                    round.TransactionsOfInterest.Confirm();

                                    this.logger.LogDebug("Batch-commit done.");
                                }
                                else
                                {
                                    this.logger.LogDebug("No transactions found in blocks {0} to {1}. Just advancing wallet tips.",
                                        (round.PrevTip?.Height ?? -1) + 1, round.NewTip.Height);

                                    HDWallet.AdvanceTip(conn, wallet, round.NewTip, round.PrevTip?.Hash ?? 0);
                                }

                                long flagFall3 = DateTime.Now.Ticks;
                                conn.Commit();
                                this.Metrics.CommitTime += (DateTime.Now.Ticks - flagFall3);
                            }
                            catch (Exception ex)
                            {
                                this.logger.LogError("An exception occurred processing block '{0}'.", chainedHeader);
                                this.logger.LogError(ex.ToString());

                                conn.Rollback();

                                throw;
                            }
                            finally
                            {
                                // Ensure locks are released.
                                this.EndBatch(round);

                                this.Metrics.ProcessTime += (DateTime.Now.Ticks - flagFall);
                                this.Metrics.LogMetrics(this, conn, chainedHeader, wallet);
                            }
                        }
                        else
                        {
                            this.EndBatch(round);
                        }

                        if (chainedHeader == null)
                            return false;
                    }
                }

                if (round.PrevTip == null)
                {
                    if (!this.StartBatch(round, chainedHeader))
                        return false;
                }

                if (block != null)
                {
                    this.logger.LogDebug("Scanning block '{0}' for transactions.", chainedHeader);

                    // Maintain metrics.
                    long flagFall2 = DateTime.Now.Ticks;
                    this.Metrics.BlockCount++;

                    // Determine the scripts for creating temporary tables and inserting the block's information into them.
                    ITransactionsToLists transactionsToLists = new TransactionsToLists(this.Network, this.ScriptAddressReader, round, this.dateTimeProvider);
                    if (transactionsToLists.ProcessTransactions(block.Transactions, new HashHeightPair(chainedHeader), blockTime: block.Header.BlockTime.ToUnixTimeSeconds()))
                    {
                        transactionOfInterestProcessed = true;
                        this.Metrics.ProcessCount++;
                    }

                    this.Metrics.BlockTime += (DateTime.Now.Ticks - flagFall2);

                    this.logger.LogDebug("Scanning done.");
                }

                round.NewTip = chainedHeader;
            }
            catch (Exception ex)
            {
                this.logger.LogError("An exception occurred processing block '{0}'.", chainedHeader);
                this.logger.LogError(ex.ToString());

                throw;
            }

            return true;
        }

        /// <summary>
        /// Start processing a batch of blocks.
        /// </summary>
        /// <param name="round">The processing context of a wallet or group of wallets.</param>
        /// <param name="header">The first block being processed. This is matched to the wallet tips to select participating wallets.</param>
        /// <returns>Returns <c>true</c> if the batch can be started.</returns>
        private bool StartBatch(ProcessBlocksInfo round, ChainedHeader header)
        {
            lock (this.lockObj)
            {
                if (!round.LockProcessBlocks.Wait(false))
                {
                    this.logger.LogDebug("Exiting due to already processing a transaction or blocks.");
                    return false;
                }

                try
                {
                    // Determine participating wallets.
                    string lastBlockSyncedHash = (header == null) ? null : (header.Previous?.HashBlock ?? (uint256)0).ToString();
                    if (round.Wallet == null && !this.DatabasePerWallet)
                        round.ParticipatingWallets = new ConcurrentHashSet<string>(this.Wallets.Values.Where(c => c.Wallet.LastBlockSyncedHash == lastBlockSyncedHash).Select(c => c.Wallet.Name));
                    else if (round.Wallet.LastBlockSyncedHash == lastBlockSyncedHash)
                        round.ParticipatingWallets = new ConcurrentHashSet<string>() { round.Wallet.Name };
                    else
                    {
                        this.logger.LogDebug("Exiting due to no wallet tips matching next block to process.");
                        round.LockProcessBlocks.Release();
                        return false;
                    }

                    // See if all the wallet locks can be obtained, otherwise do nothing.
                    this.logger.LogDebug("Obtaining locks for {0} wallets.", round.ParticipatingWallets.Count);

                    bool failed = false;
                    Parallel.ForEach(round.ParticipatingWallets, walletName =>
                    {
                        WalletContainer walletContainer = this.Wallets[walletName];

                        if (walletContainer.WriteLockWait(false))
                            return;

                        this.logger.LogDebug("Could not obtain lock for wallet '{0}'.", walletName);

                        failed = true;

                        Guard.Assert(round.ParticipatingWallets.TryRemove(walletName));
                    });

                    if (failed)
                    {
                        this.logger.LogDebug("Releasing locks and postponing until next sync event.");
                        Parallel.ForEach(round.ParticipatingWallets, walletName => this.Wallets[walletName].WriteLockRelease());
                        round.LockProcessBlocks.Release();
                        return false;
                    }

                    // Initialize round.
                    round.PrevTip = (header.Previous == null) ? new HashHeightPair(0, -1) : new HashHeightPair(header.Previous);
                    round.NewTip = null;
                    round.Trackers = new Dictionary<TopUpTracker, TopUpTracker>();
                    round.BatchDeadline = DateTime.Now.AddSeconds(MaxBatchDurationSeconds).Ticks;

                    return true;
                }
                catch (Exception ex)
                {
                    round.LockProcessBlocks.Release();
                    this.logger.LogError(ex, "An exception occurred starting batch.");
                    throw;
                }
            }
        }

        /// <summary>
        /// Ends the processing of a batch of blocks.
        /// </summary>
        /// <param name="round">The processing context of a wallet or group of wallets.</param>
        private void EndBatch(ProcessBlocksInfo round)
        {
            lock (this.lockObj)
            {
                this.logger.LogDebug("Ending processing of a batch of blocks.");

                try
                {
                    round.PrevTip = null;

                    // Update all wallets found in the DB into the containers.
                    this.logger.LogDebug("Refreshing in-memory wallet information.");
                    foreach (HDWallet updatedWallet in HDWallet.GetAll(round.Conn))
                    {
                        if (!this.Wallets.TryGetValue(updatedWallet.Name, out WalletContainer walletContainer))
                            continue;

                        walletContainer.Wallet.LastBlockSyncedHash = updatedWallet.LastBlockSyncedHash;
                        walletContainer.Wallet.LastBlockSyncedHeight = updatedWallet.LastBlockSyncedHeight;
                        walletContainer.Wallet.BlockLocator = updatedWallet.BlockLocator;
                    }
                }
                finally
                {
                    // Release all locks.
                    this.logger.LogDebug("Releasing all wallet locks.");
                    foreach (string walletName in round.ParticipatingWallets)
                        this.Wallets[walletName].WriteLockRelease();

                    round.ParticipatingWallets.Clear();
                    round.LockProcessBlocks.Release();
                }
            }
        }

        /// <inheritdoc />
        public DateTimeOffset? RemoveUnconfirmedTransaction(string walletName, uint256 transactionId)
        {
            this.logger.LogDebug("Removing unconfirmed transaction '{0}' from wallet '{1}'.", transactionId, walletName);

            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            walletContainer.WriteLockWait();

            try
            {
                conn.BeginTransaction();

                long? unixTimeSeconds = conn.RemoveUnconfirmedTransaction(wallet.WalletId, transactionId);
                conn.Commit();

                return (unixTimeSeconds == null) ? (DateTimeOffset?)null : DateTimeOffset.FromUnixTimeSeconds((long)unixTimeSeconds);
            }
            catch (Exception ex)
            {
                this.logger.LogError("An exception occurred trying to remove an unconfirmed transaction '{0}' from wallet '{1}'.", transactionId, walletName);
                this.logger.LogError(ex.ToString());

                throw ex;
            }
            finally
            {
                walletContainer.WriteLockRelease();
            }
        }

        /// <inheritdoc />
        public IEnumerable<(uint256 txId, DateTimeOffset creationTime)> RemoveAllUnconfirmedTransactions(string walletName)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            walletContainer.WriteLockWait();

            try
            {
                conn.BeginTransaction();

                this.logger.LogDebug("Removing all unconfirmed transactions from wallet '{0}'.", walletName);

                IEnumerable<(string txId, long creationTime)> res = conn.RemoveAllUnconfirmedTransactions(wallet.WalletId);
                conn.Commit();

                return res.Select(i => (uint256.Parse(i.txId), DateTimeOffset.FromUnixTimeSeconds(i.creationTime))).ToList();
            }
            finally
            {
                walletContainer.WriteLockRelease();
            }
        }

        /// <inheritdoc />
        public void ProcessTransaction(string walletName, Transaction transaction, uint256 fixedTxId = null)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            walletContainer.WriteLockWait();
            walletContainer.LockProcessBlocks.Wait();

            ProcessBlocksInfo processBlocksInfo = walletContainer;

            bool notifyWallet = false;

            try
            {
                IEnumerable<IEnumerable<string>> txToScript;
                {
                    var transactionsToLists = new TransactionsToLists(this.Network, this.ScriptAddressReader, processBlocksInfo, this.dateTimeProvider);
                    if (transactionsToLists.ProcessTransactions(new[] { transaction }, null, fixedTxId))
                        notifyWallet = true;

                    txToScript = (new[] { processBlocksInfo.Outputs, processBlocksInfo.PrevOuts }).Select(list => list.CreateScript());
                }

                conn.BeginTransaction();
                try
                {
                    this.logger.LogDebug("Processing transaction '{0}'.", transaction.GetHash());

                    // Ensure that any new addresses are present in the database before accessing the HDAddress table.
                    foreach (AddressIdentifier addressIdentifier in processBlocksInfo.AddressesOfInterest.GetTentative())
                    {
                        Guard.Assert(!string.IsNullOrEmpty(addressIdentifier.ScriptPubKey));
                        Guard.Assert(!string.IsNullOrEmpty(addressIdentifier.PubKeyScript));

                        conn.InsertOrReplace(this.CreateAddress(addressIdentifier));
                    }

                    conn.ProcessTransactions(txToScript, wallet);

                    processBlocksInfo.AddressesOfInterest.Confirm();
                    processBlocksInfo.TransactionsOfInterest.Confirm();

                    conn.Commit();
                }
                catch (Exception ex)
                {
                    this.logger.LogError("An exception occurred processing transaction '{0}'.", transaction.GetHash());
                    this.logger.LogError(ex.ToString());

                    conn.Rollback();

                    throw;
                }
            }
            finally
            {
                processBlocksInfo.Outputs.Clear();
                processBlocksInfo.PrevOuts.Clear();

                walletContainer.LockProcessBlocks.Release();
                walletContainer.WriteLockRelease();
            }

            if (notifyWallet)
            {
                // We only want to raise events for the UI (via SignalR) to query the wallet balance if a transaction pertaining to the wallet 
                // was processed.
                this.signals?.Publish(new WalletProcessedTransactionOfInterestEvent() { Source = "processtx" });
            }
        }

        /// <inheritdoc />
        public IEnumerable<UnspentOutputReference> GetSpendableTransactionsInAccount(WalletAccountReference walletAccountReference, int currentChainHeight, int confirmations = 0, int? coinBaseMaturity = null)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletAccountReference.WalletName);
            DBConnection conn = walletContainer.Conn;

            Wallet hdWallet = this.GetWallet(walletAccountReference.WalletName);
            HdAccount hdAccount = this.GetAccounts(hdWallet, walletAccountReference.AccountName).FirstOrDefault();

            var spendable = conn.GetSpendableOutputs(walletContainer.Wallet.WalletId, hdAccount.Index, currentChainHeight, coinBaseMaturity ?? this.Network.Consensus.CoinbaseMaturity, confirmations);

            ExtPubKey extPubKey = null;

            // The extPubKey will be null if this is a watch only account.
            if (hdAccount.ExtendedPubKey != null)
            {
                extPubKey = ExtPubKey.Parse(hdAccount.ExtendedPubKey, this.Network);
            }

            var cachedPubKeys = new Dictionary<KeyPath, PubKey>();

            foreach (HDTransactionData transactionData in spendable)
            {
                int tdConfirmations = (transactionData.OutputBlockHeight == null) ? 0 : (currentChainHeight + 1) - (int)transactionData.OutputBlockHeight;

                PubKey pubKey;

                if (extPubKey == null)
                {
                    if (!walletContainer.AddressesOfInterest.Contains(Script.FromHex(transactionData.RedeemScript), out AddressIdentifier addressIdentifier))
                        continue;

                    Script p2pkScript = Script.FromHex(addressIdentifier.PubKeyScript);
                    
                    pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(p2pkScript);
                }
                else
                {
                    var keyPath = new KeyPath($"{transactionData.AddressType}/{transactionData.AddressIndex}");

                    if (!cachedPubKeys.TryGetValue(keyPath, out pubKey))
                    {
                        pubKey = extPubKey.Derive(keyPath).PubKey;
                        cachedPubKeys.Add(keyPath, pubKey);
                    }
                }

                Script scriptPubKey = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(pubKey);
                Script witScriptPubKey = PayToWitPubKeyHashTemplate.Instance.GenerateScriptPubKey(pubKey);

                // We do not use the address from the transaction (i.e. UTXO) data here in case it is a segwit (P2WPKH) UTXO.
                // That is because the bech32 functionality is somewhat bolted onto the HdAddress, so we need to return an HdAddress augmented with bech32 data rather than only bech32 data.
                HdAddress hdAddress = this.ToHdAddress(new HDAddress()
                {
                    AccountIndex = transactionData.AccountIndex,
                    AddressIndex = transactionData.AddressIndex,
                    AddressType = (int)transactionData.AddressType,
                    PubKey = pubKey.ScriptPubKey.ToHex(),
                    ScriptPubKey = scriptPubKey.ToHex(),
                    Address = scriptPubKey.GetDestinationAddress(this.Network).ToString(),
                    Bech32Address = witScriptPubKey.GetDestinationAddress(this.Network).ToString()
                }, this.Network);

                hdAddress.AddressCollection = (hdAddress.AddressType == 0) ? hdAccount.ExternalAddresses : hdAccount.InternalAddresses;

                TransactionData txData = this.ToTransactionData(transactionData, hdAddress.Transactions);

                // Check if this wallet is a normal purpose wallet (not cold staking, etc).
                if (hdAccount.IsNormalAccount() || hdAccount.Name == Wallet.WatchOnlyAccountName)
                {
                    bool isColdCoinStake = txData.IsColdCoinStake ?? false;

                    // Skip listing the UTXO if this is a normal wallet, and the UTXO is marked as a cold coin stake.
                    if (isColdCoinStake)
                    {
                        continue;
                    }
                }

                yield return new UnspentOutputReference()
                {
                    Account = hdAccount,
                    Transaction = txData,
                    Confirmations = tdConfirmations,
                    Address = hdAddress
                };
            }
        }

        /// <inheritdoc />
        public IEnumerable<PaymentDetails> GetPaymentDetails(string walletName, TransactionData transactionData, bool isChange)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);

            DBConnection conn = walletContainer.Conn;

            SpendingDetails spendingDetails = transactionData.SpendingDetails;

            var res = HDPayment.GetAllPayments(conn,
                 transactionData.SpendingDetails.TransactionId.ToString(),
                 transactionData.Id.ToString(),
                 transactionData.Index,
                 transactionData.AddressScriptPubKey.ToHex())
                 .Where(p => p.SpendIsChange == (isChange ? 1 : 0)).Select(p => new PaymentDetails()
                 {
                     Amount = new Money(p.SpendValue),
                     DestinationScriptPubKey = new Script(Encoders.Hex.DecodeData(p.SpendScriptPubKey)),
                     OutputIndex = p.SpendIndex
                 }).ToList();

            if (transactionData.SpendingDetails == null || this.ScriptAddressReader == null)
                return res;

            var lookup = res.Select(d => d.DestinationScriptPubKey).Distinct().ToDictionary(d => d, d => (string)null);
            foreach (Script script in lookup.Keys.ToList())
                lookup[script] = this.ScriptAddressReader.GetAddressFromScriptPubKey(this.Network, script);

            foreach (PaymentDetails paymentDetails in res)
                paymentDetails.DestinationAddress = lookup[paymentDetails.DestinationScriptPubKey];

            return res;
        }

        /// <inheritdoc />
        public IEnumerable<PaymentDetails> GetPaymentDetails(string walletName, string transactionId)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);

            DBConnection conn = walletContainer.Conn;

            var res = HDPayment.GetPaymentsForTransactionId(conn, transactionId).ToList().Select(p => new PaymentDetails()
            {
                Amount = new Money(p.SpendValue),
                DestinationScriptPubKey = new Script(Encoders.Hex.DecodeData(p.SpendScriptPubKey)),
                OutputIndex = p.SpendIndex,
                IsChange = p.SpendIsChange == 1 ? true : false
            }).ToList();

            if (this.ScriptAddressReader == null)
                return res;

            var lookup = res.Select(d => d.DestinationScriptPubKey).Distinct().ToDictionary(d => d, d => (string)null);
            foreach (Script script in lookup.Keys.ToList())
                lookup[script] = this.ScriptAddressReader.GetAddressFromScriptPubKey(this.Network, script);

            foreach (PaymentDetails paymentDetails in res)
                paymentDetails.DestinationAddress = lookup[paymentDetails.DestinationScriptPubKey];

            return res;
        }

        /// <inheritdoc />
        public (Money totalAmount, Money confirmedAmount, Money spendableAmount) GetAccountBalance(WalletAccountReference walletAccountReference, int currentChainHeight, int confirmations = 0, int? coinBaseMaturity = null, (int, int)? address = null)
        {
            DBConnection conn = this.GetConnection(walletAccountReference.WalletName);
            HDAccount account = conn.GetAccountByName(walletAccountReference.WalletName, walletAccountReference.AccountName);

            (long total, long confirmed, long spendable) = HDTransactionData.GetBalance(conn, account.WalletId, account.AccountIndex, address, currentChainHeight, coinBaseMaturity ?? (int)this.Network.Consensus.CoinbaseMaturity, confirmations);

            return (new Money(total), new Money(confirmed), new Money(spendable));
        }

        /// <inheritdoc />
        public IWalletAddressReadOnlyLookup GetWalletAddressLookup(string walletName)
        {
            return this.GetWalletContainer(walletName).AddressesOfInterest;
        }

        /// <inheritdoc />
        public IWalletTransactionReadOnlyLookup GetWalletTransactionLookup(string walletName)
        {
            return this.GetWalletContainer(walletName).TransactionsOfInterest;
        }

        /// <inheritdoc />
        public int GetTransactionCount(string walletName, string accountName = null)
        {
            int walletId = this.GetWalletId(walletName);

            DBConnection conn = this.GetConnection(walletName);

            HDAccount account = (accountName == null) ? null : conn.GetAccountByName(walletName, accountName);

            return HDTransactionData.GetTransactionCount(conn, walletId, account?.AccountIndex);
        }

        /// <inheritdoc />
        public IEnumerable<TransactionData> GetAllTransactions(HdAddress hdAddress, int limit = int.MaxValue, TransactionData prev = null, bool descending = true)
        {
            HdAccount hdAccount = hdAddress.AddressCollection.Account;
            Wallet hdWallet = hdAccount.AccountRoot.Wallet;

            WalletContainer walletContainer = this.GetWalletContainer(hdWallet.Name);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            var prevTran = (prev == null) ? null : new HDTransactionData()
            {
                OutputTxTime = prev.CreationTime.ToUnixTimeSeconds(),
                OutputIndex = prev.Index
            };

            foreach (HDTransactionData tranData in HDTransactionData.GetAllTransactions(conn, wallet.WalletId,
                hdAccount.Index, hdAddress.AddressType, hdAddress.Index, limit, prevTran, descending))
            {
                yield return this.ToTransactionData(tranData, hdAddress.Transactions);
            }
        }

        public int GetWalletId(string walletName)
        {
            DBConnection conn = this.GetConnection(walletName);
            HDWallet wallet = conn.GetWalletByName(walletName);

            return wallet.WalletId;
        }

        /// <inheritdoc />
        public AddressIdentifier GetAddressIdentifier(string walletName, string accountName = null, int? addressType = null, int? addressIndex = null)
        {
            DBConnection conn = this.GetConnection(walletName);
            int walletId;
            int? accountIndex;

            if (accountName != null)
            {
                HDAccount account = conn.GetAccountByName(walletName, accountName);
                walletId = account.WalletId;
                accountIndex = account.AccountIndex;
            }
            else
            {
                HDWallet wallet = conn.GetWalletByName(walletName);
                walletId = wallet.WalletId;
                accountIndex = null;
            }

            return new AddressIdentifier()
            {
                WalletId = walletId,
                AccountIndex = accountIndex,
                AddressType = addressType,
                AddressIndex = addressIndex
            };
        }

        /// <inheritdoc />
        public AccountHistory GetHistory(HdAccount account, int limit, int offset, string txId = null, string accountAddress = null, bool forSmartContracts = false)
        {
            Wallet wallet = account.AccountRoot.Wallet;
            WalletContainer walletContainer = this.GetWalletContainer(wallet.Name);
            (HDWallet HDWallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            var result = HDTransactionData.GetHistory(conn, HDWallet.WalletId, account.Index, limit, offset, txId, accountAddress, forSmartContracts, this.Network.Name.Contains("Cirrus"));

            // Filter ColdstakeUtxos
            result = result.Where(r =>
            {
                var scriptPubKey = new Script(Encoders.Hex.DecodeData(r.RedeemScript));
                return !scriptPubKey.IsScriptType(ScriptType.ColdStaking);
            });

            if (!string.IsNullOrWhiteSpace(txId) && uint256.TryParse(txId, out uint256 _))
            {
                if (result.Any() && result.First().Type == (int)TransactionItemType.Send)
                {
                    var payments = this.GetPaymentDetails(account.AccountRoot.Wallet.Name, txId);
                    var grouped = payments.GroupBy(p => p.DestinationScriptPubKey);

                    foreach (var group in grouped)
                    {
                        result.First().Payments.Add(new FlattenedHistoryItemPayment() { Amount = group.First().Amount, DestinationScriptPubKey = group.First().DestinationScriptPubKey, DestinationAddress = group.First().DestinationAddress, IsChange = group.First().IsChange });
                    }
                }
            }

            var lookup = new Dictionary<string, string>();

            // Update sent to and mine/stake addresses.
            foreach (var item in result.Where(r => r.Type == (int)TransactionItemType.Send))
            {
                // Cache the address.
                if (!lookup.TryGetValue(item.SendToScriptPubkey, out string address))
                {
                    var script = new Script(Encoders.Hex.DecodeData(item.SendToScriptPubkey));
                    address = this.ScriptAddressReader.GetAddressFromScriptPubKey(this.Network, script);
                    lookup.Add(item.SendToScriptPubkey, address);
                }

                item.SendToAddress = address;
            }

            return new AccountHistory()
            {
                Account = account,
                History = result
            };
        }

        /// <inheritdoc />
        public IEnumerable<TransactionData> GetTransactionInputs(HdAccount hdAccount, DateTimeOffset? transactionTime, uint256 transactionId, bool includePayments = false)
        {
            Wallet hdWallet = hdAccount.AccountRoot.Wallet;

            WalletContainer walletContainer = this.GetWalletContainer(hdWallet.Name);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            var addressDict = new Dictionary<AddressIdentifier, HdAddress>();

            foreach (HDTransactionData tranData in HDTransactionData.FindTransactionInputs(conn, wallet.WalletId, hdAccount.Index, transactionTime?.ToUnixTimeSeconds(), transactionId.ToString()))
            {
                var outPoint = new OutPoint(uint256.Parse(tranData.OutputTxId), tranData.OutputIndex);
                if (!walletContainer.TransactionsOfInterest.Contains(outPoint, out HashSet<AddressIdentifier> addresses))
                    continue;

                AddressIdentifier addressIdentifier = addresses.First(a => a.WalletId == tranData.WalletId && a.AccountIndex == tranData.AccountIndex);
                // TODO:
                // AddressIdentifier addressIdentifier = addresses.FirstOrDefault(a => a.WalletId == tranData.WalletId && a.AccountIndex == tranData.AccountIndex && a.ScriptPubKey == tranData.ScriptPubKey);
                // if (addressIdentifier == null)
                //    continue;

                if (!addressDict.TryGetValue(addressIdentifier, out HdAddress hdAddress))
                {
                    PubKey pubKey;

                    if (hdAccount.ExtendedPubKey != null)
                    {
                        ExtPubKey extPubKey = ExtPubKey.Parse(hdAccount.ExtendedPubKey, this.Network);

                        var keyPath = new KeyPath($"{tranData.AddressType}/{tranData.AddressIndex}");

                        pubKey = extPubKey.Derive(keyPath).PubKey;
                    }
                    else
                    {
                        // TODO: Verify if PubKeyScript is getting populated correctly for TransactionsOfInterest-derived AddressIdentifiers
                        Script p2pkScript = Script.FromHex(addressIdentifier.PubKeyScript);

                        pubKey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(p2pkScript);
                    }

                    hdAddress = this.ToHdAddress(new HDAddress()
                    {
                        WalletId = addressIdentifier.WalletId,
                        AccountIndex = (int)addressIdentifier.AccountIndex,
                        AddressType = (int)addressIdentifier.AddressType,
                        AddressIndex = (int)addressIdentifier.AddressIndex,
                        ScriptPubKey = addressIdentifier.ScriptPubKey,
                        PubKey = pubKey.ScriptPubKey.ToHex(),
                        Address = tranData.Address
                    }, this.Network);

                    hdAddress.Transactions = new TransactionCollection(hdAddress);
                    hdAddress.AddressCollection = (hdAddress.AddressType == 0) ? hdAccount.ExternalAddresses : hdAccount.InternalAddresses;

                    addressDict[addressIdentifier] = hdAddress;
                }

                yield return this.ToTransactionData(tranData, hdAddress.Transactions);
            }
        }

        /// <inheritdoc />
        public IEnumerable<TransactionData> GetTransactionOutputs(HdAccount hdAccount, DateTimeOffset? transactionTime, uint256 transactionId, bool includePayments = false)
        {
            Wallet hdWallet = hdAccount.AccountRoot.Wallet;

            WalletContainer walletContainer = this.GetWalletContainer(hdWallet.Name);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            var addressDict = new Dictionary<AddressIdentifier, HdAddress>();

            foreach (HDTransactionData tranData in HDTransactionData.FindTransactionOutputs(conn, wallet.WalletId, hdAccount.Index, transactionTime?.ToUnixTimeSeconds(), transactionId.ToString()))
            {
                var outPoint = new OutPoint(uint256.Parse(tranData.OutputTxId), tranData.OutputIndex);
                if (!walletContainer.TransactionsOfInterest.Contains(outPoint, out HashSet<AddressIdentifier> addresses))
                    continue;

                AddressIdentifier addressIdentifier = addresses.First(a => a.WalletId == tranData.WalletId && a.AccountIndex == tranData.AccountIndex);
                // TODO:
                // AddressIdentifier addressIdentifier = addresses.FirstOrDefault(a => a.WalletId == tranData.WalletId && a.AccountIndex == tranData.AccountIndex && a.ScriptPubKey == tranData.ScriptPubKey);
                // if (addressIdentifier == null)
                //    continue;

                if (!addressDict.TryGetValue(addressIdentifier, out HdAddress hdAddress))
                {
                    string pubKeyHex = null;

                    if (hdAccount.ExtendedPubKey != null)
                    {
                        ExtPubKey extPubKey = ExtPubKey.Parse(hdAccount.ExtendedPubKey, this.Network);

                        var keyPath = new KeyPath($"{tranData.AddressType}/{tranData.AddressIndex}");

                        PubKey pubKey = extPubKey.Derive(keyPath).PubKey;

                        pubKeyHex = pubKey.ScriptPubKey.ToHex();
                    }
                    else
                    {
                        // If it is a watch only account we have limited information available.
                        pubKeyHex = addressIdentifier.PubKeyScript;
                    }

                    hdAddress = this.ToHdAddress(new HDAddress()
                    {
                        WalletId = addressIdentifier.WalletId,
                        AccountIndex = (int)addressIdentifier.AccountIndex,
                        AddressType = (int)addressIdentifier.AddressType,
                        AddressIndex = (int)addressIdentifier.AddressIndex,
                        ScriptPubKey = addressIdentifier.ScriptPubKey,
                        PubKey = pubKeyHex,
                        Address = tranData.Address
                    }, this.Network);

                    hdAddress.Transactions = new TransactionCollection(hdAddress);
                    hdAddress.AddressCollection = (hdAddress.AddressType == 0) ? hdAccount.ExternalAddresses : hdAccount.InternalAddresses;

                    addressDict[addressIdentifier] = hdAddress;
                }

                yield return this.ToTransactionData(tranData, hdAddress.Transactions);
            }
        }

        private class ScriptTransaction
        {
            public string ScriptPubKey { get; set; }
            public string TransactionId { get; set; }
        }

        /// <inheritdoc />
        public IEnumerable<IEnumerable<string>> GetAddressGroupings(string walletName)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            (HDWallet wallet, DBConnection conn) = (walletContainer.Wallet, walletContainer.Conn);

            var addressGroupings = new Dictionary<string, HashSet<string>>();

            // Group all input addresses with each other.
            foreach (var spendData in conn.Query<ScriptTransaction>($@"
                SELECT  ScriptPubKey
                ,       SpendTxId TransactionId
                FROM    HDTransactionData
                WHERE   WalletId = ?
                AND     TransactionId IS NOT NULL",
                wallet.WalletId))
            {
                var spendTxId = spendData.TransactionId;
                if (spendTxId != null)
                {
                    if (!addressGroupings.TryGetValue(spendTxId, out HashSet<string> grouping))
                    {
                        grouping = new HashSet<string>();
                        addressGroupings[spendTxId] = grouping;
                    }

                    grouping.Add(spendData.ScriptPubKey);
                }
            }

            // Include any change addresses.
            foreach (var outputData in conn.Query<ScriptTransaction>($@"
                SELECT  ScriptPubKey
                ,       OutputTxId TransactionId
                FROM    HDTransactionData
                WHERE   WalletId = ?",
                wallet.WalletId))
            {
                if (addressGroupings.TryGetValue(outputData.TransactionId, out HashSet<string> grouping))
                    grouping.Add(outputData.ScriptPubKey);
                else
                    // Create its own grouping
                    addressGroupings[outputData.TransactionId] = new HashSet<string> { outputData.ScriptPubKey };
            }

            // Determine unique mappings.
            var uniqueGroupings = new List<HashSet<string>>();
            var setMap = new Dictionary<string, HashSet<string>>();

            foreach ((string spendTxId, HashSet<string> grouping) in addressGroupings.Select(kv => (kv.Key, kv.Value)))
            {
                // Create a list of unique groupings intersecting this grouping.
                var hits = new List<HashSet<string>>();
                foreach (string scriptPubkey in grouping)
                    if (setMap.TryGetValue(scriptPubkey, out HashSet<string> it))
                        hits.Add(it);

                // Merge the matching uinique groupings into this grouping and remove the old groupings.
                foreach (HashSet<string> hit in hits)
                {
                    grouping.UnionWith(hit);
                    uniqueGroupings.Remove(hit);
                }

                // Add the new merged grouping.
                uniqueGroupings.Add(grouping);

                // Update the set map which maps addresses to the unique grouping they appear in.
                foreach (string scriptPubKey in grouping)
                    setMap[scriptPubKey] = grouping;
            }

            // Return the result.
            foreach (HashSet<string> scriptPubKeys in uniqueGroupings)
            {
                var addressBase58s = new List<string>();

                foreach (string scriptPubKey in scriptPubKeys)
                {
                    Script script = Script.FromHex(scriptPubKey);
                    var addressBase58 = script.GetDestinationAddress(this.Network);
                    if (addressBase58 == null)
                        continue;

                    addressBase58s.Add(addressBase58.ToString());
                }

                yield return addressBase58s;
            }
        }

        /// <inheritdoc />
        public IEnumerable<(HdAddress, IEnumerable<TransactionData>)> GetTransactionsById(string walletName, uint256 transactionId)
        {
            WalletContainer walletContainer = this.GetWalletContainer(walletName);
            var hdTransactionData = HDTransactionData.GetTransactionsById(walletContainer.Conn, walletContainer.Wallet.WalletId, transactionId.ToString());
            var grouped = hdTransactionData.GroupBy(x => x.Address);

            foreach (var group in grouped)
            {
                var hdAddress = new HdAddress() { Address = group.Key, AddressType = group.First().AddressType };
                yield return (hdAddress, group.Select(t => this.ToTransactionData(t, null)));
            }
        }
    }
}
