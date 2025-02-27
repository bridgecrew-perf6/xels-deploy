﻿using System;
using System.Collections.Generic;
using SQLite;

namespace Xels.Features.SQLiteWalletRepository.Commands
{
    internal class DBCommand
    {
        private DBConnection conn;
        private SQLiteCommand sqLiteCommand;

        // Metrics.
        internal int ProcessCount;
        internal long ProcessTime;
        double AverageTime => (double)this.ProcessTime / this.ProcessCount;

        public DBCommand(DBConnection conn, string cmdText, params object[] ps)
        {
            this.conn = conn;
            this.sqLiteCommand = conn.SQLiteConnection.CreateCommand(cmdText, ps);
        }

        public void Bind(string name, object val)
        {
            this.sqLiteCommand.Bind(name, val);
        }

        public void ExecuteNonQuery()
        {
            long flagFall = DateTime.Now.Ticks;
            this.sqLiteCommand.ExecuteNonQuery();
            this.ProcessTime += (DateTime.Now.Ticks - flagFall);
            this.ProcessCount++;
        }

        public List<T> ExecuteQuery<T>()
        {
            long flagFall = DateTime.Now.Ticks;
            List<T> res = this.sqLiteCommand.ExecuteQuery<T>();
            this.ProcessTime += (DateTime.Now.Ticks - flagFall);
            this.ProcessCount++;
            return res;
        }
    }

    internal class PaymentToDelete
    {
        public int OutputTxTime { get; set; }
        public string OutputTxId { get; set; }
        public int OutputIndex { get; set; }
        public string ScriptPubKey { get; set; }
    };

    internal static class ProcessBlockCommands
    {
        public static void RegisterProcessBlockCommands(this DBConnection conn)
        {
            conn.Commands["CmdUploadPrevOut"] = CmdUploadPrevOut(conn);
            conn.Commands["CmdReplacePayments"] = CmdReplacePayments(conn);
            conn.Commands["CmdUpdateSpending"] = CmdUpdateSpending(conn);
            conn.Commands["CmdUpdateOverlaps"] = CmdUpdateOverlaps(conn);
        }

