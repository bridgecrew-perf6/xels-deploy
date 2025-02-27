﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Configuration.Logging;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Interfaces;
using Xels.Bitcoin.Utilities;
using Xels.Features.FederatedPeg.Interfaces;

namespace Xels.Features.FederatedPeg.Wallet
{
    public class FederationWalletSyncManager : IFederationWalletSyncManager, IDisposable
    {
        protected readonly IFederationWalletManager federationWalletManager;

        protected readonly ChainIndexer chain;

        protected readonly CoinType coinType;

        private readonly ILogger logger;

        private readonly IBlockStore blockStore;

        private readonly StoreSettings storeSettings;

        /// <summary>Global application life cycle control - triggers when application shuts down.</summary>
        private readonly INodeLifetime nodeLifetime;
        private readonly IAsyncProvider asyncProvider;
        protected ChainedHeader walletTip;

        public ChainedHeader WalletTip => this.walletTip;

        /// <summary>Queue which contains blocks that should be processed by <see cref="WalletManager"/>.</summary>
        private readonly BlockQueueProcessor blockQueueProcessor;

        /// <summary>Limit <see cref="blockQueueProcessor"/> size to 100MB.</summary>
        private const int MaxQueueSize = 100 * 1024 * 1024;

        public FederationWalletSyncManager(IFederationWalletManager walletManager, ChainIndexer chain, Network network,
            IBlockStore blockStore, StoreSettings storeSettings, INodeLifetime nodeLifetime, IAsyncProvider asyncProvider)
        {
            Guard.NotNull(walletManager, nameof(walletManager));
            Guard.NotNull(chain, nameof(chain));
            Guard.NotNull(network, nameof(network));
            Guard.NotNull(blockStore, nameof(blockStore));
            Guard.NotNull(storeSettings, nameof(storeSettings));
            Guard.NotNull(nodeLifetime, nameof(nodeLifetime));
            Guard.NotNull(asyncProvider, nameof(asyncProvider));

            this.federationWalletManager = walletManager;
            this.chain = chain;
            this.blockStore = blockStore;
            this.coinType = (CoinType)network.Consensus.CoinType;
            this.storeSettings = storeSettings;
            this.nodeLifetime = nodeLifetime;
            this.asyncProvider = asyncProvider;
            this.logger = LogManager.GetCurrentClassLogger();
            this.blockQueueProcessor = new BlockQueueProcessor(this.asyncProvider, this.OnProcessBlockWrapperAsync, MaxQueueSize, nameof(FederationWalletSyncManager));
        }

        /// <inheritdoc />
        public void Initialize()
        {
            // When a node is pruned it impossible to catch up
            // if the wallet falls behind the block puller.
            // To support pruning the wallet will need to be
            // able to download blocks from peers to catch up.
            if (this.storeSettings.PruningEnabled)
                throw new WalletException("Wallet can not yet run on a pruned node");

            this.logger.LogInformation("WalletSyncManager initialized. Wallet at block {0}.", this.federationWalletManager.LastBlockSyncedHashHeight().Height);

            this.walletTip = this.chain.GetHeader(this.federationWalletManager.WalletTipHash);
            if (this.walletTip == null)
            {
                // The wallet tip was not found in the main chain.
                // this can happen if the node crashes unexpectedly.
                // To recover we need to find the first common fork
                // with the best chain. As the wallet does not have a
                // list of chain headers, we use a BlockLocator and persist
                // that in the wallet. The block locator will help finding
                // a common fork and bringing the wallet back to a good
                // state (behind the best chain).
                ICollection<uint256> locators = this.federationWalletManager.GetWallet().BlockLocator;
                var blockLocator = new BlockLocator { Blocks = locators.ToList() };
                ChainedHeader fork = this.chain.FindFork(blockLocator);
                this.federationWalletManager.RemoveBlocks(fork);
                this.federationWalletManager.WalletTipHash = fork.HashBlock;
                this.walletTip = fork;
            }
        }

        /// <inheritdoc />
        public void Stop()
        {
        }

