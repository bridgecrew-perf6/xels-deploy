using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.EventBus.CoreEvents;
using Xels.Bitcoin.Features.Wallet.Services;
using Xels.Bitcoin.Networks;
using Xunit;

namespace Xels.Bitcoin.Features.Wallet.Tests
{
    public sealed class ReserveUtxoServiceTests
    {
        private readonly ILoggerFactory loggerFactory;
        private readonly Signals.Signals signals;
        private readonly Network network;

        public ReserveUtxoServiceTests()
        {
            this.loggerFactory = new ExtendedLoggerFactory();
            this.signals = new Signals.Signals(this.loggerFactory, null);
            this.network = new StraxTest();
        }

        [Fact]
        public void CanReserveUtxo()
        {
            var service = new ReserveUtxoService(this.loggerFactory, this.signals);
            var outpoint = new OutPoint(new uint256(0), 1);
            service.ReserveUtxos(new[] { outpoint });
            Assert.True(service.IsUtxoReserved(outpoint));
        }

        [Fact]
        public void CanUnReserveUtxo()
        {
            var transaction = this.network.CreateTransaction();
            var outpoint = new OutPoint(new uint256(0), 1);
            transaction.AddInput(new TxIn(outpoint));

            var block = this.network.CreateBlock();
            block.AddTransaction(transaction);

            var service = new ReserveUtxoService(this.loggerFactory, this.signals);
            service.ReserveUtxos(new[] { outpoint });
            Assert.True(service.IsUtxoReserved(outpoint));

            this.signals.Publish(new BlockConnected(new Primitives.ChainedHeaderBlock(block, new ChainedHeader(block.Header, block.Header.GetHash(), 1))));
            Assert.False(service.IsUtxoReserved(outpoint));
        }
    }
}