using Xels.Bitcoin.EventBus;
using Xels.Features.FederatedPeg.Interfaces;

namespace Xels.Features.FederatedPeg.Events
{
    /// <summary>
    /// Raised when the partial crosschain transactions of a deposit are merged together and the final transaction is fully signed.
    /// </summary>
    /// <seealso cref="EventBase" />
    public class CrossChainTransferTransactionFullySigned : EventBase
    {
        public ICrossChainTransfer Transfer { get; }

        public CrossChainTransferTransactionFullySigned(ICrossChainTransfer transfer)
        {
            this.Transfer = transfer;
        }
    }
}
