﻿using Xels.Bitcoin.Features.Miner;
using Xels.SmartContracts.Networks;
using Xunit;

namespace Xels.Bitcoin.Features.SmartContracts.Tests
{
    public class BlockBufferGeneratorTests
    {
        [Fact]
        public void Buffer_50Kb_For_1MB_BlockSize()
        {
            var network = new SmartContractsRegTest();
            var optionsFromNetwork = new BlockDefinitionOptions(network.Consensus.Options.MaxBlockWeight, network.Consensus.Options.MaxBlockBaseSize);
            BlockDefinitionOptions newOptions = new BlockBufferGenerator().GetOptionsWithBuffer(optionsFromNetwork);

            Assert.Equal((uint)950_000, newOptions.BlockMaxWeight);
            Assert.Equal((uint)950_000, newOptions.BlockMaxSize);
        }
    }
}