﻿using System;
using System.Threading.Tasks;
using NBitcoin.Protocol;
using Xels.Bitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.Dns;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Utilities;
using Xels.Features.SQLiteWalletRepository;

namespace Xels.StraxDnsD
{
    /// <summary>
    /// Main entry point.
    /// </summary>
    public class Program
    {
        /// <summary>
        /// The async entry point for the Strax Dns process.
        /// </summary>
        /// <param name="args">Command line arguments.</param>
        /// <returns>A task used to await the operation.</returns>
        public static async Task Main(string[] args)
        {
            try
            {
                var nodeSettings = new NodeSettings(networksSelector: Networks.Strax, protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, args: args)
                {
                    MinProtocolVersion = ProtocolVersion.PROVEN_HEADER_VERSION
                };

                DbType dbType = nodeSettings.GetDbType();

                var dnsSettings = new DnsSettings(nodeSettings);

                if (string.IsNullOrWhiteSpace(dnsSettings.DnsHostName) || string.IsNullOrWhiteSpace(dnsSettings.DnsNameServer) || string.IsNullOrWhiteSpace(dnsSettings.DnsMailBox))
                    throw new ConfigurationException("When running as a DNS Seed service, the -dnshostname, -dnsnameserver and -dnsmailbox arguments must be specified on the command line.");

                // Run as a full node with DNS or just a DNS service?
                IFullNode node;
                if (dnsSettings.DnsFullNode)
                {
                    // Build the Dns full node.
                    node = new FullNodeBuilder()
                        .UseNodeSettings(nodeSettings, dbType)
                        .UseBlockStore(dbType)
                        .UsePosConsensus(dbType)
                        .UseMempool()
                        .UseWallet()
                        .AddSQLiteWalletRepository()
                        .AddPowPosMining(true)
                        .UseApi()
                        .AddRPC()
                        .UseDns()
                        .Build();
                }
                else
                {
                    // Build the Dns node.
                    node = new FullNodeBuilder()
                        .UseNodeSettings(nodeSettings, dbType)
                        .UsePosConsensus(dbType)
                        .UseApi()
                        .AddRPC()
                        .UseDns()
                        .Build();
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
    }
}
