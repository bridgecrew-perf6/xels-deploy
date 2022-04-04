using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using NBitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Builder.Feature;
using Xels.Bitcoin.Features.Collateral.MempoolRules;
using Xels.Bitcoin.Features.PoA;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral.ConsensusRules;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.PoA.Voting;

namespace Xels.Features.Collateral
{
    /// <summary>
    /// Sets up the necessary components to check the collateral requirement is met on the counter chain.
    /// </summary>
    public class DynamicMembershipFeature : FullNodeFeature
    {
        private readonly JoinFederationRequestMonitor joinFederationRequestMonitor;
        private readonly Network network;

        public DynamicMembershipFeature(JoinFederationRequestMonitor joinFederationRequestMonitor, Network network)
        {
            this.joinFederationRequestMonitor = joinFederationRequestMonitor;
            this.network = network;
        }

        public override async Task InitializeAsync()
        {
            var options = (PoAConsensusOptions)this.network.Consensus.Options;
            if (options.VotingEnabled)
            {
                if (options.AutoKickIdleMembers)
                    await this.joinFederationRequestMonitor.InitializeAsync().ConfigureAwait(false);
            }
        }

        public override void Dispose()
        {
        }
    }

    /// <summary>
    /// A class providing extension methods for <see cref="IFullNodeBuilder"/>.
    /// </summary>
    public static class FullNodeBuilderDynamicMembershipFeatureExtension
    {
        // Both Cirrus Peg and Cirrus Miner calls this.
        public static IFullNodeBuilder AddDynamicMemberhip(this IFullNodeBuilder fullNodeBuilder)
        {
            Guard.Assert(fullNodeBuilder.Network.Consensus.ConsensusFactory is CollateralPoAConsensusFactory);

            if (!fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.Contains(typeof(VotingRequestFullValidationRule)))
                fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.Add(typeof(VotingRequestFullValidationRule));

            if (!fullNodeBuilder.Network.Consensus.MempoolRules.Contains(typeof(VotingRequestValidationRule)))
                fullNodeBuilder.Network.Consensus.MempoolRules.Add(typeof(VotingRequestValidationRule));

            // Disabling this for now until we can ensure that the "stale/duplicate poll issue is resolved."
            // if (!fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.Contains(typeof(MandatoryCollateralMemberVotingRule)))
            //    fullNodeBuilder.Network.Consensus.ConsensusRules.FullValidationRules.Add(typeof(MandatoryCollateralMemberVotingRule));

            fullNodeBuilder.ConfigureFeature(features =>
            {
                features.AddFeature<DynamicMembershipFeature>()
                    .DependOn<CounterChainFeature>()
                    .DependOn<PoAFeature>()
                    .FeatureServices(services =>
                    {
                        services.AddSingleton<IJoinFederationRequestService, JoinFederationRequestService>();
                        services.AddSingleton<JoinFederationRequestMonitor>();
                    });
            });

            return fullNodeBuilder;
        }
    }
}
