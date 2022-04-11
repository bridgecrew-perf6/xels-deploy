using System;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using Xels.Bitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.SignalR;
using Xels.Bitcoin.Features.SmartContracts;
using Xels.Bitcoin.Features.SmartContracts.PoA;
using Xels.Bitcoin.Features.SmartContracts.Wallet;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.Diagnostic;
using Xels.Features.SQLiteWalletRepository;
using Xels.Features.Unity3dApi;
using Xels.Sidechains.Networks;

namespace Xels.CirrusD
{
    class Program
    {
        public static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        public static async Task MainAsync(string[] args)
        {
            try
            {
                // set the console window title to identify this as a Cirrus full node (for clarity when running Strax and Cirrus on the same machine)
                var nodeSettings = new NodeSettings(networksSelector: CirrusNetwork.NetworksSelector, protocolVersion: ProtocolVersion.CIRRUS_VERSION, args: args)
                {
                    MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
                };

                Console.Title = $"Cirrus Full Node {nodeSettings.Network.NetworkType}";

                IFullNode node = GetSideChainFullNode(nodeSettings);

                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }

        private static IFullNode GetSideChainFullNode(NodeSettings nodeSettings)
        {
            DbType dbType = nodeSettings.GetDbType();

            IFullNodeBuilder nodeBuilder = new FullNodeBuilder()
            .UseNodeSettings(nodeSettings, dbType)
            .UseBlockStore(dbType)
            .UseMempool()
            .AddSmartContracts(options =>
            {
                options.UseReflectionExecutor();
                options.UsePoAWhitelistedContracts();
            })
            .AddPoAFeature()
            .UsePoAConsensus(dbType)
            .CheckCollateralCommitment()

            // This needs to be set so that we can check the magic bytes during the Strat to Strax changeover.
            // Perhaps we can introduce a block height check rather?
            .SetCounterChainNetwork(StraxNetwork.MainChainNetworks[nodeSettings.Network.NetworkType]())

            .UseSmartContractWallet()
            .AddSQLiteWalletRepository()
            .UseApi()
            .UseUnity3dApi()
            .AddRPC()
            .AddSignalR(options =>
            {
                DaemonConfiguration.ConfigureSignalRForCirrus(options);
            })
            .UseDiagnosticFeature();

            return nodeBuilder.Build();
        }
    }
}
