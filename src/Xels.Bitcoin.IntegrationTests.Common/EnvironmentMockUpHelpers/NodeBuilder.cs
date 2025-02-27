﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using NBitcoin;
using NBitcoin.Protocol;
using NLog;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.IntegrationTests.Common.Runners;
using Xels.Bitcoin.Tests.Common;
using Xels.Features.SQLiteWalletRepository;

namespace Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers
{
    public class NodeBuilder : IDisposable
    {
        public List<CoreNode> Nodes { get; }

        public NodeConfigParameters ConfigParameters { get; }

        private readonly string rootFolder;

        public NodeBuilder(string rootFolder)
        {
            this.Nodes = new List<CoreNode>();
            this.ConfigParameters = new NodeConfigParameters();

            this.rootFolder = rootFolder;
        }

        public static NodeBuilder Create(object caller, [CallerMemberName] string callingMethod = null)
        {
            string testFolderPath = TestBase.CreateTestDir(caller, callingMethod);
            return CreateNodeBuilder(testFolderPath);
        }

        public static NodeBuilder Create(string testDirectory)
        {
            string testFolderPath = TestBase.CreateTestDir(testDirectory);
            return CreateNodeBuilder(testFolderPath);
        }

        /// <summary>
        /// Creates a node builder instance and disable logs.
        /// To enable logs please refer to the <see cref="WithLogsEnabled"/> method.
        /// </summary>
        /// <param name="testFolderPath">The test folder path.</param>
        /// <returns>A <see cref="NodeBuilder"/> instance with logs disabled.</returns>
        private static NodeBuilder CreateNodeBuilder(string testFolderPath)
        {
            return new NodeBuilder(testFolderPath);
        }

        private static string GetBitcoinCorePath(string version)
        {
            string path;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                path = $"../../../../External libs/Bitcoin Core/{version}/Windows/bitcoind.exe";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                path = $"../../../../External libs/Bitcoin Core/{version}/Linux/bitcoind";
            else
                path = $"../../../../External libs/Bitcoin Core/{version}/OSX/bitcoind";

            if (File.Exists(path))
                return path;

            throw new FileNotFoundException($"Could not load the file {path}.");
        }

        protected CoreNode CreateNode(NodeRunner runner, string configFile = "bitcoin.conf", bool useCookieAuth = false, NodeConfigParameters configParameters = null)
        {
            var node = new CoreNode(runner, configParameters, configFile, useCookieAuth);
            this.Nodes.Add(node);
            return node;
        }

        public CoreNode CreateBitcoinCoreNode(string version = "0.13.1", bool useCookieAuth = false, bool useNewConfigStyle = false)
        {
            string bitcoinDPath = GetBitcoinCorePath(version);
            return this.CreateNode(new BitcoinCoreRunner(this.GetNextDataFolderName(), bitcoinDPath, useNewConfigStyle), useCookieAuth: useCookieAuth);
        }

        /// <summary>
        /// Creates a Xels Proof-of-Work node.
        /// <para>
        /// <see cref="P2P.PeerDiscovery"/> and <see cref="P2P.PeerConnectorDiscovery"/> are disabled by default.
        /// </para>
        /// </summary>
        /// <param name="network">The network the node will run on.</param>
        /// <param name="agent">Overrides the node's agent prefix.</param>
        /// <param name="configParameters">Adds to the nodes configuration parameters.</param>
        /// <returns>The constructed PoW node.</returns>
        public CoreNode CreateXelsPowNode(Network network, string agent = null, NodeConfigParameters configParameters = null)
        {
            return CreateNode(new XelsBitcoinPowRunner(this.GetNextDataFolderName(), network, agent), configParameters: configParameters);
        }

        public CoreNode CreateXelsCustomPowNode(Network network, NodeConfigParameters configParameters)
        {
            var callback = new Action<IFullNodeBuilder>(builder => builder
               .UseBlockStore()
               .UsePowConsensus()
               .UseMempool()
               .AddMining()
               .UseWallet()
               .AddSQLiteWalletRepository()
               .AddRPC()
               .UseApi()
               .UseTestChainedHeaderTree()
               .MockIBD());

            return CreateCustomNode(callback, network, ProtocolVersion.PROTOCOL_VERSION, configParameters: configParameters);
        }

        /// <summary>
        /// Creates a Xels Proof-of-Stake node.
        /// <para>
        /// <see cref="P2P.PeerDiscovery"/> and <see cref="P2P.PeerConnectorDiscovery"/> are disabled by default.
        /// </para>
        /// </summary>
        /// <param name="network">The network the node will run on.</param>
        /// <param name="agent">Overrides the node's agent prefix.</param>
        /// <param name="configParameters">Adds to the nodes configuration parameters.</param>
        /// 
        /// <returns>The constructed PoS node.</returns>
        public CoreNode CreateXelsPosNode(Network network, string agent = "XelsBitcoin", NodeConfigParameters configParameters = null)
        {
            return CreateNode(new XelsBitcoinPosRunner(this.GetNextDataFolderName(), network, agent), network.DefaultConfigFilename, configParameters: configParameters);
        }

