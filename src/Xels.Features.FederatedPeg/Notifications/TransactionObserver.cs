﻿using NBitcoin;
using Xels.Bitcoin.EventBus;
using Xels.Bitcoin.EventBus.CoreEvents;
using Xels.Bitcoin.Signals;
using Xels.Features.FederatedPeg.Interfaces;

namespace Xels.Features.FederatedPeg.Notifications
{
    /// <summary>
    /// Observer that receives notifications about the arrival of new <see cref="Transaction"/>s.
    /// </summary>
    public class TransactionObserver
    {
        private readonly IFederationWalletSyncManager walletSyncManager;
        private readonly IInputConsolidator inputConsolidator;
        private readonly ISignals signals;

        private readonly SubscriptionToken transactionReceivedSubscription;

        public TransactionObserver(IFederationWalletSyncManager walletSyncManager,
            IInputConsolidator inputConsolidator,
            ISignals signals)
        {
            this.walletSyncManager = walletSyncManager;
            this.inputConsolidator = inputConsolidator;
            this.signals = signals;
            this.transactionReceivedSubscription = this.signals.Subscribe<TransactionReceived>(ev => this.OnReceivingTransaction(ev.ReceivedTransaction));

            // TODO: Dispose with Detach ??
        }

        /// <summary>
        /// Manages what happens when a new transaction is received.
        /// </summary>
        /// <param name="transaction">The new transaction</param>
        private void OnReceivingTransaction(Transaction transaction)
        {
            this.walletSyncManager.ProcessTransaction(transaction);
            this.inputConsolidator.ProcessTransaction(transaction);
        }
    }
}
