﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.BuilderExtensions;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;

[assembly: InternalsVisibleTo("Xels.Bitcoin.Features.ColdStaking.Tests")]
[assembly: InternalsVisibleTo("Xels.Bitcoin.IntegrationTests")]

namespace Xels.Bitcoin.Features.ColdStaking
{
    /// <summary>
    /// The manager class for implementing cold staking as covered in more detail in the remarks of
    /// the <see cref="ColdStakingFeature"/> class.
    /// This class provides the methods used by the <see cref="Controllers.ColdStakingController"/>
    /// which in turn provides the API methods for accessing this functionality.
    /// </summary>
    /// <remarks>
    /// The following functionality is implemented in this class:
    /// <list type="bullet">
    /// <item><description>Generating cold staking address via the <see cref="GetFirstUnusedColdStakingAddress"/> method. These
    /// addresses are used for generating the cold staking setup.</description></item>
    /// <item><description>Creating a build context for generating the cold staking setup via the <see
    /// cref="GetColdStakingSetupTransaction"/> method.</description></item>
    /// </list>
    /// </remarks>
    public class ColdStakingManager : WalletManager, IWalletManager
    {
        private static Func<HdAccount, bool> coldStakingAccounts = a => a.Index >= Wallet.Wallet.SpecialPurposeAccountIndexesStart;

        /// <summary>The account index of the cold wallet account.</summary>
        internal const int ColdWalletAccountIndex = Wallet.Wallet.SpecialPurposeAccountIndexesStart + 0;

        /// <summary>The account name of the cold wallet account.</summary>
        internal const string ColdWalletAccountName = "coldStakingColdAddresses";

        /// <summary>The account index of the hot wallet account.</summary>
        internal const int HotWalletAccountIndex = Wallet.Wallet.SpecialPurposeAccountIndexesStart + 1;

        /// <summary>The account name of the hot wallet account.</summary>
        internal const string HotWalletAccountName = "coldStakingHotAddresses";

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>Provider of time functions.</summary>
        private readonly IDateTimeProvider dateTimeProvider;

        /// <summary>
        /// Constructs the cold staking manager which is used by the cold staking controller.
        /// </summary>
        /// <param name="network">The network that the manager is running on.</param>
        /// <param name="chainIndexer">Thread safe class representing a chain of headers from genesis.</param>
        /// <param name="walletSettings">The wallet settings.</param>
        /// <param name="dataFolder">Contains path locations to folders and files on disk.</param>
        /// <param name="walletFeePolicy">The wallet fee policy.</param>
        /// <param name="asyncProvider">Factory for creating and also possibly starting application defined tasks inside async loop.</param>
        /// <param name="nodeLifeTime">Allows consumers to perform cleanup during a graceful shutdown.</param>
        /// <param name="scriptAddressReader">A reader for extracting an address from a <see cref="Script"/>.</param>
        /// <param name="loggerFactory">The logger factory to use to create the custom logger.</param>
        /// <param name="dateTimeProvider">Provider of time functions.</param>
        /// <param name="walletRepository">The wallet repository.</param>
        /// <param name="broadcasterManager">The broadcaster manager.</param>
        public ColdStakingManager(
            Network network,
            ChainIndexer chainIndexer,
            WalletSettings walletSettings,
            DataFolder dataFolder,
            IWalletFeePolicy walletFeePolicy,
            IAsyncProvider asyncProvider,
            INodeLifetime nodeLifeTime,
            IScriptAddressReader scriptAddressReader,
            ILoggerFactory loggerFactory,
            IDateTimeProvider dateTimeProvider,
            IWalletRepository walletRepository,
            IBroadcasterManager broadcasterManager = null) : base(
                loggerFactory,
                network,
                chainIndexer,
                walletSettings,
                dataFolder,
                walletFeePolicy,
                dateTimeProvider,
                walletRepository)
        {
            Guard.NotNull(loggerFactory, nameof(loggerFactory));
            Guard.NotNull(dateTimeProvider, nameof(dateTimeProvider));

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
            this.dateTimeProvider = dateTimeProvider;
        }

        /// <summary>
        /// Overrides the default <see cref="WalletManager.CreateAddressFromScriptLookup"/>.
        /// </summary>
        /// <returns>A new <see cref="ColdStakingAddressLookup"/> object for use by this class.</returns>
        protected override ScriptToAddressLookup CreateAddressFromScriptLookup()
        {
            return new ColdStakingAddressLookup(this.network);
        }

