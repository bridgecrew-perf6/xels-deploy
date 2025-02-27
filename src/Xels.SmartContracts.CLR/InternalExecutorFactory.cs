﻿using Microsoft.Extensions.Logging;

namespace Xels.SmartContracts.CLR
{
    /// <summary>
    /// Factory for creating internal transaction executors
    /// </summary>
    public sealed class InternalExecutorFactory : IInternalExecutorFactory
    {
        private readonly IStateProcessor stateProcessor;

        public InternalExecutorFactory(ILoggerFactory loggerFactory, IStateProcessor stateProcessor)
        {
            this.stateProcessor = stateProcessor;
        }

        public Stratis.SmartContracts.IInternalTransactionExecutor Create(RuntimeObserver.IGasMeter gasMeter, IState state)
        {
            return new InternalExecutor(gasMeter, state, this.stateProcessor);
        }
    }
}