﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.Extensions;

namespace Xels.Bitcoin.Configuration.Settings
{
    /// <summary>
    /// Configuration related to incoming and outgoing connections.
    /// </summary>
    public sealed class ConnectionManagerSettings
    {
        /// <summary>Maximum number of AgentPrefix characters to use in the Agent value.</summary>
        private const int MaximumAgentPrefixLength = 10;

        /// <summary>Default value for "blocksonly" option.</summary>
        /// <seealso cref="RelayTxes"/>
        private const bool DefaultBlocksOnly = false;

        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        /// <summary>List of end points that the node should try to connect to.</summary>
        /// <remarks>All access should be protected under <see cref="addNodeLock"/></remarks>
        private readonly List<IPEndPoint> addNode;

        /// <summary>
        /// Protects access to the list of addnode endpoints.
        /// </summary>
        private readonly object addNodeLock;

        /// <summary>
        /// Initializes an instance of the object from the node configuration.
        /// </summary>
        /// <param name="nodeSettings">The node configuration.</param>
        public ConnectionManagerSettings(NodeSettings nodeSettings)
        {
            Guard.NotNull(nodeSettings, nameof(nodeSettings));

            this.logger = nodeSettings.LoggerFactory.CreateLogger(typeof(ConnectionManagerSettings).FullName);

            this.addNodeLock = new object();

            this.Connect = new List<IPEndPoint>();

            lock (this.addNodeLock)
            {
                this.addNode = new List<IPEndPoint>();
            }

            this.Bind = new List<NodeServerEndpoint>();
            this.Whitelist = new List<IPEndPoint>();

            TextFileConfiguration config = nodeSettings.ConfigReader;

            try
            {
                this.Connect.AddRange(config.GetAll("connect", this.logger).Select(c => c.ToIPEndPoint(nodeSettings.Network.DefaultPort)));
            }
            catch (FormatException)
            {
                throw new ConfigurationException("Invalid 'connect' parameter.");
            }

            try
            {
                foreach (IPEndPoint addNode in config.GetAll("addnode", this.logger).Select(c => c.ToIPEndPoint(nodeSettings.Network.DefaultPort)))
                    this.AddAddNode(addNode);
            }
            catch (FormatException)
            {
                throw new ConfigurationException("Invalid 'addnode' parameter.");
            }

            this.Port = config.GetOrDefault<int>("port", nodeSettings.Network.DefaultPort, this.logger);

            try
            {
                IEnumerable<IPEndPoint> whitebindEndpoints = config.GetAll("whitebind", this.logger).Select(s => s.ToIPEndPoint(this.Port));

                this.Bind = whitebindEndpoints.Where(x => x.Address.AnyIP()).Select(x => new NodeServerEndpoint(x, true)).ToList();

                foreach (IPEndPoint endPoint in whitebindEndpoints.Where(x => !x.Address.AnyIP()))
                {
                    if (this.Bind.Select(x => x.Endpoint).Any(x => x.Contains(endPoint)))
                        continue;

                    this.Bind.Add(new NodeServerEndpoint(endPoint, true));
                }
            }
            catch (FormatException)
            {
                throw new ConfigurationException("Invalid 'whitebind' parameter");
            }

            try
            {
                foreach (NodeServerEndpoint endPoint in config.GetAll("bind").Select(c => new NodeServerEndpoint(c.ToIPEndPoint(this.Port), false)))
                {
                    if (this.Bind.Select(x => x.Endpoint).Any(x => x.Contains(endPoint.Endpoint)))
                        continue;

                    this.Bind.Add(endPoint);
                }
            }
            catch (FormatException)
            {
                throw new ConfigurationException("Invalid 'bind' parameter");
            }

            if (this.Bind.Count == 0)
            {
                this.Bind.Add(new NodeServerEndpoint(new IPEndPoint(IPAddress.Parse("0.0.0.0"), this.Port), false));
            }

            try
            {
                this.Whitelist.AddRange(config.GetAll("whitelist", this.logger).Select(c => c.ToIPEndPoint(nodeSettings.Network.DefaultPort)));
            }
            catch (FormatException)
            {
                throw new ConfigurationException("Invalid 'whitelist' parameter.");
            }

            string externalIp = config.GetOrDefault<string>("externalip", null, this.logger);
            if (externalIp != null)
            {
                try
                {
                    this.ExternalEndpoint = externalIp.ToIPEndPoint(this.Port);
                }
                catch (FormatException)
                {
                    throw new ConfigurationException("Invalid 'externalip' parameter");
                }
            }

            if (this.ExternalEndpoint == null)
            {
                this.ExternalEndpoint = new IPEndPoint(IPAddress.Loopback, this.Port);
            }

            this.BanTimeSeconds = config.GetOrDefault<int>("bantime", nodeSettings.Network.DefaultBanTimeSeconds, this.logger);

            // Listen option will default to true in case there are no connect option specified.
            // When running the node with connect option listen flag has to be explicitly passed to the node to enable listen flag.
            this.Listen = config.GetOrDefault<bool>("listen", !this.Connect.Any(), this.logger);

            this.MaxOutboundConnections = config.GetOrDefault<int>("maxoutboundconnections", nodeSettings.Network.DefaultMaxOutboundConnections, this.logger);
            if (this.MaxOutboundConnections <= 0)
                throw new ConfigurationException("The 'maxoutboundconnections' must be greater than zero.");

            this.MaxInboundConnections = config.GetOrDefault<int>("maxinboundconnections", nodeSettings.Network.DefaultMaxInboundConnections, this.logger);
            if (this.MaxInboundConnections < 0)
                throw new ConfigurationException("The 'maxinboundconnections' must be greater or equal to zero.");

            this.InitialConnectionTarget = config.GetOrDefault("initialconnectiontarget", 1, this.logger);
            this.SyncTimeEnabled = config.GetOrDefault<bool>("synctime", true, this.logger);
            this.RelayTxes = !config.GetOrDefault("blocksonly", DefaultBlocksOnly, this.logger);
            this.IpRangeFiltering = config.GetOrDefault<bool>("IpRangeFiltering", true, this.logger);

            var agentPrefix = config.GetOrDefault("agentprefix", string.Empty, this.logger).Replace("-", string.Empty);
            if (agentPrefix.Length > MaximumAgentPrefixLength)
                agentPrefix = agentPrefix.Substring(0, MaximumAgentPrefixLength);

            this.Agent = string.IsNullOrEmpty(agentPrefix) ? nodeSettings.Agent : $"{agentPrefix}-{nodeSettings.Agent}";
            this.logger.LogDebug("Agent set to '{0}'.", this.Agent);

            this.DisableAddNodePeerConnector = config.GetOrDefault("disableaddnodeconnector", false, this.logger);
        }

