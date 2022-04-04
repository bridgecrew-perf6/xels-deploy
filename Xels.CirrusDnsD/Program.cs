using System;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using Xels.Bitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.Dns;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.SmartContracts;
using Xels.Bitcoin.Features.SmartContracts.PoA;
using Xels.Bitcoin.Features.SmartContracts.Wallet;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.SQLiteWalletRepository;
using Xels.Sidechains.Networks;

namespace Xels.CirrusDnsD
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The async entry point for the Cirrus Dns process.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>A task used to await the operation.</returns>
        public static async Task Main(string[] args)
        {
            try
            {
                var nodeSettings = new NodeSettings(networksSelector: CirrusNetwork.NetworksSelector, protocolVersion: ProtocolVersion.CIRRUS_VERSION, args: args)
                {
                    MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
                };

                var dnsSettings = new DnsSettings(nodeSettings);

                if (string.IsNullOrWhiteSpace(dnsSettings.DnsHostName) || string.IsNullOrWhiteSpace(dnsSettings.DnsNameServer) || string.IsNullOrWhiteSpace(dnsSettings.DnsMailBox))
                    throw new ConfigurationException("When running as a DNS Seed service, the -dnshostname, -dnsnameserver and -dnsmailbox arguments must be specified on the command line.");

                IFullNode node;
                if (dnsSettings.DnsFullNode)
                {
                    // Build the Dns full node.
                    node = GetSideChainFullNode(nodeSettings);
                }
                else
                {
                    // Build the Dns node.
                    node = GetDnsNode(nodeSettings);
                }

                // Run node.
                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.ToString());
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
                .AddRPC()
                .UseDns();

            return nodeBuilder.Build();
        }

        private static IFullNode GetDnsNode(NodeSettings nodeSettings)
        {
            IFullNode node = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings)
                .AddPoAFeature()
                .UsePoAConsensus()
                .UseApi()
                .AddRPC()
                .UseDns()
                .AddRPC()
                .Build();

            return node;
        }
    }
}