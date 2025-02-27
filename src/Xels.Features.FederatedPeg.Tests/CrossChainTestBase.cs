﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.Networks;
using NSubstitute;
using Xels.Bitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Interfaces;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Signals;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Utilities;
using Xels.Features.Collateral.CounterChain;
using Xels.Features.FederatedPeg.Conversion;
using Xels.Features.FederatedPeg.Interfaces;
using Xels.Features.FederatedPeg.TargetChain;
using Xels.Features.FederatedPeg.Wallet;
using Xels.Sidechains.Networks;
using Xels.SmartContracts.Core.State;

namespace Xels.Features.FederatedPeg.Tests
{
    public class CrossChainTestBase
    {
        protected const string walletPassword = "password";
        protected Network network;
        protected Network counterChainNetwork;
        protected CounterChainNetworkWrapper counterChainNetworkWrapper;
        protected ChainIndexer ChainIndexer;
        protected ILoggerFactory loggerFactory;
        protected ILogger logger;
        protected ISignals signals;
        protected IDateTimeProvider dateTimeProvider;
        protected IOpReturnDataReader opReturnDataReader;
        protected IWithdrawalExtractor withdrawalExtractor;
        protected IBlockRepository blockRepository;
        protected IInitialBlockDownloadState ibdState;
        protected IFullNode fullNode;
        protected IFederationWalletManager federationWalletManager;
        protected IFederatedPegSettings federatedPegSettings;
        protected IConversionRequestRepository repository;
        protected IFederationWalletSyncManager federationWalletSyncManager;
        protected IFederationWalletTransactionHandler FederationWalletTransactionHandler;
        protected IWithdrawalTransactionBuilder withdrawalTransactionBuilder;
        protected IInputConsolidator inputConsolidator;
        protected IFederatedPegBroadcaster federatedPegBroadcaster;
        protected IStateRepositoryRoot stateRepositoryRoot;
        protected DataFolder dataFolder;
        protected IWalletFeePolicy walletFeePolicy;
        protected IAsyncProvider asyncProvider;
        protected INodeLifetime nodeLifetime;
        protected IConnectionManager connectionManager;
        protected DBreezeSerializer dBreezeSerializer;
        protected Dictionary<uint256, Block> blockDict;
        protected List<Transaction> fundingTransactions;
        protected FederationWallet wallet;
        protected ExtKey[] federationKeys;
        protected ExtKey extendedKey;
        protected Script redeemScript
        {
            get
            {
                return PayToFederationTemplate.Instance.GenerateScriptPubKey(this.network.Federations.GetOnlyFederation().Id);
            }
        }

