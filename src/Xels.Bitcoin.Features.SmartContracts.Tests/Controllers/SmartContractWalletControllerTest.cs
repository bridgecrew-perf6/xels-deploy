﻿using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Features.SmartContracts.Wallet;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.SmartContracts.CLR;
using Xels.SmartContracts.Core.Receipts;
using Xels.SmartContracts.Networks;

namespace Xels.Bitcoin.Features.SmartContracts.Tests.Controllers
{
    public class SmartContractWalletControllerTest
    {
        private readonly Mock<IBroadcasterManager> broadcasterManager;
        private readonly Mock<ICallDataSerializer> callDataSerializer;
        private readonly Mock<IConnectionManager> connectionManager;
        private readonly Mock<ILoggerFactory> loggerFactory;
        private readonly Network network;
        private readonly Mock<IReceiptRepository> receiptRepository;
        private readonly Mock<IWalletManager> walletManager;
        private Mock<ISmartContractTransactionService> smartContractTransactionService;

        public SmartContractWalletControllerTest()
        {
            this.broadcasterManager = new Mock<IBroadcasterManager>();
            this.callDataSerializer = new Mock<ICallDataSerializer>();
            this.connectionManager = new Mock<IConnectionManager>();
            this.loggerFactory = new Mock<ILoggerFactory>();
            this.network = new SmartContractsRegTest();
            this.receiptRepository = new Mock<IReceiptRepository>();
            this.walletManager = new Mock<IWalletManager>();
            this.smartContractTransactionService = new Mock<ISmartContractTransactionService>();
        }

        //[Fact]
        //public void GetHistoryWithValidModelWithoutTransactionSpendingDetailsReturnsWalletHistoryModel()
        //{
        //    ulong gasPrice = SmartContractMempoolValidator.MinGasPrice;
        //    int vmVersion = 1;
        //    var gasLimit = (Xels.SmartContracts.RuntimeObserver.Gas)(SmartContractFormatLogic.GasLimitMaximum / 2);
        //    var contractTxData = new ContractTxData(vmVersion, gasPrice, gasLimit, new byte[] { 0, 1, 2, 3 });
        //    var callDataSerializer = new CallDataSerializer(new ContractPrimitiveSerializer(new SmartContractsRegTest()));
        //    var contractCreateScript = new Script(callDataSerializer.Serialize(contractTxData));

        //    string walletName = "myWallet";
        //    HdAddress address = WalletTestsHelpers.CreateAddress();
        //    TransactionData normalTransaction = WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(500000), 1);
        //    TransactionData createTransaction = WalletTestsHelpers.CreateTransaction(new uint256(1), new Money(500000), 1);
        //    createTransaction.SpendingDetails = new SpendingDetails
        //    {
        //        BlockHeight = 100,
        //        CreationTime = DateTimeOffset.Now,
        //        TransactionId = uint256.One,
        //        Payments = new List<PaymentDetails>
        //        {
        //            new PaymentDetails
        //            {
        //                Amount = new Money(100000),
        //                DestinationScriptPubKey = contractCreateScript
        //            }
        //        }
        //    };

        //    address.Transactions.Add(normalTransaction);
        //    address.Transactions.Add(createTransaction);

        //    var addresses = new List<HdAddress> { address };
        //    Features.Wallet.Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);
        //    HdAccount account = wallet.AddNewAccount((ExtPubKey)null);

        //    foreach (HdAddress addr in addresses)
        //        account.ExternalAddresses.Add(address);

        //    List<FlatHistory> flat = addresses.SelectMany(s => s.Transactions.Select(t => new FlatHistory { Address = s, Transaction = t })).ToList();

        //    var accountsHistory = new List<AccountHistory> { new AccountHistory { History = flat, Account = account } };
        //    this.walletManager.Setup(w => w.GetHistory(walletName, It.IsAny<string>(), null)).Returns(accountsHistory);
        //    this.walletManager.Setup(w => w.GetWallet(walletName)).Returns(wallet);
        //    this.walletManager.Setup(w => w.GetAccounts(walletName)).Returns(new List<HdAccount> { account });

        //    var receipt = new Receipt(null, 12345, new Log[0], null, null, null, uint160.Zero, true, null, null, 2, 100000);
        //    this.receiptRepository.Setup(x => x.RetrieveMany(It.IsAny<IList<uint256>>()))
        //        .Returns(new List<Receipt> { receipt });
        //    this.callDataSerializer.Setup(x => x.Deserialize(It.IsAny<byte[]>()))
        //        .Returns(Result.Ok(new ContractTxData(0, 0, (Xels.SmartContracts.RuntimeObserver.Gas)0, new uint160(0), null, null)));

        //    var controller = new SmartContractWalletController(
        //        this.broadcasterManager.Object,
        //        this.callDataSerializer.Object,
        //        this.connectionManager.Object,
        //        this.loggerFactory.Object,
        //        this.network,
        //        this.receiptRepository.Object,
        //        this.walletManager.Object,
        //        this.smartContractTransactionService.Object);

        //    var request = new GetHistoryRequest
        //    {
        //        Address = address.Address,
        //        WalletName = walletName
        //    };

        //    IActionResult result = controller.GetHistory(request);

        //    JsonResult viewResult = Assert.IsType<JsonResult>(result);
        //    var model = viewResult.Value as IEnumerable<ContractTransactionItem>;

        //    Assert.NotNull(model);
        //    Assert.Single(model);

        //    ContractTransactionItem resultingTransaction = model.ElementAt(0);