        /// <inheritdoc />
        public override Dictionary<string, ScriptTemplate> GetValidStakingTemplates()
        {
            Dictionary<string, ScriptTemplate> templates = base.GetValidStakingTemplates();
            templates["ColdStaking"] = ColdStakingScriptTemplate.Instance;
            return templates;
        }

        // <inheritdoc />
        public override IEnumerable<BuilderExtension> GetTransactionBuilderExtensionsForStaking()
        {
            return base.GetTransactionBuilderExtensionsForStaking().Concat(new List<BuilderExtension> { new ColdStakingBuilderExtension(true) });
        }

        /// <summary>
        /// Gets all the unspent transactions in a wallet from the accounts participating in staking.
        /// </summary>
        /// <param name="walletName">Name of the wallet to get the transactions from.</param>
        /// <param name="confirmations">Number of confirmation required.</param>
        /// <returns>An enumeration of <see cref="UnspentOutputReference"/> objects.</returns>
        public override IEnumerable<UnspentOutputReference> GetSpendableTransactionsInWalletForStaking(string walletName, int confirmations = 0)
        {
            return this.GetUnspentTransactionsInWallet(walletName, confirmations,
                a => (a.Index < Wallet.Wallet.SpecialPurposeAccountIndexesStart) || (a.Index == ColdStakingManager.HotWalletAccountIndex));
        }

        /// <summary>
        /// Returns information related to cold staking.
        /// </summary>
        /// <param name="walletName">The wallet to return the information for.</param>
        /// <returns>A <see cref="Models.GetColdStakingInfoResponse"/> object containing the information.</returns>
        internal Models.GetColdStakingInfoResponse GetColdStakingInfo(string walletName)
        {
            Wallet.Wallet wallet = this.GetWallet(walletName);

            var response = new Models.GetColdStakingInfoResponse()
            {
                ColdWalletAccountExists = this.GetColdStakingAccount(wallet, true) != null,
                HotWalletAccountExists = this.GetColdStakingAccount(wallet, false) != null
            };

            this.logger.LogTrace("(-):'{0}'", response);
            return response;
        }

        /// <summary>
        /// Gets a cold staking account.
        /// </summary>
        /// <remarks>
        /// <para>In order to keep track of cold staking addresses and balances we are using <see cref="HdAccount"/>'s
        /// with indexes starting from the value defined in <see cref="Wallet.SpecialPurposeAccountIndexesStart"/>.
        /// </para><para>
        /// We are using two such accounts, one when the wallet is in the role of cold wallet, and another one when
        /// the wallet is in the role of hot wallet. For this reason we specify the required account when calling this
        /// method.
        /// </para></remarks>
        /// <param name="wallet">The wallet where we wish to create the account.</param>
        /// <param name="isColdWalletAccount">Indicates whether we need the cold wallet account (versus the hot wallet account).</param>
        /// <returns>The cold staking account or <c>null</c> if the account does not exist.</returns>
        internal HdAccount GetColdStakingAccount(Wallet.Wallet wallet, bool isColdWalletAccount)
        {
            HdAccount account = wallet.GetAccount(isColdWalletAccount ? ColdWalletAccountName : HotWalletAccountName);
            if (account == null)
            {
                this.logger.LogTrace("(-)[ACCOUNT_DOES_NOT_EXIST]:null");
                return null;
            }

            this.logger.LogTrace("(-):'{0}'", account.Name);
            return account;
        }

        /// <summary>
        /// Creates a cold staking account and ensures that it has at least one address.
        /// If the account already exists then the existing account is returned.
        /// </summary>
        /// <remarks>
        /// <para>In order to keep track of cold staking addresses and balances we are using <see cref="HdAccount"/>'s
        /// with indexes starting from the value defined in <see cref="Wallet.SpecialPurposeAccountIndexesStart"/>.
        /// </para><para>
        /// We are using two such accounts, one when the wallet is in the role of cold wallet, and another one when
        /// the wallet is in the role of hot wallet. For this reason we specify the required account when calling this
        /// method.
        /// </para></remarks>
        /// <param name="walletName">The name of the wallet where we wish to create the account.</param>
        /// <param name="isColdWalletAccount">Indicates whether we need the cold wallet account (versus the hot wallet account).</param>
        /// <param name="walletPassword">The wallet password which will be used to create the account.</param>
        /// <param name="extPubKey">The <see cref="ExtPubKey"/> of the wallet account. Can be <c>null</c> for watch-only wallets.</param>
        /// <returns>The new or existing cold staking account.</returns>
        internal HdAccount GetOrCreateColdStakingAccount(string walletName, bool isColdWalletAccount, string walletPassword, ExtPubKey extPubKey)
        {
            Wallet.Wallet wallet = this.GetWallet(walletName);

            HdAccount account = this.GetColdStakingAccount(wallet, isColdWalletAccount);
            if (account != null)
            {
                this.logger.LogTrace("(-)[ACCOUNT_ALREADY_EXIST]:'{0}'", account.Name);
                return account;
            }

            this.logger.LogDebug("The {0} wallet account for '{1}' does not exist and will now be created.", isColdWalletAccount ? "cold" : "hot", wallet.Name);

            int accountIndex;
            string accountName;

            if (isColdWalletAccount)
            {
                accountIndex = ColdWalletAccountIndex;
                accountName = ColdWalletAccountName;
            }
            else
            {
                accountIndex = HotWalletAccountIndex;
                accountName = HotWalletAccountName;
            }

            if (extPubKey == null)
                account = wallet.AddNewAccount(walletPassword, accountIndex, accountName, this.dateTimeProvider.GetTimeOffset());
            else
                account = wallet.AddNewAccount(extPubKey, accountIndex, accountName, this.dateTimeProvider.GetTimeOffset());

            this.logger.LogTrace("(-):'{0}'", account.Name);
            return account;
        }

