﻿using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Stratis.SmartContracts;

namespace Xels.SmartContracts.CLR.Tests
{
    public class InternalExecutorTestFixture
    {
        public InternalExecutorTestFixture()
        {
            var logger = new Mock<ILogger>();
            this.LoggerFactory = Mock.Of<ILoggerFactory>
                (l => l.CreateLogger(It.IsAny<string>()) == logger.Object);

            this.Snapshot = Mock.Of<IState>();

            var state = new Mock<IState>();
            state.Setup(s => s.Snapshot()).Returns(this.Snapshot);

            this.State = state;
            this.StateProcessor = new Mock<IStateProcessor>();

            this.GasMeter = new Mock<RuntimeObserver.IGasMeter>();

            var smartContractState = Mock.Of<ISmartContractState>(s =>
                s.Message == new Message("0x0000000000000000000000000000000000000001".HexToAddress(), "0x0000000000000000000000000000000000000002".HexToAddress(), 100));

            this.SmartContractState = smartContractState;

            this.FromAddress = smartContractState.Message.ContractAddress.ToUint160();
        }

        public uint160 FromAddress { get; }

        public ISmartContractState SmartContractState { get; }

        public Mock<RuntimeObserver.IGasMeter> GasMeter { get; }

        public IState Snapshot { get; }

        public Mock<IStateProcessor> StateProcessor { get; }

        public Mock<IState> State { get; }

        public ILoggerFactory LoggerFactory { get; }

        public void SetGasMeterLimitAbove(RuntimeObserver.Gas minimum)
        {
            this.GasMeter.SetupGet(g => g.GasAvailable).Returns((RuntimeObserver.Gas)(minimum + 1));
        }

        public void SetGasMeterLimitBelow(RuntimeObserver.Gas maximum)
        {
            this.GasMeter.SetupGet(g => g.GasAvailable).Returns((RuntimeObserver.Gas)(maximum - 1));
        }
    }
}