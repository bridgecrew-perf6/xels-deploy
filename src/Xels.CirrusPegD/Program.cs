using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using NBitcoin.Protocol;
using Xels.Bitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.ExternalApi;
using Xels.Bitcoin.Features.Interop;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.Notifications;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.SmartContracts;
using Xels.Bitcoin.Features.SmartContracts.PoA;
using Xels.Bitcoin.Features.SmartContracts.Wallet;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.FederatedPeg;
using Xels.Features.SQLiteWalletRepository;
using Xels.Sidechains.Networks;

namespace Xels.CirrusPegD
{
    class Program
    {
        private const string MainchainArgument = "-mainchain";
        private const string SidechainArgument = "-sidechain";

        private static readonly Dictionary<NetworkType, Func<Network>> SidechainNetworks = new Dictionary<NetworkType, Func<Network>>
        {
            { NetworkType.Mainnet, CirrusNetwork.NetworksSelector.Mainnet },
            { NetworkType.Testnet, CirrusNetwork.NetworksSelector.Testnet },
            { NetworkType.Regtest, CirrusNetwork.NetworksSelector.Regtest }
        };

        private static void Main(string[] args)
        {
            RunFederationGatewayAsync(args).Wait();
        }

        private static async Task RunFederationGatewayAsync(string[] args)
        {
            try
            {
                bool isMainchainNode = args.FirstOrDefault(a => a.ToLower() == MainchainArgument) != null;
                bool isSidechainNode = args.FirstOrDefault(a => a.ToLower() == SidechainArgument) != null;

                if (isSidechainNode == isMainchainNode)
                {
                    throw new ArgumentException($"Gateway node needs to be started specifying either a {SidechainArgument} or a {MainchainArgument} argument");
                }

                IFullNode node = isMainchainNode ? GetMainchainFullNode(args) : GetSidechainFullNode(args);

                // set the console window title to identify which node this is (for clarity when running Strax and Cirrus on the same machine)
                Console.Title = isMainchainNode ? $"Strax Full Node {node.Network.NetworkType}" : $"Cirrus Full Node {node.Network.NetworkType}";

                if (node != null)
                    await node.RunAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }

        private static IFullNode GetMainchainFullNode(string[] args)
        {
            // TODO: Hardcode -addressindex

            var nodeSettings = new NodeSettings(networksSelector: Networks.Strax, protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, args: args)
            {
                MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
            };

            NetworkType networkType = nodeSettings.Network.NetworkType;

            DbType dbType = nodeSettings.GetDbType();

            IFullNode node = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings, dbType)
                .UseBlockStore(dbType)
                .SetCounterChainNetwork(SidechainNetworks[nodeSettings.Network.NetworkType]())
                .AddFederatedPeg(isMainChain: true)
                .UseTransactionNotification()
                .UseBlockNotification()
                .UseApi()
                .UseMempool()
                .AddRPC()
                .UsePosConsensus(dbType)
                .UseWallet()
                .AddSQLiteWalletRepository()
                .AddPowPosMining(true)
                .Build();

            return node;
        }

        private static IFullNode GetSidechainFullNode(string[] args)
        {
            var nodeSettings = new NodeSettings(networksSelector: CirrusNetwork.NetworksSelector, protocolVersion: ProtocolVersion.CIRRUS_VERSION, args: args)
            {
                MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
            };

            DbType dbType = nodeSettings.GetDbType();

            IFullNode node = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings, dbType)
                .UseBlockStore(dbType)
                .SetCounterChainNetwork(StraxNetwork.MainChainNetworks[nodeSettings.Network.NetworkType]())
                .AddPoAFeature()
                .UsePoAConsensus(dbType)
                .AddFederatedPeg()
                .AddPoACollateralMiningCapability<FederatedPegBlockDefinition>()
                .CheckCollateralCommitment()
                .AddDynamicMemberhip()
                .UseTransactionNotification()
                .UseBlockNotification()
                .UseApi()
                .UseMempool()
                .AddRPC()
                .AddSmartContracts(options =>
                {
                    options.UseReflectionExecutor();
                    options.UsePoAWhitelistedContracts();
                })
                .AddExternalApi()
                .AddInteroperability()
                .UseSmartContractWallet()
                .AddSQLiteWalletRepository()
                .Build();

            return node;
        }
    }
}
