﻿using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin.Protocol;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.P2P;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Utilities;
using Xunit;
using Xunit.Abstractions;

namespace Xels.Bitcoin.Tests.P2P
{
    public sealed class NetworkPeerServerTests : LogsTestBase
    {
        private readonly ILoggerFactory extendedLoggerFactory;

        private readonly ITestOutputHelper testOutput;

        public NetworkPeerServerTests(ITestOutputHelper output)
        {
            this.testOutput = output;
            this.extendedLoggerFactory = ExtendedLoggerFactory.Create();
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, true)]
        [InlineData(true, true, false)]
        public void Validate_AllowClientConnection_State(bool inIBD, bool isWhiteListed, bool closeClient)
        {
            // Arrange
            var networkPeerFactory = new Mock<INetworkPeerFactory>();
            networkPeerFactory.Setup(npf => npf.CreateConnectedNetworkPeerAsync(It.IsAny<IPEndPoint>(),
                It.IsAny<NetworkPeerConnectionParameters>(),
                It.IsAny<NetworkPeerDisposer>())).Returns(Task.FromResult(new Mock<INetworkPeer>().Object));

            var initialBlockDownloadState = new Mock<IInitialBlockDownloadState>();
            initialBlockDownloadState.Setup(i => i.IsInitialBlockDownload()).Returns(inIBD);

            var nodeSettings = new NodeSettings(KnownNetworks.RegTest);
            var connectionManagerSettings = new ConnectionManagerSettings(nodeSettings);

            var endpointAddNode = new IPEndPoint(IPAddress.Parse("::ffff:192.168.0.1"), 80);

            var asyncProvider = this.CreateAsyncProvider();

            var peerAddressManager = new Mock<IPeerAddressManager>();
            peerAddressManager.Setup(pam => pam.FindPeersByIp(It.IsAny<IPEndPoint>())).Returns(new List<PeerAddress>());

            var networkPeerServer = new NetworkPeerServer(this.Network,
                endpointAddNode,
                endpointAddNode,
                ProtocolVersion.PROTOCOL_VERSION,
                this.extendedLoggerFactory,
                networkPeerFactory.Object,
                initialBlockDownloadState.Object,
                connectionManagerSettings,
                asyncProvider,
                peerAddressManager.Object,
                DateTimeProvider.Default);

            // Mimic external client
            const int portNumber = 80;
            var client = new TcpClient("www.xelsplatform.com", portNumber);

            string ip = string.Empty;
            var ipandport = client.Client.RemoteEndPoint.ToString();
            if (client.Client.RemoteEndPoint.AddressFamily == AddressFamily.InterNetwork)
            {
                ip = ipandport.Replace(ipandport[ipandport.IndexOf(':')..], "");
            }
            else
            {
                ip = ipandport.Substring(1, ipandport.LastIndexOf(']') - 1);
            }

            var endpointDiscovered = new IPEndPoint(IPAddress.Parse(ip), portNumber);

            // Include the external client as a NodeServerEndpoint.
            connectionManagerSettings.Bind.Add(new NodeServerEndpoint(endpointDiscovered, isWhiteListed));

            // Act
            var result = networkPeerServer.InvokeMethod("AllowClientConnection", client, new NetworkPeerCollection(), new List<IPEndPoint>());

            // Assert
            Assert.True((inIBD && !isWhiteListed) == closeClient);

            this.testOutput.WriteLine(
                $"In IBD : {inIBD}, " +
                $"Is White Listed : {isWhiteListed}, " +
                $"Close Client : {result}");
        }
    }
}