﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.AutoMock;
using NBitcoin;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Wallet.Broadcasting;
using Xels.Bitcoin.Features.Wallet.Controllers;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.Features.Wallet.Services;
using Xels.Bitcoin.P2P.Peer;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Tests.Wallet.Common;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonErrors;
using Xunit;

namespace Xels.Bitcoin.Features.Wallet.Tests
{
    public class WalletControllerTest : LogsTestBase
    {
        private readonly ChainIndexer chainIndexer;
        private static readonly IDictionary<string, PropertyInfo> WordLists;
        private readonly Dictionary<Type, object> configuredMocks = new Dictionary<Type, object>();

        static WalletControllerTest()
        {
            WordLists = typeof(Wordlist)
                .GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.PropertyType == typeof(Wordlist))
                .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase);
        }

        public WalletControllerTest()
        {
            this.chainIndexer = new ChainIndexer(this.Network);
        }

        [Fact]
        public async Task GenerateMnemonicWithoutParametersCreatesMnemonicWithDefaultsAsync()
        {
            var controller = this.GetWalletController();

            IActionResult result = await controller.GenerateMnemonicAsync();

            var viewResult = Assert.IsType<JsonResult>(result);

            string[] resultingWords = (viewResult.Value as string).Split(' ');

            Assert.Equal(12, resultingWords.Length);

            foreach (string word in resultingWords)
            {
                Assert.True(Wordlist.English.WordExists(word, out int _));
            }
        }

        [Fact]
        public async Task GenerateMnemonicWithDifferentWordCountCreatesMnemonicWithCorrectNumberOfWordsAsync()
        {
            var controller = this.GetWalletController();

            IActionResult result = await controller.GenerateMnemonicAsync(wordCount: 24);

            var viewResult = Assert.IsType<JsonResult>(result);

            string[] resultingWords = (viewResult.Value as string).Split(' ');

            Assert.Equal(24, resultingWords.Length);
        }

        [Theory]
        [InlineData("eNgLiSh", ' ')]
        [InlineData("english", ' ')]
        [InlineData("french", ' ')]
        [InlineData("spanish", ' ')]
        [InlineData("japanese", '　')]
        [InlineData("chinesetraditional", ' ')]
        [InlineData("chinesesimplified", ' ')]
        public async Task GenerateMnemonicWithStrangeLanguageCasingReturnsCorrectMnemonicAsync(string language,
            char separator)
        {
            var controller = this.GetWalletController();
            var wordList = (Wordlist)WordLists[language].GetValue(null, null);

            IActionResult result = await controller.GenerateMnemonicAsync(language);

            var viewResult = Assert.IsType<JsonResult>(result);

            string[] resultingWords = (viewResult.Value as string).Split(separator);

            Assert.Equal(12, resultingWords.Length);

            Assert.True(resultingWords.All(word => wordList.WordExists(word, out int _)));
        }

        [Fact]
        public async Task GenerateMnemonicWithUnknownLanguageReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            IActionResult result = await controller.GenerateMnemonicAsync("invalidlanguage");

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.FormatException", error.Description);
            Assert.Equal(
                "Invalid language 'invalidlanguage'. Choices are: English, French, Spanish, Japanese, ChineseSimplified and ChineseTraditional.",
                error.Message);
        }

        [Fact]
        public async Task CreateWalletSuccessfullyReturnsMnemonicAsync()
        {
            var mnemonic = new Mnemonic(Wordlist.English, WordCount.Twelve);

            var mockWalletCreate = this.ConfigureMock<IWalletManager>(
                mock =>
                {
                    mock.Setup(wallet => wallet.CreateWallet(It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<string>(), It.IsAny<Mnemonic>())).Returns((null, mnemonic));
                });

            var controller = this.GetWalletController();

            IActionResult result = await controller.CreateAsync(new WalletCreationRequest
            {
                Name = "myName",
                Password = "",
                Passphrase = "",
            });

            mockWalletCreate.VerifyAll();
            var viewResult = Assert.IsType<JsonResult>(result);
            Assert.Equal(mnemonic.ToString(), viewResult.Value);
            Assert.NotNull(result);
        }

        [Fact]
        public async Task CreateWalletWithInvalidModelStateReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Name", "Name cannot be empty.");

            IActionResult result = await controller.CreateAsync(new WalletCreationRequest
            {
                Name = "",
                Password = "",
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("Name cannot be empty.", error.Message);
        }

        [Fact]
        public async Task CreateWalletWithInvalidOperationExceptionReturnsConflictAsync()
        {
            string errorMessage = "An error occurred.";

            var mockWalletCreate = this.ConfigureMock<IWalletManager>(
                mock =>
                {
                    mock.Setup(wallet =>
                            wallet.CreateWallet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                                It.IsAny<Mnemonic>()))
                        .Throws(new WalletException(errorMessage));
                });

            var controller = this.GetWalletController();

            IActionResult result = await controller.CreateAsync(new WalletCreationRequest
            {
                Name = "myName",
                Password = "",
                Passphrase = "",
            });

            mockWalletCreate.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(409, error.Status);
            Assert.Equal(errorMessage, error.Message);
        }

        [Fact]
        public async Task CreateWalletWithNotSupportedExceptionExceptionReturnsBadRequestAsync()
        {
            var mockWalletCreate = this.ConfigureMock<IWalletManager>(
                mock =>
                {
                    mock.Setup(wallet =>
                            wallet.CreateWallet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                                It.IsAny<Mnemonic>()))
                        .Throws(new NotSupportedException("Not supported"));
                });


            var controller = this.GetWalletController();

            IActionResult result = await controller.CreateAsync(new WalletCreationRequest
            {
                Name = "myName",
                Password = "",
                Passphrase = "",
            });

            mockWalletCreate.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("There was a problem creating a wallet.", error.Message);
        }

        [Fact]
        public async Task RecoverWalletSuccessfullyReturnsWalletModelAsync()
        {
            var wallet = new Wallet
            {
                Name = "myWallet",
                Network = NetworkHelpers.GetNetwork("mainnet")
            };

            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
            {
                mock.Setup(w => w.RecoverWallet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<DateTime>(), null, null)).Returns(wallet);
                mock.Setup(w => w.IsStarted).Returns(true);
            });

            this.ConfigureMock<IWalletSyncManager>(mock =>
                mock.Setup(w => w.WalletTip).Returns(new ChainedHeader(this.Network.GetGenesis().Header,
                    this.Network.GetGenesis().Header.GetHash(), 3)));

            var controller = this.GetWalletController();

            IActionResult result = await controller.RecoverAsync(new WalletRecoveryRequest
            {
                Name = "myWallet",
                Password = "",
                Mnemonic = "mnemonic"
            });

            mockWalletManager.VerifyAll();
            var viewResult = Assert.IsType<OkResult>(result);
            Assert.Equal(200, viewResult.StatusCode);
        }

        /// <summary>
        /// This is to cover the scenario where a wallet is syncing at height X
        /// and the user recovers a new wallet at height X + Y.
        /// The wallet should continue syncing from X without jumpoing forward.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task RecoverWalletWithDatedAfterCurrentSyncHeightDoesNotMoveSyncHeightAsync()
        {
            var wallet = new Wallet
            {
                Name = "myWallet",
                Network = NetworkHelpers.GetNetwork("mainnet")
            };

            DateTime lastBlockDateTime = this.chainIndexer.Tip.Header.BlockTime.DateTime;

            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
            {
                mock.Setup(w => w.RecoverWallet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<DateTime>(), null, null)).Returns(wallet);
                mock.Setup(w => w.IsStarted).Returns(true);
            });

            Mock<IWalletSyncManager> walletSyncManager = this.ConfigureMock<IWalletSyncManager>(mock =>
                mock.Setup(w => w.WalletTip).Returns(new ChainedHeader(this.Network.GetGenesis().Header,
                    this.Network.GetGenesis().Header.GetHash(), 3)));

            walletSyncManager.Verify(w => w.SyncFromHeight(100, It.IsAny<string>()), Times.Never);

            var controller = this.GetWalletController();

            IActionResult result = await controller.RecoverAsync(new WalletRecoveryRequest
            {
                Name = "myWallet",
                Password = "",
                Mnemonic = "mnemonic",
                CreationDate = lastBlockDateTime
            });

            mockWalletManager.VerifyAll();

            var viewResult = Assert.IsType<OkResult>(result);
            Assert.Equal(200, viewResult.StatusCode);
        }

        [Fact]
        public async Task RecoverWalletWithInvalidModelStateReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Password", "A password is required.");

            IActionResult result = await controller.RecoverAsync(new WalletRecoveryRequest
            {
                Name = "myWallet",
                Password = "",
                Mnemonic = "mnemonic"
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("A password is required.", error.Message);
        }

        [Fact]
        public async Task RecoverWalletWithInvalidOperationExceptionReturnsConflictAsync()
        {
            string errorMessage = "An error occurred.";

            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
            {
                mock.Setup(w => w.RecoverWallet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<DateTime>(), null, null))
                    .Throws(new WalletException(errorMessage));
                mock.Setup(w => w.IsStarted).Returns(true);
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.RecoverAsync(new WalletRecoveryRequest
            {
                Name = "myWallet",
                Password = "",
                Mnemonic = "mnemonic"
            });

            mockWalletManager.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(409, error.Status);
            Assert.Equal(errorMessage, error.Message);
        }

        [Fact]
        public async Task RecoverWalletWithFileNotFoundExceptionReturnsNotFoundAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
            {
                mock.Setup(w => w.RecoverWallet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<DateTime>(), null, null))
                    .Throws(new FileNotFoundException("File not found."));
                mock.Setup(w => w.IsStarted).Returns(true);
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.RecoverAsync(new WalletRecoveryRequest
            {
                Name = "myWallet",
                Password = "",
                Mnemonic = "mnemonic"
            });

            mockWalletManager.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(404, error.Status);
            Assert.StartsWith("System.IO.FileNotFoundException", error.Description);
            Assert.Equal("Wallet not found.", error.Message);
        }

        [Fact]
        public async Task RecoverWalletWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
            {
                mock.Setup(w => w.RecoverWallet(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                        It.IsAny<DateTime>(), null, null))
                    .Throws(new FormatException("Formatting failed."));
                mock.Setup(w => w.IsStarted).Returns(true);
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.RecoverAsync(new WalletRecoveryRequest
            {
                Name = "myWallet",
                Password = "",
                Mnemonic = "mnemonic"
            });

            mockWalletManager.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.FormatException", error.Description);
            Assert.Equal("Formatting failed.", error.Message);
        }

        [Fact]
        public async Task RecoverWalletViaExtPubKeySuccessfullyReturnsWalletModelAsync()
        {
            string walletName = "myWallet";
            string extPubKey =
                "xpub661MyMwAqRbcEgnsMFfhjdrwR52TgicebTrbnttywb9zn3orkrzn6MHJrgBmKrd7MNtS6LAim44a6V2gizt3jYVPHGYq1MzAN849WEyoedJ";

            await this.RecoverWithExtPubAndCheckSuccessfulResponseAsync(walletName, extPubKey);
        }

        [Fact]
        public async Task RecoverWalletViaExtPubKeySupportsXelsLegacyExtpubKeyAsync()
        {
            string walletName = "myWallet";
            string extPubKey =
                "xpub6CCo1eBTzCPDuV7MDAV3SmRPNJyygTVc9FLwWey8qYQSnKFyv3iGsYpX9P5opDj1DXhbTxSgyy5jnKZPoCWqCtpsZdcGJWqrWri5LnQbPex";

            await this.RecoverWithExtPubAndCheckSuccessfulResponseAsync(walletName, extPubKey);
        }

        private async Task RecoverWithExtPubAndCheckSuccessfulResponseAsync(string walletName, string extPubKey)
        {
            var wallet = new Wallet
            {
                Name = walletName,
                Network = KnownNetworks.StraxMain
            };

            var walletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(w => w.RecoverWallet(walletName, It.IsAny<ExtPubKey>(), 1, It.IsAny<DateTime>(), null))
                    .Returns(wallet));

            this.ConfigureMockInstance(KnownNetworks.StraxMain);
            this.ConfigureMock<IWalletSyncManager>(mock =>
                mock.Setup(w => w.WalletTip).Returns(new ChainedHeader(this.Network.GetGenesis().Header,
                    this.Network.GetGenesis().Header.GetHash(), 3)));

            var controller = this.GetWalletController();

            IActionResult result = await controller.RecoverViaExtPubKeyAsync(new WalletExtPubRecoveryRequest
            {
                Name = walletName,
                ExtPubKey = extPubKey,
                AccountIndex = 1,
            });

            walletManager.VerifyAll();

            var viewResult = Assert.IsType<OkResult>(result);
            Assert.Equal(200, viewResult.StatusCode);
        }

        /// <summary>
        /// This is to cover the scenario where a wallet is syncing at height X
        /// and the user recovers a new wallet at height X + Y.
        /// The wallet should continue syncing from X without jumpoing forward.
        /// </summary>
        /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous unit test.</placeholder></returns>
        [Fact]
        public async Task RecoverWalletWithExtPubDatedAfterCurrentSyncHeightDoesNotMoveSyncHeightAsync()
        {
            string walletName = "myWallet";
            string extPubKey =
                "xpub661MyMwAqRbcEgnsMFfhjdrwR52TgicebTrbnttywb9zn3orkrzn6MHJrgBmKrd7MNtS6LAim44a6V2gizt3jYVPHGYq1MzAN849WEyoedJ";

            var wallet = new Wallet
            {
                Name = walletName,
                Network = KnownNetworks.StraxMain
            };

            DateTime lastBlockDateTime = this.chainIndexer.Tip.Header.BlockTime.DateTime;

            var walletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(w =>
                        w.RecoverWallet(It.IsAny<string>(), It.IsAny<ExtPubKey>(), 1, It.IsAny<DateTime>(), null))
                    .Returns(wallet));

            this.ConfigureMockInstance(KnownNetworks.StraxMain);

            Mock<IWalletSyncManager> walletSyncManager = this.ConfigureMock<IWalletSyncManager>(mock =>
                mock.Setup(w => w.WalletTip).Returns(new ChainedHeader(this.Network.GetGenesis().Header,
                    this.Network.GetGenesis().Header.GetHash(), 3)));

            walletSyncManager.Verify(w => w.SyncFromHeight(100, It.IsAny<string>()), Times.Never);

            var controller = this.GetWalletController();

            IActionResult result = await controller.RecoverViaExtPubKeyAsync(new WalletExtPubRecoveryRequest
            {
                Name = walletName,
                ExtPubKey = extPubKey,
                AccountIndex = 1,
                CreationDate = lastBlockDateTime
            });

            walletManager.VerifyAll();

            var viewResult = Assert.IsType<OkResult>(result);
            Assert.Equal(200, viewResult.StatusCode);
        }

        [Fact]
        public async Task LoadWalletSuccessfullyReturnsWalletModelAsync()
        {
            var wallet = new Wallet
            {
                Name = "myWallet",
                Network = NetworkHelpers.GetNetwork("mainnet")
            };
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(w => w.LoadWallet(It.IsAny<string>(), It.IsAny<string>())).Returns(wallet));

            var controller = this.GetWalletController();

            IActionResult result = await controller.LoadAsync(new WalletLoadRequest
            {
                Name = "myWallet",
                Password = ""
            });

            mockWalletManager.VerifyAll();
            var viewResult = Assert.IsType<OkResult>(result);
            Assert.Equal(200, viewResult.StatusCode);
        }

        [Fact]
        public async Task LoadWalletWithInvalidModelReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Password", "A password is required.");

            IActionResult result = await controller.LoadAsync(new WalletLoadRequest
            {
                Name = "myWallet",
                Password = ""
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("A password is required.", error.Message);
        }

        [Fact]
        public async Task LoadWalletWithFileNotFoundExceptionandReturnsNotFoundAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(wallet => wallet.LoadWallet(It.IsAny<string>(), It.IsAny<string>()))
                    .Throws<FileNotFoundException>());

            var controller = this.GetWalletController();

            IActionResult result = await controller.LoadAsync(new WalletLoadRequest
            {
                Name = "myName",
                Password = ""
            });

            mockWalletManager.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(404, error.Status);
            Assert.StartsWith("System.IO.FileNotFoundException", error.Description);
            Assert.Equal("This wallet was not found at the specified location.", error.Message);
        }

        [Fact]
        public async Task LoadWalletWithSecurityExceptionandReturnsForbiddenAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(wallet => wallet.LoadWallet(It.IsAny<string>(), It.IsAny<string>()))
                    .Throws<SecurityException>());

            var controller = this.GetWalletController();

            IActionResult result = await controller.LoadAsync(new WalletLoadRequest
            {
                Name = "myName",
                Password = ""
            });

            mockWalletManager.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(403, error.Status);
            Assert.StartsWith("System.Security.SecurityException", error.Description);
            Assert.Equal("Wrong password, please try again.", error.Message);
        }

        [Fact]
        public async Task LoadWalletWithOtherExceptionandReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(wallet => wallet.LoadWallet(It.IsAny<string>(), It.IsAny<string>()))
                    .Throws<FormatException>());

            var controller = this.GetWalletController();

            IActionResult result = await controller.LoadAsync(new WalletLoadRequest
            {
                Name = "myName",
                Password = ""
            });

            mockWalletManager.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.FormatException", error.Description);
        }

        [Fact]
        public async Task GetGeneralInfoSuccessfullyReturnsWalletGeneralInfoModelAsync()
        {
            var wallet = new Wallet
            {
                Name = "myWallet",
                Network = NetworkHelpers.GetNetwork("mainnet"),
                CreationTime = new DateTime(2017, 6, 19, 1, 1, 1),
                AccountsRoot = new List<AccountRoot>()
            };

            wallet.AccountsRoot.Add(new AccountRoot(wallet)
            {
                CoinType = (CoinType)this.Network.Consensus.CoinType,
                LastBlockSyncedHeight = 15
            });

            var concurrentChain = new ChainIndexer(this.Network);
            ChainedHeader tip = WalletTestsHelpers.AppendBlock(this.Network, null, new[] { concurrentChain });

            var connectionManagerMock = this.ConfigureMock<IConnectionManager>(mock =>
                mock.Setup(c => c.ConnectedPeers)
                    .Returns(new NetworkPeerCollection()));

            var consensusManager =
                this.ConfigureMock<IConsensusManager>(s => s.Setup(w => w.HeaderTip).Returns(tip.Height));

            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(w => w.GetWallet("myWallet")).Returns(wallet));

            string walletFileExtension = "wallet.json";
            string testWalletFileName = Path.ChangeExtension("myWallet", walletFileExtension);
            string testWalletPath = Path.Combine(AppContext.BaseDirectory, "xelsnode", testWalletFileName);

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetGeneralInfo(new WalletName
            {
                Name = "myWallet"
            });

            mockWalletManager.VerifyAll();
            var viewResult = Assert.IsType<JsonResult>(result);
            var resultValue = Assert.IsType<WalletGeneralInfoModel>(viewResult.Value);

            Assert.Equal(wallet.Network.Name, resultValue.Network);
            Assert.Equal(wallet.CreationTime, resultValue.CreationTime);
            Assert.Equal(15, resultValue.LastBlockSyncedHeight);
            Assert.Equal(0, resultValue.ConnectedNodes);
            Assert.Equal(tip.Height, resultValue.ChainTip);
            Assert.True(resultValue.IsDecrypted);
            Assert.Equal(wallet.Name, resultValue.WalletName);
        }

        [Fact]
        public async Task GetGeneralInfoWithModelStateErrorReturnsBadRequestAsync()
        {
            var wallet = new Wallet
            {
                Name = "myWallet",
            };

            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(w => w.GetWallet("myWallet")).Returns(wallet));

            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Name", "Invalid name.");

            IActionResult result = await controller.GetGeneralInfo(new WalletName
            {
                Name = "myWallet"
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("Invalid name.", error.Message);
        }

        [Fact]
        public async Task GetGeneralInfoWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(w => w.GetWallet("myWallet")).Throws<FormatException>());

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetGeneralInfo(new WalletName
            {
                Name = "myWallet"
            });

            mockWalletManager.VerifyAll();
            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.FormatException", error.Description);
        }

        [Fact]
        public void GetHistoryWithExceptionReturnsBadRequest()
        {
            string walletName = "myWallet";
            var mockWalletManager = this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(w => w.GetHistory("myWallet", WalletManager.DefaultAccount, null, 100, 0, null, false))
                    .Throws(new InvalidOperationException("Issue retrieving wallets.")));
            mockWalletManager.Setup(w => w.GetWallet(walletName)).Returns(new Wallet());

            var controller = this.GetWalletController();

            IActionResult result = controller.GetHistory(new WalletHistoryRequest
            {
                WalletName = walletName,
                Skip = 0,
                Take = 100
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.InvalidOperationException", error.Description);
            Assert.Equal("Issue retrieving wallets.", error.Message);
        }

        [Fact]
        public async Task GetBalanceWithValidModelStateReturnsWalletBalanceModelAsync()
        {
            HdAccount account = WalletTestsHelpers.CreateAccount("account 1");
            HdAddress accountAddress1 = WalletTestsHelpers.CreateAddress();
            accountAddress1.Transactions.Add(
                WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(15000), null));
            accountAddress1.Transactions.Add(WalletTestsHelpers.CreateTransaction(new uint256(2), new Money(10000), 1));

            HdAddress accountAddress2 = WalletTestsHelpers.CreateAddress(true);
            accountAddress2.Transactions.Add(
                WalletTestsHelpers.CreateTransaction(new uint256(3), new Money(20000), null));
            accountAddress2.Transactions.Add(
                WalletTestsHelpers.CreateTransaction(new uint256(4), new Money(120000), 2));

            account.ExternalAddresses.Add(accountAddress1);
            account.InternalAddresses.Add(accountAddress2);

            HdAccount account2 = WalletTestsHelpers.CreateAccount("account 2");
            HdAddress account2Address1 = WalletTestsHelpers.CreateAddress();
            account2Address1.Transactions.Add(
                WalletTestsHelpers.CreateTransaction(new uint256(5), new Money(74000), null));
            account2Address1.Transactions.Add(
                WalletTestsHelpers.CreateTransaction(new uint256(6), new Money(18700), 3));

            HdAddress account2Address2 = WalletTestsHelpers.CreateAddress(true);
            account2Address2.Transactions.Add(
                WalletTestsHelpers.CreateTransaction(new uint256(7), new Money(65000), null));
            account2Address2.Transactions.Add(
                WalletTestsHelpers.CreateTransaction(new uint256(8), new Money(89300), 4));

            account2.ExternalAddresses.Add(account2Address1);
            account2.InternalAddresses.Add(account2Address2);

            var accountsBalances = new List<AccountBalance>
            {
                new AccountBalance
                {
                    Account = account, AmountConfirmed = new Money(130000), AmountUnconfirmed = new Money(35000),
                    SpendableAmount = new Money(130000)
                },
                new AccountBalance
                {
                    Account = account2, AmountConfirmed = new Money(108000), AmountUnconfirmed = new Money(139000),
                    SpendableAmount = new Money(108000)
                }
            };

            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(w => w.GetBalances("myWallet", WalletManager.DefaultAccount, It.IsAny<int>()))
                .Returns(accountsBalances);

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetBalanceAsync(new WalletBalanceRequest
            {
                WalletName = "myWallet"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletBalanceModel;

            Assert.NotNull(model);
            Assert.Equal(2, model.AccountsBalances.Count);

            AccountBalanceModel resultingBalance = model.AccountsBalances[0];
            Assert.Equal(this.Network.Consensus.CoinType, (int)resultingBalance.CoinType);
            Assert.Equal(account.Name, resultingBalance.Name);
            Assert.Equal(account.HdPath, resultingBalance.HdPath);
            Assert.Equal(new Money(130000), resultingBalance.AmountConfirmed);
            Assert.Equal(new Money(35000), resultingBalance.AmountUnconfirmed);
            Assert.Equal(new Money(130000), resultingBalance.SpendableAmount);

            resultingBalance = model.AccountsBalances[1];
            Assert.Equal(this.Network.Consensus.CoinType, (int)resultingBalance.CoinType);
            Assert.Equal(account2.Name, resultingBalance.Name);
            Assert.Equal(account2.HdPath, resultingBalance.HdPath);
            Assert.Equal(new Money(108000), resultingBalance.AmountConfirmed);
            Assert.Equal(new Money(139000), resultingBalance.AmountUnconfirmed);
            Assert.Equal(new Money(108000), resultingBalance.SpendableAmount);
        }

        [Fact]
        public async Task WalletSyncFromDateReturnsOKAsync()
        {
            string walletName = "myWallet";
            DateTime syncDate = DateTime.Now.Subtract(new TimeSpan(1)).Date;

            var mockWalletSyncManager = new Mock<IWalletSyncManager>();
            mockWalletSyncManager.Setup(w => w.SyncFromDate(
                It.Is<DateTime>((val) => val.Equals(syncDate)),
                It.Is<string>(val => walletName.Equals(val))));

            var controller = this.GetWalletController();

            IActionResult result = await controller.SyncFromDateAsync(new WalletSyncRequest
            {
                WalletName = walletName,
                Date = DateTime.Now.Subtract(new TimeSpan(1)).Date
            });

            var viewResult = Assert.IsType<OkResult>(result);
            mockWalletSyncManager.Verify();
            Assert.NotNull(viewResult);
            Assert.Equal((int)HttpStatusCode.OK, viewResult.StatusCode);
        }

        [Fact]
        public async Task WalletSyncAllReturnsOKAsync()
        {
            string walletName = "myWallet";

            var mockWalletSyncManager = new Mock<IWalletSyncManager>();
            mockWalletSyncManager.Setup(w => w.SyncFromHeight(
                It.Is<int>((val) => val.Equals(0)),
                It.Is<string>(val => walletName.Equals(val))));

            var controller = this.GetWalletController();

            IActionResult result = await controller.SyncFromDateAsync(new WalletSyncRequest
            {
                WalletName = walletName,
                All = true
            });

            var viewResult = Assert.IsType<OkResult>(result);
            mockWalletSyncManager.Verify();
            Assert.NotNull(viewResult);
            Assert.Equal((int)HttpStatusCode.OK, viewResult.StatusCode);
        }

        [Fact]
        public async Task GetBalanceWithEmptyListOfAccountsReturnsWalletBalanceModelAsync()
        {
            var accounts = new List<HdAccount>();
            var mockWalletManager = new Mock<IWalletManager>();
            mockWalletManager.Setup(w => w.GetAccounts("myWallet"))
                .Returns(accounts);

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetBalanceAsync(new WalletBalanceRequest
            {
                WalletName = "myWallet",
                AccountName = null
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletBalanceModel;

            Assert.NotNull(model);
            Assert.Empty(model.AccountsBalances);
        }

        [Fact]
        public async Task GetBalanceWithInvalidValidModelStateReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("WalletName", "A walletname is required.");
            IActionResult result = await controller.GetBalanceAsync(new WalletBalanceRequest
            {
                WalletName = ""
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("A walletname is required.", error.Message);
        }

        [Fact]
        public async Task GetBalanceWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetBalances("myWallet", WalletManager.DefaultAccount, It.IsAny<int>()))
                .Throws(new InvalidOperationException("Issue retrieving accounts."));

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetBalanceAsync(new WalletBalanceRequest
            {
                WalletName = "myWallet"
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.InvalidOperationException", error.Description);
            Assert.Equal("Issue retrieving accounts.", error.Message);
        }

        [Fact]
        public async Task GetAddressBalanceWithValidModelStateReturnsAddressBalanceModelAsync()
        {
            HdAccount account = WalletTestsHelpers.CreateAccount("account 1");
            HdAddress accountAddress = WalletTestsHelpers.CreateAddress(true);
            account.InternalAddresses.Add(accountAddress);

            var addressBalance = new AddressBalance
            {
                Address = accountAddress.Address,
                AmountConfirmed = new Money(75000),
                AmountUnconfirmed = new Money(500000),
                SpendableAmount = new Money(75000)
            };

            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(w => w.GetAddressBalance(accountAddress.Address)).Returns(addressBalance);

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetReceivedByAddressAsync(new ReceivedByAddressRequest
            {
                Address = accountAddress.Address
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as AddressBalanceModel;

            Assert.NotNull(model);
            Assert.Equal(this.Network.Consensus.CoinType, (int)model.CoinType);
            Assert.Equal(accountAddress.Address, model.Address);
            Assert.Equal(addressBalance.AmountConfirmed, model.AmountConfirmed);
            Assert.Equal(addressBalance.SpendableAmount, model.SpendableAmount);
        }

        [Fact]
        public async Task GetAddressBalanceWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetAddressBalance("MyAddress"))
                .Throws(new InvalidOperationException("Issue retrieving address balance."));

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetReceivedByAddressAsync(new ReceivedByAddressRequest
            {
                Address = "MyAddress"
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.InvalidOperationException", error.Description);
            Assert.Equal("Issue retrieving address balance.", error.Message);
        }

        [Fact]
        public async Task GetAddressBalanceWithInvalidModelStateReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Address", "An address is required.");
            IActionResult result = await controller.GetReceivedByAddressAsync(new ReceivedByAddressRequest
            {
                Address = ""
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("An address is required.", error.Message);
        }

        [Fact]
        public async Task BuildTransactionWithValidRequestAllowingUnconfirmedReturnsWalletBuildTransactionModelAsync()
        {
            var mockWalletTransactionHandler = this.ConfigureMock<IWalletTransactionHandler>();

            var key = new Key();
            var sentTrx = new Transaction();
            mockWalletTransactionHandler.Setup(m => m.BuildTransaction(It.IsAny<TransactionBuildContext>()))
                .Returns(sentTrx);

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 1",
                AllowUnconfirmed = true,
                Recipients = new List<RecipientModel>
                {
                    new RecipientModel
                    {
                        DestinationAddress = key.PubKey.GetAddress(this.Network).ToString(),
                        Amount = new Money(150000).ToString()
                    }
                },
                FeeType = "105",
                Password = "test",
                WalletName = "myWallet"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletBuildTransactionModel;

            Assert.NotNull(model);
            Assert.Equal(sentTrx.ToHex(), model.Hex);
            Assert.Equal(sentTrx.GetHash(), model.TransactionId);
        }

        [Fact]
        public async Task BuildTransactionWithCustomFeeAmountAndFeeTypeReturnsWalletBuildTransactionModelWithFeeAmountAsync()
        {
            var key = new Key();
            this.ConfigureMock<IWalletTransactionHandler>(mock =>
            {
                var sentTrx = new Transaction();
                mock.Setup(m => m.BuildTransaction(It.IsAny<TransactionBuildContext>()))
                    .Returns(sentTrx);
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 1",
                AllowUnconfirmed = true,
                Recipients = new List<RecipientModel>
                {
                    new RecipientModel
                    {
                        DestinationAddress = key.PubKey.GetAddress(this.Network).ToString(),
                        Amount = new Money(150000).ToString()
                    }
                },
                FeeType = "105",
                FeeAmount = "0.1234",
                Password = "test",
                WalletName = "myWallet"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletBuildTransactionModel;

            Assert.NotNull(model);
            Assert.Equal(new Money(12340000), model.Fee);
        }

        [Fact]
        public async Task
            BuildTransactionWithCustomFeeAmountAndNoFeeTypeReturnsWalletBuildTransactionModelWithFeeAmountAsync()
        {
            var key = new Key();
            this.ConfigureMock<IWalletTransactionHandler>(mock =>
            {
                var sentTrx = new Transaction();
                mock.Setup(m => m.BuildTransaction(It.IsAny<TransactionBuildContext>()))
                    .Returns(sentTrx);
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 1",
                AllowUnconfirmed = true,
                Recipients = new List<RecipientModel>
                {
                    new RecipientModel
                    {
                        DestinationAddress = key.PubKey.GetAddress(this.Network).ToString(),
                        Amount = new Money(150000).ToString()
                    }
                },
                FeeAmount = "0.1234",
                Password = "test",
                WalletName = "myWallet"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletBuildTransactionModel;

            Assert.NotNull(model);
            Assert.Equal(new Money(12340000), model.Fee);
        }

        [Fact]
        public async Task BuildTransactionWithValidRequestNotAllowingUnconfirmedReturnsWalletBuildTransactionModelAsync()
        {
            var key = new Key();
            var sentTrx = new Transaction();
            this.ConfigureMock<IWalletTransactionHandler>(mock =>
            {
                mock.Setup(m => m.BuildTransaction(It.IsAny<TransactionBuildContext>()))
                    .Returns(sentTrx);
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 1",
                AllowUnconfirmed = false,
                Recipients = new List<RecipientModel>
                {
                    new RecipientModel
                    {
                        DestinationAddress = key.PubKey.GetAddress(this.Network).ToString(),
                        Amount = new Money(150000).ToString()
                    }
                },
                FeeType = "105",
                Password = "test",
                WalletName = "myWallet"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletBuildTransactionModel;

            Assert.NotNull(model);
            Assert.Equal(sentTrx.ToHex(), model.Hex);
            Assert.Equal(sentTrx.GetHash(), model.TransactionId);
        }

        [Fact]
        public async Task BuildTransactionWithChangeAddressReturnsWalletBuildTransactionModelAsync()
        {
            string walletName = "myWallet";

            HdAddress usedReceiveAddress = WalletTestsHelpers.CreateAddress();

            Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);
            HdAccount account = wallet.AddNewAccount((ExtPubKey)null, accountName: "Account 0");
            account.ExternalAddresses.Add(usedReceiveAddress);

            this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(m => m.GetWallet(walletName)).Returns(wallet));

            var mockWalletTransactionHandler = this.ConfigureMock<IWalletTransactionHandler>(mock =>
            {
                var sentTrx = new Transaction();
                mock.Setup(m =>
                        m.BuildTransaction(It.Is<TransactionBuildContext>(t => t.ChangeAddress == usedReceiveAddress)))
                    .Returns(sentTrx);
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 0",
                Recipients = new List<RecipientModel>() { new RecipientModel() { Amount = "1.0", DestinationAddress = new Key().PubKey.Hash.GetAddress(this.Network).ToString() } },
                WalletName = walletName,
                ChangeAddress = usedReceiveAddress.Address
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletBuildTransactionModel;

            // Verify the transaction builder was invoked with the change address.
            mockWalletTransactionHandler.Verify(
                w => w.BuildTransaction(It.Is<TransactionBuildContext>(t => t.ChangeAddress == usedReceiveAddress)),
                Times.Once);

            Assert.NotNull(model);
        }

        [Fact]
        public async Task BuildTransactionWithChangeAddressNotInWalletReturnsBadRequestAsync()
        {
            string walletName = "myWallet";

            Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);
            wallet.AccountsRoot.First().Accounts.Add(WalletTestsHelpers.CreateAccount("Account 0"));

            this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(m => m.GetWallet(walletName)).Returns(wallet));

            var mockWalletTransactionHandler = this.ConfigureMock<IWalletTransactionHandler>();

            HdAddress addressNotInWallet = WalletTestsHelpers.CreateAddress();

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 0",
                Recipients = new List<RecipientModel>() { new RecipientModel() { Amount = "1.0", DestinationAddress = new Key().PubKey.Hash.GetAddress(this.Network).ToString() } },
                WalletName = walletName,
                ChangeAddress = addressNotInWallet.Address
            });

            // Verify the transaction builder was never invoked.
            mockWalletTransactionHandler.Verify(w => w.BuildTransaction(It.IsAny<TransactionBuildContext>()),
                Times.Never);

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("Change address not found.", error.Message);
        }

        [Fact]
        public async Task BuildTransactionWithChangeAddressAccountNotInWalletReturnsBadRequestAsync()
        {
            string walletName = "myWallet";

            // Create a wallet with no accounts.
            Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);

            this.ConfigureMock<IWalletManager>(mock =>
                mock.Setup(m => m.GetWallet(walletName)).Returns(wallet));

            var mockWalletTransactionHandler = this.ConfigureMock<IWalletTransactionHandler>();

            HdAddress addressNotInWallet = WalletTestsHelpers.CreateAddress();

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 0",
                Recipients = new List<RecipientModel>() { new RecipientModel() { Amount = "1.0", DestinationAddress = new Key().PubKey.Hash.GetAddress(this.Network).ToString() } },
                WalletName = walletName,
                ChangeAddress = addressNotInWallet.Address
            });

            // Verify the transaction builder was never invoked.
            mockWalletTransactionHandler.Verify(w => w.BuildTransaction(It.IsAny<TransactionBuildContext>()),
                Times.Never);

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("Account not found.", error.Message);
        }

        [Fact]
        public async Task BuildTransactionWithInvalidModelStateReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("WalletName", "A walletname is required.");
            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                WalletName = ""
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("A walletname is required.", error.Message);
        }

        [Fact]
        public async Task BuildTransactionWithExceptionReturnsBadRequestAsync()
        {
            var key = new Key();
            this.ConfigureMock<IWalletTransactionHandler>(mock =>
            {
                mock.Setup(m => m.BuildTransaction(It.IsAny<TransactionBuildContext>()))
                    .Throws(new InvalidOperationException("Issue building transaction."));
            });

            var controller = this.GetWalletController();

            IActionResult result = await controller.BuildTransactionAsync(new BuildTransactionRequest
            {
                AccountName = "Account 1",
                AllowUnconfirmed = false,
                Recipients = new List<RecipientModel>
                {
                    new RecipientModel
                    {
                        DestinationAddress = key.PubKey.GetAddress(this.Network).ToString(),
                        Amount = new Money(150000).ToString()
                    }
                },
                FeeType = "105",
                Password = "test",
                WalletName = "myWallet"
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.InvalidOperationException", error.Description);
            Assert.Equal("Issue building transaction.", error.Message);
        }

        [Fact]
        public async Task SendTransactionSuccessfulReturnsWalletSendTransactionModelResponseAsync()
        {
            string transactionHex =
                "010000000189c041f79aac3aa7e7a72804a9a55cd9eceba41a0586640f602eb9823540ce89010000006b483045022100ab9597b37cb8796aefa30b207abb248c8003d4d153076997e375b0daf4f9f7050220546397fee1cefe54c49210ea653e9e61fb88adf51b68d2c04ad6d2b46ddf97a30121035cc9de1f233469dad8a3bbd1e61b699a7dd8e0d8370c6f3b1f2a16167da83546ffffffff02f6400a00000000001976a914accf603142aaa5e22dc82500d3e187caf712f11588ac3cf61700000000001976a91467872601dda216fbf4cab7891a03ebace87d8e7488ac00000000";

            var mockBroadcasterManager = this.ConfigureMock<IBroadcasterManager>();

            mockBroadcasterManager.Setup(m => m.GetTransaction(It.IsAny<uint256>())).Returns(
                new TransactionBroadcastEntry(this.Network.CreateTransaction(transactionHex), TransactionBroadcastState.Broadcasted, null));

            var connectionManagerMock = this.ConfigureMock<IConnectionManager>();
            var peers = new List<INetworkPeer>();
            peers.Add(null);
            connectionManagerMock.Setup(c => c.ConnectedPeers).Returns(new TestReadOnlyNetworkPeerCollection(peers));

            var controller = this.GetWalletController();

            IActionResult result = await controller.SendTransactionAsync(new SendTransactionRequest(transactionHex));

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletSendTransactionModel;
            Assert.NotNull(model);
            Assert.Equal(new uint256("96b4f0c2f0aa2cecd43fa66b5e3227c56afd8791e18fcc572d9625ee05d6741c"),
                model.TransactionId);
            Assert.Equal("1GkjeiT7Y6RdPPL3p3nUME9DLJchhLNCsJ", model.Outputs.First().Address);
            Assert.Equal(new Money(671990), model.Outputs.First().Amount);
            Assert.Equal("1ASQW3EkkQ1zCpq3HAVfrGyVrSwVz4cbzU", model.Outputs.ElementAt(1).Address);
            Assert.Equal(new Money(1570364), model.Outputs.ElementAt(1).Amount);
        }

        [Fact]
        public async Task SendTransactionFailedBecauseNoNodesConnectedAsync()
        {
            var mockBroadcasterManager = this.ConfigureMock<IBroadcasterManager>();

            var connectionManagerMock = this.ConfigureMock<IConnectionManager>();
            connectionManagerMock.Setup(c => c.ConnectedPeers)
                .Returns(new NetworkPeerCollection());

            var controller = this.GetWalletController();

            IActionResult result = await controller.SendTransactionAsync(new SendTransactionRequest(new uint256(15555).ToString()));

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(403, error.Status);
            Assert.Equal("Can't send transaction: sending transaction requires at least one connection.", error.Message);
        }

        [Fact]
        public async Task SendTransactionWithInvalidModelStateReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Hex", "Hex required.");
            IActionResult result = await controller.SendTransactionAsync(new SendTransactionRequest(""));

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("Hex required.", error.Message);
        }

        [Fact]
        public async Task ListWalletFilesWithExistingWalletFilesReturnsWalletFileModelAsync()
        {
            var walletManager = this.ConfigureMock<IWalletManager>();
            walletManager.Setup(m => m.GetWalletsNames())
                .Returns(new[] { "wallet1.wallet.json", "wallet2.wallet.json" });

            walletManager.Setup(m => m.GetWalletFileExtension()).Returns("wallet.json");

            var controller = this.GetWalletController();

            IActionResult result = await controller.ListWalletsAsync();

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletInfoModel;

            Assert.NotNull(model);
            Assert.Equal(2, model.WalletNames.Count());
            Assert.EndsWith("wallet1.wallet.json", model.WalletNames.ElementAt(0));
            Assert.EndsWith("wallet2.wallet.json", model.WalletNames.ElementAt(1));
        }

        [Fact]
        public async Task ListWalletFilesWithoutExistingWalletFilesReturnsWalletFileModelAsync()
        {
            var walletManager = this.ConfigureMock<IWalletManager>();

            walletManager.Setup(m => m.GetWalletsNames())
                .Returns(Enumerable.Empty<string>());

            var controller = this.GetWalletController();

            IActionResult result = await controller.ListWalletsAsync();

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as WalletInfoModel;

            Assert.NotNull(model);
            Assert.Empty(model.WalletNames);
        }

        [Fact]
        public async Task ListWalletFilesWithExceptionReturnsBadRequestAsync()
        {
            var walletManager = this.ConfigureMock<IWalletManager>();
            walletManager.Setup(m => m.GetWalletsNames())
                .Throws(new Exception("something happened."));

            var controller = this.GetWalletController();

            IActionResult result = await controller.ListWalletsAsync();

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("something happened.", error.Message);
        }

        [Fact]
        public async Task CreateNewAccountWithValidModelReturnsAccountNameAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetUnusedAccount("myWallet", "test"))
                .Returns(new HdAccount { Name = "Account 1" });

            var controller = this.GetWalletController();

            IActionResult result = await controller.CreateNewAccountAsync(new GetUnusedAccountModel
            {
                WalletName = "myWallet",
                Password = "test"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            Assert.Equal("Account 1", viewResult.Value as string);
        }

        [Fact]
        public async Task CreateNewAccountWithInvalidValidModelReturnsBadRequestAsync()
        {
            var mockWalletManager = new Mock<IWalletManager>();

            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Password", "A password is required.");

            IActionResult result = await controller.CreateNewAccountAsync(new GetUnusedAccountModel
            {
                WalletName = "myWallet",
                Password = ""
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("A password is required.", error.Message);
        }

        [Fact]
        public async Task CreateNewAccountWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetUnusedAccount("myWallet", "test"))
                .Throws(new InvalidOperationException("Wallet not found."));

            var controller = this.GetWalletController();

            IActionResult result = await controller.CreateNewAccountAsync(new GetUnusedAccountModel
            {
                WalletName = "myWallet",
                Password = "test"
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.InvalidOperationException", error.Description);
            Assert.StartsWith("Wallet not found.", error.Message);
        }

        [Fact]
        public async Task ListAccountsWithValidModelStateReturnsAccountsAsync()
        {
            string walletName = "wallet 1";
            Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);
            wallet.AddNewAccount((ExtPubKey)null);
            wallet.AddNewAccount((ExtPubKey)null);

            var mockWalletManager = this.ConfigureMock<IWalletManager>();

            mockWalletManager.Setup(m => m.GetAccounts(walletName))
                .Returns(wallet.AccountsRoot.SelectMany(x => x.Accounts));

            var controller = this.GetWalletController();

            IActionResult result = await controller.ListAccountsAsync(new ListAccountsModel
            {
                WalletName = "wallet 1"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as IEnumerable<string>;

            Assert.NotNull(model);
            Assert.Equal(2, model.Count());
            Assert.Equal("account 0", model.First());
            Assert.Equal("account 1", model.Last());
        }

        [Fact]
        public async Task ListAccountsWithInvalidModelReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("WalletName", "A wallet name is required.");

            IActionResult result = await controller.ListAccountsAsync(new ListAccountsModel
            {
                WalletName = ""
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("A wallet name is required.", error.Message);
        }

        [Fact]
        public async Task ListAccountsWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetAccounts("wallet 0"))
                .Throws(new InvalidOperationException("Wallet not found."));

            var controller = this.GetWalletController();

            IActionResult result = await controller.ListAccountsAsync(new ListAccountsModel
            {
                WalletName = "wallet 0",
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.InvalidOperationException", error.Description);
            Assert.StartsWith("Wallet not found.", error.Message);
        }

        [Fact]
        public async Task GetUnusedAddressWithValidModelReturnsUnusedAddressAsync()
        {
            HdAddress address = WalletTestsHelpers.CreateAddress();
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetUnusedAddress(new WalletAccountReference("myWallet", "Account 1")))
                .Returns(address);

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetUnusedAddressAsync(new GetUnusedAddressModel
            {
                WalletName = "myWallet",
                AccountName = "Account 1"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            Assert.Equal(address.Address, viewResult.Value as string);
        }

        [Fact]
        public async Task GetUnusedAddressWithInvalidValidModelReturnsBadRequestAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("AccountName", "An account name is required.");

            IActionResult result = await controller.GetUnusedAddressAsync(new GetUnusedAddressModel
            {
                WalletName = "myWallet",
                AccountName = ""
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.Equal("An account name is required.", error.Message);
        }

        [Fact]
        public async Task GetUnusedAddressWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetUnusedAddress(new WalletAccountReference("myWallet", "Account 1")))
                .Throws(new InvalidOperationException("Wallet not found."));

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetUnusedAddressAsync(new GetUnusedAddressModel
            {
                WalletName = "myWallet",
                AccountName = "Account 1"
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.Equal(400, error.Status);
            Assert.StartsWith("System.InvalidOperationException", error.Description);
            Assert.StartsWith("Wallet not found.", error.Message);
        }

        [Fact]
        public async Task GetAllAddressesWithValidModelReturnsAllAddressesAsync()
        {
            string walletName = "myWallet";

            // Receive address with a transaction
            HdAddress usedReceiveAddress = WalletTestsHelpers.CreateAddress();
            TransactionData receiveTransaction =
                WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(500000), 1);
            usedReceiveAddress.Transactions.Add(receiveTransaction);

            // Receive address without a transaction
            HdAddress unusedReceiveAddress = WalletTestsHelpers.CreateAddress();

            // Change address with a transaction
            HdAddress usedChangeAddress = WalletTestsHelpers.CreateAddress(true);
            TransactionData changeTransaction =
                WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(500000), 1);
            usedChangeAddress.Transactions.Add(changeTransaction);

            // Change address without a transaction
            HdAddress unusedChangeAddress = WalletTestsHelpers.CreateAddress(true);

            var receiveAddresses = new List<HdAddress> { usedReceiveAddress, unusedReceiveAddress };
            var changeAddresses = new List<HdAddress> { usedChangeAddress, unusedChangeAddress };

            Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);
            HdAccount account = wallet.AddNewAccount((ExtPubKey)null, accountName: "Account 0");

            foreach (HdAddress addr in receiveAddresses)
                account.ExternalAddresses.Add(addr);
            foreach (HdAddress addr in changeAddresses)
                account.InternalAddresses.Add(addr);

            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            mockWalletManager.Setup(m => m.GetWallet(walletName)).Returns(wallet);
            mockWalletManager.Setup(m => m.GetUnusedAddresses(It.IsAny<WalletAccountReference>(), false))
                .Returns(new[] { unusedReceiveAddress }.ToList());
            mockWalletManager.Setup(m => m.GetUnusedAddresses(It.IsAny<WalletAccountReference>(), true))
                .Returns(new[] { unusedChangeAddress }.ToList());
            mockWalletManager.Setup(m => m.GetUsedAddresses(It.IsAny<WalletAccountReference>(), false))
                .Returns(new[] { (usedReceiveAddress, Money.Zero, Money.Zero) }.ToList());
            mockWalletManager.Setup(m => m.GetUsedAddresses(It.IsAny<WalletAccountReference>(), true))
                .Returns(new[] { (usedChangeAddress, Money.Zero, Money.Zero) }.ToList());

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetAllAddressesAsync(new GetAllAddressesModel
            { WalletName = "myWallet", AccountName = "Account 0" });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as AddressesModel;

            Assert.NotNull(model);
            Assert.Equal(4, model.Addresses.Count());

            AddressModel modelUsedReceiveAddress = model.Addresses.Single(a => a.Address == usedReceiveAddress.Address);
            Assert.Equal(modelUsedReceiveAddress.Address,
                model.Addresses.Single(a => a.Address == modelUsedReceiveAddress.Address).Address);
            Assert.False(model.Addresses.Single(a => a.Address == modelUsedReceiveAddress.Address).IsChange);
            Assert.True(model.Addresses.Single(a => a.Address == modelUsedReceiveAddress.Address).IsUsed);

            AddressModel modelUnusedReceiveAddress =
                model.Addresses.Single(a => a.Address == unusedReceiveAddress.Address);
            Assert.Equal(modelUnusedReceiveAddress.Address,
                model.Addresses.Single(a => a.Address == modelUnusedReceiveAddress.Address).Address);
            Assert.False(model.Addresses.Single(a => a.Address == modelUnusedReceiveAddress.Address).IsChange);
            Assert.False(model.Addresses.Single(a => a.Address == modelUnusedReceiveAddress.Address).IsUsed);

            AddressModel modelUsedChangeAddress = model.Addresses.Single(a => a.Address == usedChangeAddress.Address);
            Assert.Equal(modelUsedChangeAddress.Address,
                model.Addresses.Single(a => a.Address == modelUsedChangeAddress.Address).Address);
            Assert.True(model.Addresses.Single(a => a.Address == modelUsedChangeAddress.Address).IsChange);
            Assert.True(model.Addresses.Single(a => a.Address == modelUsedChangeAddress.Address).IsUsed);

            AddressModel modelUnusedChangeAddress =
                model.Addresses.Single(a => a.Address == unusedChangeAddress.Address);
            Assert.Equal(modelUnusedChangeAddress.Address,
                model.Addresses.Single(a => a.Address == modelUnusedChangeAddress.Address).Address);
            Assert.True(model.Addresses.Single(a => a.Address == modelUnusedChangeAddress.Address).IsChange);
            Assert.False(model.Addresses.Single(a => a.Address == modelUnusedChangeAddress.Address).IsUsed);
        }

        [Fact]
        public async Task GetMaximumBalanceWithValidModelStateReturnsMaximumBalanceAsync()
        {
            var controller = this.GetWalletController();

            controller.ModelState.AddModelError("Error in model", "There was an error in the model.");

            IActionResult result = await controller.GetMaximumSpendableBalanceAsync(new WalletMaximumBalanceRequest
            {
                WalletName = "myWallet",
                AccountName = "account 1",
                FeeType = "low",
                AllowUnconfirmed = true
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);

            ErrorModel error = errorResponse.Errors[0];
            Assert.NotNull(errorResult.StatusCode);
            Assert.Equal((int)HttpStatusCode.BadRequest, errorResult.StatusCode.Value);
            Assert.Equal("There was an error in the model.", error.Message);
        }

        [Fact]
        public async Task GetMaximumBalanceSuccessfullyReturnsMaximumBalanceAndFeeAsync()
        {
            var mockWalletTransactionHandler = this.ConfigureMock<IWalletTransactionHandler>();
            mockWalletTransactionHandler
                .Setup(w => w.GetMaximumSpendableAmount(It.IsAny<WalletAccountReference>(), It.IsAny<FeeType>(), true))
                .Returns((new Money(1000000), new Money(100)));

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetMaximumSpendableBalanceAsync(new WalletMaximumBalanceRequest
            {
                WalletName = "myWallet",
                AccountName = "account 1",
                FeeType = "low",
                AllowUnconfirmed = true
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as MaxSpendableAmountModel;

            Assert.NotNull(model);
            Assert.Equal(new Money(1000000), model.MaxSpendableAmount);
            Assert.Equal(new Money(100), model.Fee);
        }

        [Fact]
        public async Task GetMaximumBalanceWithExceptionReturnsBadRequestAsync()
        {
            var mockWalletTransactionHandler = this.ConfigureMock<IWalletTransactionHandler>();
            mockWalletTransactionHandler
                .Setup(w => w.GetMaximumSpendableAmount(It.IsAny<WalletAccountReference>(), It.IsAny<FeeType>(), true))
                .Throws(new Exception("failure"));

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetMaximumSpendableBalanceAsync(new WalletMaximumBalanceRequest
            {
                WalletName = "myWallet",
                AccountName = "account 1",
                FeeType = "low",
                AllowUnconfirmed = true
            });

            var errorResult = Assert.IsType<ErrorResult>(result);
            var errorResponse = Assert.IsType<ErrorResponse>(errorResult.Value);
            Assert.Single(errorResponse.Errors);
            Assert.NotNull(errorResult.StatusCode);
            Assert.Equal((int)HttpStatusCode.BadRequest, errorResult.StatusCode.Value);
        }

        [Fact]
        public async Task GetTransactionFeeEstimateWithValidRequestReturnsFeeAsync()
        {
            var mockWalletManager = this.ConfigureMock<IWalletManager>();
            var mockWalletTransactionHandler = this.ConfigureMock<IWalletTransactionHandler>();
            var key = new Key();
            var expectedFee = new Money(1000);
            mockWalletTransactionHandler.Setup(m => m.EstimateFee(It.IsAny<TransactionBuildContext>()))
                .Returns(expectedFee);

            var controller = this.GetWalletController();

            IActionResult result = await controller.GetTransactionFeeEstimateAsync(new TxFeeEstimateRequest
            {
                AccountName = "Account 1",
                Recipients = new List<RecipientModel>
                {
                    new RecipientModel
                    {
                        DestinationAddress = key.PubKey.GetAddress(this.Network).ToString(),
                        Amount = new Money(150000).ToString()
                    }
                },
                FeeType = "105",
                WalletName = "myWallet"
            });

            var viewResult = Assert.IsType<JsonResult>(result);
            var actualFee = viewResult.Value as Money;

            Assert.NotNull(actualFee);
            Assert.Equal(expectedFee, actualFee);
        }

        [Fact]
        public async Task RemoveAllTransactionsWithSyncEnabledSyncsAfterRemovalAsync()
        {
            // Arrange.
            string walletName = "wallet1";
            Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);

            uint256 trxId1 = uint256.Parse("d6043add63ec364fcb591cf209285d8e60f1cc06186d4dcbce496cdbb4303400");
            uint256 trxId2 = uint256.Parse("a3dd63ec364fcb59043a1cf209285d8e60f1cc06186d4dcbce496cdbb4303401");
            var resultModel = new HashSet<(uint256 trxId, DateTimeOffset creationTime)>();
            resultModel.Add((trxId1, DateTimeOffset.Now));
            resultModel.Add((trxId2, DateTimeOffset.Now));

            var walletManager = this.ConfigureMock<IWalletManager>();
            var walletSyncManager = this.ConfigureMock<IWalletSyncManager>();
            walletManager.Setup(manager => manager.GetWallet(It.IsAny<string>())).Returns(wallet);
            walletManager.Setup(manager => manager.RemoveAllTransactions(walletName)).Returns(resultModel);
            walletSyncManager.Setup(manager => manager.SyncFromHeight(It.IsAny<int>(), It.IsAny<string>()));
            ChainIndexer chainIndexer = WalletTestsHelpers.GenerateChainWithHeight(3, this.Network);

            var controller = this.GetWalletController();

            var requestModel = new RemoveTransactionsModel
            {
                WalletName = walletName,
                ReSync = true,
                DeleteAll = true
            };

            // Act.
            IActionResult result = await controller.RemoveTransactionsAsync(requestModel);

            // Assert.
            walletManager.VerifyAll();
            walletSyncManager.Verify(manager => manager.SyncFromHeight(It.IsAny<int>(), It.IsAny<string>()),
                Times.Once);

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as IEnumerable<RemovedTransactionModel>;
            Assert.NotNull(model);
            Assert.Equal(2, model.Count());
            Assert.True(model.SingleOrDefault(t => t.TransactionId == trxId1) != null);
            Assert.True(model.SingleOrDefault(t => t.TransactionId == trxId2) != null);
        }

        [Fact]
        public async Task RemoveAllTransactionsWithSyncDisabledDoesNotSyncAfterRemovalAsync()
        {
            // Arrange.
            string walletName = "wallet1";
            uint256 trxId1 = uint256.Parse("d6043add63ec364fcb591cf209285d8e60f1cc06186d4dcbce496cdbb4303400");
            uint256 trxId2 = uint256.Parse("a3dd63ec364fcb59043a1cf209285d8e60f1cc06186d4dcbce496cdbb4303401");
            var resultModel = new HashSet<(uint256 trxId, DateTimeOffset creationTime)>();
            resultModel.Add((trxId1, DateTimeOffset.Now));
            resultModel.Add((trxId2, DateTimeOffset.Now));

            var walletManager = this.ConfigureMock<IWalletManager>();
            var walletSyncManager = this.ConfigureMock<IWalletSyncManager>();
            walletManager.Setup(manager => manager.RemoveAllTransactions(walletName)).Returns(resultModel);
            ChainIndexer chainIndexer = WalletTestsHelpers.GenerateChainWithHeight(3, this.Network);

            var controller = this.GetWalletController();

            var requestModel = new RemoveTransactionsModel
            {
                WalletName = walletName,
                ReSync = false,
                DeleteAll = true
            };

            // Act.
            IActionResult result = await controller.RemoveTransactionsAsync(requestModel);

            // Assert.
            walletManager.VerifyAll();
            walletSyncManager.Verify(manager => manager.SyncFromHeight(It.IsAny<int>(), It.IsAny<string>()),
                Times.Never);

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as IEnumerable<RemovedTransactionModel>;
            Assert.NotNull(model);
            Assert.Equal(2, model.Count());
            Assert.True(model.SingleOrDefault(t => t.TransactionId == trxId1) != null);
            Assert.True(model.SingleOrDefault(t => t.TransactionId == trxId2) != null);
        }

        [Fact]
        public async Task RemoveTransactionsWithIdsRemovesAllTransactionsByIdsAsync()
        {
            // Arrange.
            string walletName = "wallet1";
            Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);

            uint256 trxId1 = uint256.Parse("d6043add63ec364fcb591cf209285d8e60f1cc06186d4dcbce496cdbb4303400");
            var resultModel = new HashSet<(uint256 trxId, DateTimeOffset creationTime)>();
            resultModel.Add((trxId1, DateTimeOffset.Now));

            var walletManager = this.ConfigureMock<IWalletManager>();
            var walletSyncManager = this.ConfigureMock<IWalletSyncManager>();
            walletManager.Setup(manager => manager.GetWallet(It.IsAny<string>())).Returns(wallet);
            walletManager.Setup(manager => manager.RemoveTransactionsByIds(walletName, new[] { trxId1 }))
                .Returns(resultModel);
            walletSyncManager.Setup(manager => manager.SyncFromHeight(It.IsAny<int>(), It.IsAny<string>()));
            ChainIndexer chainIndexer = WalletTestsHelpers.GenerateChainWithHeight(3, this.Network);

            var controller = this.GetWalletController();

            var requestModel = new RemoveTransactionsModel
            {
                WalletName = walletName,
                ReSync = true,
                TransactionsIds = new[] { "d6043add63ec364fcb591cf209285d8e60f1cc06186d4dcbce496cdbb4303400" }
            };

            // Act.
            IActionResult result = await controller.RemoveTransactionsAsync(requestModel);

            // Assert.
            walletManager.VerifyAll();
            walletManager.Verify(manager => manager.RemoveAllTransactions(It.IsAny<string>()), Times.Never);
            walletSyncManager.Verify(manager => manager.SyncFromHeight(It.IsAny<int>(), It.IsAny<string>()),
                Times.Once);

            var viewResult = Assert.IsType<JsonResult>(result);
            var model = viewResult.Value as IEnumerable<RemovedTransactionModel>;
            Assert.NotNull(model);
            Assert.Single(model);
            Assert.True(model.SingleOrDefault(t => t.TransactionId == trxId1) != null);
        }

        private TMock ConfigureMockInstance<TMock>(TMock value) where TMock : class
        {
            if (!this.configuredMocks.ContainsKey(typeof(TMock)))
            {
                this.configuredMocks.Add(typeof(TMock), value);
            }

            return (TMock)this.configuredMocks[typeof(TMock)];
        }

        private Mock<TMock> ConfigureMock<TMock>(Action<Mock<TMock>> setup = null) where TMock : class
        {
            if (!this.configuredMocks.ContainsKey(typeof(TMock)))
            {
                this.configuredMocks.Add(typeof(TMock), new Mock<TMock>());
            }

            setup?.Invoke((Mock<TMock>)this.configuredMocks[typeof(TMock)]);
            return (Mock<TMock>)this.configuredMocks[typeof(TMock)];
        }

        private TMock GetMock<TMock>(bool createIfNotExists = false) where TMock : class
        {
            if (this.configuredMocks.ContainsKey(typeof(TMock))
                && this.configuredMocks[typeof(TMock)] as Mock<TMock> != null)
            {
                return ((Mock<TMock>)this.configuredMocks[typeof(TMock)]).Object;
            }

            return this.configuredMocks.ContainsKey(typeof(TMock))
                ? (TMock)this.configuredMocks[typeof(TMock)]
                : createIfNotExists
                    ? new Mock<TMock>().Object
                    : null;
        }

        private WalletController GetWalletController()
        {
            var mocker = new AutoMocker();

            mocker.Use(typeof(ILoggerFactory), this.GetMock<ILoggerFactory>() ?? this.LoggerFactory.Object);
            mocker.Use(typeof(IWalletManager), this.GetMock<IWalletManager>(true));
            mocker.Use(typeof(IWalletTransactionHandler), this.GetMock<IWalletTransactionHandler>(true));
            mocker.Use(typeof(IWalletSyncManager), this.GetMock<IWalletSyncManager>(true));
            mocker.Use(typeof(Network), this.GetMock<Network>() ?? this.Network);
            mocker.Use(typeof(ChainIndexer), this.GetMock<ChainIndexer>() ?? this.chainIndexer);
            mocker.Use(typeof(IBroadcasterManager), this.GetMock<IBroadcasterManager>(true));
            mocker.Use(typeof(IConsensusManager), this.GetMock<IConsensusManager>(true));
            mocker.Use(typeof(IDateTimeProvider), this.GetMock<IDateTimeProvider>() ?? DateTimeProvider.Default);
            mocker.Use(typeof(IConnectionManager), this.GetMock<IConnectionManager>(true));
            mocker.Use(typeof(NodeSettings), NodeSettings.Default(this.Network));
            mocker.Use(typeof(IWalletService), this.GetMock<WalletService>() ?? mocker.CreateInstance<WalletService>());

            return mocker.CreateInstance<WalletController>();
        }
    }
}