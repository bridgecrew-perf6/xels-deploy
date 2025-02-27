﻿using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.EventBus;
using Xels.Bitcoin.EventBus.CoreEvents;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.IntegrationTests.Common.Runners;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.P2P;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.P2P.Protocol.Payloads;
using Xels.Bitcoin.Primitives;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;

namespace Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers
{
    /// <summary>
    /// Full node wrapper for testing purposes.
    /// </summary>
    public class CoreNode
    {
        private readonly NetworkCredential creds;
        private readonly object lockObject = new object();
        private readonly ILoggerFactory loggerFactory;
        internal readonly NodeRunner runner;
        private List<Transaction> transactions = new List<Transaction>();

        public int ApiPort => int.Parse(this.ConfigParameters["apiport"]);

        public BitcoinSecret MinerSecret { get; private set; }
        public HdAddress MinerHDAddress { get; internal set; }
        public int ProtocolPort => int.Parse(this.ConfigParameters["port"]);
        public int RpcPort => int.Parse(this.ConfigParameters["rpcport"]);

        /// <summary>Location of the data directory for the node.</summary>
        public string DataFolder => this.runner.DataFolder;

        public IPEndPoint Endpoint => new IPEndPoint(IPAddress.Parse("127.0.0.1"), this.ProtocolPort);

        public string Config { get; }

        public NodeConfigParameters ConfigParameters { get; set; }

        public bool CookieAuth { get; set; }

        public Mnemonic Mnemonic { get; set; }

        public string WalletName => this.builderWalletName;
        public string WalletPassword => this.builderWalletPassword;

        private bool addRewardClaimer;
        private bool builderAlwaysFlushBlocks;
        private bool builderEnablePeerDiscovery;
        private bool builderNoValidation;
        private bool builderOverrideDateTimeProvider;
        private bool builderWithDummyWallet;
        private bool builderWithWallet;
        private string builderWalletName;
        private string builderWalletPassword;
        private string builderWalletPassphrase;
        private string builderWalletMnemonic;

        private SubscriptionToken blockConnectedSubscription;
        private SubscriptionToken blockDisconnectedSubscription;

        public CoreNode(NodeRunner runner, NodeConfigParameters configParameters, string configfile, bool useCookieAuth = false)
        {
            this.runner = runner;

            this.State = CoreNodeState.Stopped;
            string user = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20));
            string pass = Encoders.Hex.EncodeData(RandomUtils.GetBytes(20));
            this.creds = new NetworkCredential(user, pass);
            this.Config = Path.Combine(this.runner.DataFolder, configfile);
            this.CookieAuth = useCookieAuth;

            this.ConfigParameters = new NodeConfigParameters();
            if (configParameters != null)
                this.ConfigParameters.Import(configParameters);

            var randomFoundPorts = new int[3];
            IpHelper.FindPorts(randomFoundPorts);
            this.ConfigParameters.SetDefaultValueIfUndefined("port", randomFoundPorts[0].ToString());
            this.ConfigParameters.SetDefaultValueIfUndefined("rpcport", randomFoundPorts[1].ToString());
            this.ConfigParameters.SetDefaultValueIfUndefined("apiport", randomFoundPorts[2].ToString());

            this.loggerFactory = ExtendedLoggerFactory.Create();

