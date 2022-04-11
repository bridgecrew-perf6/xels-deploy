using System;
using Xels.Bitcoin.EventBus;
using Xels.Bitcoin.EventBus.CoreEvents;

namespace Xels.Bitcoin.Features.SignalR.Events
{
    public class TransactionReceivedClientEvent : IClientEvent
    {
        public string TxHash { get; set; }

        public bool IsCoinbase { get; set; }

        public bool IsCoinstake { get; set; }
        
        public Type NodeEventType { get; } = typeof(TransactionReceived);

        public void BuildFrom(EventBase @event)
        {
            if (@event is TransactionReceived transactionReceived)
            {
                this.TxHash = transactionReceived.ReceivedTransaction.GetHash().ToString();
                this.IsCoinbase = transactionReceived.ReceivedTransaction.IsCoinBase;
                this.IsCoinstake = transactionReceived.ReceivedTransaction.IsCoinStake;

                return;
            }

            throw new ArgumentException();
        }
    }
}
