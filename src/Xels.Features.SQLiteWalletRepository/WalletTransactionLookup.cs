﻿using System.Collections.Generic;
using NBitcoin;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Utilities;
using Xels.Features.SQLiteWalletRepository.External;
using Xels.Features.SQLiteWalletRepository.Tables;

namespace Xels.Features.SQLiteWalletRepository
{
    internal class WalletTransactionLookup : BaseLookup, IWalletTransactionLookup
    {
        private readonly DBConnection conn;
        private readonly int? walletId;

        internal WalletTransactionLookup(DBConnection conn, int? walletId) :
            // Create a bigger hash table if its shared.
            // TODO: Make this configurable.
            base(conn.Repository.DatabasePerWallet ? 20 : 26)
        {
            this.conn = conn;
            this.walletId = walletId;
        }

        /// <inheritdoc />
        public bool Contains(OutPoint outPoint, out HashSet<AddressIdentifier> addresses)
        {
            return base.Contains(outPoint.ToBytes(), out addresses) ?? Exists(outPoint, out addresses);
        }

        /// <inheritdoc />
        public void Confirm()
        {
            base.Confirm(o => { var x = new OutPoint(); x.FromBytes(o); return this.Exists(x, out _); });
        }

        /// <inheritdoc />
        public void AddTentative(OutPoint outPoint, AddressIdentifier address)
        {
            base.AddTentative(outPoint.ToBytes(), address);
        }

        /// <inheritdoc />
        public void AddSpendableTransactions(int? walletId = null, int? accountIndex = null, int? fromBlock = null)
        {
            Guard.Assert((walletId ?? this.walletId) == (this.walletId ?? walletId));

            walletId = this.walletId ?? walletId;

            string strWalletId = DBParameter.Create(walletId);
            string strAccountIndex = DBParameter.Create(accountIndex);

            List<HDTransactionData> spendableTransactions = this.conn.Query<HDTransactionData>($@"
                SELECT  *
                FROM    HDTransactionData{((fromBlock == null) ? $@"
                WHERE   SpendBlockHash IS NULL
                AND     SpendBlockHeight IS NULL" : $@"
                WHERE   SpendBlockHeight > {fromBlock} ")}{
                // Restrict to wallet if provided.
                ((walletId != null) ? $@"
                AND      WalletId = {strWalletId}" : "")}{
                // Restrict to account if provided.
                ((accountIndex != null) ? $@"
                AND     AccountIndex = {strAccountIndex}" : "")}");

            foreach (HDTransactionData transactionData in spendableTransactions)
                this.Add(new OutPoint(uint256.Parse(transactionData.OutputTxId), transactionData.OutputIndex));
        }

        private void Add(OutPoint outPoint)
        {
            this.Add(outPoint.ToBytes());
        }

        private bool Exists(OutPoint outPoint, out HashSet<AddressIdentifier> addresses)
        {
            string strWalletId = DBParameter.Create(this.walletId);

            addresses = new HashSet<AddressIdentifier>(
                this.conn.Query<AddressIdentifier>($@"
                SELECT  WalletId
                ,       AccountIndex
                ,       AddressType
                ,       AddressIndex
                ,       ScriptPubKey
                FROM    HDTransactionData
                WHERE   OutputTxId = ?
                AND     OutputIndex = ? {
                // Restrict to wallet if provided.
                // "BETWEEN" boosts performance from half a seconds to 2ms.
                ((this.walletId != null) ? $@"
                AND     WalletId BETWEEN {strWalletId} AND {strWalletId}" : $@"
                AND     WalletId IN (SELECT WalletId FROM HDWallet)")}",
                outPoint.Hash.ToString(),
                outPoint.N));

            return addresses.Count != 0;
        }
    }
}
