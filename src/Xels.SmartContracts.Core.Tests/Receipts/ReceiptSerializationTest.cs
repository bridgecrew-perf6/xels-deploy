﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NBitcoin;
using Nethereum.RLP;
using Xels.SmartContracts.Core.Receipts;
using Xunit;

namespace Xels.SmartContracts.Core.Tests.Receipts
{
    public class ReceiptSerializationTest
    {
        [Fact]
        public void Log_Serializes_And_Deserializes()
        {
            var data = new byte[] { 1, 2, 3, 4 };
            var topics = new List<byte[]>
            {
                Encoding.UTF8.GetBytes("ALogStructName"),
                BitConverter.GetBytes(123)
            };
            var log = new Log(new uint160(1234), topics, data);

            byte[] serialized = log.ToBytesRlp();
            Log deserialized = Log.FromBytesRlp(serialized);
            TestLogsEqual(log, deserialized);
        }

        [Fact]
        public void Receipt_Serializes_And_Deserializes()
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

            var receipt = new Receipt(new uint256(1234), 12345, new Log[] { log1, log2 });
            TestConsensusSerialize(receipt);
            receipt = new Receipt(receipt.PostState, receipt.GasUsed, receipt.Logs, new uint256(12345), new uint160(25), new uint160(24), new uint160(23), true, null, null, 54321, 1_000_000) { BlockHash = new uint256(1234) };
            TestStorageSerialize(receipt);

            // Test cases where either the sender or contract is null - AKA CALL vs CREATE
            receipt = new Receipt(receipt.PostState, receipt.GasUsed, receipt.Logs, new uint256(12345), new uint160(25), new uint160(24), null, true, "Test Result", "Test Error Message", 54321, 1_000_000, "TestMethodName", 123456) { BlockHash = new uint256(1234) };
            TestStorageSerialize(receipt);
            receipt = new Receipt(receipt.PostState, receipt.GasUsed, receipt.Logs, new uint256(12345), new uint160(25), null, new uint160(23), true, "Test Result 2", "Test Error Message 2", 54321, 1_000_000) { BlockHash = new uint256(1234) };
            TestStorageSerialize(receipt);
        }

        [Fact]
        public void SanityTest()
        {
            // https://github.com/Nethereum/Nethereum/issues/510

            var item1 = new byte[] { 0x01 };
            var item2 = new byte[] { 0x01, 0x02 };

            byte[] encoded = RLP.EncodeList(RLP.EncodeElement(item1), RLP.EncodeElement(item2));

            RLPCollection decoded = (RLPCollection)RLP.Decode(encoded);

            // The actual list used to be at decoded[0]. Previously, these asserts would fail.
            Assert.Equal(item1, decoded[0].RLPData);
            Assert.Equal(item2, decoded[1].RLPData);
        }

        [Fact]
        public void Receipt_With_No_MethodName_Or_BlockNumber_Deserializes_Correctly()
        {
            var receipt = new Receipt(new uint256(1234), 12345, new Log[]{}, new uint256(12345), new uint160(25), new uint160(24), null, true, "Test Result", "Test Error Message", 54321, 1_000_000) { BlockHash = new uint256(1234) };

            byte[] serialized = ToStorageBytesRlp_NoMethodName(receipt);

            Receipt deserialized = Receipt.FromStorageBytesRlp(serialized);
            TestStorageReceiptEquality(receipt, deserialized);
        }

        private void TestConsensusSerialize(Receipt receipt)
        {
            byte[] serialized = receipt.ToConsensusBytesRlp();
            Receipt deserialized = Receipt.FromConsensusBytesRlp(serialized);
            Assert.Equal(receipt.PostState, deserialized.PostState);
            Assert.Equal(receipt.GasUsed, deserialized.GasUsed);
            Assert.Equal(receipt.Bloom, deserialized.Bloom);
            Assert.Equal(receipt.Logs.Length, deserialized.Logs.Length);

            for(int i=0; i < receipt.Logs.Length; i++)
            {
                TestLogsEqual(receipt.Logs[i], deserialized.Logs[i]);
            }
        }