        public static DBCommand CmdUploadPrevOut(this DBConnection conn)
        {
            // UPSERTs TransactionDatas. If they already exist (i.e. as mempool transactions), they will be
            // "confirmed" into a block, otherwise they will be created and inserted.

            return conn.CreateCommand($@"
                INSERT INTO HDTransactionData
                SELECT A.WalletID
                ,      A.AccountIndex
                ,      A.AddressType
                ,      A.AddressIndex
                ,      T.RedeemScript
                ,      T.ScriptPubKey
                ,      T.Address
                ,      T.Value
                ,      T.OutputBlockHeight
                ,      T.OutputBlockHash
                ,      T.OutputTxIsCoinBase
                ,      T.OutputTxTime
                ,      T.OutputTxId
                ,      T.OutputIndex
                ,      NULL SpendTxTime
                ,      NULL SpendTxId
                ,      NULL SpendBlockHeight
                ,      NULL SpendBlockHash
                ,      NULL SpendTxIsCoinBase
                ,      NULL SpendTxTotalOut
                FROM   temp.TempOutput T
                JOIN   HDAddress A
                ON     A.ScriptPubKey = T.ScriptPubKey
                AND    A.WalletId IN (
                       SELECT WalletId
                       FROM   HDWallet
                       WHERE  IFNULL(@walletName, Name) = Name
                       AND    IFNULL(@prevHash, LastBlockSyncedHash) = LastBlockSyncedHash)
                WHERE  NOT (A.WalletID, A.AccountIndex, A.AddressType, A.AddressIndex, T.OutputTxId, T.OutputIndex) IN (
                       SELECT TD.WalletID, TD.AccountIndex, TD.AddressType, TD.AddressIndex, TD.OutputTxId, TD.OutputIndex
                       FROM   temp.TempOutput T
                       JOIN   HDTransactionData TD
                       ON     TD.OutputTxId = T.OutputTxId
                       AND    TD.OutputIndex = T.OutputIndex
                       AND    TD.ScriptPubKey = T.ScriptPubKey
                       AND    (TD.OutputBlockHash IS NOT NULL OR TD.OutputBlockHeight IS NOT NULL))
                ON CONFLICT(WalletId, AccountIndex, AddressType, AddressIndex, OutputTxId, OutputIndex) DO UPDATE SET 
                       OutputBlockHeight = excluded.OutputBlockHeight,
                       OutputBlockHash = excluded.OutputBlockHash");
        }

        public static DBCommand CmdReplacePayments(this DBConnection conn)
        {
            return conn.CreateCommand($@"
                REPLACE INTO HDPayment
                SELECT  T.SpendTxTime
                ,       T.SpendTxId
                ,       T.OutputTxId
                ,       T.OutputIndex
                ,       T.ScriptPubKey
                ,       O.OutputIndex SpendIndex
                ,       O.RedeemScript SpendScriptPubKey
                ,       O.Value SpendValue
                ,       O.IsChange SpendIsChange
                FROM    temp.TempPrevOut T
                JOIN    temp.TempOutput O
                ON      O.OutputTxId = T.SpendTxId
                AND     O.ScriptPubKey IS NULL
                JOIN    HDTransactionData TD
                ON      TD.OutputTxId = T.OutputTxId
                AND     TD.OutputIndex = T.OutputIndex
                AND     TD.ScriptPubKey = T.ScriptPubKey
                AND     TD.SpendBlockHeight IS NULL
                AND     TD.SpendBlockHash IS NULL
                AND     TD.WalletId IN (
                        SELECT   WalletId
                        FROM     HDWallet
                        WHERE    Name = IFNULL(@walletName, Name)
                        AND      LastBlockSyncedHash = IFNULL(@prevHash, LastBlockSyncedHash))");
        }

        public static DBCommand CmdUpdateSpending(this DBConnection conn)
        {
            return conn.CreateCommand($@"
                REPLACE INTO HDTransactionData
                SELECT TD.WalletId
                ,      TD.AccountIndex
                ,      TD.AddressType
                ,      TD.AddressIndex
                ,      TD.RedeemScript
                ,      TD.ScriptPubKey
                ,      TD.Address
                ,      TD.Value
                ,      TD.OutputBlockHeight
                ,      TD.OutputBlockHash
                ,      TD.OutputTxIsCoinBase
                ,      TD.OutputTxTime
                ,      TD.OutputTxId
                ,      TD.OutputIndex
                ,      T.SpendBlockHeight
                ,      T.SpendBlockHash
                ,      T.SpendTxIsCoinBase
                ,      T.SpendTxTime
                ,      T.SpendTxId
                ,      T.SpendTxTotalOut
                FROM   temp.TempPrevOut T
                JOIN   HDTransactionData TD
                ON     TD.OutputTxID = T.OutputTxId
                AND    TD.OutputIndex = T.OutputIndex
                AND    TD.ScriptPubKey = T.ScriptPubKey
                AND    TD.SpendBlockHeight IS NULL
                AND    TD.SpendBlockHash IS NULL
                AND    TD.WalletId IN (
                       SELECT   WalletId
                       FROM     HDWallet
                       WHERE    Name = IFNULL(@walletName, Name)
                       AND      LastBlockSyncedHash = IFNULL(@prevHash, LastBlockSyncedHash))");
        }

        public static DBCommand CmdUpdateOverlaps(this DBConnection conn)
        {
            // Gets conflicting transactions, while leaving the transactions themselves.

            return conn.CreateCommand($@"
                SELECT TD.*
                FROM   temp.TempPrevOut T
                JOIN   HDTransactionData TD
                ON     TD.OutputTxID = T.OutputTxId
                AND    TD.OutputIndex = T.OutputIndex
                AND    TD.ScriptPubKey = T.ScriptPubKey
                AND    TD.SpendTxId IS NOT NULL
                AND    TD.SpendTxId != T.SpendTxId
                AND    TD.WalletId IN (
                       SELECT   WalletId
                       FROM     HDWallet
                       WHERE    Name = IFNULL(@walletName, Name)
                       AND      LastBlockSyncedHash = IFNULL(@prevHash, LastBlockSyncedHash))");
        }
    }
}
