﻿using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.RPC.Controllers;
using Xels.Bitcoin.Features.RPC.Models;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.Interfaces;
using Xunit;

namespace Xels.Bitcoin.IntegrationTests.RPC
{
    public class GetInfoActionTests : BaseRPCControllerTest
    {
        [Fact]
        public void CallWithDependencies()
        {
            string dir = CreateTestDir(this);
            IFullNode fullNode = this.BuildServicedNode(dir);
            var controller = fullNode.NodeController<FullNodeController>();

            Assert.NotNull(fullNode.NodeService<INetworkDifficulty>(true));

            GetInfoModel info = controller.GetInfo();

            NodeSettings nodeSettings = NodeSettings.Default(fullNode.Network);
            uint expectedProtocolVersion = (uint)nodeSettings.ProtocolVersion;
            decimal expectedRelayFee = nodeSettings.MinRelayTxFeeRate.FeePerK.ToUnit(NBitcoin.MoneyUnit.BTC);
            Assert.NotNull(info);
            Assert.Equal(0, info.Blocks);
            Assert.NotEqual<uint>(0, info.Version);
            Assert.Equal(expectedProtocolVersion, info.ProtocolVersion);
            Assert.Equal(0, info.TimeOffset);
            Assert.Equal(0, info.Connections);
            Assert.NotNull(info.Proxy);
            Assert.Equal(0, info.Difficulty);
            Assert.True(info.Testnet);
            Assert.Equal(expectedRelayFee, info.RelayFee);
            Assert.Empty(info.Errors);
        }
    }
}