        /// <summary>
        /// Gets the first unused cold staking address. Creates a new address if required.
        /// </summary>
        /// <param name="walletName">The name of the wallet providing the cold staking address.</param>
        /// <param name="isColdWalletAddress">Indicates whether we need the cold wallet address (versus the hot wallet address).</param>
        /// <returns>The cold staking address or <c>null</c> if the required account does not exist.</returns>
        internal HdAddress GetFirstUnusedColdStakingAddress(string walletName, bool isColdWalletAddress)
        {
            Guard.NotNull(walletName, nameof(walletName));

            Wallet.Wallet wallet = this.GetWallet(walletName);
            HdAccount account = this.GetColdStakingAccount(wallet, isColdWalletAddress);
            if (account == null)
            {
                this.logger.LogTrace("(-)[ACCOUNT_DOES_NOT_EXIST]:null");
                return null;
            }

            HdAddress address = account.GetFirstUnusedReceivingAddress();
            if (address == null)
            {
                this.logger.LogDebug("No unused address exists on account '{0}'. Adding new address.", account.Name);
                // TODO:
                /*
                IEnumerable<HdAddress> newAddresses = account.CreateAddresses(wallet.Network, 1);
                this.UpdateKeysLookupLocked(newAddresses);
                address = newAddresses.First();
                */
            }

            this.logger.LogTrace("(-):'{0}'", address.Address);
            return address;
        }

        /// <summary>
        /// Creates cold staking setup <see cref="Transaction"/>.
        /// </summary>
        /// <remarks>
        /// The <paramref name="coldWalletAddress"/> and <paramref name="hotWalletAddress"/> would be expected to be
        /// from different wallets and typically also different physical machines under normal circumstances. The following
        /// rules are enforced by this method and would lead to a <see cref="WalletException"/> otherwise:
        /// <list type="bullet">
        /// <item><description>The cold and hot wallet addresses are expected to belong to different wallets.</description></item>
        /// <item><description>Either the cold or hot wallet address must belong to a cold staking account in the wallet identified
        /// by <paramref name="walletName"/></description></item>
        /// <item><description>The account specified in <paramref name="walletAccount"/> can't be a cold staking account.</description></item>
        /// </list>
        /// </remarks>
        /// <param name="walletTransactionHandler">The wallet transaction handler. Contains the <see cref="WalletTransactionHandler.BuildTransaction"/> method.</param>
        /// <param name="coldWalletAddress">The cold wallet address generated by <see cref="GetColdStakingAddress"/>.</param>
        /// <param name="hotWalletAddress">The hot wallet address generated by <see cref="GetColdStakingAddress"/>.</param>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="walletAccount">The wallet account.</param>
        /// <param name="walletPassword">The wallet password.</param>
        /// <param name="amount">The amount to cold stake.</param>
        /// <param name="feeAmount">The fee to pay for the cold staking setup transaction.</param>
        /// <param name="subtractFeeFromAmount">Whether the transaction fee should be subtracted from the amount being transferred into the cold staking account.</param>
        /// <param name="offline">Whether the transaction should be left unsigned so that it can be transferred to an offline wallet for signing.</param>
        /// <param name="splitCount">The number of UTXOs of similar value the setup transaction will be split into. Defaults to 1.</param>
        /// <param name="useSegwitChangeAddress">Use a segwit style change address.</param>
        /// <returns>The <see cref="Transaction"/> for setting up cold staking.</returns>
        /// <exception cref="WalletException">Thrown if any of the rules listed in the remarks section of this method are broken.</exception>
        internal (Transaction, TransactionBuildContext) GetColdStakingSetupTransaction(IWalletTransactionHandler walletTransactionHandler,
            string coldWalletAddress, string hotWalletAddress, string walletName, string walletAccount,
            string walletPassword, Money amount, Money feeAmount, bool subtractFeeFromAmount, bool offline, int splitCount, bool useSegwitChangeAddress = false)
        {
            TransactionBuildContext context = this.GetSetupTransactionBuildContext(walletTransactionHandler, coldWalletAddress, hotWalletAddress, walletName, walletAccount,
                walletPassword, amount, feeAmount, subtractFeeFromAmount, offline, useSegwitChangeAddress, splitCount);

            context.Sign = !offline;

            // Build the transaction.
            Transaction transaction = walletTransactionHandler.BuildTransaction(context);

            this.logger.LogTrace("(-)");
            return (transaction, context);
        }