        /// <summary>
        /// Initializes the cross-chain transfer tests.
        /// </summary>
        /// <param name="network">The network to run the tests for.</param>
        public CrossChainTestBase(Network network = null, Network counterChainNetwork = null)
        {
            this.network = network ?? CirrusNetwork.NetworksSelector.Regtest();
            this.counterChainNetwork = counterChainNetwork ?? Networks.Strax.Regtest();
            this.counterChainNetworkWrapper = new CounterChainNetworkWrapper(counterChainNetwork);

            NetworkRegistration.Register(this.network);

            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.nodeLifetime = new NodeLifetime();
            this.logger = Substitute.For<ILogger>();
            this.signals = Substitute.For<ISignals>();
            this.asyncProvider = new AsyncProvider(this.loggerFactory, this.signals);
            this.loggerFactory.CreateLogger(null).ReturnsForAnyArgs(this.logger);
            this.dateTimeProvider = DateTimeProvider.Default;
            this.opReturnDataReader = new OpReturnDataReader(this.counterChainNetworkWrapper.CounterChainNetwork);
            this.blockRepository = Substitute.For<IBlockRepository>();
            this.fullNode = Substitute.For<IFullNode>();
            this.withdrawalTransactionBuilder = Substitute.For<IWithdrawalTransactionBuilder>();
            this.federationWalletManager = Substitute.For<IFederationWalletManager>();
            this.federationWalletSyncManager = Substitute.For<IFederationWalletSyncManager>();
            this.FederationWalletTransactionHandler = Substitute.For<IFederationWalletTransactionHandler>();
            this.walletFeePolicy = new WalletFeePolicy(NodeSettings.Default(this.network));

            this.connectionManager = Substitute.For<IConnectionManager>();
            this.federatedPegBroadcaster = Substitute.For<IFederatedPegBroadcaster>();
            this.inputConsolidator = Substitute.For<IInputConsolidator>();
            this.dBreezeSerializer = new DBreezeSerializer(this.network.Consensus.ConsensusFactory);
            this.ibdState = Substitute.For<IInitialBlockDownloadState>();
            this.wallet = null;
            this.federatedPegSettings = Substitute.For<IFederatedPegSettings>();
            this.repository = Substitute.For<IConversionRequestRepository>();
            this.ChainIndexer = new ChainIndexer(this.network);

            // Generate the keys used by the federation members for our tests.
            this.federationKeys = new[]
            {
                "ensure feel swift crucial bridge charge cloud tell hobby twenty people mandate",
                "quiz sunset vote alley draw turkey hill scrap lumber game differ fiction",
                "exchange rent bronze pole post hurry oppose drama eternal voice client state"
            }.Select(m => HdOperations.GetExtendedKey(m)).ToArray();

            SetExtendedKey(0);

            this.fundingTransactions = new List<Transaction>();

            this.blockDict = new Dictionary<uint256, Block>();
            this.blockDict[this.network.GenesisHash] = this.network.GetGenesis();

            this.blockRepository.GetBlocks(Arg.Any<List<uint256>>()).ReturnsForAnyArgs((x) =>
            {
                List<uint256> hashes = x.ArgAt<List<uint256>>(0);
                var blocks = new List<Block>();
                for (int i = 0; i < hashes.Count; i++)
                {
                    blocks.Add(this.blockDict.TryGetValue(hashes[i], out Block block) ? block : null);
                }

                return blocks;
            });

            this.blockRepository.GetBlock(Arg.Any<uint256>()).ReturnsForAnyArgs((x) =>
            {
                uint256 hash = x.ArgAt<uint256>(0);
                this.blockDict.TryGetValue(hash, out Block block);

                return block;
            });

            this.blockRepository.TipHashAndHeight.Returns((x) =>
            {
                return new HashHeightPair(this.blockDict.Last().Value.GetHash(), this.blockDict.Count - 1);
            });
        }

        /// <summary>
        /// Chooses the key we use.
        /// </summary>
        /// <param name="keyNum">The key number.</param>
        protected void SetExtendedKey(int keyNum)
        {
            this.extendedKey = this.federationKeys[keyNum];

            this.federatedPegSettings.IsMainChain.Returns(false);
            this.federatedPegSettings.MultiSigRedeemScript.Returns(this.redeemScript);
            this.federatedPegSettings.MultiSigAddress.Returns(this.redeemScript.Hash.GetAddress(this.network));
            this.federatedPegSettings.PublicKey.Returns(this.extendedKey.PrivateKey.PubKey.ToHex());
            this.federatedPegSettings.MaximumPartialTransactionThreshold.Returns(CrossChainTransferStore.MaximumPartialTransactions);
            this.withdrawalExtractor = new WithdrawalExtractor(this.federatedPegSettings, this.opReturnDataReader, this.network);
        }

        protected (Transaction, ChainedHeader) AddFundingTransaction(Money[] amounts)
        {
            Transaction transaction = this.network.CreateTransaction();

            foreach (Money amount in amounts)
            {
                transaction.Outputs.Add(new TxOut(amount, this.wallet.MultiSigAddress.ScriptPubKey));
            }

            // This is just a dummy unique input so don't take the implementation too seriously. It ensures a unique txid.
            transaction.AddInput(new TxIn(new OutPoint(this.ChainIndexer.Tip.HashBlock, 0), new Script(OpcodeType.OP_1)));

            ChainedHeader chainedHeader = this.AppendBlock(transaction);

            this.fundingTransactions.Add(transaction);

            return (transaction, chainedHeader);
        }

        protected void AddFunding()
        {
            AddFundingTransaction(new Money[] { Money.COIN * 90, Money.COIN * 80 });
            AddFundingTransaction(new Money[] { Money.COIN * 70 });
        }

