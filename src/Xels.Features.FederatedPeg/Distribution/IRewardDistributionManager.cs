using System.Collections.Generic;
using NBitcoin;
using Xels.Features.FederatedPeg.Wallet;

namespace Xels.Features.FederatedPeg.Distribution
{
    public interface IRewardDistributionManager
    {
        List<Recipient> DistributeToMultisigNodes(int blockHeight, Money totalReward);

        /// <summary>
        /// Finds the proportion of blocks mined by each miner.
        /// Creates a corresponding list of recipient scriptPubKeys and reward amounts.
        /// </summary>
        List<Recipient> Distribute(int blockHeight, Money totalReward);
    }
}
