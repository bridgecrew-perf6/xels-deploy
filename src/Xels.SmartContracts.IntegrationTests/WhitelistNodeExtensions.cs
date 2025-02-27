﻿using System.Collections.Generic;
using NBitcoin;
using Xels.Bitcoin.Features.PoA.Voting;
using Xels.Bitcoin.Features.SmartContracts.Interfaces;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.SmartContracts.Tests.Common.MockChain;

namespace Xels.SmartContracts.IntegrationTests
{
    public static class WhitelistNodeExtensions
    {
        public static void WhitelistCode(this IMockChain chain, byte[] code)
        {
            foreach (MockChainNode node in chain.Nodes)
            {
                var hasher = node.CoreNode.FullNode.NodeService<IContractCodeHashingStrategy>();
                var hash = new uint256(hasher.Hash(code));
                node.CoreNode.FullNode.NodeService<IWhitelistedHashesRepository>().AddHash(hash);
            }
        }

        public static List<uint256> GetWhitelistedHashes(this CoreNode node)
        {
            return node.FullNode.NodeService<IWhitelistedHashesRepository>().GetHashes();
        }
    }
}