        private TransactionBuildContext GetSetupTransactionBuildContext(IWalletTransactionHandler walletTransactionHandler,
            string coldWalletAddress, string hotWalletAddress, string walletName, string walletAccount,
            string walletPassword, Money amount, Money feeAmount, bool subtractFeeFromAmount, bool offline, bool useSegwitChangeAddress, int splitCount, ExtPubKey extPubKey = null)
        {
            Guard.NotNull(walletTransactionHandler, nameof(walletTransactionHandler));
            Guard.NotEmpty(coldWalletAddress, nameof(coldWalletAddress));
            Guard.NotEmpty(hotWalletAddress, nameof(hotWalletAddress));
            Guard.NotEmpty(walletName, nameof(walletName));
            Guard.NotEmpty(walletAccount, nameof(walletAccount));
            Guard.NotNull(amount, nameof(amount));

            Wallet.Wallet wallet = this.GetWallet(walletName);

            KeyId hotPubKeyHash = null;
            KeyId coldPubKeyHash = null;

            if (!offline)
            {
                // Get/create the cold staking accounts.
                HdAccount coldAccount = this.GetOrCreateColdStakingAccount(walletName, true, walletPassword, extPubKey);
                HdAccount hotAccount = this.GetOrCreateColdStakingAccount(walletName, false, walletPassword, extPubKey);

                HdAddress coldAddress = coldAccount?.ExternalAddresses.FirstOrDefault(s => s.Address == coldWalletAddress || s.Bech32Address == coldWalletAddress);
                HdAddress hotAddress = hotAccount?.ExternalAddresses.FirstOrDefault(s => s.Address == hotWalletAddress || s.Bech32Address == hotWalletAddress);

                bool thisIsColdWallet = coldAddress != null;
                bool thisIsHotWallet = hotAddress != null;

                this.logger.LogDebug("Local wallet '{0}' does{1} contain cold wallet address '{2}' and does{3} contain hot wallet address '{4}'.",
                    walletName, thisIsColdWallet ? "" : " NOT", coldWalletAddress, thisIsHotWallet ? "" : " NOT", hotWalletAddress);

                if (thisIsColdWallet && thisIsHotWallet)
                {
                    this.logger.LogTrace("(-)[COLDSTAKE_BOTH_HOT_AND_COLD]");
                    throw new WalletException("You can't use this wallet as both the hot wallet and cold wallet.");
                }

                if (!thisIsColdWallet && !thisIsHotWallet)
                {
                    this.logger.LogTrace("(-)[COLDSTAKE_ADDRESSES_NOT_IN_ACCOUNTS]");
                    throw new WalletException("The hot and cold wallet addresses could not be found in the corresponding accounts.");
                }

                // Check if this is a segwit address.
                if (coldAddress?.Bech32Address == coldWalletAddress || hotAddress?.Bech32Address == hotWalletAddress)
                {
                    hotPubKeyHash = new BitcoinWitPubKeyAddress(hotWalletAddress, wallet.Network).Hash.AsKeyId();
                    coldPubKeyHash = new BitcoinWitPubKeyAddress(coldWalletAddress, wallet.Network).Hash.AsKeyId();
                }
                else
                {
                    hotPubKeyHash = new BitcoinPubKeyAddress(hotWalletAddress, wallet.Network).Hash;
                    coldPubKeyHash = new BitcoinPubKeyAddress(coldWalletAddress, wallet.Network).Hash;
                }
            }
            else
            {
                // In offline mode we relax all the restrictions to enable simpler setup. The user should ensure they are using separate wallets, or the cold private key could be inadvertently loaded on the online node.
                IDestination hot = BitcoinAddress.Create(hotWalletAddress, this.network);
                IDestination cold = BitcoinAddress.Create(coldWalletAddress, this.network);

                if (hot is BitcoinPubKeyAddress && cold is BitcoinPubKeyAddress)
                {
                    hotPubKeyHash = new BitcoinPubKeyAddress(hotWalletAddress, wallet.Network).Hash;
                    coldPubKeyHash = new BitcoinPubKeyAddress(coldWalletAddress, wallet.Network).Hash;
                }

                if (hot is BitcoinWitPubKeyAddress && cold is BitcoinWitPubKeyAddress)
                {
                    hotPubKeyHash = new BitcoinWitPubKeyAddress(hotWalletAddress, wallet.Network).Hash.AsKeyId();
                    coldPubKeyHash = new BitcoinWitPubKeyAddress(coldWalletAddress, wallet.Network).Hash.AsKeyId();
                }
            }

            if (hotPubKeyHash == null || coldPubKeyHash == null)
            {
                this.logger.LogTrace("(-)[PUBKEYHASH_NOT_AVAILABLE]");
                throw new WalletException($"Unable to compute the needed hashes from the given addresses.");
            }

            Script destination = ColdStakingScriptTemplate.Instance.GenerateScriptPubKey(hotPubKeyHash, coldPubKeyHash);

            // Only normal accounts should be allowed.
            if (!this.GetAccounts(walletName).Any(a => a.Name == walletAccount))
            {
                this.logger.LogTrace("(-)[COLDSTAKE_ACCOUNT_NOT_FOUND]");
                throw new WalletException($"Can't find wallet account '{walletAccount}'.");
            }

            List<Recipient> recipients = GetRecipients(destination, amount, subtractFeeFromAmount, splitCount);

            var context = new TransactionBuildContext(wallet.Network)
            {
                AccountReference = new WalletAccountReference(walletName, walletAccount),
                TransactionFee = feeAmount,
                MinConfirmations = 0,
                Shuffle = false,
                UseSegwitChangeAddress = useSegwitChangeAddress,
                WalletPassword = walletPassword,
                Recipients = recipients
            };

            // Register the cold staking builder extension with the transaction builder.
            context.TransactionBuilder.Extensions.Add(new ColdStakingBuilderExtension(false));

            return context;
        }

