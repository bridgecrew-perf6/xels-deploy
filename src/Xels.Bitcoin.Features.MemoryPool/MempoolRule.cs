﻿using Microsoft.Extensions.Logging;
using NBitcoin;
using Xels.Bitcoin.Features.MemoryPool.Interfaces;

namespace Xels.Bitcoin.Features.MemoryPool
{
    public abstract class MempoolRule : IMempoolRule
    {
        protected readonly Network network;

        protected readonly ITxMempool mempool;

        protected readonly MempoolSettings settings;

        protected readonly ChainIndexer chainIndexer;

        protected readonly ILogger logger;

        protected MempoolRule(Network network,
            ITxMempool mempool,
            MempoolSettings settings,
            ChainIndexer chainIndexer,
            ILoggerFactory loggerFactory)
        {
            this.network = network;
            this.mempool = mempool;
            this.settings = settings;
            this.chainIndexer = chainIndexer;

            this.logger = loggerFactory.CreateLogger(this.GetType().FullName);
        }

        /// <inheritdoc />
        public abstract void CheckTransaction(MempoolValidationContext context);
    }
}