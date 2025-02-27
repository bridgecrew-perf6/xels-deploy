﻿using System;
using System.Collections.Generic;
using FluentAssertions.Common;
using Microsoft.Extensions.Logging;
using Moq;
using NBitcoin;
using Xels.Bitcoin.Base;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Configuration.Settings;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.Consensus.Interfaces;
using Xels.Bitcoin.Features.Consensus.ProvenBlockHeaders;
using Xels.Bitcoin.Features.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.MemoryPool.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Mining;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Tests.Common.Logging;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.Extensions;
using Xunit;

namespace Xels.Bitcoin.Features.Miner.Tests
{
    public class PosBlockAssemblerTest : LogsTestBase
    {
        private RuleContext callbackRuleContext = null;
        private readonly Mock<IConsensusManager> consensusManager;
        private readonly Mock<IDateTimeProvider> dateTimeProvider;
        private readonly Key key;
        private readonly Mock<ITxMempool> mempool;
        private readonly MinerSettings minerSettings;
        private readonly Mock<IStakeChain> stakeChain;
        private readonly Mock<IStakeValidator> stakeValidator;

        public PosBlockAssemblerTest() : base(new StraxTest())
        {
            this.consensusManager = new Mock<IConsensusManager>();
            this.mempool = new Mock<ITxMempool>();
            this.dateTimeProvider = new Mock<IDateTimeProvider>();
            this.stakeValidator = new Mock<IStakeValidator>();
            this.stakeChain = new Mock<IStakeChain>();
            this.key = new Key();
            this.minerSettings = new MinerSettings(NodeSettings.Default(this.Network));
            SetupConsensusManager();
        }

        [Fact]
        public void UpdateHeaders_UsingChainAndNetwork_PreparesStakeBlockHeaders()
        {
            this.ExecuteWithConsensusOptions(new PosConsensusOptions(), () =>
            {
                this.dateTimeProvider.Setup(d => d.GetTimeOffset()).Returns(new DateTimeOffset(new DateTime(2017, 1, 7, 0, 0, 0, DateTimeKind.Utc)));

                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, new Key());

                this.stakeValidator.Setup(s => s.GetNextTargetRequired(this.stakeChain.Object, chainIndexer.Tip, this.Network.Consensus, true))
                    .Returns(new Target(new uint256(1123123123)))
                    .Verifiable();

                var posBlockAssembler = new PosTestBlockAssembler(this.consensusManager.Object, this.Network, new MempoolSchedulerLock(), this.mempool.Object, this.minerSettings, this.dateTimeProvider.Object, this.stakeChain.Object, this.stakeValidator.Object, this.LoggerFactory.Object, new NodeDeployments(this.Network, chainIndexer));

                Block block = posBlockAssembler.UpdateHeaders(chainIndexer.Tip);

                Assert.Equal(chainIndexer.Tip.HashBlock, block.Header.HashPrevBlock);
                Assert.Equal((uint)1483747200, block.Header.Time);
                Assert.Equal(2.400408204198463E+58, block.Header.Bits.Difficulty);
                Assert.Equal((uint)0, block.Header.Nonce);
                this.stakeValidator.Verify();
            });
        }

