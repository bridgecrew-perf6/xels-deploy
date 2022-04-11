using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.TargetChain;
using Xunit;

namespace Xels.Features.FederatedPeg.Tests
{
    public class PartialTransactionsRequesterTests
    {
        private readonly ILogger logger;
        private readonly ILoggerFactory loggerFactory;
        private readonly ICrossChainTransferStore store;
        private readonly IAsyncProvider asyncProvider;
        private readonly INodeLifetime nodeLifetime;
        private readonly IFederatedPegBroadcaster federatedPegBroadcaster;
        private readonly IInputConsolidator inputConsolidator;

        private readonly IInitialBlockDownloadState ibdState;
        private readonly IFederatedPegSettings federationSettings;
        private readonly IFederationWalletManager federationWalletManager;

        public PartialTransactionsRequesterTests()
        {
            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.logger = Substitute.For<ILogger>();
            this.loggerFactory.CreateLogger(null).ReturnsForAnyArgs(this.logger);
            this.store = Substitute.For<ICrossChainTransferStore>();
            this.asyncProvider = Substitute.For<IAsyncProvider>();
            this.nodeLifetime = Substitute.For<INodeLifetime>();
            this.federatedPegBroadcaster = Substitute.For<IFederatedPegBroadcaster>();
            this.inputConsolidator = Substitute.For<IInputConsolidator>();
            this.ibdState = Substitute.For<IInitialBlockDownloadState>();
            this.federationSettings = Substitute.For<IFederatedPegSettings>();
            this.federationSettings.PublicKey.Returns("03191898544c4061ef427dd0a2feff8d7bf66ed6ae9db47f1a00e78f4a6439dc28");
            this.federationWalletManager = Substitute.For<IFederationWalletManager>();
            this.federationWalletManager.IsFederationWalletActive().Returns(true);
        }

        [Fact]
        public async Task DoesntBroadcastInIBDAsync()
        {
            this.ibdState.IsInitialBlockDownload().Returns(true);

            var partialRequester = new PartialTransactionRequester(
                this.store,
                this.asyncProvider,
                this.nodeLifetime,
                this.federatedPegBroadcaster,
                this.ibdState,
                this.federationWalletManager,
                this.inputConsolidator);

            await partialRequester.BroadcastPartialTransactionsAsync();

            this.store.Received(0).GetTransfersByStatus(Arg.Any<CrossChainTransferStatus[]>());
        }

        [Fact]
        public async Task DoesntBroadcastWithInactiveFederationAsync()
        {
            this.federationWalletManager.IsFederationWalletActive().Returns(false);

            var partialRequester = new PartialTransactionRequester(
                this.store,
                this.asyncProvider,
                this.nodeLifetime,
                this.federatedPegBroadcaster,
                this.ibdState,
                this.federationWalletManager,
                this.inputConsolidator);

            await partialRequester.BroadcastPartialTransactionsAsync();

            this.store.Received(0).GetTransfersByStatus(Arg.Any<CrossChainTransferStatus[]>());
        }
    }
}