        //    ContractTransactionItem resultingCreate = model.ElementAt(0);
        //    Assert.Equal(ContractTransactionItemType.ContractCreate, resultingTransaction.Type);
        //    Assert.Equal(createTransaction.SpendingDetails.TransactionId, resultingTransaction.Hash);
        //    Assert.Equal(createTransaction.SpendingDetails.Payments.First().Amount.ToUnit(MoneyUnit.Satoshi), resultingTransaction.Amount);
        //    Assert.Equal(uint160.Zero.ToBase58Address(this.network), resultingTransaction.To);
        //    Assert.Equal(createTransaction.SpendingDetails.BlockHeight, resultingTransaction.BlockHeight);
        //    Assert.Equal((createTransaction.Amount - createTransaction.SpendingDetails.Payments.First().Amount).ToUnit(MoneyUnit.Satoshi), resultingTransaction.TransactionFee);
        //    Assert.Equal(receipt.GasPrice * receipt.GasUsed, resultingTransaction.GasFee);
        //}

        //[Fact]
        //public void GetHistoryWithValidModelWithSkipAndTakeReturnsWalletHistoryModel()
        //{
        //    ulong gasPrice = SmartContractMempoolValidator.MinGasPrice;
        //    int vmVersion = 1;
        //    var gasLimit = (Xels.SmartContracts.RuntimeObserver.Gas)(SmartContractFormatLogic.GasLimitMaximum / 2);
        //    var contractTxData = new ContractTxData(vmVersion, gasPrice, gasLimit, new byte[] { 0, 1, 2, 3 });
        //    var callDataSerializer = new CallDataSerializer(new ContractPrimitiveSerializer(new SmartContractsRegTest()));
        //    var contractCreateScript = new Script(callDataSerializer.Serialize(contractTxData));

        //    string walletName = "myWallet";
        //    HdAddress address = WalletTestsHelpers.CreateAddress();

        //    const int totalHistoryLength = 100;
        //    const int toSkip = 10;
        //    const int toTake = 10;

        //    for (int i = 0; i < totalHistoryLength; i++)
        //    {
        //        TransactionData createTransaction = WalletTestsHelpers.CreateTransaction(new uint256((ulong)i), new Money(500000), 100 + i);
        //        createTransaction.SpendingDetails = new SpendingDetails
        //        {
        //            BlockHeight = 100 + i,
        //            CreationTime = DateTimeOffset.Now,
        //            TransactionId = new uint256((ulong)i),
        //            Payments = new List<PaymentDetails>
        //            {
        //                new PaymentDetails
        //                {
        //                    Amount = new Money(100000),
        //                    DestinationScriptPubKey = contractCreateScript
        //                }
        //            }
        //        };

        //        address.Transactions.Add(createTransaction);
        //    }

        //    var addresses = new List<HdAddress> { address };
        //    Features.Wallet.Wallet wallet = WalletTestsHelpers.CreateWallet(walletName);
        //    HdAccount account = wallet.AddNewAccount((ExtPubKey)null);

        //    foreach (HdAddress addr in addresses)
        //        account.ExternalAddresses.Add(addr);

        //    List<FlatHistory> flat = addresses.SelectMany(s => s.Transactions.Select(t => new FlatHistory { Address = s, Transaction = t })).ToList();

        //    var accountsHistory = new List<AccountHistory> { new AccountHistory { History = flat, Account = account } };
        //    this.walletManager.Setup(w => w.GetHistory(walletName, It.IsAny<string>(), null)).Returns(accountsHistory);
        //    this.walletManager.Setup(w => w.GetWallet(walletName)).Returns(wallet);
        //    this.walletManager.Setup(w => w.GetAccounts(walletName)).Returns(new List<HdAccount> { account });

        //    var receipt = new Receipt(null, 12345, new Log[0], null, null, null, uint160.Zero, true, null, null, 2, 100000);
        //    var receiptList = new List<Receipt>();
        //    for (int i = 0; i < totalHistoryLength; i++)
        //    {
        //        receiptList.Add(receipt);
        //    }

        //    this.receiptRepository.Setup(x => x.RetrieveMany(It.IsAny<IList<uint256>>()))
        //        .Returns(receiptList);
        //    this.callDataSerializer.Setup(x => x.Deserialize(It.IsAny<byte[]>()))
        //        .Returns(Result.Ok(new ContractTxData(0, 0, (Xels.SmartContracts.RuntimeObserver.Gas)0, new uint160(0), null, null)));

        //    var controller = new SmartContractWalletController(
        //        this.broadcasterManager.Object,
        //        this.callDataSerializer.Object,
        //        this.connectionManager.Object,
        //        this.loggerFactory.Object,
        //        this.network,
        //        this.receiptRepository.Object,
        //        this.walletManager.Object,
        //        this.smartContractTransactionService.Object);

        //    var request = new GetHistoryRequest
        //    {
        //        Address = address.Address,
        //        WalletName = walletName,
        //        Skip = toSkip,
        //        Take = toTake
        //    };

        //    IActionResult result = controller.GetHistory(request);

        //    JsonResult viewResult = Assert.IsType<JsonResult>(result);
        //    var model = viewResult.Value as IEnumerable<ContractTransactionItem>;

        //    Assert.NotNull(model);
        //    Assert.Equal(toTake, model.Count());
        //    Assert.Equal(new uint256(toSkip), model.ElementAt(toTake - 1).Hash);
        //    Assert.Equal(new uint256(toSkip + toTake - 1), model.ElementAt(0).Hash);
        //}
    }
}
