﻿using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Base.Deployments;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Consensus.Rules;
using Xels.Bitcoin.Features.Consensus.CoinViews;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xels.Bitcoin.Features.SmartContracts.Caching;
using Xels.Bitcoin.Utilities;
using Xels.SmartContracts.CLR;
using Xels.SmartContracts.Core;
using Xels.SmartContracts.Core.Receipts;
using Xels.SmartContracts.Core.State;
using Xels.SmartContracts.Core.Util;

namespace Xels.Bitcoin.Features.SmartContracts.Rules
{
    /// <inheritdoc />
    public abstract class SmartContractCoinviewRule : CoinViewRule
    {
        protected List<Transaction> blockTxsProcessed;
        protected Transaction generatedTransaction;
        protected uint refundCounter;
        private SmartContractCoinViewRuleLogic logic;

        private readonly Network network;
        private readonly IStateRepositoryRoot stateRepositoryRoot;
        private readonly IContractExecutorFactory executorFactory;
        private readonly ICallDataSerializer callDataSerializer;
        private readonly ISenderRetriever senderRetriever;
        private readonly IReceiptRepository receiptRepository;
        private readonly ICoinView coinView;
        private readonly IBlockExecutionResultCache executionCache;
        private readonly ILoggerFactory loggerFactory;

        protected SmartContractCoinviewRule(Network network,
            IStateRepositoryRoot stateRepositoryRoot,
            IContractExecutorFactory executorFactory,
            ICallDataSerializer callDataSerializer,
            ISenderRetriever senderRetriever,
            IReceiptRepository receiptRepository,
            ICoinView coinView,
            IBlockExecutionResultCache executionCache,
            ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.stateRepositoryRoot = stateRepositoryRoot;
            this.executorFactory = executorFactory;
            this.callDataSerializer = callDataSerializer;
            this.senderRetriever = senderRetriever;
            this.receiptRepository = receiptRepository;
            this.coinView = coinView;
            this.executionCache = executionCache;
            this.loggerFactory = loggerFactory;
        }

        /// <inheritdoc />
        public override void Initialize()
        {
            base.Initialize();

            this.logic = new SmartContractCoinViewRuleLogic(this.stateRepositoryRoot, this.executorFactory, this.callDataSerializer, this.senderRetriever, this.receiptRepository, this.coinView, this.executionCache, this.loggerFactory);
        }

        /// <inheritdoc />
        public override async Task RunAsync(RuleContext context)
        {
            await this.logic.RunAsync(base.RunAsync, context);
        }

        /// <inheritdoc/>
        protected override bool CheckInput(Transaction tx, int inputIndexCopy, TxOut txout, PrecomputedTransactionData txData, TxIn input, DeploymentFlags flags)
        {
            return this.logic.CheckInput(base.CheckInput, tx, inputIndexCopy, txout, txData, input, flags);
        }

        /// <inheritdoc/>
        public override void CheckBlockReward(RuleContext context, Money fees, int height, NBitcoin.Block block)
        {
            Money blockReward = fees + this.GetProofOfWorkReward(height);
            if (block.Transactions[0].TotalOut > blockReward)
            {
                this.Logger.LogTrace("(-)[BAD_COINBASE_AMOUNT]");
                ConsensusErrors.BadCoinbaseAmount.Throw();
            }
        }

        /// <inheritdoc/>
        public override void CheckMaturity(UnspentOutput coins, int spendHeight)
        {
            base.CheckCoinbaseMaturity(coins, spendHeight);
        }

        /// <inheritdoc />
        /// <remarks>Should someone wish to use POW only we'll need to implement subsidy halving.</remarks>
        public override Money GetProofOfWorkReward(int height)
        {
            if (height == this.network.Consensus.PremineHeight)
                return this.network.Consensus.PremineReward;

            return this.network.Consensus.ProofOfWorkReward;
        }

        /// <summary>
        /// Executes contracts as necessary and updates the coinview / UTXOset after execution.
        /// </summary>
        /// <inheritdoc/>
        public override void UpdateCoinView(RuleContext context, Transaction transaction)
        {
            this.logic.UpdateCoinView(base.UpdateUTXOSet, context, transaction);
        }

        /// <summary>
        /// Validates that any condensing transaction matches the transaction generated during execution
        /// </summary>
        /// <param name="transaction">The generated transaction to validate.</param>
        protected void ValidateGeneratedTransaction(Transaction transaction)
        {
            this.logic.ValidateGeneratedTransaction(transaction);
        }

        /// <summary>
        /// Validates that a submitted transaction doesn't contain illegal operations.
        /// </summary>
        /// <param name="transaction">The submitted transaction to validate.</param>
        protected void ValidateSubmittedTransaction(Transaction transaction)
        {
            this.logic.ValidateSubmittedTransaction(transaction);
        }

        /// <summary>Executes the smart contract part of a transaction.</summary>
        protected void ExecuteContractTransaction(RuleContext context, Transaction transaction)
        {
            this.logic.ExecuteContractTransaction(context, transaction);
        }
    }
}