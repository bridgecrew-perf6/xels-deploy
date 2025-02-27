﻿using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.SmartContracts.PoW;
using Xels.Bitcoin.Mining;

namespace Xels.Bitcoin.Features.SmartContracts.PoS
{
    public sealed class SmartContractBlockProvider : IBlockProvider
    {
        private readonly Network network;

        /// <summary>Defines how proof of work blocks are built.</summary>
        private readonly SmartContractBlockDefinition powBlockDefinition;

        /// <summary>Defines how proof of work blocks are built on a proof-of-stake network.</summary>
        private readonly SmartContractPosPowBlockDefinition posPowBlockDefinition;

        public SmartContractBlockProvider(Network network, IEnumerable<BlockDefinition> definitions)
        {
            this.network = network;

            this.powBlockDefinition = definitions.OfType<SmartContractBlockDefinition>().FirstOrDefault();
            this.posPowBlockDefinition = definitions.OfType<SmartContractPosPowBlockDefinition>().FirstOrDefault();

        }

        /// <inheritdoc/>
        public BlockTemplate BuildPosBlock(ChainedHeader chainTip, Script script)
        {
            throw new System.NotImplementedException();
        }

        /// <inheritdoc/>
        public BlockTemplate BuildPowBlock(ChainedHeader chainTip, Script script)
        {
            if (this.network.Consensus.IsProofOfStake)
                return this.posPowBlockDefinition.Build(chainTip, script);

            return this.powBlockDefinition.Build(chainTip, script);
        }

        /// <inheritdoc/>
        public void BlockModified(ChainedHeader chainTip, Block block)
        {
            if (this.network.Consensus.IsProofOfStake)
            {
                this.posPowBlockDefinition.BlockModified(chainTip, block);
            }

            this.powBlockDefinition.BlockModified(chainTip, block);
        }
    }
}