        private List<Recipient> GetRecipients(Script destination, Money overallAmount, bool subtractFeeFromAmount, int splitCount)
        {
            var recipients = new List<Recipient>();

            Money moneyPerRecipient = overallAmount / splitCount;

            while (recipients.Count < splitCount)
            {
                recipients.Add(new Recipient
                {
                    Amount = moneyPerRecipient,
                    ScriptPubKey = destination,
                    SubtractFeeFromAmount = false
                });
            }

            if (recipients.Count == 0)
                throw new WalletException($"Couldn't construct recipients list.");

            recipients.Last().SubtractFeeFromAmount = subtractFeeFromAmount;

            return recipients;
        }

        internal Money EstimateSetupTransactionFee(IWalletTransactionHandler walletTransactionHandler,
            string coldWalletAddress, string hotWalletAddress, string walletName, string walletAccount,
            string walletPassword, Money amount, bool subtractFeeFromAmount, bool offline, bool useSegwitChangeAddress, int splitCount)
        {
            TransactionBuildContext context = this.GetSetupTransactionBuildContext(walletTransactionHandler, coldWalletAddress, hotWalletAddress, walletName, walletAccount,
                walletPassword, amount, null, subtractFeeFromAmount, offline, useSegwitChangeAddress, splitCount);

            Money estimatedFee = walletTransactionHandler.EstimateFee(context);

            return estimatedFee;
        }

