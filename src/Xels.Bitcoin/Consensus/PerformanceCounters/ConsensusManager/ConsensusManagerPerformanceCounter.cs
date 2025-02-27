﻿using System;
using NBitcoin;
using Xels.Bitcoin.Utilities;
using TracerAttributes;

namespace Xels.Bitcoin.Consensus.PerformanceCounters.ConsensusManager
{
    public class ConsensusManagerPerformanceCounter
    {
        /// <summary>Snapshot that is currently being populated.</summary>
        private ConsensusManagerPerformanceSnapshot currentSnapshot;
        private readonly ChainIndexer chainIndexer;

        public ConsensusManagerPerformanceCounter(ChainIndexer chainIndexer)
        {
            this.chainIndexer = chainIndexer;
            this.currentSnapshot = new ConsensusManagerPerformanceSnapshot(this.chainIndexer);
        }

        /// <summary>
        /// Measures time to execute <c>OnPartialValidationSucceededAsync</c>.
        /// </summary>
        [NoTrace]
        public IDisposable MeasureTotalConnectionTime()
        {
            return new StopwatchDisposable((elapsed) => this.currentSnapshot.TotalConnectionTime.Increment(elapsed));
        }

        [NoTrace]
        public IDisposable MeasureBlockConnectionFV()
        {
            return new StopwatchDisposable((elapsed) => this.currentSnapshot.ConnectBlockFV.Increment(elapsed));
        }

        [NoTrace]
        public IDisposable MeasureBlockConnectedSignal()
        {
            return new StopwatchDisposable((elapsed) => this.currentSnapshot.BlockConnectedSignal.Increment(elapsed));
        }

        [NoTrace]
        public IDisposable MeasureBlockDisconnectedSignal()
        {
            return new StopwatchDisposable((elapsed) => this.currentSnapshot.BlockDisconnectedSignal.Increment(elapsed));
        }

        /// <summary>Takes current snapshot.</summary>
        /// <remarks>Not thread-safe. Caller should ensure that it's not called from different threads at once.</remarks>
        [NoTrace]
        public ConsensusManagerPerformanceSnapshot TakeSnapshot()
        {
            var newSnapshot = new ConsensusManagerPerformanceSnapshot(this.chainIndexer);
            ConsensusManagerPerformanceSnapshot previousSnapshot = this.currentSnapshot;
            this.currentSnapshot = newSnapshot;

            return previousSnapshot;
        }
    }
}