        public void AddAddNode(IPEndPoint addNode)
        {
            lock (this.addNodeLock)
            {
                this.addNode.Add(addNode);
            }
        }

        public void RemoveAddNode(IPEndPoint addNode)
        {
            lock (this.addNodeLock)
            {
                this.addNode.Remove(addNode);
            }
        }

        public List<IPEndPoint> RetrieveAddNodes()
        {
            lock (this.addNodeLock)
            {
                return this.addNode.ToList();
            }
        }

        /// <summary>
        /// Get the default configuration.
        /// </summary>
        /// <param name="builder">The string builder to add the settings to.</param>
        /// <param name="network">The network to base the defaults off.</param>
        public static void BuildDefaultConfigurationFile(StringBuilder builder, Network network)
        {
            builder.AppendLine("####ConnectionManager Settings####");
            builder.AppendLine($"#The default network port to connect to. Default { network.DefaultPort }.");
            builder.AppendLine($"#port={network.DefaultPort}");
            builder.AppendLine($"#Accept connections from the outside.");
            builder.AppendLine($"#listen=<0 or 1>");
            builder.AppendLine($"#This can be used to accept incoming connections when -connect is specified.");
            builder.AppendLine($"#forcelisten=<0 or 1>");
            builder.AppendLine($"#Specified node to connect to. Can be specified multiple times.");
            builder.AppendLine($"#connect=<ip:port>");
            builder.AppendLine($"#Add a node to connect to and attempt to keep the connection open. Can be specified multiple times.");
            builder.AppendLine($"#addnode=<ip:port>");
            builder.AppendLine($"#Bind to given address. Use [host]:port notation for IPv6. Can be specified multiple times.");
            builder.AppendLine($"#bind=<ip:port>");
            builder.AppendLine($"#Bind to given address and whitelist peers connecting to it. Use [host]:port notation for IPv6. Can be specified multiple times.");
            builder.AppendLine($"#whitebind=<ip:port>");
            builder.AppendLine($"#Whitelist peers having the given IP:port address, both inbound or outbound. Can be specified multiple times.");
            builder.AppendLine($"#whitelist=<ip:port>");
            builder.AppendLine($"#Specify your own public address.");
            builder.AppendLine($"#externalip=<ip>");
            builder.AppendLine($"#Number of seconds to keep misbehaving peers from reconnecting. Default {network.DefaultBanTimeSeconds}.");
            builder.AppendLine($"#bantime=<number>");
            builder.AppendLine($"#The maximum number of outbound connections. Default {network.DefaultMaxOutboundConnections}.");
            builder.AppendLine($"#maxoutboundconnections=<number>");
            builder.AppendLine($"#The maximum number of inbound connections. Default {network.DefaultMaxInboundConnections}.");
            builder.AppendLine($"#maxinboundconnections=<number>");
            builder.AppendLine($"#The number of connections to be reached before a 1 second connection interval (initally 100ms). Default 1.");
            builder.AppendLine($"#initialconnectiontarget=<number>");
            builder.AppendLine($"#Sync with peers. Default 1.");
            builder.AppendLine($"#synctime=1");
            builder.AppendLine($"#An optional prefix for the node's user agent shared with peers. Truncated if over { MaximumAgentPrefixLength } characters.");
            builder.AppendLine($"#agentprefix=<string>");
            builder.AppendLine($"#Enable bandwidth saving setting to send and received confirmed blocks only. Defaults to { (DefaultBlocksOnly ? 1 : 0) }.");
            builder.AppendLine($"#blocksonly={ (DefaultBlocksOnly ? 1 : 0) }");
            builder.AppendLine($"#bantime=<number>");
            builder.AppendLine($"#Disallow connection to peers in same IP range. Default is 1 for remote hosts.");
            builder.AppendLine($"#iprangefiltering=<0 or 1>");
        }

