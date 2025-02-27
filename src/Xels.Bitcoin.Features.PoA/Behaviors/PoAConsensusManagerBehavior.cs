﻿using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.PoA.Payloads;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.P2P.Protocol;
using Xels.Bitcoin.P2P.Protocol.Payloads;
using Xels.Bitcoin.Utilities;

namespace Xels.Bitcoin.Features.PoA.Behaviors
{
    public class PoAConsensusManagerBehavior : ConsensusManagerBehavior
    {
        /// <summary>Instance logger.</summary>
        private readonly ILogger logger;

        public PoAConsensusManagerBehavior(ChainIndexer chainIndexer, IInitialBlockDownloadState initialBlockDownloadState,
            IConsensusManager consensusManager, IPeerBanning peerBanning, ILoggerFactory loggerFactory)
        : base(chainIndexer, initialBlockDownloadState, consensusManager, peerBanning, loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger(this.GetType().FullName, $"[{this.GetHashCode():x}] ");
        }

        /// <inheritdoc />
        /// <remarks>It replaces processing normal headers payloads with processing PoA headers payload.</remarks>
        protected override async Task OnMessageReceivedAsync(INetworkPeer peer, IncomingMessage message)
        {
            switch (message.Message.Payload)
            {
                case GetHeadersPayload getHeaders:
                    await this.ProcessGetHeadersAsync(peer, getHeaders).ConfigureAwait(false);
                    break;

                case PoAHeadersPayload headers:
                    await this.ProcessHeadersAsync(peer, headers.Headers.Cast<BlockHeader>().ToList()).ConfigureAwait(false);
                    break;
            }
        }

        /// <inheritdoc />
        /// <remarks>Creates <see cref="PoAHeadersPayload"/> instead of <see cref="HeadersPayload"/> like base implementation does.</remarks>
        protected override Payload ConstructHeadersPayload(GetHeadersPayload getHeadersPayload, out ChainedHeader lastHeader)
        {
            ChainedHeader fork = this.ChainIndexer.FindFork(getHeadersPayload.BlockLocator);
            lastHeader = null;

            if (fork == null)
            {
                this.logger.LogTrace("(-)[INVALID_LOCATOR]:null");
                return null;
            }

            var headersPayload = new PoAHeadersPayload();

            foreach (ChainedHeader chainedHeader in this.ChainIndexer.EnumerateToTip(fork).Skip(1))
            {
                lastHeader = chainedHeader;

                if (chainedHeader.Header is PoABlockHeader header)
                {
                    headersPayload.Headers.Add(header);

                    if ((chainedHeader.HashBlock == getHeadersPayload.HashStop) || (headersPayload.Headers.Count == MaxItemsPerHeadersMessage))
                        break;
                }
                else
                {
                    throw new Exception("Not a PoA header!");
                }
            }

            this.logger.LogDebug("{0} headers were selected for sending, last one is '{1}'.", headersPayload.Headers.Count, headersPayload.Headers.LastOrDefault()?.GetHash());

            return headersPayload;
        }

        /// <inheritdoc />
        public override object Clone()
        {
            return new PoAConsensusManagerBehavior(this.ChainIndexer, this.InitialBlockDownloadState, this.ConsensusManager, this.PeerBanning, this.LoggerFactory);
        }
    }
}