        /// <summary>
        /// Creates a Xels Proof-of-Stake node with the cold staking feature in lieu of the regular wallet.
        /// <para>
        /// <see cref="P2P.PeerDiscovery"/> and <see cref="P2P.PeerConnectorDiscovery"/> are disabled by default.
        /// </para>
        /// </summary>
        /// <param name="network">The network the node will run on.</param>
        /// <param name="agent">Overrides the node's agent prefix.</param>
        /// <param name="configParameters">Adds to the nodes configuration parameters.</param>
        /// 
        /// <returns>The constructed PoS node.</returns>
        public CoreNode CreateXelsColdStakingNode(Network network, string agent = "XelsBitcoin", NodeConfigParameters configParameters = null)
        {
            return CreateNode(new XelsBitcoinColdStakingRunner(this.GetNextDataFolderName(), network, agent), network.DefaultConfigFilename, configParameters: configParameters);
        }

        public CoreNode CloneXelsNode(CoreNode cloneNode, string agent = "XelsBitcoin")
        {
            var node = new CoreNode(new XelsBitcoinPowRunner(cloneNode.FullNode.Settings.DataFolder.RootPath, cloneNode.FullNode.Network, agent), this.ConfigParameters, cloneNode.FullNode.Network.DefaultConfigFilename);
            this.Nodes.Add(node);
            this.Nodes.Remove(cloneNode);
            return node;
        }

        /// <summary>A helper method to create a node instance with a non-standard set of features enabled. The node can be PoW or PoS, as long as the appropriate features are provided.</summary>
        /// <param name="callback">A callback accepting an instance of <see cref="IFullNodeBuilder"/> that constructs a node with a custom feature set.</param>
        /// <param name="network">The network the node will be running on.</param>
        /// <param name="protocolVersion">Use <see cref="ProtocolVersion.PROTOCOL_VERSION"/> for BTC PoW-like networks and <see cref="ProtocolVersion.ALT_PROTOCOL_VERSION"/> for Xels PoS-like networks.</param>
        /// <param name="agent">A user agent string to distinguish different node versions from each other.</param>
        /// <param name="configParameters">Use this to pass in any custom configuration parameters used to set up the CoreNode</param>
        public CoreNode CreateCustomNode(Action<IFullNodeBuilder> callback, Network network, ProtocolVersion protocolVersion = ProtocolVersion.PROTOCOL_VERSION, string agent = "Custom", NodeConfigParameters configParameters = null, ProtocolVersion minProtocolVersion = ProtocolVersion.PROTOCOL_VERSION)
        {
            configParameters = configParameters ?? new NodeConfigParameters();

            configParameters.SetDefaultValueIfUndefined("conf", "custom.conf");
            string configFileName = configParameters["conf"];

            configParameters.SetDefaultValueIfUndefined("datadir", this.GetNextDataFolderName(agent));
            string dataDir = configParameters["datadir"];

            configParameters.ToList().ForEach(p => this.ConfigParameters[p.Key] = p.Value);
            return CreateNode(new CustomNodeRunner(dataDir, callback, network, protocolVersion, configParameters, agent, minProtocolVersion), configFileName);
        }

        public string GetNextDataFolderName(string folderName = null)
        {
            string hash = Guid.NewGuid().ToString("N").Substring(0, 7);
            string numberedFolderName = string.Join(
                ".",
                new[] { hash, folderName }.Where(s => s != null));
            string dataFolderName = Path.Combine(this.rootFolder, numberedFolderName);

            return dataFolderName;
        }

        public void Dispose()
        {
            foreach (CoreNode node in this.Nodes)
                node.Kill();

            // Logs are static so clear them after every run.
            if (LogManager.Configuration != null)
            {
                LogManager.Configuration.LoggingRules.Clear();
                LogManager.ReconfigExistingLoggers();
            }
        }

        /// <summary>
        /// By default, logs are disabled when using <see cref="Create(string)"/> or <see cref="Create(object, string)"/> methods,
        /// by using this fluent method the caller can enable the logs at will.
        /// </summary>
        /// <returns>Current <see cref="NodeBuilder"/> instance, used for fluent API style</returns>
        /// <example>
        /// //default use (without logs)
        /// using (NodeBuilder builder = NodeBuilder.Create(this))
        /// {
        ///     //your test code here
        /// }
        ///
        /// //with logs enabled
        /// using (NodeBuilder builder = NodeBuilder.Create(this).WithLogsEnabled())
        /// {
        ///     //your test code here
        /// }
        /// </example>
        public NodeBuilder WithLogsEnabled()
        {
            // NLog Enable/Disable logging is based on internal counter. To ensure logs are enabled
            // keep calling EnableLogging until IsLoggingEnabled returns true.
            while (!LogManager.IsLoggingEnabled())
            {
                LogManager.EnableLogging();
            }

            return this;
        }

        /// <summary>
        /// If logs have been enabled by calling WithLogsEnabled you can disable it manually by calling this method.
        /// If the test is running within an "using block" where the nodebuilder is created, without using <see cref="WithLogsEnabled"/>,
        /// you shouldn't need to call this method.
        /// </summary>
        /// <returns>Current <see cref="NodeBuilder"/> instance, used for fluent API style.</returns>
        public NodeBuilder WithLogsDisabled()
        {
            // NLog Enable/Disable logging is based on internal counter. To ensure logs are disabled
            // keep calling DisableLogging until IsLoggingEnabled returns false.
            while (LogManager.IsLoggingEnabled())
            {
                LogManager.DisableLogging();
            }

            return this;
        }
    }
}