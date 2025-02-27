﻿using System;
using NBitcoin;

namespace Xels.Bitcoin.Features.Wallet.Interfaces
{
    public interface IWalletSyncManager
    {
        /// <summary>
        /// Starts the walletSyncManager.
        /// </summary>
        void Start();

        /// <summary>
        /// Stops the walletSyncManager.
        /// <para>
        /// We need to call <see cref="Stop"/> explicitly to check that the internal async loop isn't still running
        /// and subsequentlly dispose of it properly.
        /// </para>
        /// </summary>
        void Stop();

        /// <summary>
        /// Processes a new block.
        /// </summary>
        /// <param name="block">The block to process.</param>
        void ProcessBlock(Block block);

        /// <summary>
        /// Processes a new transaction which is in a pending state (not included in a block).
        /// </summary>
        /// <param name="transaction">The transaction to process.</param>
        void ProcessTransaction(Transaction transaction);

        /// <summary>
        /// Synchronize the wallet starting from the date passed as a parameter.
        /// </summary>
        /// <param name="date">The date from which to start the sync process.</param>
        /// <param name="walletName">The wallet to sync or <c>null</c> to rewind and sync all wallet.</param>
        void SyncFromDate(DateTime date, string walletName = null);

        /// <summary>
        /// Synchronize the wallet starting from the height passed as a parameter.
        /// </summary>
        /// <param name="height">The height from which to start the sync process.</param>
        /// <param name="walletName">The wallet to sync or <c>null</c> to rewind and sync all wallet.</param>
        void SyncFromHeight(int height, string walletName = null);

        /// <summary>
        /// The current tip of the wallet.
        /// </summary>
        ChainedHeader WalletTip { get; }
    }
}
