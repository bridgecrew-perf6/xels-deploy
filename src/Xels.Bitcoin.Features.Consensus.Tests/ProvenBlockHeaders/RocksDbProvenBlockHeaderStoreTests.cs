﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NBitcoin;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Features.Consensus.Tests.ProvenBlockHeaders
{
    public sealed class RocksDbProvenBlockHeaderStoreTests : LogsTestBase
    {
        private readonly ProvenBlockHeaderStore provenBlockHeaderStore;
        private readonly IProvenBlockHeaderRepository provenBlockHeaderRepository;

        private SortedDictionary<int, ProvenBlockHeader> PendingBatch
        {
            get { return this.provenBlockHeaderStore.GetMemberValue("pendingBatch") as SortedDictionary<int, ProvenBlockHeader>; }
        }

        public RocksDbProvenBlockHeaderStoreTests() : base(new StraxTest())
        {
            var nodeStats = new NodeStats(DateTimeProvider.Default, NodeSettings.Default(this.Network), new Mock<IVersionProvider>().Object);

            var dBreezeSerializer = new DBreezeSerializer(this.Network.Consensus.ConsensusFactory);

            var ibdMock = new Mock<IInitialBlockDownloadState>();
            ibdMock.Setup(s => s.IsInitialBlockDownload()).Returns(false);

            this.provenBlockHeaderRepository = new RocksDbProvenBlockHeaderRepository(CreateTestDir(this), dBreezeSerializer, this.Network);

            this.provenBlockHeaderStore = new ProvenBlockHeaderStore(DateTimeProvider.Default, this.LoggerFactory.Object, this.provenBlockHeaderRepository, nodeStats, ibdMock.Object);
        }

        [Fact]
        public async Task InitialiseStoreToGenesisChainHeaderAsync()
        {
            var genesis = this.BuildProvenHeaderChain(1);

            await this.provenBlockHeaderStore.InitializeAsync(genesis).ConfigureAwait(false);

            this.provenBlockHeaderStore.TipHashHeight.Hash.Should().Be(genesis.HashBlock);
        }

        [Fact]
        public async Task AddToPending_Adds_To_CacheAsync()
        {
            // Initialise store.
            await this.provenBlockHeaderStore.InitializeAsync(this.BuildProvenHeaderChain(1)).ConfigureAwait(false);

            // Add to pending (add to internal cache).
            var inHeader = this.CreateNewProvenBlockHeaderMock();
            this.provenBlockHeaderStore.AddToPendingBatch(inHeader, new HashHeightPair(inHeader.GetHash(), 1));

            // Check Item in cache.
            var cacheCount = this.PendingBatch.Count;
            cacheCount.Should().Be(1);

            // Get item.
            var outHeader = await this.provenBlockHeaderStore.GetAsync(1).ConfigureAwait(false);
            outHeader.GetHash().Should().Be(inHeader.GetHash());

            // Check if it has been saved to disk.  It shouldn't as the asyncProvider would not have been called yet.
            var outHeaderRepo = await this.provenBlockHeaderRepository.GetAsync(1).ConfigureAwait(false);
            outHeaderRepo.Should().BeNull();
        }


        [Fact]
        public async Task AddToPending_Adds_To_Cache_Then_Save_To_DiskAsync()
        {
            // Initialise store.
            await this.provenBlockHeaderStore.InitializeAsync(BuildProvenHeaderChain(1)).ConfigureAwait(false);

            // Add to pending (add to internal cache).
            var inHeader = this.CreateNewProvenBlockHeaderMock();
            this.provenBlockHeaderStore.AddToPendingBatch(inHeader, new HashHeightPair(inHeader.GetHash(), 0));

            // Check Item in cache.
            var cacheCount = this.PendingBatch.Count;
            cacheCount.Should().Be(1);

            // Get item.
            var outHeader = await this.provenBlockHeaderStore.GetAsync(0).ConfigureAwait(false);
            outHeader.GetHash().Should().Be(inHeader.GetHash());

            // Call the internal save method to save cached item to disk.
            this.provenBlockHeaderStore.InvokeMethod("SaveAsync");

            WaitLoop(() =>
            {
                // Check if it has been saved to disk.
                var outHeaderRepo = this.provenBlockHeaderRepository.GetAsync(0).GetAwaiter().GetResult();
                if (outHeaderRepo == null)
                    return false;
                return outHeaderRepo.GetHash() == outHeader.GetHash();
            });
        }

        [Fact]
        public async Task Add_2k_ProvenHeaders_ToPending_CacheAsync()
        {
            // Initialise store.
            await this.provenBlockHeaderStore.InitializeAsync(this.BuildProvenHeaderChain(1)).ConfigureAwait(false);

            ProvenBlockHeader inHeader = null;

            // Add to pending (add to internal cache).
            for (int i = 0; i < 2_000; i++)
            {
                var block = this.CreatePosBlock();
                if (inHeader != null)
                    block.Header.HashPrevBlock = inHeader.GetHash();
                inHeader = this.CreateNewProvenBlockHeaderMock(block);
                this.provenBlockHeaderStore.AddToPendingBatch(inHeader, new HashHeightPair(inHeader.GetHash(), i));
            }

            // Check Item in cache.
            var cacheCount = this.PendingBatch.Count;
            cacheCount.Should().Be(2_000);

            // Check if it has been saved to disk.  It shouldn't as the asyncProvider would not have been called yet.
            var outHeaderRepo = await this.provenBlockHeaderRepository.GetAsync(1).ConfigureAwait(false);
            outHeaderRepo.Should().BeNull();
        }

        [Fact]
        public async Task Add_2k_ProvenHeaders_To_PendingBatch_Then_Save_Then_PendingBatch_Should_Be_EmptyAsync()
        {
            // Initialise store.
            await this.provenBlockHeaderStore.InitializeAsync(this.BuildProvenHeaderChain(1)).ConfigureAwait(false);

            ProvenBlockHeader inHeader = null;

            // Add to pending (add to internal cache).
            for (int i = 0; i < 2_000; i++)
            {
                var block = this.CreatePosBlock();
                if (inHeader != null)
                    block.Header.HashPrevBlock = inHeader.GetHash();
                inHeader = this.CreateNewProvenBlockHeaderMock(block);
                this.provenBlockHeaderStore.AddToPendingBatch(inHeader, new HashHeightPair(inHeader.GetHash(), i));
            }

            // Check Item in cache.
            var cacheCount = this.PendingBatch.Count;
            cacheCount.Should().Be(2_000);

            // Call the internal save method to save cached item to disk.
            this.provenBlockHeaderStore.InvokeMethod("SaveAsync");

            // when pendingTipHashHeight is null we can safely say the items were saved to the repository, based on the above SaveAsync.
            WaitLoop(() =>
            {
                var pendingTipHashHeight = this.provenBlockHeaderStore.GetMemberValue("pendingTipHashHeight");
                return pendingTipHashHeight == null;
            });

            WaitLoop(() =>
            {
                // Check if it has been saved to disk.
                var outHeaderRepo = this.provenBlockHeaderRepository.GetAsync(1999).ConfigureAwait(false).GetAwaiter().GetResult();
                return outHeaderRepo != null;
            });

            // Check items in cache - should now be empty.
            cacheCount = this.PendingBatch.Count;
            cacheCount.Should().Be(0);
        }

        [Fact]
        public void AddToPending_Then_Reorg_New_Items_Consecutive_Not_Tip_Then_Save()
        {
            var chainWithHeaders = this.BuildProvenHeaderChain(21);

            var chainedHeaders = chainWithHeaders.EnumerateToGenesis().Reverse().ToList();

            // items 1-20 on main chain
            foreach (ChainedHeader chainedHeader in chainedHeaders.Skip(1).Take(20))
            {
                this.provenBlockHeaderStore.AddToPendingBatch(chainedHeader.Header as ProvenBlockHeader, new HashHeightPair(chainedHeader.HashBlock, chainedHeader.Height));
            }

            Assert.Equal(20, this.PendingBatch.Count);
            this.CompareCollections(chainedHeaders.Skip(1).ToList(), this.PendingBatch);

            var newChainWithHeaders = this.BuildProvenHeaderChain(20, chainedHeaders[10]);

            var newChainedHeaders = newChainWithHeaders.EnumerateToGenesis().Reverse().ToList();

            // 1-10 on main chain then items 10-15 on a fork
            foreach (ChainedHeader chainedHeader in newChainedHeaders.Skip(10).Take(6))
            {
                this.provenBlockHeaderStore.AddToPendingBatch(chainedHeader.Header as ProvenBlockHeader, new HashHeightPair(chainedHeader.HashBlock, chainedHeader.Height));
            }

            Assert.Equal(15, this.PendingBatch.Count);
            this.CompareCollections(newChainedHeaders.Skip(1).Take(15).ToList(), this.PendingBatch);

            this.provenBlockHeaderStore.InvokeMethod("SaveAsync");

            Assert.Empty(this.PendingBatch);

            var error = this.provenBlockHeaderStore.GetMemberValue("saveAsyncLoopException") as Exception;
            Assert.Null(error);
        }

        [Fact]
        public void AddToPending_Then_Reorg_New_Items_Consecutive_Is_Tip_Then_Save()
        {
            var chainWithHeaders = this.BuildProvenHeaderChain(21);

            var chainedHeaders = chainWithHeaders.EnumerateToGenesis().Reverse().ToList();

            // items 1-20 on main chain
            foreach (ChainedHeader chainedHeader in chainedHeaders.Skip(1).Take(21))
            {
                this.provenBlockHeaderStore.AddToPendingBatch(chainedHeader.Header as ProvenBlockHeader, new HashHeightPair(chainedHeader.HashBlock, chainedHeader.Height));
            }

            Assert.Equal(20, this.PendingBatch.Count);
            this.CompareCollections(chainedHeaders.Skip(1).ToList(), this.PendingBatch);

            var newChainWithHeaders = this.BuildProvenHeaderChain(11, chainedHeaders[10]);

            var newChainedHeaders = newChainWithHeaders.EnumerateToGenesis().Reverse().ToList();

            // all items 1-20 are on main chain after a fork
            foreach (ChainedHeader chainedHeader in newChainedHeaders.Skip(10).Take(11))
            {
                this.provenBlockHeaderStore.AddToPendingBatch(chainedHeader.Header as ProvenBlockHeader, new HashHeightPair(chainedHeader.HashBlock, chainedHeader.Height));
            }

            Assert.Equal(20, this.PendingBatch.Count);
            this.CompareCollections(newChainedHeaders.Skip(1).ToList(), this.PendingBatch);

            this.provenBlockHeaderStore.InvokeMethod("SaveAsync");

            Assert.Empty(this.PendingBatch);

            var error = this.provenBlockHeaderStore.GetMemberValue("saveAsyncLoopException") as Exception;
            Assert.Null(error);
        }

        [Fact]
        public void AddToPending_Then_Reorg_New_Items_Not_Consecutive_Is_Not_Tip_Then_Save()
        {
            var chainWithHeaders = this.BuildProvenHeaderChain(21);

            var chainedHeaders = chainWithHeaders.EnumerateToGenesis().Reverse().ToList();

            // Items 1-15 pending
            foreach (ChainedHeader chainedHeader in chainedHeaders.Skip(1).Take(15))
            {
                this.provenBlockHeaderStore.AddToPendingBatch(chainedHeader.Header as ProvenBlockHeader, new HashHeightPair(chainedHeader.HashBlock, chainedHeader.Height));
            }

            Assert.Equal(15, this.PendingBatch.Count);
            this.CompareCollections(chainedHeaders.Skip(1).Take(15).ToList(), this.PendingBatch);

            // Save items 1-15
            this.provenBlockHeaderStore.InvokeMethod("SaveAsync");
            var error = this.provenBlockHeaderStore.GetMemberValue("saveAsyncLoopException") as Exception;
            Assert.Null(error);
            Assert.Empty(this.PendingBatch);

            // Add items 16-20
            foreach (ChainedHeader chainedHeader in chainedHeaders.Skip(16).Take(5))
            {
                this.provenBlockHeaderStore.AddToPendingBatch(chainedHeader.Header as ProvenBlockHeader, new HashHeightPair(chainedHeader.HashBlock, chainedHeader.Height));
            }

            Assert.Equal(5, this.PendingBatch.Count);
            this.CompareCollections(chainedHeaders.Skip(16).Take(5).ToList(), this.PendingBatch);

            var newChainWithHeaders = this.BuildProvenHeaderChain(15, chainedHeaders[10]);

            var newChainedHeaders = newChainWithHeaders.EnumerateToGenesis().Reverse().ToList();

            // Add new fork items 10-13, items of the old fork 16-20 are still in the batch.
            foreach (ChainedHeader chainedHeader in newChainedHeaders.Skip(10).Take(3))
            {
                this.provenBlockHeaderStore.AddToPendingBatch(chainedHeader.Header as ProvenBlockHeader, new HashHeightPair(chainedHeader.HashBlock, chainedHeader.Height));
            }

            Assert.Equal(3, this.PendingBatch.Count);
            this.CompareCollections(newChainedHeaders.Skip(10).Take(3).ToList(), this.PendingBatch);

            this.provenBlockHeaderStore.InvokeMethod("SaveAsync");

            Assert.Empty(this.PendingBatch);

            error = this.provenBlockHeaderStore.GetMemberValue("saveAsyncLoopException") as Exception;
            Assert.Null(error);
        }

        [Fact]
        public void AddToPending_Then_Save_Incorrect_Sequence_Push_To_Store()
        {
            var inHeader = this.CreateNewProvenBlockHeaderMock();

            // Add headers to pending batch in the wrong height order.
            for (int i = 1; i >= 0; i--)
            {
                this.provenBlockHeaderStore.AddToPendingBatch(inHeader, new HashHeightPair(inHeader.GetHash(), i));
            }

            var taskResult = this.provenBlockHeaderStore.InvokeMethod("SaveAsync") as Task;
            taskResult.Wait();

            taskResult.IsCompletedSuccessfully.Should().BeTrue();
        }

        [Fact]
        public async Task AddToPending_Store_TipHash_Is_The_Same_As_ChainHeaderTipAsync()
        {
            var chainWithHeaders = this.BuildProvenHeaderChain(3);
            SortedDictionary<int, ProvenBlockHeader> provenBlockheaders = this.ConvertToDictionaryOfProvenHeaders(chainWithHeaders);

            // Persist current chain.
            await this.provenBlockHeaderRepository.PutAsync(
                provenBlockheaders,
                new HashHeightPair(provenBlockheaders.Last().Value.GetHash(), provenBlockheaders.Count - 1)).ConfigureAwait(false);

            using (IProvenBlockHeaderStore store = this.SetupStore())
            {
                var header = this.CreateNewProvenBlockHeaderMock();

                this.provenBlockHeaderStore.AddToPendingBatch(header, new HashHeightPair(header.GetHash(), chainWithHeaders.Height));

                this.provenBlockHeaderStore.InvokeMethod("SaveAsync");

                HashHeightPair tipHashHeight = null;

                WaitLoop(() =>
                {
                    tipHashHeight = this.provenBlockHeaderStore.GetMemberValue("TipHashHeight") as HashHeightPair;
                    return tipHashHeight == this.provenBlockHeaderRepository.TipHashHeight;
                });

                tipHashHeight.Height.Should().Be(chainWithHeaders.Height);
            }
        }

        private ProvenBlockHeaderStore SetupStore()
        {
            var ibdMock = new Mock<IInitialBlockDownloadState>();
            ibdMock.Setup(s => s.IsInitialBlockDownload()).Returns(false);

            return new ProvenBlockHeaderStore(DateTimeProvider.Default, this.LoggerFactory.Object, this.provenBlockHeaderRepository, new NodeStats(DateTimeProvider.Default, NodeSettings.Default(this.Network), new Mock<IVersionProvider>().Object), ibdMock.Object);
        }

        private static void WaitLoop(Func<bool> act, string failureReason = "Unknown Reason", int retryDelayInMiliseconds = 1000, CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken = cancellationToken == default
                ? new CancellationTokenSource(Debugger.IsAttached ? 15 * 60 * 1000 : 60 * 1000).Token
                : cancellationToken;

            while (!act())
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    Thread.Sleep(retryDelayInMiliseconds);
                }
                catch (OperationCanceledException e)
                {
                    Assert.False(true, $"{failureReason}{Environment.NewLine}{e.Message}");
                }
            }
        }
    }
}
