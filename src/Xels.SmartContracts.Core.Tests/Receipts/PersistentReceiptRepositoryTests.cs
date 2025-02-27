﻿using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using Xels.Bitcoin.Tests.Common;
using Xels.SmartContracts.Core.Receipts;
using Xunit;

namespace Xels.SmartContracts.Core.Tests.Receipts
{
    public class PersistentReceiptRepositoryTests
    {
        private readonly IReceiptRepository db;

        public PersistentReceiptRepositoryTests()
        {
            this.db = new PersistentReceiptRepository(TestBase.CreateDataFolder(this));
        }

        [Fact]
        public void Store_Basic_Receipt()
        {
            var data1 = new byte[] { 1, 2, 3, 4 };
            var topics1 = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("ALogStructName"),
                BitConverter.GetBytes(123)
            };
            var log1 = new Log(new uint160(1234), topics1, data1);

            var data2 = new byte[] { 4, 5, 6, 7 };
            var topics2 = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("ALogStructName2"),
                BitConverter.GetBytes(234)
            };
            var log2 = new Log(new uint160(12345), topics2, data2);

            var receipt = new Receipt(new uint256(1234), 12345, new Log[] { log1, log2 }, new uint256(12345), new uint160(25), new uint160(24), new uint160(23), true, "SomeResult", "SomeExceptionString", 54321, 1_000_000, "TestMethodName", 123456) { BlockHash = new uint256(1234) };
            this.db.Store(new Receipt[] { receipt });
            Receipt retrievedReceipt = this.db.Retrieve(receipt.TransactionHash);
            ReceiptSerializationTest.TestStorageReceiptEquality(receipt, retrievedReceipt);
        }

        [Fact]
        public void Store_Receipt_With_Null_Optionals()
        {
            var receipt = new Receipt(new uint256(1234), 12345, new Log[] { }, new uint256(12345), new uint160(25), new uint160(24), new uint160(23), true, "SomeResult", "SomeExceptionString", 54321, 1_000_000, null, null) { BlockHash = new uint256(1234) };
            this.db.Store(new Receipt[] { receipt });
            Receipt retrievedReceipt = this.db.Retrieve(receipt.TransactionHash);
            ReceiptSerializationTest.TestStorageReceiptEquality(receipt, retrievedReceipt);
        }
    }
}