        private async Task OnProcessBlockWrapperAsync(Block block, CancellationToken cancellationToken)
        {
            // This way the queue should continue working, but if / when it fails at least we can see why without it pushing up to AsyncProvider
            try
            {
                await this.OnProcessBlockAsync(block, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                this.logger.LogError(e.ToString());
            }
        }

        private Task OnProcessBlockAsync(Block block, CancellationToken cancellationToken)
        {
            Guard.NotNull(block, nameof(block));

            ChainedHeader newTip = this.chain.GetHeader(block.GetHash());
            if (newTip == null)
            {
                this.logger.LogTrace("(-)[NEW_TIP_REORG]");
                return Task.CompletedTask;
            }

            // If the new block's previous hash is the same as the
            // wallet hash then just pass the block to the manager.
            if (block.Header.HashPrevBlock != this.walletTip.HashBlock)
            {
                // If previous block does not match there might have
                // been a reorg, check if the wallet is still on the main chain.
                ChainedHeader inBestChain = this.chain.GetHeader(this.walletTip.HashBlock);
                if (inBestChain == null)
                {
                    // The current wallet hash was not found on the main chain.
                    // A reorg happened so bring the wallet back top the last known fork.
                    ChainedHeader fork = this.walletTip;

                    // We walk back the chained block object to find the fork.
                    while (this.chain.GetHeader(fork.HashBlock) == null)
                        fork = fork.Previous;

                    this.logger.LogInformation("Reorg detected, going back from '{0}' to '{1}'.", this.walletTip, fork);

                    this.federationWalletManager.RemoveBlocks(fork);
                    this.walletTip = fork;

                    this.logger.LogDebug("Wallet tip set to '{0}'.", this.walletTip);
                }

                // The new tip can be ahead or behind the wallet.
                // If the new tip is ahead we try to bring the wallet up to the new tip.
                // If the new tip is behind we just check the wallet and the tip are in the same chain.
                if (newTip.Height > this.walletTip.Height)
                {
                    ChainedHeader findTip = newTip.FindAncestorOrSelf(this.walletTip);
                    if (findTip == null)
                    {
                        this.logger.LogTrace("(-)[NEW_TIP_AHEAD_NOT_IN_WALLET]");
                        return Task.CompletedTask;
                    }

                    this.logger.LogDebug("Wallet tip '{0}' is behind the new tip '{1}'.", this.walletTip, newTip);

                    ChainedHeader next = this.walletTip;
                    while (next != newTip)
                    {
                        // While the wallet is catching up the entire node will wait.
                        // If a wallet is recovered to a date in the past. Consensus will stop till the wallet is up to date.

                        // TODO: This code should be replaced with a different approach
                        // Similar to BlockStore the wallet should be standalone and not depend on consensus.
                        // The block should be put in a queue and pushed to the wallet in an async way.
                        // If the wallet is behind it will just read blocks from store (or download in case of a pruned node).

                        next = newTip.GetAncestor(next.Height + 1);
                        Block nextblock = null;
                        int index = 0;
                        while (true)
                        {
                            if (cancellationToken.IsCancellationRequested)
                            {
                                this.logger.LogTrace("(-)[CANCELLATION_REQUESTED]");
                                return Task.CompletedTask;
                            }

                            nextblock = this.blockStore.GetBlock(next.HashBlock);
                            if (nextblock == null)
                            {
                                // The idea in this abandoning of the loop is to release consensus to push the block.
                                // That will make the block available in the next push from consensus.
                                index++;
                                if (index > 10)
                                {
                                    this.logger.LogTrace("(-)[WALLET_CATCHUP_INDEX_MAX]");
                                    return Task.CompletedTask;
                                }

                                // Really ugly hack to let store catch up.
                                // This will block the entire consensus pulling.
                                this.logger.LogWarning("Wallet is behind the best chain and the next block is not found in store.");
                                Thread.Sleep(100);
                                continue;
                            }

                            break;
                        }

                        this.walletTip = next;
                        this.federationWalletManager.ProcessBlock(nextblock, next);
                    }
                }
                else
                {
                    ChainedHeader findTip = this.walletTip.FindAncestorOrSelf(newTip);
                    if (findTip == null)
                    {
                        this.logger.LogTrace("(-)[NEW_TIP_BEHIND_NOT_IN_WALLET]");
                        return Task.CompletedTask;
                    }

                    this.logger.LogDebug("Wallet tip '{0}' is ahead or equal to the new tip '{1}'.", this.walletTip, newTip);
                }
            }
            else this.logger.LogDebug("New block follows the previously known block '{0}'.", this.walletTip);

            this.walletTip = newTip;
            this.federationWalletManager.ProcessBlock(block, newTip);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public virtual void ProcessBlock(Block block)
        {
            Guard.NotNull(block, nameof(block));

            this.blockQueueProcessor.TryEnqueue(block);
        }

        /// <inheritdoc />
        public virtual void ProcessTransaction(Transaction transaction)
        {
            Guard.NotNull(transaction, nameof(transaction));

            this.logger.LogDebug("Processing transaction from mempool: {0}", transaction.GetHash());

            if (this.federationWalletManager.ProcessTransaction(transaction))
                this.federationWalletManager.SaveWallet();
        }

        /// <inheritdoc />
        public virtual void SyncFromDate(DateTime date)
        {
            int blockSyncStart = this.chain.GetHeightAtTime(date);
            this.SyncFromHeight(blockSyncStart);
        }

        /// <inheritdoc />
        public virtual void SyncFromHeight(int height)
        {
            ChainedHeader chainedBlock = this.chain.GetHeader(height);
            this.walletTip = chainedBlock ?? throw new WalletException("Invalid block height");
            this.federationWalletManager.WalletTipHash = chainedBlock.HashBlock;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            this.blockQueueProcessor.Dispose();
        }
    }
}
