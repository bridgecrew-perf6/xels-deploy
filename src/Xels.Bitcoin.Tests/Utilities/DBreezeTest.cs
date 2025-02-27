﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DBreeze;
using DBreeze.DataTypes;
using FluentAssertions;
using NBitcoin;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Tests.Utilities
{
    /// <summary>
    /// Tests of DBreeze database and <see cref="DBreezeSerializer"/> class.
    /// </summary>
    public class DBreezeTest : TestBase
    {
        /// <summary>Provider of binary (de)serialization for data stored in the database.</summary>
        private readonly DBreezeSerializer dbreezeSerializer;

        /// <summary>
        /// Initializes the DBreeze serializer.
        /// </summary>
        public DBreezeTest() : base(KnownNetworks.StraxRegTest)
        {
            this.dbreezeSerializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);
        }

        [Fact]
        public void SerializerWithBitcoinSerializableReturnsAsBytes()
        {
            Block block = KnownNetworks.StraxRegTest.Consensus.ConsensusFactory.CreateBlock();

            byte[] result = this.dbreezeSerializer.Serialize(block);

            Assert.Equal(block.ToBytes(), result);
        }

        [Fact]
        public void SerializerWithUint256ReturnsAsBytes()
        {
            var val = new uint256();

            byte[] result = this.dbreezeSerializer.Serialize(val);

            Assert.Equal(val.ToBytes(), result);
        }

        [Fact]
        public void SerializerWithUnsupportedObjectThrowsException()
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                string test = "Should throw exception.";

                this.dbreezeSerializer.Serialize(test);
            });
        }

        [Fact]
        public void DeserializerWithCoinsDeserializesObject()
        {
            Network network = KnownNetworks.StraxRegTest;
            Block genesis = network.GetGenesis();
            var coins = new Bitcoin.Utilities.Coins(0, genesis.Transactions[0].Outputs.First(), true);

            var result = (Bitcoin.Utilities.Coins)this.dbreezeSerializer.Deserialize(coins.ToBytes(KnownNetworks.StraxRegTest.Consensus.ConsensusFactory), typeof(Bitcoin.Utilities.Coins));

            Assert.Equal(coins.IsCoinbase, result.IsCoinbase);
            Assert.Equal(coins.Height, result.Height);
            Assert.Equal(coins.TxOut.ScriptPubKey.Hash, result.TxOut.ScriptPubKey.Hash);
            Assert.Equal(coins.TxOut.Value, result.TxOut.Value);
        }

        [Fact]
        public void DeserializerWithBlockHeaderDeserializesObject()
        {
            Network network = KnownNetworks.StraxRegTest;
            Block genesis = network.GetGenesis();
            BlockHeader blockHeader = genesis.Header;

            var result = (BlockHeader)this.dbreezeSerializer.Deserialize(blockHeader.ToBytes(KnownNetworks.StraxRegTest.Consensus.ConsensusFactory), typeof(BlockHeader));

            Assert.Equal(blockHeader.GetHash(), result.GetHash());
        }

        [Fact]
        public void DeserializerWithRewindDataDeserializesObject()
        {
            Network network = KnownNetworks.StraxRegTest;
            Block genesis = network.GetGenesis();
            var rewindData = new RewindData(new HashHeightPair(genesis.GetHash(), 0));

            var result = (RewindData)this.dbreezeSerializer.Deserialize(rewindData.ToBytes(), typeof(RewindData));

            Assert.Equal(genesis.GetHash(), result.PreviousBlockHash.Hash);
        }

        [Fact]
        public void DeserializerWithuint256DeserializesObject()
        {
            uint256 val = uint256.One;

            var result = (uint256)this.dbreezeSerializer.Deserialize(val.ToBytes(), typeof(uint256));

            Assert.Equal(val, result);
        }

        [Fact]
        public void DeserializerWithBlockDeserializesObject()
        {
            Network network = KnownNetworks.StraxRegTest;
            Block block = network.GetGenesis();

            var result = (Block)this.dbreezeSerializer.Deserialize(block.ToBytes(KnownNetworks.StraxRegTest.Consensus.ConsensusFactory), typeof(Block));

            Assert.Equal(block.GetHash(), result.GetHash());
        }

        [Fact]
        public void DeserializerWithNotSupportedClassThrowsException()
        {
            Assert.Throws<NotSupportedException>(() =>
            {
                string test = "Should throw exception.";

                this.dbreezeSerializer.Deserialize(Encoding.UTF8.GetBytes(test), typeof(string));
            });
        }

        private class UnknownBitcoinSerialisable : IBitcoinSerializable
        {
            public int ReadWriteCalls;

            public void ReadWrite(BitcoinStream stream) { this.ReadWriteCalls++; }
        }

        [Fact]
        public void DeserializeAnyIBitcoinSerializableDoesNotThrowException()
        {
            var result = (UnknownBitcoinSerialisable)this.dbreezeSerializer.Deserialize(Encoding.UTF8.GetBytes("useless"), typeof(UnknownBitcoinSerialisable));
            result.ReadWriteCalls.Should().Be(1);
        }

        [Fact]
        public void SerializeAnyIBitcoinSerializableDoesNotThrowException()
        {
            var serialisable = new UnknownBitcoinSerialisable();
            this.dbreezeSerializer.Serialize(serialisable);
            serialisable.ReadWriteCalls.Should().Be(1);
        }

        [Fact]
        public void DBreezeEngineAbleToAccessExistingTransactionData()
        {
            string dir = CreateTestDir(this);
            uint256[] data = SetupTransactionData(dir);

            using (var engine = new DBreezeEngine(dir))
            {
                using (DBreeze.Transactions.Transaction transaction = engine.GetTransaction())
                {
                    var data2 = new uint256[data.Length];
                    int i = 0;
                    foreach (Row<int, byte[]> row in transaction.SelectForward<int, byte[]>("Table"))
                    {
                        data2[i++] = new uint256(row.Value, false);
                    }

                    Assert.True(data.SequenceEqual(data2));
                }
            }
        }

        private static uint256[] SetupTransactionData(string folder)
        {
            using (var engine = new DBreezeEngine(folder))
            {
                var data = new[]
                {
                    new uint256(3),
                    new uint256(2),
                    new uint256(5),
                    new uint256(10),
                };

                int i = 0;
                using (DBreeze.Transactions.Transaction tx = engine.GetTransaction())
                {
                    foreach (uint256 d in data)
                        tx.Insert<int, byte[]>("Table", i++, d.ToBytes(false));

                    tx.Commit();
                }

                return data;
            }
        }

        [Fact]
        public void IsAbleToSerializeCollections()
        {
            var data = new List<uint256>
            {
                new uint256(3),
                new uint256(2),
                new uint256(5),
                new uint256(10),
            };

            byte[] bytes1 = this.dbreezeSerializer.Serialize(data);
            byte[] bytes2 = this.dbreezeSerializer.Serialize(data.ToArray());
            Assert.True(bytes1.SequenceEqual(bytes2));

            this.dbreezeSerializer.Serialize(data.ToHashSet());
        }
    }
}
