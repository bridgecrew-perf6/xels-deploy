﻿using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using NBitcoin.Networks;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Signals;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.TargetChain;
using Xels.Features.FederatedPeg.Wallet;
using Xels.Sidechains.Networks;
using Xunit;
using Recipient = Xels.Features.FederatedPeg.Wallet.Recipient;
using TransactionBuildContext = Xels.Features.FederatedPeg.Wallet.TransactionBuildContext;
using UnspentOutputReference = Xels.Features.FederatedPeg.Wallet.UnspentOutputReference;

namespace Xels.Features.FederatedPeg.Tests
{
    public class WithdrawalTransactionBuilderTests
    {
        private readonly Network network;
        private readonly Mock<ILoggerFactory> loggerFactory;
        private readonly Mock<ILogger> logger;
        private readonly Mock<IFederationWalletManager> federationWalletManager;
        private readonly Mock<IFederationWalletTransactionHandler> federationWalletTransactionHandler;
        private readonly Mock<IFederatedPegSettings> federationGatewaySettings;
        private readonly Mock<ISignals> signals;

        public WithdrawalTransactionBuilderTests()
        {
            this.loggerFactory = new Mock<ILoggerFactory>();
            this.network = NetworkRegistration.Register(new CirrusRegTest());
            this.federationWalletManager = new Mock<IFederationWalletManager>();
            this.federationWalletTransactionHandler = new Mock<IFederationWalletTransactionHandler>();
            this.federationGatewaySettings = new Mock<IFederatedPegSettings>();
            this.signals = new Mock<ISignals>();

            this.logger = new Mock<ILogger>();
            this.loggerFactory.Setup(x => x.CreateLogger(It.IsAny<string>())).Returns(this.logger.Object);

            this.federationWalletManager.Setup(x => x.Secret).Returns(new WalletSecret());

            this.federationWalletTransactionHandler.Setup(x => x.BuildTransaction(It.IsAny<TransactionBuildContext>())).Returns(this.network.CreateTransaction());
        }

        [Fact]
        public void FeeIsTakenFromRecipient()
        {
            Script redeemScript = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, new[] { new Key().PubKey, new Key().PubKey });

            this.federationWalletManager.Setup(x => x.GetSpendableTransactionsInWallet(It.IsAny<int>()))
                .Returns(new List<UnspentOutputReference>
                {
                    new UnspentOutputReference
                    {
                        Transaction = new FederatedPeg.Wallet.TransactionData
                        {
                            Amount = Money.Coins(105),
                            Id = uint256.One,
                            ScriptPubKey = redeemScript.Hash.ScriptPubKey
                        }
                    }
                });

            this.federationWalletManager.Setup(x => x.GetWallet())
                .Returns(new FederationWallet
                {
                    MultiSigAddress = new MultiSigAddress
                    {
                        RedeemScript = redeemScript
                    }
                });

            var txBuilder = new WithdrawalTransactionBuilder(
                this.network,
                this.federationWalletManager.Object,
                this.federationWalletTransactionHandler.Object,
                this.federationGatewaySettings.Object,
                this.signals.Object,
                null
                );

            var recipient = new Recipient
            {
                Amount = Money.Coins(101),
                ScriptPubKey = new Script()
            };

            Transaction ret = txBuilder.BuildWithdrawalTransaction(0, uint256.One, 100, recipient);

            Assert.NotNull(ret);

            // Fee taken from amount should be the total fee.
            this.federationWalletTransactionHandler.Verify(x => x.BuildTransaction(It.Is<TransactionBuildContext>(y => y.Recipients.First().Amount < recipient.Amount && (recipient.Amount - y.Recipients.First().Amount > 0))));
        }

        [Fact]
        public void NoSpendableTransactionsLogWarning()
        {
            // Throw a 'no spendable transactions' exception
            this.federationWalletTransactionHandler.Setup(x => x.BuildTransaction(It.IsAny<TransactionBuildContext>()))
                .Throws(new WalletException(FederationWalletTransactionHandler.NoSpendableTransactionsMessage));

            var txBuilder = new WithdrawalTransactionBuilder(
                this.network,
                this.federationWalletManager.Object,
                this.federationWalletTransactionHandler.Object,
                this.federationGatewaySettings.Object,
                this.signals.Object,
                null
            );

            var recipient = new Recipient
            {
                Amount = Money.Coins(101),
                ScriptPubKey = new Script()
            };

            Transaction ret = txBuilder.BuildWithdrawalTransaction(0, uint256.One, 100, recipient);

            // Log out a warning in this case, not an error.
            this.logger
                .Setup(f => f.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
                .Callback(new InvocationAction(invocation =>
                {
                    ((LogLevel)invocation.Arguments[0]).Should().Be(LogLevel.Warning);
                }));
        }

        [Fact]
        public void NotEnoughFundsLogWarning()
        {
            // Throw a 'no spendable transactions' exception
            this.federationWalletTransactionHandler.Setup(x => x.BuildTransaction(It.IsAny<TransactionBuildContext>()))
                .Throws(new WalletException(FederationWalletTransactionHandler.NotEnoughFundsMessage));

            var txBuilder = new WithdrawalTransactionBuilder(
                this.network,
                this.federationWalletManager.Object,
                this.federationWalletTransactionHandler.Object,
                this.federationGatewaySettings.Object,
                this.signals.Object,
                null
            );

            var recipient = new Recipient
            {
                Amount = Money.Coins(101),
                ScriptPubKey = new Script()
            };

            Transaction ret = txBuilder.BuildWithdrawalTransaction(0, uint256.One, 100, recipient);

            // Log out a warning in this case, not an error.
            this.logger
                .Setup(f => f.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
                .Callback(new InvocationAction(invocation =>
                {
                    ((LogLevel)invocation.Arguments[0]).Should().Be(LogLevel.Warning);
                }));
        }
    }
}
