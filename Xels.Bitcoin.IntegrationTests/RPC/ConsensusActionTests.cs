using NBitcoin;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Interfaces;
using Xunit;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Tests.Common;

namespace Xels.Bitcoin.IntegrationTests.RPC
{
    public class ConsensusActionTests : BaseRPCControllerTest
    {
        [Fact]
        public void CanCall_GetBestBlockHash()
        {
            string dir = CreateTestDir(this);

            IFullNode fullNode = this.BuildServicedNode(dir);
            var controller = fullNode.NodeController<ConsensusController>();

            uint256 result = controller.GetBestBlockHashRPC();

            Assert.Null(result);
        }

        [Fact]
        public void CanCall_GetBlockHash()
        {
            string dir = CreateTestDir(this);

            IFullNode fullNode = this.BuildServicedNode(dir);
            var controller = fullNode.NodeController<ConsensusController>();

            uint256 result = controller.GetBlockHashRPC(0);

            Assert.Null(result);
        }

        [Fact]
        public void CanCall_IsInitialBlockDownload()
        {
            string dir = CreateTestDir(this);

            IFullNode fullNode = this.BuildServicedNode(dir, KnownNetworks.StraxMain);
            var isIBDProvider = fullNode.NodeService<IInitialBlockDownloadState>(true);
            var chainState = fullNode.NodeService<IChainState>(true);
            chainState.ConsensusTip = new ChainedHeader(fullNode.Network.GetGenesis().Header, fullNode.Network.GenesisHash, 0);

            Assert.NotNull(isIBDProvider);
            Assert.True(isIBDProvider.IsInitialBlockDownload());
        }
    }
}