        /// <summary>
        /// Builds an unsigned transaction template for a cold staking withdrawal transaction.
        /// This requires specialised logic due to the lack of a private key for the cold account.
        /// </summary>
        /// <param name="walletTransactionHandler">The <see cref="IWalletTransactionHandler"/>.</param>
        /// <param name="receivingAddress">The receiving address.</param>
        /// <param name="walletName">The spending wallet name.</param>
        /// <param name="accountName">The spending account name.</param>
        /// <param name="amount">The amount to spend.</param>
        /// <param name="feeAmount">The fee amount.</param>
        /// <param name="subtractFeeFromAmount">Set to <c>true</c> to subtract the <paramref name="feeAmount"/> from the <paramref name="amount"/>.</param>
        /// <returns>See <see cref="BuildOfflineSignResponse"/>.</returns>
        public BuildOfflineSignResponse BuildOfflineColdStakingWithdrawalRequest(IWalletTransactionHandler walletTransactionHandler, string receivingAddress,
            string walletName, string accountName, Money amount, Money feeAmount, bool subtractFeeFromAmount)
        {
            TransactionBuildContext context = this.GetOfflineWithdrawalBuildContext(receivingAddress, walletName, accountName, amount, feeAmount, subtractFeeFromAmount);

            Transaction transactionResult = walletTransactionHandler.BuildTransaction(context);

            var utxos = new List<UtxoDescriptor>();
            var addresses = new List<AddressDescriptor>();
            foreach (ICoin coin in context.TransactionBuilder.FindSpentCoins(transactionResult))
            {
                utxos.Add(new UtxoDescriptor()
                {
                    Amount = coin.TxOut.Value.ToUnit(MoneyUnit.BTC).ToString(),
                    TransactionId = coin.Outpoint.Hash.ToString(),
                    Index = coin.Outpoint.N.ToString(),
                    ScriptPubKey = coin.TxOut.ScriptPubKey.ToHex()
                });

                // We do not include address descriptors as the cold staking scripts are not really regarded as having addresses in the conventional sense.
                // There is also typically only a single script involved so the keypath hinting is of little use.
            }

            var hotAccountReference = new WalletAccountReference(walletName, accountName);

            // Return transaction hex and UTXO list.
            return new BuildOfflineSignResponse()
            {
                WalletName = hotAccountReference.WalletName,
                WalletAccount = hotAccountReference.AccountName,
                Fee = context.TransactionFee.ToUnit(MoneyUnit.BTC).ToString(),
                UnsignedTransaction = transactionResult.ToHex(),
                Utxos = utxos,
                Addresses = addresses
            };
        }

        public Money EstimateOfflineWithdrawalFee(IWalletTransactionHandler walletTransactionHandler, string receivingAddress,
            string walletName, string accountName, Money amount, bool subtractFeeFromAmount)
        {
            TransactionBuildContext context = this.GetOfflineWithdrawalBuildContext(receivingAddress, walletName, accountName, amount, null, subtractFeeFromAmount);

            context.TransactionBuilder.Extensions.Add(new ColdStakingBuilderExtension(false));

            return walletTransactionHandler.EstimateFee(context);
        }

        private TransactionBuildContext GetOfflineWithdrawalBuildContext(string receivingAddress, string walletName, string accountName, Money amount, Money feeAmount, bool subtractFeeFromAmount)
        {
            // We presume that the amount given by the user is accurate and optimistically pass it to the build context.
            var recipient = new List<Recipient>() { new Recipient() { Amount = amount, ScriptPubKey = BitcoinAddress.Create(receivingAddress, this.network).ScriptPubKey, SubtractFeeFromAmount = subtractFeeFromAmount } };

            var hotAccountReference = new WalletAccountReference(walletName, accountName);

            // As a simplification, the change address is defaulted to be the same cold staking script the UTXOs originate from.
            UnspentOutputReference coldStakingUtxo = this.GetSpendableTransactionsInAccount(hotAccountReference, 0).FirstOrDefault();

            if (coldStakingUtxo == null)
            {
                throw new WalletException("No unspent transactions found in cold staking hot account.");
            }

            var context = new TransactionBuildContext(this.network)
            {
                AccountReference = hotAccountReference,
                TransactionFee = feeAmount,
                MinConfirmations = 0,
                Shuffle = true, // We shuffle transaction outputs by default as it's better for anonymity.
                Recipients = recipient,
                ChangeScript = coldStakingUtxo.Transaction.ScriptPubKey, // We specifically use this instead of ChangeAddress.

                Sign = false
            };

            // As we don't actually know when the signed offline transaction will be broadcast, give it the highest chance of success if no fee was specified.
            if (context.TransactionFee == null)
            {
                context.FeeType = FeeType.High;
            }

            return context;
        }

