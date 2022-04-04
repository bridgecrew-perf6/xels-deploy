using System;
using Xels.Bitcoin.EventBus;
using Xels.Bitcoin.Features.Wallet.Events;

namespace Xels.Bitcoin.Features.SignalR.Events
{
    public sealed class WalletProcessedTransactionOfInterestClientEvent : IClientEvent
    {
        public string Source { get; set; }

        public Type NodeEventType { get; } = typeof(WalletProcessedTransactionOfInterestEvent);

        public void BuildFrom(EventBase @event)
        {
            if (@event is WalletProcessedTransactionOfInterestEvent txEvent)
            {
                this.Source = txEvent.Source;
                return;
            }

            throw new ArgumentException();
        }
    }
}
