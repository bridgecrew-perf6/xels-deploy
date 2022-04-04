using System;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using Xels.Bitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.ColdStaking;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.ExternalApi;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.SignalR;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Utilities;
using Xels.Features.Diagnostic;
using Xels.Features.SQLiteWalletRepository;
using Xels.Features.Unity3dApi;

namespace Xels.StraxD
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            try
            {
                var nodeSettings = new NodeSettings(networksSelector: Networks.Strax, protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, args: args)
                {
                    MinProtocolVersion = ProtocolVersion.PROVEN_HEADER_VERSION
                };

                // Set the console window title to identify this as a Strax full node (for clarity when running Strax and Cirrus on the same machine).
                Console.Title = $"Strax Full Node {nodeSettings.Network.NetworkType}";

                DbType dbType = nodeSettings.GetDbType();

                IFullNodeBuilder nodeBuilder = new FullNodeBuilder()
                    .UseNodeSettings(nodeSettings, dbType)
                    .UseBlockStore(dbType)
                    .UsePosConsensus(dbType)
                    .UseMempool()
                    .UseColdStakingWallet()
                    .AddSQLiteWalletRepository()
                    .AddPowPosMining(true)
                    .UseApi()
                    .UseUnity3dApi()
                    .AddRPC()
                    .AddSignalR(options =>
                    {
                        DaemonConfiguration.ConfigureSignalRForStrax(options);
                    })
                    .UseDiagnosticFeature()
                    .AddExternalApi();

                IFullNode node = nodeBuilder.Build();

                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex);
            }
        }
    }
}
