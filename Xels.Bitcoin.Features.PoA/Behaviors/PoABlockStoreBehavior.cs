using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.PoA.Payloads;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.P2P.Protocol.Payloads;

namespace Xels.Bitcoin.Features.PoA.Behaviors
{
    public sealed class PoABlockStoreBehavior : BlockStoreBehavior
    {
        public PoABlockStoreBehavior(ChainIndexer chainIndexer, IChainState chainState, ILoggerFactory loggerFactory, IConsensusManager consensusManager, IBlockStoreQueue blockStoreQueue)
            : base(chainIndexer, chainState, loggerFactory, consensusManager, blockStoreQueue)
        {
        }

        /// <inheritdoc />
        protected override Payload BuildHeadersAnnouncePayload(IEnumerable<ChainedHeader> headers)
        {
            var poaHeaders = headers.Select(s => s.Header).Cast<PoABlockHeader>().ToList();
            return new PoAHeadersPayload(poaHeaders);
        }

        public override object Clone()
        {
            var clone = new PoABlockStoreBehavior(this.ChainIndexer, this.chainState, this.loggerFactory, this.consensusManager, this.blockStoreQueue)
            {
                CanRespondToGetDataPayload = this.CanRespondToGetDataPayload
            };

            return clone;
        }
    }
}
