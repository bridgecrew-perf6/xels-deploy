﻿using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.Protocol;
using TracerAttributes;

namespace Xels.Bitcoin.P2P.Protocol.Payloads
{
    /// <summary>
    /// Block headers received after a getheaders messages.
    /// </summary>
    [Payload("headers")]
    public class HeadersPayload : Payload
    {
        private class BlockHeaderWithTxCount : IBitcoinSerializable
        {
            private BlockHeader header;

            public BlockHeader Header => this.header;

            public BlockHeaderWithTxCount()
            {
            }

            public BlockHeaderWithTxCount(BlockHeader header)
            {
                this.header = header;
            }

            [NoTrace]
            public void ReadWrite(BitcoinStream stream)
            {
                stream.ReadWrite(ref this.header);
                var txCount = new VarInt(0);
                stream.ReadWrite(ref txCount);

                // Xels adds an additional byte to the end of a header need to investigate why.
                if (stream.ConsensusFactory is PosConsensusFactory)
                    stream.ReadWrite(ref txCount);
            }
        }

        private List<BlockHeader> headers = new List<BlockHeader>();

        public List<BlockHeader> Headers { get { return this.headers; } }

        public HeadersPayload()
        {
        }

        public HeadersPayload(IEnumerable<BlockHeader> headers)
        {
            this.Headers.AddRange(headers);
        }

        [NoTrace]
        public override void ReadWriteCore(BitcoinStream stream)
        {
            if (stream.Serializing)
            {
                List<BlockHeaderWithTxCount> headersOff = this.headers.Select(h => new BlockHeaderWithTxCount(h)).ToList();
                stream.ReadWrite(ref headersOff);
            }
            else
            {
                this.headers.Clear();
                var headersOff = new List<BlockHeaderWithTxCount>();
                stream.ReadWrite(ref headersOff);
                this.headers.AddRange(headersOff.Select(h => h.Header));
            }
        }
    }
}