        /// <summary>
        /// Displays command-line help.
        /// </summary>
        /// <param name="network">The network to extract values from.</param>
        public static void PrintHelp(Network network)
        {
            Guard.NotNull(network, nameof(network));

            var defaults = NodeSettings.Default(network: network);

            var builder = new StringBuilder();
            builder.AppendLine($"-port=<port>              The default network port to connect to. Default { network.DefaultPort }.");
            builder.AppendLine($"-listen=<0 or 1>          Accept connections from the outside (defaulted to 1 unless -connect args specified).");
            builder.AppendLine($"-connect=<ip:port>        Specified node to connect to. Can be specified multiple times.");
            builder.AppendLine($"-addnode=<ip:port>        Add a node to connect to and attempt to keep the connection open. Can be specified multiple times.");
            builder.AppendLine($"-bind=<ip:port>           Bind to given address. Use [host]:port notation for IPv6. Can be specified multiple times.");
            builder.AppendLine($"-whitebind=<ip:port>      Bind to given address and whitelist peers connecting to it. Use [host]:port notation for IPv6. Can be specified multiple times.");
            builder.AppendLine($"-whitelist=<ip:port>      Whitelist peers having the given IP:port address, both inbound or outbound. Can be specified multiple times.");
            builder.AppendLine($"-externalip=<ip>          Specify your own public address.");
            builder.AppendLine($"-bantime=<number>         Number of seconds to keep misbehaving peers from reconnecting. Default {network.DefaultBanTimeSeconds}.");
            builder.AppendLine($"-maxoutboundconnections=<number> The maximum number of outbound connections. Default {network.DefaultMaxOutboundConnections}.");
            builder.AppendLine($"-maxinboundconnections=<number> The maximum number of inbound connections. Default {network.DefaultMaxInboundConnections}.");
            builder.AppendLine($"-initialconnectiontarget=<number> The number of connections to be reached before a 1 second connection interval (initally 100ms). Default 1.");
            builder.AppendLine($"-synctime=<0 or 1>        Sync with peers. Default 1.");
            builder.AppendLine($"-agentprefix=<string>     An optional prefix for the node's user agent that will be shared with peers in the version handshake.");
            builder.AppendLine($"-blocksonly=<0 or 1>      Enable bandwidth saving setting to send and received confirmed blocks only. Defaults to { DefaultBlocksOnly }.");
            builder.AppendLine($"-iprangefiltering=<0 or 1> Disallow connection to peers in same IP range. Default is 1 for remote hosts.");

            defaults.Logger.LogInformation(builder.ToString());
        }

