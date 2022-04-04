using NBitcoin;
using Xels.Bitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.PoA.IntegrationTests.Common;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.SmartContracts;
using Xels.Bitcoin.Features.SmartContracts.PoA;
using Xels.Bitcoin.Features.SmartContracts.Wallet;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.Runners;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.SQLiteWalletRepository;

namespace Xels.Features.FederatedPeg.IntegrationTests.Utils
{
    public class SidechainUserNodeRunner : NodeRunner
    {
        private readonly IDateTimeProvider timeProvider;

        public SidechainUserNodeRunner(string dataDir, string agent, Network network, IDateTimeProvider dateTimeProvider)
            : base(dataDir, agent)
        {
            this.Network = network;
            this.timeProvider = dateTimeProvider;
        }

        public override void BuildNode()
        {
            var nodeSettings = new NodeSettings(this.Network, args: new string[] { "-conf=poa.conf", "-datadir=" + this.DataFolder });

            this.FullNode = (FullNode)new FullNodeBuilder()
                .UseNodeSettings(nodeSettings)
                .UseBlockStore()
                .AddSmartContracts(options =>
                {
                    options.UseReflectionExecutor();
                    options.UsePoAWhitelistedContracts();
                })
                .AddPoAFeature()
                .UsePoAConsensus()
                .AddPoAMiningCapability<SmartContractPoABlockDefinition>()
                .SetCounterChainNetwork(StraxNetwork.MainChainNetworks[nodeSettings.Network.NetworkType]())
                .UseSmartContractWallet()
                .AddSQLiteWalletRepository()
                .UseMempool()
                .UseApi()
                .MockIBD()
                .AddRPC()
                .ReplaceTimeProvider(this.timeProvider)
                .AddFastMiningCapability()
                .Build();
        }
    }
}
