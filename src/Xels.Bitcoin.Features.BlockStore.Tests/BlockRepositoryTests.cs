﻿using System;
using System.Collections.Generic;
using System.Linq;
using LevelDB;
using NBitcoin;
using Xels.Bitcoin.Features.BlockStore.Repositories;
using Xels.Bitcoin.Persistence;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Features.BlockStore.Tests
{
    public sealed class BlockRepositoryTests : LogsTestBase
    {
        [Fact]
        public void InitializesGenesisBlockAndTxIndexOnFirstLoad()
        {
            string dir = CreateTestDir(this);
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
            }

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                byte[] blockRow = engine.Get(BlockRepositoryConstants.CommonTableName, new byte[0]);
                bool txIndexRow = BitConverter.ToBoolean(engine.Get(BlockRepositoryConstants.CommonTableName, new byte[1]));

                Assert.Equal(this.Network.GetGenesis().GetHash(), this.DBreezeSerializer.Deserialize<HashHeightPair>(blockRow).Hash);
                Assert.False(txIndexRow);
            }
        }

        [Fact]
        public void DoesNotOverwriteExistingBlockAndTxIndexOnFirstLoad()
        {
            string dir = CreateTestDir(this);

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(new uint256(56), 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
            }

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                byte[] blockRow = engine.Get(BlockRepositoryConstants.CommonTableName, new byte[0]);
                bool txIndexRow = BitConverter.ToBoolean(engine.Get(BlockRepositoryConstants.CommonTableName, new byte[1]));

                Assert.Equal(new HashHeightPair(new uint256(56), 1), this.DBreezeSerializer.Deserialize<HashHeightPair>(blockRow));
                Assert.True(txIndexRow);
            }
        }

        [Fact]
        public void GetTrxAsyncWithoutTransactionIndexReturnsNewTransaction()
        {
            string dir = CreateTestDir(this);

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(false));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(default(Transaction), repository.GetTransactionById(uint256.Zero));
            }
        }

        [Fact]
        public void GetTrxAsyncWithoutTransactionInIndexReturnsNull()
        {
            string dir = CreateTestDir(this);

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                var blockId = new uint256(8920);
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Null(repository.GetTransactionById(new uint256(65)));
            }
        }

        [Fact]
        public void GetTrxAsyncWithTransactionReturnsExistingTransaction()
        {
            string dir = CreateTestDir(this);
            Transaction trans = this.Network.CreateTransaction();
            trans.Version = 125;

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                Block block = this.Network.CreateBlock();
                block.Header.GetHash();
                block.Transactions.Add(trans);

                engine.Put(BlockRepositoryConstants.BlockTableName, block.Header.GetHash().ToBytes(), block.ToBytes());
                engine.Put(BlockRepositoryConstants.TransactionTableName, trans.GetHash().ToBytes(), block.Header.GetHash().ToBytes());
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal((uint)125, repository.GetTransactionById(trans.GetHash()).Version);
            }
        }

        [Fact]
        public void GetTrxBlockIdAsyncWithoutTxIndexReturnsDefaultId()
        {
            string dir = CreateTestDir(this);

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(false));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(default(uint256), repository.GetBlockIdByTransactionId(new uint256(26)));
            }
        }

        [Fact]
        public void GetTrxBlockIdAsyncWithoutExistingTransactionReturnsNull()
        {
            string dir = CreateTestDir(this);

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Null(repository.GetBlockIdByTransactionId(new uint256(26)));
            }
        }

        [Fact]
        public void GetTrxBlockIdAsyncWithTransactionReturnsBlockId()
        {
            string dir = CreateTestDir(this);

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.TransactionTableName, new uint256(26).ToBytes(), new uint256(42).ToBytes());
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(new uint256(42), repository.GetBlockIdByTransactionId(new uint256(26)));
            }
        }

        [Fact]
        public void PutAsyncWritesBlocksAndTransactionsToDbAndSavesNextBlockHash()
        {
            string dir = CreateTestDir(this);

            var nextBlockHash = new uint256(1241256);
            var blocks = new List<Block>();
            Block block = this.Network.Consensus.ConsensusFactory.CreateBlock();
            BlockHeader blockHeader = block.Header;
            blockHeader.Bits = new Target(12);
            Transaction transaction = this.Network.CreateTransaction();
            transaction.Version = 32;
            block.Transactions.Add(transaction);
            transaction = this.Network.CreateTransaction();
            transaction.Version = 48;
            block.Transactions.Add(transaction);
            blocks.Add(block);

            Block block2 = this.Network.Consensus.ConsensusFactory.CreateBlock();
            block2.Header.Nonce = 11;
            transaction = this.Network.CreateTransaction();
            transaction.Version = 15;
            block2.Transactions.Add(transaction);
            blocks.Add(block2);

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[0], this.DBreezeSerializer.Serialize(new HashHeightPair(uint256.Zero, 1)));
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.PutBlocks(new HashHeightPair(nextBlockHash, 100), blocks);
            }

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                byte[] blockHashKeyRow = engine.Get(BlockRepositoryConstants.CommonTableName, new byte[0]);

                Dictionary<byte[], byte[]> blockDict = engine.SelectDictionary(BlockRepositoryConstants.BlockTableName);
                Dictionary<byte[], byte[]> transDict = engine.SelectDictionary(BlockRepositoryConstants.TransactionTableName);

                Assert.Equal(new HashHeightPair(nextBlockHash, 100), this.DBreezeSerializer.Deserialize<HashHeightPair>(blockHashKeyRow));
                Assert.Equal(2, blockDict.Count);
                Assert.Equal(3, transDict.Count);

                foreach (KeyValuePair<byte[], byte[]> item in blockDict)
                {
                    Block bl = blocks.Single(b => b.GetHash() == new uint256(item.Key));
                    Assert.Equal(bl.Header.GetHash(), Block.Load(item.Value, this.Network.Consensus.ConsensusFactory).Header.GetHash());
                }

                foreach (KeyValuePair<byte[], byte[]> item in transDict)
                {
                    Block bl = blocks.Single(b => b.Transactions.Any(t => t.GetHash() == new uint256(item.Key)));
                    Assert.Equal(bl.GetHash(), new uint256(item.Value));
                }
            }
        }

        [Fact]
        public void SetTxIndexUpdatesTxIndex()
        {
            string dir = CreateTestDir(this);
            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(false);
            }

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                bool txIndexRow = BitConverter.ToBoolean(engine.Get(BlockRepositoryConstants.CommonTableName, new byte[1]));
                Assert.False(txIndexRow);
            }
        }

        [Fact]
        public void GetAsyncWithExistingBlockReturnsBlock()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.Consensus.ConsensusFactory.CreateBlock();

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.BlockTableName, block.GetHash().ToBytes(), block.ToBytes());
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Equal(block.GetHash(), repository.GetBlock(block.GetHash()).GetHash());
            }
        }

        [Fact]
        public void GetAsyncWithExistingBlocksReturnsBlocks()
        {
            string dir = CreateTestDir(this);
            var blocks = new Block[10];

            blocks[0] = this.Network.Consensus.ConsensusFactory.CreateBlock();
            for (int i = 1; i < blocks.Length; i++)
            {
                blocks[i] = this.Network.Consensus.ConsensusFactory.CreateBlock();
                blocks[i].Header.HashPrevBlock = blocks[i - 1].Header.GetHash();
            }

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                for (int i = 0; i < blocks.Length; i++)
                    engine.Put(BlockRepositoryConstants.BlockTableName, blocks[i].GetHash().ToBytes(), blocks[i].ToBytes());
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                List<Block> result = repository.GetBlocks(blocks.Select(b => b.GetHash()).ToList());

                Assert.Equal(blocks.Length, result.Count);
                for (int i = 0; i < 10; i++)
                    Assert.Equal(blocks[i].GetHash(), result[i].GetHash());
            }
        }

        [Fact]
        public void GetAsyncWithoutExistingBlockReturnsNull()
        {
            string dir = CreateTestDir(this);

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.Null(repository.GetBlock(new uint256()));
            }
        }

        [Fact]
        public void ExistAsyncWithExistingBlockReturnsTrue()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.Consensus.ConsensusFactory.CreateBlock();

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.BlockTableName, block.GetHash().ToBytes(), block.ToBytes());
            }

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.True(repository.Exist(block.GetHash()));
            }
        }

        [Fact]
        public void ExistAsyncWithoutExistingBlockReturnsFalse()
        {
            string dir = CreateTestDir(this);

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Assert.False(repository.Exist(new uint256()));
            }
        }

        [Fact]
        public void DeleteAsyncRemovesBlocksAndTransactions()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.CreateBlock();
            block.Transactions.Add(this.Network.CreateTransaction());

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.BlockTableName, block.GetHash().ToBytes(), block.ToBytes());
                engine.Put(BlockRepositoryConstants.TransactionTableName, block.Transactions[0].GetHash().ToBytes(), block.GetHash().ToBytes());
                engine.Put(BlockRepositoryConstants.CommonTableName, new byte[1], BitConverter.GetBytes(true));
            }

            var tip = new HashHeightPair(new uint256(45), 100);

            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.Delete(tip, new List<uint256> { block.GetHash() });
            }

            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                byte[] blockHashKeyRow = engine.Get(BlockRepositoryConstants.CommonTableName, new byte[0]);
                Dictionary<byte[], byte[]> blockDict = engine.SelectDictionary(BlockRepositoryConstants.BlockTableName);
                Dictionary<byte[], byte[]> transDict = engine.SelectDictionary(BlockRepositoryConstants.TransactionTableName);

                Assert.Equal(tip, this.DBreezeSerializer.Deserialize<HashHeightPair>(blockHashKeyRow));
                Assert.Empty(blockDict);
                Assert.Empty(transDict);
            }
        }

        [Fact]
        public void ReIndexAsync_TxIndex_OffToOn()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.CreateBlock();
            Transaction transaction = this.Network.CreateTransaction();
            block.Transactions.Add(transaction);

            // Set up database to mimic that created when TxIndex was off. No transactions stored.
            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.BlockTableName, block.GetHash().ToBytes(), block.ToBytes());
            }

            // Turn TxIndex on and then reindex database, as would happen on node startup if -txindex and -reindex are set.
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(true);
                repository.ReIndex();
            }

            // Check that after indexing database, the transaction inside the block is now indexed.
            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                Dictionary<byte[], byte[]> blockDict = engine.SelectDictionary(BlockRepositoryConstants.BlockTableName);
                Dictionary<byte[], byte[]> transDict = engine.SelectDictionary(BlockRepositoryConstants.TransactionTableName);

                // Block stored as expected.
                Assert.Single(blockDict);
                Assert.Equal(block.GetHash(), this.DBreezeSerializer.Deserialize<Block>(blockDict.FirstOrDefault().Value).GetHash());

                // Transaction row in database stored as expected.
                Assert.Single(transDict);
                KeyValuePair<byte[], byte[]> savedTransactionRow = transDict.FirstOrDefault();
                Assert.Equal(transaction.GetHash().ToBytes(), savedTransactionRow.Key);
                Assert.Equal(block.GetHash().ToBytes(), savedTransactionRow.Value);
            }
        }

        [Fact]
        public void ReIndexAsync_TxIndex_OnToOff()
        {
            string dir = CreateTestDir(this);
            Block block = this.Network.CreateBlock();
            Transaction transaction = this.Network.CreateTransaction();
            block.Transactions.Add(transaction);

            // Set up database to mimic that created when TxIndex was on. Transaction from block is stored.
            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                engine.Put(BlockRepositoryConstants.BlockTableName, block.GetHash().ToBytes(), block.ToBytes());
                engine.Put(BlockRepositoryConstants.TransactionTableName, transaction.GetHash().ToBytes(), block.GetHash().ToBytes());
            }

            // Turn TxIndex off and then reindex database, as would happen on node startup if -txindex=0 and -reindex are set.
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(false);
                repository.ReIndex();
            }

            // Check that after indexing database, the transaction is no longer stored.
            using (var engine = new DB(new Options() { CreateIfMissing = true }, dir))
            {
                Dictionary<byte[], byte[]> blockDict = engine.SelectDictionary(BlockRepositoryConstants.BlockTableName);
                Dictionary<byte[], byte[]> transDict = engine.SelectDictionary(BlockRepositoryConstants.TransactionTableName);

                // Block still stored as expected.
                Assert.Single(blockDict);
                Assert.Equal(block.GetHash(), this.DBreezeSerializer.Deserialize<Block>(blockDict.FirstOrDefault().Value).GetHash());

                // No transactions indexed.
                Assert.Empty(transDict);
            }
        }

        [Fact]
        public void GetBlockByHashReturnsGenesisBlock()
        {
            string dir = CreateTestDir(this);
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                Block genesis = repository.GetBlock(this.Network.GetGenesis().GetHash());

                Assert.Equal(this.Network.GetGenesis().GetHash(), genesis.GetHash());
            }
        }

        [Fact]
        public void GetBlocksByHashReturnsGenesisBlock()
        {
            string dir = CreateTestDir(this);
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                List<Block> results = repository.GetBlocks(new List<uint256> { this.Network.GetGenesis().GetHash() });

                Assert.NotEmpty(results);
                Assert.NotNull(results.First());
                Assert.Equal(this.Network.GetGenesis().GetHash(), results.First().GetHash());
            }
        }

        [Fact]
        public void GetTransactionByIdForGenesisBlock()
        {
            var genesis = this.Network.GetGenesis();
            var genesisTransactions = genesis.Transactions;

            string dir = CreateTestDir(this);
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(true);

                foreach (var transaction in genesisTransactions)
                {
                    var result = repository.GetTransactionById(transaction.GetHash());

                    Assert.NotNull(result);
                    Assert.Equal(transaction.GetHash(), result.GetHash());
                }
            }
        }

        [Fact]
        public void GetTransactionsByIdsForGenesisBlock()
        {
            var genesis = this.Network.GetGenesis();
            var genesisTransactions = genesis.Transactions;

            string dir = CreateTestDir(this);
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(true);

                var result = repository.GetTransactionsByIds(genesis.Transactions.Select(t => t.GetHash()).ToArray());

                Assert.NotNull(result);

                for (var i = 0; i < genesisTransactions.Count; i++)
                {
                    Assert.Equal(genesisTransactions[i].GetHash(), result[i].GetHash());
                }
            }
        }

        [Fact]
        public void GetBlockIdByTransactionIdForGenesisBlock()
        {
            var genesis = this.Network.GetGenesis();
            var genesisTransactions = genesis.Transactions;

            string dir = CreateTestDir(this);
            using (IBlockRepository repository = this.SetupRepository(this.Network, dir))
            {
                repository.SetTxIndex(true);

                foreach (var transaction in genesisTransactions)
                {
                    var result = repository.GetBlockIdByTransactionId(transaction.GetHash());

                    Assert.NotNull(result);
                    Assert.Equal(this.Network.GenesisHash, result);
                }
            }
        }

        private IBlockRepository SetupRepository(Network main, string dataFolder)
        {
            var dBreezeSerializer = new DBreezeSerializer(main.Consensus.ConsensusFactory);
            var repository = new LevelDbBlockRepository(main, dataFolder, dBreezeSerializer);
            repository.Initialize();
            return repository;
        }
    }
}