        /// <summary>List of exclusive end points that the node should be connected to.</summary>
        public List<IPEndPoint> Connect { get; set; }

        /// <summary>
        /// Accepts incoming connections by starting the node server.
        /// <para>
        /// Set to true if no -connect args specified or explicility set to -listen=1.
        /// </para>
        /// <para>
        /// E.g.
        /// -listen=1 -connect=127.0.0.0 -> Listen = true
        /// -listen -connect=127.0.0.0 -> Listen = true
        /// -listen=0 -connect=127.0.0.0 -> Listen = false
        /// -connect=127.0.0.0 -> Listen = false
        /// </para>
        /// </summary>
        public bool Listen { get; set; }

        /// <summary>List of network interfaces on which the node should listen on.</summary>
        public List<NodeServerEndpoint> Bind { get; set; }

        /// <summary>External (or public) IP address of the node.</summary>
        public IPEndPoint ExternalEndpoint { get; internal set; }

        /// <summary>Port of the node.</summary>
        public int Port { get; internal set; }

        /// <summary>Number of seconds to keep misbehaving peers from reconnecting.</summary>
        public int BanTimeSeconds { get; internal set; }

        /// <summary>Maximum number of outbound connections.</summary>
        public int MaxOutboundConnections { get; internal set; }

        /// <summary>Maximum number of inbound connections.</summary>
        public int MaxInboundConnections { get; internal set; }

        /// <summary>
        /// The amount of connections to be reached before a 1 second connection interval in the <see cref="P2P.PeerConnectorDiscovery"/> is set.
        /// <para>
        /// When the <see cref="P2P.PeerConnectorDiscovery"/> starts up, a 100ms delay is set as the connection interval in order for
        /// the node to quickly connect to other peers.
        /// </para>
        /// </summary>
        public int InitialConnectionTarget { get; internal set; }

        /// <summary><c>true</c> to sync time with other peers and calculate adjusted time, <c>false</c> to use our system clock only.</summary>
        public bool SyncTimeEnabled { get; private set; }

        /// <summary>The node's user agent.</summary>
        public string Agent { get; private set; }

        /// <summary><c>true</c> to enable bandwidth saving setting to send and received confirmed blocks only.</summary>
        public bool RelayTxes { get; set; }

        /// <summary>Filter peers that are within the same IP range to prevent sybil attacks.</summary>
        public bool IpRangeFiltering { get; internal set; }

        /// <summary>List of white listed IP endpoint. The node will flags peers that connects to the node, or that the node connects to, as whitelisted.</summary>
        public List<IPEndPoint> Whitelist { get; set; }

        public bool DisableAddNodePeerConnector { get; set; }
    }
}