        /// <summary>
        /// Creates a cold staking withdrawal <see cref="Transaction"/>.
        /// </summary>
        /// <remarks>
        /// Cold staking withdrawal is performed on the wallet that is in the role of the cold staking cold wallet.
        /// </remarks>
        /// <param name="walletTransactionHandler">The wallet transaction handler used to build the transaction.</param>
        /// <param name="receivingAddress">The address that will receive the withdrawal.</param>
        /// <param name="walletName">The name of the wallet in the role of cold wallet.</param>
        /// <param name="walletPassword">The wallet password.</param>
        /// <param name="amount">The amount to remove from cold staking.</param>
        /// <param name="feeAmount">The fee to pay for cold staking transaction withdrawal.</param>
        /// <param name="subtractFeeFromAmount">Set to <c>true</c> to subtract the <paramref name="feeAmount"/> from the <paramref name="amount"/>.</param>
        /// <returns>The <see cref="Transaction"/> for cold staking withdrawal.</returns>
        /// <exception cref="WalletException">Thrown if the receiving address is in a cold staking account in this wallet.</exception>
        /// <exception cref="ArgumentNullException">Thrown if the receiving address is invalid.</exception>
        internal Transaction GetColdStakingWithdrawalTransaction(IWalletTransactionHandler walletTransactionHandler, string receivingAddress,
            string walletName, string walletPassword, Money amount, Money feeAmount, bool subtractFeeFromAmount)
        {
            (TransactionBuildContext context, HdAccount coldAccount, Script destination) = this.GetWithdrawalTransactionBuildContext(receivingAddress, walletName, amount, feeAmount, subtractFeeFromAmount);

            // Build the withdrawal transaction according to the settings recorded in the context.
            Transaction transaction = walletTransactionHandler.BuildTransaction(context);

            // Map OutPoint to UnspentOutputReference.
            var accountReference = new WalletAccountReference(walletName, coldAccount.Name);
            Dictionary<OutPoint, UnspentOutputReference> mapOutPointToUnspentOutput = this.GetSpendableTransactionsInAccount(accountReference)
                .ToDictionary(unspent => unspent.ToOutPoint(), unspent => unspent);

            // Set the cold staking scriptPubKey on the change output.
            TxOut changeOutput = transaction.Outputs.SingleOrDefault(output => (output.ScriptPubKey != destination) && (output.Value != 0));
            if (changeOutput != null)
            {
                // Find the largest input.
                TxIn largestInput = transaction.Inputs.OrderByDescending(input => mapOutPointToUnspentOutput[input.PrevOut].Transaction.Amount).Take(1).Single();

                // Set the scriptPubKey of the change output to the scriptPubKey of the largest input.
                changeOutput.ScriptPubKey = mapOutPointToUnspentOutput[largestInput.PrevOut].Transaction.ScriptPubKey;
            }

            Wallet.Wallet wallet = this.GetWallet(walletName);

            // Add keys for signing inputs. This takes time so only add keys for distinct addresses.
            foreach (HdAddress address in transaction.Inputs.Select(i => mapOutPointToUnspentOutput[i.PrevOut].Address).Distinct())
            {
                context.TransactionBuilder.AddKeys(wallet.GetExtendedPrivateKeyForAddress(walletPassword, address));
            }

            // Sign the transaction.
            context.TransactionBuilder.SignTransactionInPlace(transaction);

            this.logger.LogTrace("(-):'{0}'", transaction.GetHash());
            return transaction;
        }

        private (TransactionBuildContext, HdAccount, Script) GetWithdrawalTransactionBuildContext(string receivingAddress, string walletName, Money amount, Money feeAmount, bool subtractFeeFromAmount)
        {
            Guard.NotEmpty(receivingAddress, nameof(receivingAddress));
            Guard.NotEmpty(walletName, nameof(walletName));
            Guard.NotNull(amount, nameof(amount));

            Wallet.Wallet wallet = this.GetWallet(walletName);

            // Get the cold staking account.
            HdAccount coldAccount = this.GetColdStakingAccount(wallet, true);
            if (coldAccount == null)
            {
                this.logger.LogTrace("(-)[COLDSTAKE_ACCOUNT_DOES_NOT_EXIST]");
                throw new WalletException("The cold wallet account does not exist.");
            }

            // Prevent reusing cold stake addresses as regular withdrawal addresses.
            if (coldAccount.ExternalAddresses.Concat(coldAccount.InternalAddresses).Any(s => s.Address == receivingAddress || s.Bech32Address == receivingAddress))
            {
                this.logger.LogTrace("(-)[COLDSTAKE_INVALID_COLD_WALLET_ADDRESS_USAGE]");
                throw new WalletException("You can't send the money to a cold staking cold wallet account.");
            }

            HdAccount hotAccount = this.GetColdStakingAccount(wallet, false);
            if (hotAccount != null && hotAccount.ExternalAddresses.Concat(hotAccount.InternalAddresses).Any(s => s.Address == receivingAddress || s.Bech32Address == receivingAddress))
            {
                this.logger.LogTrace("(-)[COLDSTAKE_INVALID_HOT_WALLET_ADDRESS_USAGE]");
                throw new WalletException("You can't send the money to a cold staking hot wallet account.");
            }

            Script destination = null;

            if (BitcoinWitPubKeyAddress.IsValid(receivingAddress, this.network, out Exception _))
            {
                destination = new BitcoinWitPubKeyAddress(receivingAddress, wallet.Network).ScriptPubKey;
            }
            else
            {
                // Send the money to the receiving address.
                destination = BitcoinAddress.Create(receivingAddress, wallet.Network).ScriptPubKey;
            }

            // Create the transaction build context (used in BuildTransaction).
            var accountReference = new WalletAccountReference(walletName, coldAccount.Name);
            var context = new TransactionBuildContext(wallet.Network)
            {
                AccountReference = accountReference,
                // Specify a dummy change address to prevent a change (internal) address from being created.
                // Will be changed after the transaction is built and before it is signed.
                ChangeAddress = coldAccount.ExternalAddresses.First(),
                TransactionFee = feeAmount,
                MinConfirmations = 0,
                Shuffle = false,
                Sign = false,
                Recipients = new[] { new Recipient { Amount = amount, ScriptPubKey = destination, SubtractFeeFromAmount = subtractFeeFromAmount } }.ToList()
            };

            // Register the cold staking builder extension with the transaction builder.
            context.TransactionBuilder.Extensions.Add(new ColdStakingBuilderExtension(false));

            // Avoid script errors due to missing scriptSig.
            context.TransactionBuilder.StandardTransactionPolicy.ScriptVerify = null;

            return (context, coldAccount, destination);
        }