        private void TestStorageSerialize(Receipt receipt)
        {
            byte[] serialized = receipt.ToStorageBytesRlp();
            Receipt deserialized = Receipt.FromStorageBytesRlp(serialized);
            TestStorageReceiptEquality(receipt, deserialized);
        }

        /// <summary>
        /// Serializes a receipt without including the method name. For backwards compatibility testing.
        /// </summary>
        /// <param name="receipt">See <see cref="Receipt"/>.</param>
        /// <returns>A byte array which is the serialized receipt.</returns>
        public byte[] ToStorageBytesRlp_NoMethodName(Receipt receipt)
        {
            IList<byte[]> encodedLogs = receipt.Logs.Select(x => RLP.EncodeElement(x.ToBytesRlp())).ToList();

            return RLP.EncodeList(
                RLP.EncodeElement(receipt.PostState.ToBytes()),
                RLP.EncodeElement(BitConverter.GetBytes(receipt.GasUsed)),
                RLP.EncodeElement(receipt.Bloom.ToBytes()),
                RLP.EncodeElement(RLP.EncodeList(encodedLogs.ToArray())),
                RLP.EncodeElement(receipt.TransactionHash.ToBytes()),
                RLP.EncodeElement(receipt.BlockHash.ToBytes()),
                RLP.EncodeElement(receipt.From.ToBytes()),
                RLP.EncodeElement(receipt.To?.ToBytes()),
                RLP.EncodeElement(receipt.NewContractAddress?.ToBytes()),
                RLP.EncodeElement(BitConverter.GetBytes(receipt.Success)),
                RLP.EncodeElement(Encoding.UTF8.GetBytes(receipt.Result ?? "")),
                RLP.EncodeElement(Encoding.UTF8.GetBytes(receipt.ErrorMessage ?? "")),
                RLP.EncodeElement(BitConverter.GetBytes(receipt.GasPrice)),
                RLP.EncodeElement(BitConverter.GetBytes(receipt.Amount))
            );
        }

        /// <summary>
        /// Ensures that two receipts and all their properties are equal.
        /// </summary>
        /// <param name="receipt1">The first receipt to compare.</param>
        /// <param name="receipt2">The second receipt to compare.</param>
        internal static void TestStorageReceiptEquality(Receipt receipt1, Receipt receipt2)
        {
            Assert.Equal(receipt1.PostState, receipt2.PostState);
            Assert.Equal(receipt1.GasUsed, receipt2.GasUsed);
            Assert.Equal(receipt1.Bloom, receipt2.Bloom);
            Assert.Equal(receipt1.Logs.Length, receipt2.Logs.Length);

            for (int i = 0; i < receipt1.Logs.Length; i++)
            {
                TestLogsEqual(receipt1.Logs[i], receipt2.Logs[i]);
            }

            Assert.Equal(receipt1.TransactionHash, receipt2.TransactionHash);
            Assert.Equal(receipt1.BlockHash, receipt2.BlockHash);
            Assert.Equal(receipt1.From, receipt2.From);
            Assert.Equal(receipt1.To, receipt2.To);
            Assert.Equal(receipt1.NewContractAddress, receipt2.NewContractAddress);
            Assert.Equal(receipt1.Success, receipt2.Success);
            Assert.Equal(receipt1.ErrorMessage, receipt2.ErrorMessage);
            Assert.Equal(receipt1.MethodName, receipt2.MethodName);
            Assert.Equal(receipt1.BlockNumber, receipt2.BlockNumber);
        }

        private static void TestLogsEqual(Log log1, Log log2)
        {
            Assert.Equal(log1.Address, log2.Address);
            Assert.Equal(log1.Data, log2.Data);
            Assert.Equal(log1.Topics.Count, log2.Topics.Count);
            for(int i=0; i < log1.Topics.Count; i++)
            {
                Assert.Equal(log1.Topics[i], log2.Topics[i]);
            }
        }
    }
}
