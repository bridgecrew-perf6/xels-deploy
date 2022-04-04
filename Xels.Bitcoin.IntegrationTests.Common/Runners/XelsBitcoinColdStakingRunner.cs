using NBitcoin;
using NBitcoin.Protocol;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.ColdStaking;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.P2P;
using Xels.Features.SQLiteWalletRepository;

namespace Xels.Bitcoin.IntegrationTests.Common.Runners
{
    // TODO: Should the regular PoS runner be changed to use cold staking by default instead of this additional class?
    public sealed class XelsBitcoinColdStakingRunner : NodeRunner
    {
        public XelsBitcoinColdStakingRunner(string dataDir, Network network, string agent = "XelsBitcoin")
            : base(dataDir, agent)
        {
            this.Network = network;
        }

        public override void BuildNode()
        {
            var settings = new NodeSettings(this.Network, ProtocolVersion.PROVEN_HEADER_VERSION, this.Agent, args: new string[] { $"-conf={this.Network.DefaultConfigFilename}", "-datadir=" + this.DataFolder, "-displayextendednodestats=true" });

            var builder = new FullNodeBuilder()
                .UseNodeSettings(settings)
                .UseBlockStore()
                .UsePosConsensus()
                .UseMempool()
                .UseColdStakingWallet()
                .AddSQLiteWalletRepository()
                .AddPowPosMining(!(this.Network is XelsMain || this.Network is XelsTest || this.Network is XelsRegTest))
                .AddRPC()
                .UseApi()
                .UseTestChainedHeaderTree()
                .MockIBD();

            if (this.OverrideDateTimeProvider)
                builder.OverrideDateTimeProviderFor<MiningFeature>();

            if (!this.EnablePeerDiscovery)
            {
                builder.RemoveImplementation<PeerConnectorDiscovery>();
                builder.ReplaceService<IPeerDiscovery, BaseFeature>(new PeerDiscoveryDisabled());
            }

            this.FullNode = (FullNode)builder.Build();
        }
    }
}