        /// <summary>
        /// Create the wallet manager and wallet transaction handler.
        /// </summary>
        /// <param name="dataFolder">The data folder.</param>
        protected void Init(DataFolder dataFolder)
        {
            this.dataFolder = dataFolder;

            // Create the wallet manager.
            this.federationWalletManager = new FederationWalletManager(
                this.network,
                Substitute.For<INodeStats>(),
                this.ChainIndexer,
                dataFolder,
                this.walletFeePolicy,
                this.asyncProvider,
                new NodeLifetime(),
                this.dateTimeProvider,
                this.federatedPegSettings,
                this.withdrawalExtractor,
                this.blockRepository);

            // Starts and creates the wallet.
            this.federationWalletManager.Start();
            this.wallet = this.federationWalletManager.GetWallet();

            // TODO: The transaction builder, cross-chain store and fed wallet tx handler should be tested individually.
            this.FederationWalletTransactionHandler = new FederationWalletTransactionHandler(this.federationWalletManager, this.walletFeePolicy, this.network, this.federatedPegSettings);
            this.stateRepositoryRoot = Substitute.For<IStateRepositoryRoot>();
            this.withdrawalTransactionBuilder = new WithdrawalTransactionBuilder(this.network, this.federationWalletManager, this.FederationWalletTransactionHandler, this.federatedPegSettings, this.signals, null);

            var storeSettings = (StoreSettings)FormatterServices.GetUninitializedObject(typeof(StoreSettings));

            this.federationWalletSyncManager = new FederationWalletSyncManager(this.federationWalletManager, this.ChainIndexer, this.network, this.blockRepository,
                storeSettings, Substitute.For<INodeLifetime>(), this.asyncProvider);

            this.federationWalletSyncManager.Initialize();

            // Set up the encrypted seed on the wallet.
            string encryptedSeed = this.extendedKey.PrivateKey.GetEncryptedBitcoinSecret(walletPassword, this.network).ToWif();
            this.wallet.EncryptedSeed = encryptedSeed;

            this.federationWalletManager.Secret = new WalletSecret() { WalletPassword = walletPassword };

            FieldInfo isFederationActiveField = this.federationWalletManager.GetType().GetField("isFederationActive", BindingFlags.NonPublic | BindingFlags.Instance);
            isFederationActiveField.SetValue(this.federationWalletManager, true);
        }

        protected ICrossChainTransferStore CreateStore()
        {
            return new CrossChainTransferStore(this.network, Substitute.For<INodeStats>(), this.dataFolder, this.ChainIndexer, this.federatedPegSettings, this.dateTimeProvider,
                this.withdrawalExtractor, Substitute.For<IWithdrawalHistoryProvider>(), this.blockRepository, this.federationWalletManager, this.withdrawalTransactionBuilder, this.dBreezeSerializer, this.signals, this.stateRepositoryRoot);
        }

        /// <summary>
        /// Builds a chain with the requested number of blocks.
        /// </summary>
        /// <param name="blocks">The number of blocks.</param>
        protected void AppendBlocks(int blocks)
        {
            for (int i = 0; i < blocks; i++)
            {
                this.AppendBlock();
            }
        }

        /// <summary>
        /// Create a block and add it to the dictionary used by the mock block repository.
        /// </summary>
        /// <param name="transactions">Additional transactions to add to the block.</param>
        /// <returns>The last chained header.</returns>
        protected ChainedHeader AppendBlock(params Transaction[] transactions)
        {
            Block block = this.network.CreateBlock();

            // Create coinbase.
            block.AddTransaction(this.network.CreateTransaction());

            // Add additional transactions if any.
            foreach (Transaction transaction in transactions)
            {
                block.AddTransaction(transaction);
            }

            block.UpdateMerkleRoot();
            block.Header.HashPrevBlock = this.ChainIndexer.Tip.HashBlock;
            block.Header.Nonce = RandomUtils.GetUInt32();
            block.ToBytes(); // Funnily enough, the only way to set .BlockSize. Forgive me for my sins.

            return AppendBlock(block);
        }

        /// <summary>
        /// Adds a previously created block to the dictionary used by the mock block repository.
        /// </summary>
        /// <param name="block">The block to add.</param>
        /// <returns>The last chained header.</returns>
        protected ChainedHeader AppendBlock(Block block)
        {
            if (!this.ChainIndexer.TrySetTip(block.Header, out ChainedHeader last))
                throw new InvalidOperationException("Previous not existing");

            last.Block = block;

            this.blockDict[block.GetHash()] = block;

            this.federationWalletSyncManager.ProcessBlock(block);

            // Ensure that the block was processed.
            TestBase.WaitLoop(() => this.federationWalletManager.WalletTipHash == block.GetHash());

            return last;
        }
    }
}