        internal Money EstimateWithdrawalTransactionFee(IWalletTransactionHandler walletTransactionHandler, string receivingAddress,
            string walletName, Money amount, bool subtractFeeFromAmount)
        {
            (TransactionBuildContext context, _, _) = this.GetWithdrawalTransactionBuildContext(receivingAddress, walletName, amount, null, subtractFeeFromAmount);

            Money estimatedFee = walletTransactionHandler.EstimateFee(context);

            return estimatedFee;
        }

        /// <summary>
        /// Gets the spendable transactions associated with cold wallet addresses.
        /// </summary>
        /// <param name="walletName">The name of the wallet.</param>
        /// <param name="isColdWalletAccount">The cold staking account to get the transactions for.</param>
        /// <param name="confirmations">The number of confirmations.</param>
        /// <returns>An enumeration of <see cref="UnspentOutputReference"/> items.</returns>
        public IEnumerable<UnspentOutputReference> GetSpendableTransactionsInColdWallet(string walletName, bool isColdWalletAccount, int confirmations = 0)
        {
            Guard.NotEmpty(walletName, nameof(walletName));

            Wallet.Wallet wallet = this.GetWallet(walletName);
            UnspentOutputReference[] res = null;
            lock (this.lockObject)
            {
                res = wallet.GetAllSpendableTransactions(this.ChainIndexer.Tip.Height, confirmations,
                    a => a.Index == (isColdWalletAccount ? ColdWalletAccountIndex : HotWalletAccountIndex)).ToArray();
            }

            this.logger.LogTrace("(-):*.Count={0}", res.Count());
            return res;
        }

        public List<Transaction> RetrieveFilteredUtxos(string walletName, string walletPassword, string transactionHex, FeeRate feeRate, string walletAccount = null)
        {
            var retrievalTransactions = new List<Transaction>();

            Transaction transactionToReclaim = this.network.Consensus.ConsensusFactory.CreateTransaction(transactionHex);

            foreach (TxOut output in transactionToReclaim.Outputs)
            {
                Wallet.Wallet wallet = this.GetWallet(walletName);

                HdAddress address = wallet.GetAllAddresses(Wallet.Wallet.AllAccounts).FirstOrDefault(a => a.ScriptPubKey == output.ScriptPubKey);

                // The address is not in the wallet so ignore this output.
                if (address == null)
                    continue;

                HdAccount destinationAccount = wallet.GetAccounts(Wallet.Wallet.NormalAccounts).First();

                // This shouldn't really happen unless the user has no proper accounts in the wallet.
                if (destinationAccount == null)
                    continue;

                Script destination = destinationAccount.GetFirstUnusedReceivingAddress().ScriptPubKey;

                ISecret extendedPrivateKey = wallet.GetExtendedPrivateKeyForAddress(walletPassword, address);

                Key privateKey = extendedPrivateKey.PrivateKey;

                var builder = new TransactionBuilder(this.network);

                var coin = new Coin(transactionToReclaim, output);

                builder.AddCoins(coin);
                builder.AddKeys(privateKey);
                builder.Send(destination, output.Value);
                builder.SubtractFees();
                builder.SendEstimatedFees(feeRate);

                Transaction builtTransaction = builder.BuildTransaction(true);

                retrievalTransactions.Add(builtTransaction);
            }

            return retrievalTransactions;
        }
    }
}
