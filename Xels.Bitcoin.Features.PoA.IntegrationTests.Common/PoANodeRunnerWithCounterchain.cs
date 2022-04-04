using NBitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.SmartContracts;
using Xels.Bitcoin.Features.SmartContracts.PoA;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.Runners;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.SQLiteWalletRepository;

namespace Xels.Bitcoin.Features.PoA.IntegrationTests.Common
{
    public class PoANodeRunnerWithCounterchain : NodeRunner
    {
        private readonly IDateTimeProvider timeProvider;
        private readonly Network counterChain;

        public PoANodeRunnerWithCounterchain(string dataDir, PoANetwork network, Network counterChain, EditableTimeProvider timeProvider)
            : base(dataDir, null)
        {
            this.Network = network;
            this.timeProvider = timeProvider;
            this.counterChain = counterChain;
        }

        public override void BuildNode()
        {
            var settings = new NodeSettings(this.Network, args: new string[] { "-conf=poa.conf", "-datadir=" + this.DataFolder });

            this.FullNode = (FullNode)new FullNodeBuilder()
                .UseNodeSettings(settings)
                .UseBlockStore()
                .SetCounterChainNetwork(this.counterChain)
                .AddPoAFeature()
                .UsePoAConsensus()
                .AddPoAMiningCapability<PoABlockDefinition>()
                .AddDynamicMemberhip()
                .UseMempool()
                .UseWallet()
                .AddSQLiteWalletRepository()
                .UseApi()
                .AddRPC()
                .MockIBD()
                .UseTestChainedHeaderTree()
                .ReplaceTimeProvider(this.timeProvider)
                .AddFastMiningCapability()
                .Build();
        }
    }
}