            CreateConfigFile(this.ConfigParameters);
        }

        /// <summary>Get xels full node if possible.</summary>
        public FullNode FullNode => this.runner.FullNode;

        public CoreNodeState State { get; private set; }

        private string GetRPCAuth()
        {
            if (!this.CookieAuth)
                return this.creds.UserName + ":" + this.creds.Password;
            else
                return "cookiefile=" + Path.Combine(this.runner.DataFolder, "regtest", ".cookie");
        }

        public CoreNode NoValidation()
        {
            this.builderNoValidation = true;
            return this;
        }

        public CoreNode AddRewardClaimer()
        {
            this.addRewardClaimer = true;
            return this;
        }

        /// <summary>
        /// Executes a function when a block has connected.
        /// </summary>
        /// <param name="interceptor">A function that is called everytime a block connects.</param>
        /// <returns>This node.</returns>
        public CoreNode SetConnectInterceptor(Action<ChainedHeaderBlock> interceptor)
        {
            this.blockConnectedSubscription = this.FullNode.NodeService<ISignals>().Subscribe<BlockConnected>(ev => interceptor(ev.ConnectedBlock));

            return this;
        }

        /// <summary>
        /// Executes a function when a block has disconnected.
        /// </summary>
        /// <param name="interceptor">A function that is called when a block disconnects.</param>
        /// <returns>This node.</returns>
        public CoreNode SetDisconnectInterceptor(Action<ChainedHeaderBlock> interceptor)
        {
            this.blockDisconnectedSubscription = this.FullNode.NodeService<ISignals>().Subscribe<BlockDisconnected>(ev => interceptor(ev.DisconnectedBlock));

            return this;
        }

        /// <summary>
        /// Enables <see cref="PeerDiscovery"/> and <see cref="PeerConnectorDiscovery"/> which is disabled by default.
        /// </summary>
        /// <returns>This node.</returns>
        public CoreNode EnablePeerDiscovery()
        {
            this.builderEnablePeerDiscovery = true;
            return this;
        }

        public CoreNode AlwaysFlushBlocks()
        {
            this.builderAlwaysFlushBlocks = true;
            return this;
        }

        /// <summary>
        /// Overrides the node's date time provider with one where the current date time starts 2018-01-01.
        /// <para>
        /// This is primarily used where we want to mine coins in the past used for staking.
        /// </para>
        /// </summary>
        /// <returns>This node.</returns>
        public CoreNode OverrideDateTimeProvider()
        {
            this.builderOverrideDateTimeProvider = true;
            return this;
        }

        /// <summary>
        /// Overrides a node service.
        /// </summary>
        /// <param name="serviceToOverride">A function that will override a given service in the node.</param>
        /// <returns>This node.</returns>
        public CoreNode OverrideService(Action<IServiceCollection> serviceToOverride)
        {
            this.runner.ServiceToOverride = serviceToOverride;
            return this;
        }

        /// <summary>
        /// This does not create a physical wallet but only sets the miner secret on the node.
        /// </summary>
        /// <returns>This node.</returns>
        public CoreNode WithDummyWallet()
        {
            this.builderWithDummyWallet = true;
            this.builderWithWallet = false;
            return this;
        }

        /// <summary>
        /// Adds a wallet to this node with defaulted parameters.
        /// </summary>
        /// <param name="walletPassword">Wallet password defaulted to "password".</param>
        /// <param name="walletName">Wallet name defaulted to "mywallet".</param>
        /// <param name="walletPassphrase">Wallet passphrase defaulted to "passphrase".</param>
        /// <param name="walletMnemonic">Optional wallet mnemonic.</param>
        /// <returns>This node.</returns>
        public CoreNode WithWallet(string walletPassword = "password", string walletName = "mywallet", string walletPassphrase = "passphrase", string walletMnemonic = null)
        {
            this.builderWithDummyWallet = false;
            this.builderWithWallet = true;
            this.builderWalletName = walletName;
            this.builderWalletPassphrase = walletPassphrase;
            this.builderWalletPassword = walletPassword;
            this.builderWalletMnemonic = walletMnemonic;
            return this;
        }

        public CoreNode WithReadyBlockchainData(string readyDataName)
        {
            // Extract the zipped blockchain data to the node's DataFolder.
            ZipFile.ExtractToDirectory(Path.GetFullPath(readyDataName), this.DataFolder, true);

            // Import whole wallets to DB.
            this.startActions.Add(() =>
            {
                var walletManager = ((WalletManager)this.FullNode?.NodeService<IWalletManager>(true));
                if (walletManager != null)
                    walletManager.ExcludeTransactionsFromWalletImports = false;
            });

            return this;
        }

        public RPCClient CreateRPCClient()
        {
            Network network;

            network = this.FullNode?.Network ?? KnownNetworks.RegTest;

            return new RPCClient(this.GetRPCAuth(), new Uri("http://127.0.0.1:" + this.RpcPort + "/"), network);
        }

        public INetworkPeer CreateNetworkPeerClient()
        {
            ConnectionManagerSettings connectionManagerSettings = null;

            if (this.runner is BitcoinCoreRunner)
            {
                var nodeSettings = new NodeSettings(this.runner.Network, args: new string[] { "-conf=bitcoin.conf", "-datadir=" + this.runner.DataFolder });
                connectionManagerSettings = new ConnectionManagerSettings(nodeSettings);
            }
            else
            {
                connectionManagerSettings = this.runner.FullNode.ConnectionManager.ConnectionSettings;
            }

            var selfEndPointTracker = new SelfEndpointTracker(this.loggerFactory, connectionManagerSettings);

            // Needs to be initialized beforehand.
            selfEndPointTracker.UpdateAndAssignMyExternalAddress(new IPEndPoint(IPAddress.Parse("0.0.0.0").MapToIPv6Ex(), this.ProtocolPort), false);

            var ibdState = new Mock<IInitialBlockDownloadState>();
            ibdState.Setup(x => x.IsInitialBlockDownload()).Returns(() => true);

            var peerAddressManager = new Mock<IPeerAddressManager>().Object;

            var networkPeerFactory = new NetworkPeerFactory(this.runner.Network,
                DateTimeProvider.Default,
                this.loggerFactory,
                new PayloadProvider().DiscoverPayloads(),
                selfEndPointTracker,
                ibdState.Object,
                connectionManagerSettings,
                this.GetOrCreateAsyncProvider(),
                peerAddressManager
                );

            return networkPeerFactory.CreateConnectedNetworkPeerAsync("127.0.0.1:" + this.ProtocolPort).ConfigureAwait(false).GetAwaiter().GetResult();
        }

        private IAsyncProvider GetOrCreateAsyncProvider()
        {
            if (this.runner.FullNode == null)
                return new AsyncProvider(this.loggerFactory, new Signals.Signals(this.loggerFactory, null));
            else
                return this.runner.FullNode.NodeService<IAsyncProvider>();
        }

        List<Action> startActions = new List<Action>();
        List<Action> runActions = new List<Action>();

        public CoreNode Start(Action startAction = null)
        {
            lock (this.lockObject)
            {
                this.runner.AddRewardClaimer = this.addRewardClaimer;
                this.runner.AlwaysFlushBlocks = this.builderAlwaysFlushBlocks;
                this.runner.EnablePeerDiscovery = this.builderEnablePeerDiscovery;
                this.runner.OverrideDateTimeProvider = this.builderOverrideDateTimeProvider;

                if (this.builderNoValidation)
                    this.DisableValidation();

                this.runner.BuildNode();

                startAction?.Invoke();
                foreach (Action action in this.startActions)
                    action.Invoke();

                this.runner.Start();
                this.State = CoreNodeState.Starting;
            }

            if (this.runner is BitcoinCoreRunner)
                WaitForExternalNodeStartup();
            else
                StartXelsRunner();

            this.State = CoreNodeState.Running;

            foreach (Action runAction in this.runActions)
                runAction.Invoke();

            return this;
        }

        private void CreateConfigFile(NodeConfigParameters configParameters = null)
        {
            Directory.CreateDirectory(this.runner.DataFolder);

            configParameters = configParameters ?? new NodeConfigParameters();
            configParameters.SetDefaultValueIfUndefined("regtest", "1");
            configParameters.SetDefaultValueIfUndefined("rest", "1");
            configParameters.SetDefaultValueIfUndefined("server", "1");
            configParameters.SetDefaultValueIfUndefined("txindex", "1");

            if (this.runner is BitcoinCoreRunner)
            {
                // TODO: Migrate to using `generatetoaddress` RPC for newer Core versions
                configParameters.SetDefaultValueIfUndefined("deprecatedrpc", "generate");
            }

            if (!this.CookieAuth)
            {
                configParameters.SetDefaultValueIfUndefined("rpcuser", this.creds.UserName);
                configParameters.SetDefaultValueIfUndefined("rpcpassword", this.creds.Password);
            }

            configParameters.SetDefaultValueIfUndefined("printtoconsole", "1");

            configParameters.SetDefaultValueIfUndefined("keypool", "10");
            configParameters.SetDefaultValueIfUndefined("agentprefix", "node" + this.ProtocolPort);
            configParameters.Import(this.ConfigParameters);

            // Need special handling for config files used by newer versions of Bitcoin Core.
            // These have specialised sections for [regtest], [test] and [main] in which certain options
            // only have an effect when they appear in their respective section.
            var builder = new StringBuilder();

            // Scan for network setting. These need to be at the top of the config file.
            bool testnet = configParameters.Any(a => a.Key.Equals("testnet") && a.Value.Equals("1"));
            bool regtest = configParameters.Any(a => a.Key.Equals("regtest") && a.Value.Equals("1"));
            bool mainnet = !testnet && !regtest;

            if (testnet)
            {
                builder.AppendLine("testnet=1");
                if (this.runner.UseNewConfigStyle) builder.AppendLine("[test]");
            }

            if (regtest)
            {
                builder.AppendLine("regtest=1");
                if (this.runner.UseNewConfigStyle) builder.AppendLine("[regtest]");
            }

            if (mainnet)
            {
                // Mainnet is implied by the absence of both testnet and regtest. But it should still get its own config section.
                if (this.runner.UseNewConfigStyle) builder.AppendLine("[main]");
            }

            foreach (KeyValuePair<string, string> kv in configParameters)
            {
                if (kv.Key.Equals("testnet") || kv.Key.Equals("regtest")) continue;

                builder.AppendLine(kv.Key + "=" + kv.Value);
            }

            File.WriteAllText(this.Config, builder.ToString());
        }

        public void Restart()
        {
            this.Kill();
            this.Start();
        }

        /// <summary>
        /// Used with precompiled bitcoind and xelsd node
        /// executables, not SBFN runners.
        /// </summary>
        private void WaitForExternalNodeStartup()
        {
            TimeSpan duration = TimeSpan.FromMinutes(5);
            var cancellationToken = new CancellationTokenSource(duration).Token;
            TestBase.WaitLoop(() =>
            {
                try
                {
                    CreateRPCClient().GetBlockHashAsync(0).GetAwaiter().GetResult();
                    this.State = CoreNodeState.Running;
                    return true;
                }
                catch
                {
                    return false;
                }
            }, cancellationToken: cancellationToken,
                failureReason: $"Failed to invoke GetBlockHash on node instance after {duration}");
        }

        private void StartXelsRunner()
        {
            var timeToNodeInit = TimeSpan.FromMinutes(1);
            var timeToNodeStart = TimeSpan.FromMinutes(1);

            TestBase.WaitLoop(() => this.runner.FullNode != null,
                cancellationToken: new CancellationTokenSource(timeToNodeInit).Token,
                failureReason: $"Failed to assign instance of FullNode within {timeToNodeInit}");

            TestBase.WaitLoop(() => this.runner.FullNode.State == FullNodeState.Started,
                cancellationToken: new CancellationTokenSource(timeToNodeStart).Token,
                failureReason: $"Failed to achieve state = started within {timeToNodeStart}");

            if (this.builderWithDummyWallet)
                this.SetMinerSecret(new BitcoinSecret(new Key(), this.FullNode.Network));

            if (this.builderWithWallet)
            {
                (_, this.Mnemonic) = this.FullNode.WalletManager().CreateWallet(
                    this.builderWalletPassword,
                    this.builderWalletName,
                    this.builderWalletPassphrase,
                    string.IsNullOrEmpty(this.builderWalletMnemonic) ? null : new Mnemonic(this.builderWalletMnemonic));
            }
        }

        /// <summary>
        /// Clears all consensus rules for this node.
        /// </summary>
        public void DisableValidation()
        {
            this.runner.Network.Consensus.ConsensusRules.FullValidationRules.Clear();
            this.runner.Network.Consensus.ConsensusRules.HeaderValidationRules.Clear();
            this.runner.Network.Consensus.ConsensusRules.IntegrityValidationRules.Clear();
            this.runner.Network.Consensus.ConsensusRules.PartialValidationRules.Clear();
        }

        public void Broadcast(Transaction transaction)
        {
            using (INetworkPeer peer = this.CreateNetworkPeerClient())
            {
                peer.VersionHandshakeAsync().GetAwaiter().GetResult();
                peer.SendMessageAsync(new InvPayload(transaction)).GetAwaiter().GetResult();
                peer.SendMessageAsync(new TxPayload(transaction)).GetAwaiter().GetResult();
                this.PingPongAsync(peer).GetAwaiter().GetResult();
            }
        }

        /// <summary>
        /// Emit a ping and wait the pong.
        /// </summary>
        /// <param name="peer"><see cref="INetworkPeer"/>.</param>
        /// <param name="cancellation">See <see cref="CancellationToken"/>.</param>
        /// <returns>Latency.</returns>
        public async Task<TimeSpan> PingPongAsync(INetworkPeer peer, CancellationToken cancellation = default(CancellationToken))
        {
            using (var listener = new NetworkPeerListener(peer, this.GetOrCreateAsyncProvider()))
            {
                var ping = new PingPayload()
                {
                    Nonce = RandomUtils.GetUInt64()
                };

                DateTimeOffset before = DateTimeOffset.UtcNow;
                await peer.SendMessageAsync(ping, cancellation).ConfigureAwait(false);

                while ((await listener.ReceivePayloadAsync<PongPayload>(cancellation).ConfigureAwait(false)).Nonce != ping.Nonce)
                {
                }

                DateTimeOffset after = DateTimeOffset.UtcNow;

                return after - before;
            }
        }

        public void SelectMempoolTransactions()
        {
            RPCClient rpc = this.CreateRPCClient();
            uint256[] txs = rpc.GetRawMempool();
            Task<Transaction>[] tasks = txs.Select(t => rpc.GetRawTransactionAsync(t)).ToArray();
            Task.WaitAll(tasks);
            this.transactions.AddRange(tasks.Select(t => t.Result).ToArray());
        }

        public void Kill()
        {
            lock (this.lockObject)
            {
                this.runner.Stop();

                if (!this.runner.IsDisposed)
                {
                    throw new Exception($"Problem disposing of a node of type {this.runner.GetType()}.");
                }

                this.State = CoreNodeState.Killed;
            }
        }

        public DateTimeOffset? MockTime { get; set; }

        public void SetMinerSecret(BitcoinSecret secret)
        {
            this.MinerSecret = secret;
        }

        public async Task<Block[]> GenerateAsync(int blockCount, bool includeUnbroadcasted = true, bool broadcast = true)
        {
            RPCClient rpc = this.CreateRPCClient();
            BitcoinSecret dest = this.GetFirstSecret(rpc);
            uint256 bestBlock = rpc.GetBestBlockHash();
            var blocks = new List<Block>();
            DateTimeOffset now = this.MockTime == null ? DateTimeOffset.UtcNow : this.MockTime.Value;

            using (INetworkPeer peer = this.CreateNetworkPeerClient())
            {
                await peer.VersionHandshakeAsync().ConfigureAwait(false);

                var chain = bestBlock == this.runner.Network.GenesisHash ? new ChainIndexer(this.runner.Network) : this.GetChain(peer);

                for (int i = 0; i < blockCount; i++)
                {
                    uint nonce = 0;

                    var block = this.runner.Network.Consensus.ConsensusFactory.CreateBlock();
                    block.Header.HashPrevBlock = chain.Tip.HashBlock;
                    block.Header.Bits = block.Header.GetWorkRequired(rpc.Network, chain.Tip);
                    block.Header.UpdateTime(now, rpc.Network, chain.Tip);

                    var coinbase = this.runner.Network.CreateTransaction();
                    coinbase.AddInput(TxIn.CreateCoinbase(chain.Height + 1));
                    coinbase.AddOutput(new TxOut(rpc.Network.GetReward(chain.Height + 1), dest.GetAddress()));
                    block.AddTransaction(coinbase);

                    if (includeUnbroadcasted)
                    {
                        this.transactions = TestHelper.Reorder(this.transactions);
                        block.Transactions.AddRange(this.transactions);
                        this.transactions.Clear();
                    }

                    block.UpdateMerkleRoot();

                    while (!block.CheckProofOfWork())
                        block.Header.Nonce = ++nonce;

                    blocks.Add(block);
                    chain.SetTip(block.Header);
                }

                if (broadcast)
                    await this.BroadcastBlocksAsync(blocks.ToArray(), peer);
            }

            return blocks.ToArray();
        }

        /// <summary>
        /// Get the chain of headers from the peer (thread safe).
        /// </summary>
        /// <param name="peer">Peer to get chain from.</param>
        /// <param name="hashStop">The highest block wanted.</param>
        /// <param name="cancellationToken">See <see cref="CancellationToken"/>.</param>
        /// <returns>The chain of headers.</returns>
        private ChainIndexer GetChain(INetworkPeer peer, uint256 hashStop = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var chain = new ChainIndexer(peer.Network);
            this.SynchronizeChain(peer, chain, hashStop, cancellationToken);
            return chain;
        }

        /// <summary>
        /// Synchronize a given Chain to the tip of the given node if its height is higher. (Thread safe).
        /// </summary>
        /// <param name="peer">Node to synchronize the chain for.</param>
        /// <param name="chain">The chain to synchronize.</param>
        /// <param name="hashStop">The location until which it synchronize.</param>
        /// <param name="cancellationToken">See <see cref="CancellationToken"/>.</param>
        /// <returns>An enumeration of <see cref="ChainedHeader"/> objects added to the fork point.</returns>
        private IEnumerable<ChainedHeader> SynchronizeChain(INetworkPeer peer, ChainIndexer chain, uint256 hashStop = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            ChainedHeader oldTip = chain.Tip;
            List<ChainedHeader> headers = this.GetHeadersFromFork(peer, oldTip, hashStop, cancellationToken).ToList();
            if (headers.Count == 0)
                return new ChainedHeader[0];

            ChainedHeader newTip = headers[headers.Count - 1];

            if (newTip.Height <= oldTip.Height)
                throw new ProtocolException("No tip should have been recieved older than the local one");

            foreach (ChainedHeader header in headers)
            {
                if (!header.Validate(peer.Network))
                {
                    throw new ProtocolException("A header which does not pass proof of work verification has been received");
                }
            }

            chain.SetTip(newTip);

            return headers;
        }

        private async Task AssertStateAsync(INetworkPeer peer, NetworkPeerState peerState, CancellationToken cancellationToken = default(CancellationToken))
        {
            if ((peerState == NetworkPeerState.HandShaked) && (peer.State == NetworkPeerState.Connected))
                await peer.VersionHandshakeAsync(cancellationToken);

            if (peerState != peer.State)
                throw new InvalidOperationException("Invalid Node state, needed=" + peerState + ", current= " + this.State);
        }

        public IEnumerable<ChainedHeader> GetHeadersFromFork(INetworkPeer peer, ChainedHeader currentTip, uint256 hashStop = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            this.AssertStateAsync(peer, NetworkPeerState.HandShaked, cancellationToken).GetAwaiter().GetResult();

            using (var listener = new NetworkPeerListener(peer, this.GetOrCreateAsyncProvider()))
            {
                int acceptMaxReorgDepth = 0;
                while (true)
                {
                    // Get before last so, at the end, we should only receive 1 header equals to this one (so we will not have race problems with concurrent GetChains).
                    BlockLocator awaited = currentTip.Previous == null ? currentTip.GetLocator() : currentTip.Previous.GetLocator();
                    peer.SendMessageAsync(new GetHeadersPayload()
                    {
                        BlockLocator = awaited,
                        HashStop = hashStop
                    }, cancellationToken).GetAwaiter().GetResult();

                    while (true)
                    {
                        bool isOurs = false;
                        HeadersPayload headers = null;

                        using (CancellationTokenSource headersCancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
                        {
                            headersCancel.CancelAfter(TimeSpan.FromMinutes(1.0));
                            try
                            {
                                headers = listener.ReceivePayloadAsync<HeadersPayload>(headersCancel.Token).GetAwaiter().GetResult();
                            }
                            catch (OperationCanceledException)
                            {
                                acceptMaxReorgDepth += 6;
                                if (cancellationToken.IsCancellationRequested)
                                    throw;

                                // Send a new GetHeaders.
                                break;
                            }
                        }

                        // In the special case where the remote node is at height 0 as well as us, then the headers count will be 0.
                        if ((headers.Headers.Count == 0) && (peer.PeerVersion.StartHeight == 0) && (currentTip.HashBlock == peer.Network.GenesisHash))
                            yield break;

                        if ((headers.Headers.Count == 1) && (headers.Headers[0].GetHash() == currentTip.HashBlock))
                            yield break;

                        foreach (BlockHeader header in headers.Headers)
                        {
                            uint256 hash = header.GetHash();
                            if (hash == currentTip.HashBlock)
                                continue;

                            // The previous headers request timeout, this can arrive in case of big reorg.
                            if (header.HashPrevBlock != currentTip.HashBlock)
                            {
                                int reorgDepth = 0;
                                ChainedHeader tempCurrentTip = currentTip;
                                while (reorgDepth != acceptMaxReorgDepth && tempCurrentTip != null && header.HashPrevBlock != tempCurrentTip.HashBlock)
                                {
                                    reorgDepth++;
                                    tempCurrentTip = tempCurrentTip.Previous;
                                }

                                if (reorgDepth != acceptMaxReorgDepth && tempCurrentTip != null)
                                    currentTip = tempCurrentTip;
                            }

                            if (header.HashPrevBlock == currentTip.HashBlock)
                            {
                                isOurs = true;
                                currentTip = new ChainedHeader(header, hash, currentTip);

                                yield return currentTip;

                                if (currentTip.HashBlock == hashStop)
                                    yield break;
                            }
                            else break; // Not our headers, continue receive.
                        }

                        if (isOurs)
                            break;  //Go ask for next header.
                    }
                }
            }
        }

        public bool AddToXelsMempool(Transaction trx)
        {
            var state = new MempoolValidationState(true);
            return this.runner.FullNode.MempoolManager().Validator.AcceptToMemoryPool(state, trx).Result;
        }

        public async Task BroadcastBlocksAsync(Block[] blocks, INetworkPeer peer)
        {
            foreach (Block block in blocks)
            {
                await peer.SendMessageAsync(new InvPayload(block));
                await peer.SendMessageAsync(new BlockPayload(block));
            }
            await this.PingPongAsync(peer);
        }

        public Block[] FindBlock(int blockCount = 1, bool includeMempool = true)
        {
            this.SelectMempoolTransactions();
            return this.GenerateAsync(blockCount, includeMempool).GetAwaiter().GetResult();
        }

        private BitcoinSecret GetFirstSecret(RPCClient rpc)
        {
            if (this.MinerSecret != null)
                return this.MinerSecret;

            BitcoinSecret dest = rpc.ListSecrets().FirstOrDefault();
            if (dest != null) return dest;

            BitcoinAddress address = rpc.GetNewAddress();
            dest = rpc.DumpPrivKey(address);
            return dest;
        }

        public ChainedHeader GetTip()
        {
            return this.FullNode.NodeService<IConsensusManager>().Tip;
        }
    }
}