        [Fact]
        public void CreateNewBlock_WithScript_ReturnsBlockTemplate()
        {
            this.ExecuteWithConsensusOptions(new PosConsensusOptions(), () =>
            {
                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, this.key);
                this.SetupRulesEngine(chainIndexer);
                var datetime = new DateTime(2017, 1, 7, 0, 0, 1, DateTimeKind.Utc);
                this.dateTimeProvider.Setup(d => d.GetAdjustedTimeAsUnixTimestamp()).Returns(datetime.ToUnixTimestamp());
                // Ensure that the BlockDefinition UpdateBaseHeaders sets the timestamp as required
                this.dateTimeProvider.Setup(d => d.GetTimeOffset()).Returns(datetime.ToDateTimeOffset());
                Transaction transaction = CreateTransaction(this.Network, this.key, 5, new Money(400 * 1000 * 1000), new Key(), new uint256(124124));
                var txFee = new Money(1000);

                SetupTxMempool(chainIndexer, this.Network.Consensus.Options as PosConsensusOptions, txFee, transaction);

                this.stakeValidator.Setup(s => s.GetNextTargetRequired(this.stakeChain.Object, chainIndexer.Tip, this.Network.Consensus, true))
                    .Returns(new Target(new uint256(1123123123)))
                    .Verifiable();

                var posBlockAssembler = new PosBlockDefinition(this.consensusManager.Object, this.dateTimeProvider.Object, this.LoggerFactory.Object, this.mempool.Object, new MempoolSchedulerLock(), this.minerSettings, this.Network, this.stakeChain.Object, this.stakeValidator.Object, new NodeDeployments(this.Network, chainIndexer));
                BlockTemplate blockTemplate = posBlockAssembler.Build(chainIndexer.Tip, this.key.ScriptPubKey);

                Assert.Equal(new Money(1000), blockTemplate.TotalFee);
                Assert.Equal(2, blockTemplate.Block.Transactions.Count);
                Assert.Equal(536870912, blockTemplate.Block.Header.Version);

                Assert.Equal(2, blockTemplate.Block.Transactions.Count);

                Transaction resultingTransaction = blockTemplate.Block.Transactions[0];
                Assert.Equal((uint)new DateTime(2017, 1, 7, 0, 0, 1, DateTimeKind.Utc).ToUnixTimestamp(), blockTemplate.Block.Header.Time);
                Assert.NotEmpty(resultingTransaction.Inputs);
                Assert.NotEmpty(resultingTransaction.Outputs);
                Assert.True(resultingTransaction.IsCoinBase);
                Assert.False(resultingTransaction.IsCoinStake);
                Assert.Equal(TxIn.CreateCoinbase(6).ScriptSig, resultingTransaction.Inputs[0].ScriptSig);
                Assert.Equal(new Money(0), resultingTransaction.TotalOut);
                Assert.Equal(new Script(), resultingTransaction.Outputs[0].ScriptPubKey);
                Assert.Equal(new Money(0), resultingTransaction.Outputs[0].Value);

                resultingTransaction = blockTemplate.Block.Transactions[1];
                Assert.NotEmpty(resultingTransaction.Inputs);
                Assert.NotEmpty(resultingTransaction.Outputs);
                Assert.False(resultingTransaction.IsCoinBase);
                Assert.False(resultingTransaction.IsCoinStake);
                Assert.Equal(new Money(400 * 1000 * 1000), resultingTransaction.TotalOut);
                Assert.Equal(transaction.Inputs[0].PrevOut.Hash, resultingTransaction.Inputs[0].PrevOut.Hash);
                Assert.Equal(transaction.Inputs[0].ScriptSig, transaction.Inputs[0].ScriptSig);

                Assert.Equal(transaction.Outputs[0].ScriptPubKey, resultingTransaction.Outputs[0].ScriptPubKey);
                Assert.Equal(new Money(400 * 1000 * 1000), resultingTransaction.Outputs[0].Value);
            });
        }

        [Fact]
        public void CreateNewBlock_WithScript_DoesNotValidateTemplateUsingRuleContext()
        {
            var newOptions = new PosConsensusOptions();

            this.ExecuteWithConsensusOptions(newOptions, () =>
            {
                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, this.key);
                this.SetupRulesEngine(chainIndexer);
                this.dateTimeProvider.Setup(d => d.GetAdjustedTimeAsUnixTimestamp()).Returns(new DateTime(2017, 1, 7, 0, 0, 1, DateTimeKind.Utc).ToUnixTimestamp());
                this.consensusManager.Setup(c => c.Tip).Returns(chainIndexer.GetHeader(5));

                this.stakeValidator.Setup(s => s.GetNextTargetRequired(this.stakeChain.Object, chainIndexer.Tip, this.Network.Consensus, true))
                    .Returns(new Target(new uint256(1123123123)))
                    .Verifiable();

                Transaction transaction = CreateTransaction(this.Network, this.key, 5, new Money(400 * 1000 * 1000), new Key(), new uint256(124124));
                var txFee = new Money(1000);

                SetupTxMempool(chainIndexer, newOptions, txFee, transaction);

                var posBlockAssembler = new PosBlockDefinition(this.consensusManager.Object, this.dateTimeProvider.Object, this.LoggerFactory.Object, this.mempool.Object, new MempoolSchedulerLock(), this.minerSettings, this.Network, this.stakeChain.Object, this.stakeValidator.Object, new NodeDeployments(this.Network, chainIndexer));

                BlockTemplate blockTemplate = posBlockAssembler.Build(chainIndexer.Tip, this.key.ScriptPubKey);

                this.consensusManager.Verify(c => c.ConsensusRules.PartialValidationAsync(It.IsAny<ChainedHeader>(), It.IsAny<Block>()), Times.Exactly(0));
                Assert.Null(this.callbackRuleContext);
                this.stakeValidator.Verify();
            });
        }

        [Fact]
        public void ComputeBlockVersion_UsingChainTipAndConsensus_NoBip9DeploymentActive_UpdatesHeightAndVersion()
        {
            this.ExecuteWithConsensusOptions(new PosConsensusOptions(), () =>
            {
                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, new Key());
                this.SetupRulesEngine(chainIndexer);

                var posBlockAssembler = new PosTestBlockAssembler(this.consensusManager.Object, this.Network, new MempoolSchedulerLock(), this.mempool.Object, this.minerSettings,
                                                 this.dateTimeProvider.Object, this.stakeChain.Object, this.stakeValidator.Object, this.LoggerFactory.Object, new NodeDeployments(this.Network, chainIndexer));

                (int Height, int Version) result = posBlockAssembler.ComputeBlockVersion(chainIndexer.GetHeader(4));

                Assert.Equal(5, result.Height);
                Assert.Equal((int)ThresholdConditionCache.VersionbitsTopBits, result.Version);
            });
        }

        [Fact]
        public void ComputeBlockVersion_UsingChainTipAndConsensus_Bip9DeploymentActive_UpdatesHeightAndVersion()
        {
            ConsensusOptions options = this.Network.Consensus.Options;
            int minerConfirmationWindow = this.Network.Consensus.MinerConfirmationWindow;

            try
            {
                var newOptions = new PosConsensusOptions();
                this.Network.Consensus.Options = newOptions;

                // Set the BIP9 activation to expire one day from now so that it is still signaled in the blocks.
                this.Network.Consensus.BIP9Deployments[0] = new BIP9DeploymentsParameters("Test", 19,
                    new DateTimeOffset(new DateTime(2016, 1, 1, 0, 0, 0, DateTimeKind.Utc)),
                    new DateTimeOffset(DateTime.UtcNow + TimeSpan.FromDays(1)),
                    2);

                this.Network.Consensus.MinerConfirmationWindow = 2;

                ChainIndexer chainIndexer = GenerateChainWithHeightAndActivatedBip9(5, this.Network, new Key(), this.Network.Consensus.BIP9Deployments[0]);
                this.SetupRulesEngine(chainIndexer);

                var posBlockAssembler = new PosTestBlockAssembler(this.consensusManager.Object, this.Network, new MempoolSchedulerLock(), this.mempool.Object, this.minerSettings, this.dateTimeProvider.Object, this.stakeChain.Object, this.stakeValidator.Object, this.LoggerFactory.Object, new NodeDeployments(this.Network, chainIndexer));

                (int Height, int Version) result = posBlockAssembler.ComputeBlockVersion(chainIndexer.GetHeader(4));

                Assert.Equal(5, result.Height);
                int expectedVersion = (int)(ThresholdConditionCache.VersionbitsTopBits | (((uint)1) << 19));
                Assert.Equal(expectedVersion, result.Version);
                Assert.NotEqual((int)ThresholdConditionCache.VersionbitsTopBits, result.Version);
            }
            finally
            {
                // This is a static in the global context so be careful updating it. I'm resetting it after being done testing so I don't influence other tests.
                this.Network.Consensus.Options = options;
                this.Network.Consensus.BIP9Deployments[0] = null;
                this.Network.Consensus.MinerConfirmationWindow = minerConfirmationWindow;
            }
        }

        [Fact]
        public void CreateCoinbase_CreatesCoinbaseTemplateTransaction_AddsToBlockTemplate()
        {
            this.ExecuteWithConsensusOptions(new PosConsensusOptions(), () =>
            {
                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, this.key);
                this.dateTimeProvider.Setup(d => d.GetAdjustedTimeAsUnixTimestamp())
                    .Returns(new DateTime(2017, 1, 7, 0, 0, 1, DateTimeKind.Utc).ToUnixTimestamp());

                var posBlockAssembler = new PosTestBlockAssembler(this.consensusManager.Object, this.Network, new MempoolSchedulerLock(), this.mempool.Object, this.minerSettings, this.dateTimeProvider.Object, this.stakeChain.Object, this.stakeValidator.Object, this.LoggerFactory.Object, new NodeDeployments(this.Network, chainIndexer));

                BlockTemplate result = posBlockAssembler.CreateCoinBase(chainIndexer.Tip, this.key.ScriptPubKey);

                Assert.NotEmpty(result.Block.Transactions);

                Transaction resultingTransaction = result.Block.Transactions[0];
                // Creating a coinbase will not by itself update the header timestamp. So we don't check that here.
                Assert.True(resultingTransaction.IsCoinBase);
                Assert.False(resultingTransaction.IsCoinStake);
                Assert.Equal(Money.Zero, resultingTransaction.TotalOut);

                Assert.NotEmpty(resultingTransaction.Inputs);
                Assert.Equal(TxIn.CreateCoinbase(6).ScriptSig, resultingTransaction.Inputs[0].ScriptSig);

                Assert.NotEmpty(resultingTransaction.Outputs);
                Assert.Equal(this.key.ScriptPubKey, resultingTransaction.Outputs[0].ScriptPubKey);
                Assert.Equal(Money.Zero, resultingTransaction.Outputs[0].Value);
            });
        }

        [Fact]
        public void AddTransactions_WithoutTransactionsInMempool_DoesNotAddEntriesToBlock()
        {
            var newOptions = new PosConsensusOptions();

            this.ExecuteWithConsensusOptions(newOptions, () =>
            {
                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, new Key());
                this.consensusManager.Setup(c => c.Tip)
                    .Returns(chainIndexer.GetHeader(5));
                var indexedTransactionSet = new TxMempool.IndexedTransactionSet();
                this.mempool.Setup(t => t.MapTx)
                    .Returns(indexedTransactionSet);

                var posBlockAssembler = new PosTestBlockAssembler(this.consensusManager.Object, this.Network, new MempoolSchedulerLock(), this.mempool.Object, this.minerSettings,
                                                 this.dateTimeProvider.Object, this.stakeChain.Object, this.stakeValidator.Object, this.LoggerFactory.Object, new NodeDeployments(this.Network, chainIndexer));

                (Block Block, int Selected, int Updated) result = posBlockAssembler.AddTransactions();

                Assert.Empty(result.Block.Transactions);
                Assert.Equal(0, result.Selected);
                Assert.Equal(0, result.Updated);
            });
        }

        [Fact]
        public void AddTransactions_TransactionNotInblock_AddsTransactionToBlock()
        {
            var newOptions = new PosConsensusOptions();

            this.ExecuteWithConsensusOptions(newOptions, () =>
            {
                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, this.key);
                this.consensusManager.Setup(c => c.Tip)
                    .Returns(chainIndexer.GetHeader(5));

                Mock<IConsensusRuleEngine> consensusRuleEngine = new Mock<IConsensusRuleEngine>();
                consensusRuleEngine.Setup(s => s.GetRule<PosFutureDriftRule>()).Returns(new PosFutureDriftRule());

                this.consensusManager.Setup(c => c.ConsensusRules)
                    .Returns(consensusRuleEngine.Object);

                Transaction transaction = CreateTransaction(this.Network, this.key, 5, new Money(400 * 1000 * 1000), new Key(), new uint256(124124));

                this.dateTimeProvider.Setup(s => s.GetAdjustedTimeAsUnixTimestamp()).Returns(chainIndexer.Tip.Header.Time);

                var txFee = new Money(1000);
                SetupTxMempool(chainIndexer, newOptions, txFee, transaction);

                var posBlockAssembler = new PosTestBlockAssembler(this.consensusManager.Object, this.Network, new MempoolSchedulerLock(), this.mempool.Object, this.minerSettings, this.dateTimeProvider.Object, this.stakeChain.Object, this.stakeValidator.Object, this.LoggerFactory.Object, new NodeDeployments(this.Network, chainIndexer));

                posBlockAssembler.CreateCoinBase(new ChainedHeader(this.Network.GetGenesis().Header, this.Network.GetGenesis().Header.GetHash(), 0), new KeyId().ScriptPubKey);

                (Block Block, int Selected, int Updated) result = posBlockAssembler.AddTransactions();

                Assert.NotEmpty(result.Block.Transactions);

                Assert.Equal(transaction.ToHex(), result.Block.Transactions[1].ToHex());
                Assert.Equal(1, result.Selected);
                Assert.Equal(0, result.Updated);
            });
        }

        [Fact]
        public void AddTransactions_TransactionAlreadyInInblock_DoesNotAddTransactionToBlock()
        {
            var newOptions = new PosConsensusOptions();

            this.ExecuteWithConsensusOptions(newOptions, () =>
            {
                ChainIndexer chainIndexer = GenerateChainWithHeight(5, this.Network, this.key);
                this.consensusManager.Setup(c => c.Tip)
                    .Returns(chainIndexer.GetHeader(5));
                Transaction transaction = CreateTransaction(this.Network, this.key, 5, new Money(400 * 1000 * 1000), new Key(), new uint256(124124));
                var txFee = new Money(1000);
                TxMempoolEntry[] entries = SetupTxMempool(chainIndexer, newOptions, txFee, transaction);

                var posBlockAssembler = new PosTestBlockAssembler(this.consensusManager.Object, this.Network, new MempoolSchedulerLock(), this.mempool.Object, this.minerSettings, this.dateTimeProvider.Object, this.stakeChain.Object, this.stakeValidator.Object, this.LoggerFactory.Object, new NodeDeployments(this.Network, chainIndexer));
                posBlockAssembler.AddInBlockTxEntries(entries);

                (Block Block, int Selected, int Updated) result = posBlockAssembler.AddTransactions();

                Assert.Empty(result.Block.Transactions);
                Assert.Equal(0, result.Selected);
                Assert.Equal(0, result.Updated);
            });
        }

        private void ExecuteWithConsensusOptions(PosConsensusOptions newOptions, Action action)
        {
            ConsensusOptions options = this.Network.Consensus.Options;
            try
            {
                this.Network.Consensus.Options = newOptions;

                action();
            }
            finally
            {
                // This is a static in the global context so be careful updating it. I'm resetting it after being done testing so I don't influence other tests.
                this.Network.Consensus.Options = options;
                this.Network.Consensus.BIP9Deployments[0] = null;
            }
        }

        private static ChainIndexer GenerateChainWithHeight(int blockAmount, Network network, Key key)
        {
            var chain = new ChainIndexer(network);
            uint nonce = RandomUtils.GetUInt32();
            uint256 prevBlockHash = chain.Genesis.HashBlock;
            for (int i = 0; i < blockAmount; i++)
            {
                Block block = network.Consensus.ConsensusFactory.CreateBlock();
                Transaction coinStake = CreateCoinStakeTransaction(network, key, chain.Height + 1, new uint256((ulong)12312312 + (ulong)i));

                block.AddTransaction(coinStake);
                block.UpdateMerkleRoot();
                block.Header.BlockTime = new DateTimeOffset(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i));
                block.Header.HashPrevBlock = prevBlockHash;
                block.Header.Nonce = nonce;

                chain.SetTip(block.Header);
                prevBlockHash = block.GetHash();
            }

            return chain;
        }

        private static ChainIndexer GenerateChainWithHeightAndActivatedBip9(int blockAmount, Network network, Key key, BIP9DeploymentsParameters parameter, Target bits = null)
        {
            var chain = new ChainIndexer(network);
            uint nonce = RandomUtils.GetUInt32();
            uint256 prevBlockHash = chain.Genesis.HashBlock;
            for (int i = 0; i < blockAmount; i++)
            {
                Block block = network.Consensus.ConsensusFactory.CreateBlock();
                Transaction coinbase = CreateCoinStakeTransaction(network, key, chain.Height + 1, new uint256((ulong)12312312 + (ulong)i));

                block.AddTransaction(coinbase);
                block.UpdateMerkleRoot();
                block.Header.BlockTime = new DateTimeOffset(new DateTime(2017, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddDays(i));
                block.Header.HashPrevBlock = prevBlockHash;
                block.Header.Nonce = nonce;

                if (bits != null)
                {
                    block.Header.Bits = bits;
                }

                if (parameter != null)
                {
                    uint version = ThresholdConditionCache.VersionbitsTopBits;
                    version |= ((uint)1) << parameter.Bit;
                    block.Header.Version = (int)version;
                }

                chain.SetTip(block.Header);
                prevBlockHash = block.GetHash();
            }

            return chain;
        }

        private static Transaction CreateCoinStakeTransaction(Network network, Key key, int height, uint256 prevout)
        {
            var coinStake = new Transaction();
            coinStake.AddInput(new TxIn(new OutPoint(prevout, 1)));
            coinStake.AddOutput(new TxOut(0, new Script()));
            coinStake.AddOutput(new TxOut(network.GetReward(height), key.ScriptPubKey));
            return coinStake;
        }

        private static Transaction CreateTransaction(Network network, Key inkey, int height, Money amount, Key outKey, uint256 prevOutHash)
        {
            var coinbase = new Transaction();
            coinbase.AddInput(new TxIn(new OutPoint(prevOutHash, 1), inkey.ScriptPubKey));
            coinbase.AddOutput(new TxOut(amount, outKey));
            return coinbase;
        }

        private void SetupConsensusManager()
        {
            this.callbackRuleContext = null;
            this.consensusManager.Setup(c => c.ConsensusRules.PartialValidationAsync(It.IsAny<ChainedHeader>(), It.IsAny<Block>()))
                .Callback((ChainedHeader ch, Block b) =>
                {
                    this.callbackRuleContext = new RuleContext();
                });
        }

        private void SetupRulesEngine(ChainIndexer chainIndexer)
        {
            var dateTimeProvider = new DateTimeProvider();

            var consensusRulesContainer = new ConsensusRulesContainer();

            foreach (var ruleType in this.Network.Consensus.ConsensusRules.HeaderValidationRules)
                consensusRulesContainer.HeaderValidationRules.Add(Activator.CreateInstance(ruleType) as HeaderValidationConsensusRule);
            foreach (var ruleType in this.Network.Consensus.ConsensusRules.PartialValidationRules)
                consensusRulesContainer.PartialValidationRules.Add(Activator.CreateInstance(ruleType) as PartialValidationConsensusRule);
            foreach (var ruleType in this.Network.Consensus.ConsensusRules.FullValidationRules)
                consensusRulesContainer.FullValidationRules.Add(Activator.CreateInstance(ruleType) as FullValidationConsensusRule);

            var posConsensusRules = new PosConsensusRuleEngine(
                this.Network,
                this.LoggerFactory.Object,
                this.dateTimeProvider.Object,
                chainIndexer,
                new NodeDeployments(this.Network, chainIndexer),
                new ConsensusSettings(new NodeSettings(this.Network)),
                new Checkpoints(),
                new Mock<ICoinView>().Object,
                new Mock<IStakeChain>().Object,
                new Mock<IStakeValidator>().Object,
                new Mock<IChainState>().Object,
                new InvalidBlockHashStore(dateTimeProvider),
                new NodeStats(dateTimeProvider, NodeSettings.Default(this.Network), new Mock<IVersionProvider>().Object),
                new Mock<IRewindDataIndexCache>().Object,
                this.CreateAsyncProvider(),
                consensusRulesContainer);

            posConsensusRules.SetupRulesEngineParent();

            this.consensusManager.SetupGet(x => x.ConsensusRules).Returns(posConsensusRules);
        }

        private TxMempoolEntry[] SetupTxMempool(ChainIndexer chainIndexer, PosConsensusOptions newOptions, Money txFee, params Transaction[] transactions)
        {
            uint txTime = Utils.DateTimeToUnixTime(chainIndexer.Tip.Header.BlockTime.AddSeconds(25));
            var lockPoints = new LockPoints()
            {
                Height = 4,
                MaxInputBlock = chainIndexer.GetHeader(4),
                Time = chainIndexer.GetHeader(4).Header.Time
            };

            var resultingTransactionEntries = new List<TxMempoolEntry>();
            var indexedTransactionSet = new TxMempool.IndexedTransactionSet();
            foreach (Transaction transaction in transactions)
            {
                var txPoolEntry = new TxMempoolEntry(transaction, txFee, txTime, 1, 4, new Money(400000000), false, 2, lockPoints, newOptions);
                indexedTransactionSet.Add(txPoolEntry);
                resultingTransactionEntries.Add(txPoolEntry);
            }


            this.mempool.Setup(t => t.MapTx)
                .Returns(indexedTransactionSet);

            return resultingTransactionEntries.ToArray();
        }

        private class PosTestBlockAssembler : PosBlockDefinition
        {
            public PosTestBlockAssembler(
                IConsensusManager consensusManager,
                Network network,
                MempoolSchedulerLock mempoolLock,
                ITxMempool mempool,
                MinerSettings minerSettings,
                IDateTimeProvider dateTimeProvider,
                IStakeChain stakeChain,
                IStakeValidator stakeValidator,
                ILoggerFactory loggerFactory,
                NodeDeployments nodeDeployments)
                : base(consensusManager, dateTimeProvider, loggerFactory, mempool, mempoolLock, minerSettings, network, stakeChain, stakeValidator, nodeDeployments)
            {
                base.block = this.BlockTemplate.Block;
            }

            public Block UpdateHeaders(ChainedHeader chainTip)
            {
                this.ChainTip = chainTip;
                base.UpdateHeaders();
                return this.block;
            }

            public (int Height, int Version) ComputeBlockVersion(ChainedHeader chainTip)
            {
                base.ChainTip = chainTip;

                base.ComputeBlockVersion();

                return (base.height, base.block.Header.Version);
            }

            public BlockTemplate CreateCoinBase(ChainedHeader chainTip, Script scriptPubKeyIn)
            {
                base.scriptPubKey = scriptPubKeyIn;
                base.ChainTip = chainTip;

                base.CreateCoinbase();

                base.BlockTemplate.Block = base.block;

                return base.BlockTemplate;
            }

            public (Block Block, int Selected, int Updated) AddTransactions()
            {
                base.AddTransactions(out int selected, out int updated);

                return (base.block, selected, updated);
            }

            public void AddInBlockTxEntries(params TxMempoolEntry[] entries)
            {
                foreach (TxMempoolEntry entry in entries)
                {
                    base.inBlock.Add(entry);
                }
            }
        }

        private class StraxOverrideRegTest : StraxRegTest
        {
            public StraxOverrideRegTest() : base()
            {
                this.Name = Guid.NewGuid().ToString();
            }
        }
    }
}