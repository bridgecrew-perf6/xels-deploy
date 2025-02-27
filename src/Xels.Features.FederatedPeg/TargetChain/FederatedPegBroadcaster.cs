﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.P2P.Protocol.Payloads;
using Xels.Features.FederatedPeg.Interfaces;

namespace Xels.Features.FederatedPeg.TargetChain
{
    public class FederatedPegBroadcaster : IFederatedPegBroadcaster
    {
        private readonly IConnectionManager connectionManager;
        private readonly IFederatedPegSettings federatedPegSettings;
        private readonly ILogger logger;

        public FederatedPegBroadcaster(
            IConnectionManager connectionManager,
            IFederatedPegSettings federatedPegSettings)
        {
            this.connectionManager = connectionManager;
            this.federatedPegSettings = federatedPegSettings;
            this.logger = LogManager.GetCurrentClassLogger();
        }

        /// <inheritdoc />
        public Task BroadcastAsync(Payload payload)
        {
            IEnumerable<INetworkPeer> connectedPeers = this.connectionManager.ConnectedPeers.Where(peer => (peer?.IsConnected ?? false) && this.federatedPegSettings.FederationNodeIpAddresses.Contains(peer.PeerEndPoint.Address));

            this.logger.LogTrace($"Sending {payload.GetType()} to {connectedPeers.Count()} peers.");

            Parallel.ForEach(connectedPeers, async (INetworkPeer peer) =>
            {
                try
                {
                    this.logger.LogTrace($"Sending {payload.GetType()} to {peer.RemoteSocketAddress}");
                    await peer.SendMessageAsync(payload).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception ex)
                {
                    this.logger.LogError($"Error sending {payload.GetType().Name} to {peer.PeerEndPoint.Address}:{ex.ToString()}");
                }
            });

            return Task.CompletedTask;
        }
    }
}
