using Xels.Bitcoin.Controllers.Models;

namespace Xels.Bitcoin.Features.BlockStore.Models
{
    public class LastBalanceDecreaseTransactionModel
    {
        public TransactionVerboseModel Transaction { get; set; }

        public int BlockHeight { get; set; }
    }
}
