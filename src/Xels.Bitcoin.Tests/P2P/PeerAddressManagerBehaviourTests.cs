﻿using System.Net;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.P2P;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.P2P.Protocol;
using Xels.Bitcoin.P2P.Protocol.Payloads;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Tests.P2P
{
    public sealed class PeerAddressManagerBehaviourTests : LogsTestBase
    {
        private readonly ILoggerFactory extendedLoggerFactory;
        private readonly INetworkPeerFactory networkPeerFactory;
        private readonly ConnectionManagerSettings connectionManagerSettings;
        private readonly ISignals signals;
        private readonly AsyncProvider asyncProvider;

        public PeerAddressManagerBehaviourTests()
        {
            this.extendedLoggerFactory = ExtendedLoggerFactory.Create();
            this.connectionManagerSettings = new ConnectionManagerSettings(NodeSettings.Default(this.Network));
            this.signals = new Bitcoin.Signals.Signals(this.extendedLoggerFactory, null);
            this.asyncProvider = new AsyncProvider(this.extendedLoggerFactory, this.signals);

            this.networkPeerFactory = new NetworkPeerFactory(this.Network,
                DateTimeProvider.Default,
                this.extendedLoggerFactory,
                new PayloadProvider().DiscoverPayloads(),
                new SelfEndpointTracker(this.extendedLoggerFactory, this.connectionManagerSettings),
                new Mock<IInitialBlockDownloadState>().Object,
                this.connectionManagerSettings,
                this.asyncProvider,
                new Mock<IPeerAddressManager>().Object);
        }

        [Fact]
        public void PeerAddressManagerBehaviour_ReceivedPing_UpdateLastSeen()
        {
            IPAddress ipAddress = IPAddress.Parse("::ffff:192.168.0.1");
            var endpoint = new IPEndPoint(ipAddress, 80);

            DataFolder peerFolder = CreateDataFolder(this);
            var addressManager = new PeerAddressManager(DateTimeProvider.Default, peerFolder, this.LoggerFactory.Object,
                new SelfEndpointTracker(this.extendedLoggerFactory, this.connectionManagerSettings));
            addressManager.AddPeer(endpoint, IPAddress.Loopback);

            var networkPeer = new Mock<INetworkPeer>();
            networkPeer.SetupGet(n => n.PeerEndPoint).Returns(endpoint);
            networkPeer.SetupGet(n => n.State).Returns(NetworkPeerState.HandShaked);

            var messageReceived = new AsyncExecutionEvent<INetworkPeer, IncomingMessage>();
            networkPeer.SetupGet(n => n.MessageReceived).Returns(messageReceived);

            var stateChanged = new AsyncExecutionEvent<INetworkPeer, NetworkPeerState>();
            networkPeer.SetupGet(n => n.StateChanged).Returns(stateChanged);

            var behaviour = new PeerAddressManagerBehaviour(DateTimeProvider.Default, addressManager, new Mock<IPeerBanning>().Object, this.extendedLoggerFactory) { Mode = PeerAddressManagerBehaviourMode.AdvertiseDiscover };
            behaviour.Attach(networkPeer.Object);

            var incomingMessage = new IncomingMessage();
            incomingMessage.Message = new Message(new PayloadProvider().DiscoverPayloads())
            {
                Magic = this.Network.Magic,
                Payload = new PingPayload(),
            };

            //Trigger the event handler
            networkPeer.Object.MessageReceived.ExecuteCallbacksAsync(networkPeer.Object, incomingMessage).GetAwaiter().GetResult();

            PeerAddress peer = addressManager.FindPeer(endpoint);
            Assert.Equal(DateTimeProvider.Default.GetUtcNow().Date, peer.LastSeen.Value.Date);
        }

        [Fact]
        public void PeerAddressManagerBehaviour_ReceivedPong_UpdateLastSeen()
        {
            IPAddress ipAddress = IPAddress.Parse("::ffff:192.168.0.1");
            var endpoint = new IPEndPoint(ipAddress, 80);

            DataFolder peerFolder = CreateDataFolder(this);
            var addressManager = new PeerAddressManager(DateTimeProvider.Default, peerFolder, this.LoggerFactory.Object,
                new SelfEndpointTracker(this.extendedLoggerFactory, this.connectionManagerSettings));
            addressManager.AddPeer(endpoint, IPAddress.Loopback);

            var networkPeer = new Mock<INetworkPeer>();
            networkPeer.SetupGet(n => n.PeerEndPoint).Returns(endpoint);
            networkPeer.SetupGet(n => n.State).Returns(NetworkPeerState.HandShaked);

            var messageReceived = new AsyncExecutionEvent<INetworkPeer, IncomingMessage>();
            networkPeer.SetupGet(n => n.MessageReceived).Returns(messageReceived);

            var stateChanged = new AsyncExecutionEvent<INetworkPeer, NetworkPeerState>();
            networkPeer.SetupGet(n => n.StateChanged).Returns(stateChanged);

            var behaviour = new PeerAddressManagerBehaviour(DateTimeProvider.Default, addressManager, new Mock<IPeerBanning>().Object, this.extendedLoggerFactory) { Mode = PeerAddressManagerBehaviourMode.AdvertiseDiscover };
            behaviour.Attach(networkPeer.Object);

            var incomingMessage = new IncomingMessage();
            incomingMessage.Message = new Message(new PayloadProvider().DiscoverPayloads())
            {
                Magic = this.Network.Magic,
                Payload = new PingPayload(),
            };

            //Trigger the event handler
            networkPeer.Object.MessageReceived.ExecuteCallbacksAsync(networkPeer.Object, incomingMessage).GetAwaiter().GetResult();

            PeerAddress peer = addressManager.FindPeer(endpoint);
            Assert.Equal(DateTimeProvider.Default.GetUtcNow().Date, peer.LastSeen.Value.Date);
        }

        [Fact]
        public void PeerAddressManagerBehaviour_DoesntSendAddress_Outbound()
        {
            IPAddress ipAddress = IPAddress.Parse("::ffff:192.168.0.1");
            var endpoint = new IPEndPoint(ipAddress, 80);

            DataFolder peerFolder = CreateDataFolder(this);
            var addressManager = new PeerAddressManager(DateTimeProvider.Default, peerFolder, this.LoggerFactory.Object,
                new SelfEndpointTracker(this.extendedLoggerFactory, this.connectionManagerSettings));
            addressManager.AddPeer(endpoint, IPAddress.Loopback);

            var networkPeer = new Mock<INetworkPeer>();
            networkPeer.SetupGet(n => n.PeerEndPoint).Returns(endpoint);
            networkPeer.SetupGet(n => n.State).Returns(NetworkPeerState.HandShaked);
            networkPeer.SetupGet(n => n.Inbound).Returns(false); // Outbound

            var messageReceived = new AsyncExecutionEvent<INetworkPeer, IncomingMessage>();
            networkPeer.SetupGet(n => n.MessageReceived).Returns(messageReceived);

            var stateChanged = new AsyncExecutionEvent<INetworkPeer, NetworkPeerState>();
            networkPeer.SetupGet(n => n.StateChanged).Returns(stateChanged);

            var behaviour = new PeerAddressManagerBehaviour(DateTimeProvider.Default, addressManager, new Mock<IPeerBanning>().Object, this.extendedLoggerFactory) { Mode = PeerAddressManagerBehaviourMode.AdvertiseDiscover };
            behaviour.Attach(networkPeer.Object);

            var incomingMessage = new IncomingMessage();
            incomingMessage.Message = new Message(new PayloadProvider().DiscoverPayloads())
            {
                Magic = this.Network.Magic,
                Payload = new GetAddrPayload(),
            };

            // Event handler triggered, but SendMessage shouldn't be called as the node is Outbound.
            networkPeer.Object.MessageReceived.ExecuteCallbacksAsync(networkPeer.Object, incomingMessage).GetAwaiter().GetResult();
            networkPeer.Verify(x => x.SendMessageAsync(It.IsAny<Payload>(), It.IsAny<CancellationToken>()), Times.Never);
        }

        [Fact]
        public void PeerAddressManagerBehaviour_DoesntSendAddress_Twice()
        {
            IPAddress ipAddress = IPAddress.Parse("::ffff:192.168.0.1");
            var endpoint = new IPEndPoint(ipAddress, 80);

            DataFolder peerFolder = CreateDataFolder(this);
            var addressManager = new PeerAddressManager(DateTimeProvider.Default, peerFolder, this.LoggerFactory.Object,
                new SelfEndpointTracker(this.extendedLoggerFactory, this.connectionManagerSettings));
            addressManager.AddPeer(endpoint, IPAddress.Loopback);

            var networkPeer = new Mock<INetworkPeer>();
            networkPeer.SetupGet(n => n.PeerEndPoint).Returns(endpoint);
            networkPeer.SetupGet(n => n.State).Returns(NetworkPeerState.HandShaked);
            networkPeer.SetupGet(n => n.Inbound).Returns(true);

            var messageReceived = new AsyncExecutionEvent<INetworkPeer, IncomingMessage>();
            networkPeer.SetupGet(n => n.MessageReceived).Returns(messageReceived);

            var stateChanged = new AsyncExecutionEvent<INetworkPeer, NetworkPeerState>();
            networkPeer.SetupGet(n => n.StateChanged).Returns(stateChanged);

            var behaviour = new PeerAddressManagerBehaviour(DateTimeProvider.Default, addressManager, new Mock<IPeerBanning>().Object, this.extendedLoggerFactory) { Mode = PeerAddressManagerBehaviourMode.AdvertiseDiscover };
            behaviour.Attach(networkPeer.Object);

            var incomingMessage = new IncomingMessage();
            incomingMessage.Message = new Message(new PayloadProvider().DiscoverPayloads())
            {
                Magic = this.Network.Magic,
                Payload = new GetAddrPayload(),
            };

            // Event handler triggered several times
            networkPeer.Object.MessageReceived.ExecuteCallbacksAsync(networkPeer.Object, incomingMessage).GetAwaiter().GetResult();
            networkPeer.Object.MessageReceived.ExecuteCallbacksAsync(networkPeer.Object, incomingMessage).GetAwaiter().GetResult();
            networkPeer.Object.MessageReceived.ExecuteCallbacksAsync(networkPeer.Object, incomingMessage).GetAwaiter().GetResult();

            // SendMessage should only be called once.
            networkPeer.Verify(x => x.SendMessageAsync(It.IsAny<Payload>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [Fact]
        public void PeerAddressManagerBehaviour_InboundConnectionIsLoopBack_Add_PeerEndPoint_ToAddressBook()
        {
            var addressFromEndpoint = new IPEndPoint(IPAddress.Loopback, this.Network.DefaultPort);

            IPAddress peerEndPointAddress = IPAddress.Parse("::ffff:192.168.0.1");
            var peerEndPoint = new IPEndPoint(peerEndPointAddress, 80);

            DataFolder peerFolder = CreateDataFolder(this);
            var addressManager = new PeerAddressManager(DateTimeProvider.Default, peerFolder, this.LoggerFactory.Object,
                new SelfEndpointTracker(this.extendedLoggerFactory, this.connectionManagerSettings));

            var networkPeer = new Mock<INetworkPeer>();
            networkPeer.SetupGet(n => n.Inbound).Returns(true);
            networkPeer.SetupGet(n => n.Network).Returns(this.Network);
            networkPeer.SetupGet(n => n.PeerEndPoint).Returns(peerEndPoint);
            networkPeer.SetupGet(n => n.PeerVersion).Returns(new VersionPayload() { AddressFrom = addressFromEndpoint });
            networkPeer.SetupGet(n => n.State).Returns(NetworkPeerState.HandShaked);

            var messageReceived = new AsyncExecutionEvent<INetworkPeer, IncomingMessage>();
            networkPeer.SetupGet(n => n.MessageReceived).Returns(messageReceived);

            var stateChanged = new AsyncExecutionEvent<INetworkPeer, NetworkPeerState>();
            networkPeer.SetupGet(n => n.StateChanged).Returns(stateChanged);

            var behaviour = new PeerAddressManagerBehaviour(DateTimeProvider.Default, addressManager, new Mock<IPeerBanning>().Object, this.extendedLoggerFactory) { Mode = PeerAddressManagerBehaviourMode.AdvertiseDiscover };
            behaviour.Attach(networkPeer.Object);

            // Trigger the event handler that signals that the peer has handshaked.
            networkPeer.Object.StateChanged.ExecuteCallbacksAsync(networkPeer.Object, NetworkPeerState.HandShaked).GetAwaiter().GetResult();

            // The address manager should contain the inbound peer's address.
            var endpointToFind = new IPEndPoint(peerEndPoint.Address, this.Network.DefaultPort);
            Assert.NotNull(addressManager.FindPeer(endpointToFind));
        }

        [Fact]
        public void PeerAddressManagerBehaviour_InboundConnectionIsNotLoopBack_Add_AddressFrom_ToAddressBook()
        {
            IPAddress addressFromIPAddress = IPAddress.Parse("::ffff:192.168.0.1");
            var addressFromEndpoint = new IPEndPoint(addressFromIPAddress, this.Network.DefaultPort);

            IPAddress peerEndPointAddress = IPAddress.Parse("::ffff:192.168.0.2");
            var peerEndPoint = new IPEndPoint(peerEndPointAddress, 80);

            DataFolder peerFolder = CreateDataFolder(this);
            var addressManager = new PeerAddressManager(DateTimeProvider.Default, peerFolder, this.LoggerFactory.Object,
                new SelfEndpointTracker(this.extendedLoggerFactory, this.connectionManagerSettings));

            var networkPeer = new Mock<INetworkPeer>();
            networkPeer.SetupGet(n => n.Inbound).Returns(true);
            networkPeer.SetupGet(n => n.Network).Returns(this.Network);
            networkPeer.SetupGet(n => n.PeerEndPoint).Returns(peerEndPoint);
            networkPeer.SetupGet(n => n.PeerVersion).Returns(new VersionPayload() { AddressFrom = addressFromEndpoint });
            networkPeer.SetupGet(n => n.State).Returns(NetworkPeerState.HandShaked);

            var messageReceived = new AsyncExecutionEvent<INetworkPeer, IncomingMessage>();
            networkPeer.SetupGet(n => n.MessageReceived).Returns(messageReceived);

            var stateChanged = new AsyncExecutionEvent<INetworkPeer, NetworkPeerState>();
            networkPeer.SetupGet(n => n.StateChanged).Returns(stateChanged);

            var behaviour = new PeerAddressManagerBehaviour(DateTimeProvider.Default, addressManager, new Mock<IPeerBanning>().Object, this.extendedLoggerFactory) { Mode = PeerAddressManagerBehaviourMode.AdvertiseDiscover };
            behaviour.Attach(networkPeer.Object);

            // Trigger the event handler that signals that the peer has handshaked.
            networkPeer.Object.StateChanged.ExecuteCallbacksAsync(networkPeer.Object, NetworkPeerState.HandShaked).GetAwaiter().GetResult();

            // The address manager should contain the inbound peer's address.
            var endpointToFind = new IPEndPoint(addressFromEndpoint.Address, this.Network.DefaultPort);
            Assert.NotNull(addressManager.FindPeer(endpointToFind));
        }
    }
}
