﻿using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xels.Bitcoin.AsyncWork;
using Xels.Bitcoin.Utilities;
using Xunit;

namespace Xels.Bitcoin.Tests.Utilities
{
    /// <summary>
    /// Tests of <see cref="AsyncQueue{T}"/> class.
    /// </summary>
    public class AsyncProviderTest
    {
        /// <summary>Source of randomness.</summary>
        private Random random = new Random();
        private AsyncProvider asyncProvider;
        private Mock<ILogger> mockLogger;

        public AsyncProviderTest()
        {
            this.mockLogger = new Mock<ILogger>();
            var mockLoggerFactory = new Mock<ILoggerFactory>();
            mockLoggerFactory.Setup(l => l.CreateLogger(It.IsAny<string>())).Returns(this.mockLogger.Object).Verifiable();

            var signals = new Bitcoin.Signals.Signals(mockLoggerFactory.Object, null);
            var nodeLifetime = new Mock<INodeLifetime>().Object;

            this.asyncProvider = new AsyncProvider(mockLoggerFactory.Object, signals);
        }

        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.Dispose"/> triggers cancellation inside the on-enqueue callback.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_DelegateDequeuer_DisposeCancelsEnqueueAsync()
        {
            bool signal = false;

            var asyncQueue = this.asyncProvider.CreateAndRunAsyncDelegateDequeuer<int>(this.GetType().Name, async (item, cancellation) =>
            {
                // We set the signal and wait and if the wait is finished, we reset the signal, but that should not happen.
                signal = true;
                await Task.Delay(500, cancellation);
                signal = false;
            });

            // Enqueue an item, which should trigger the callback.
            asyncQueue.Enqueue(1);

            // Wait a bit and dispose the queue, which should trigger the cancellation.
            await Task.Delay(100);
            asyncQueue.Dispose();

            Assert.True(signal);
        }

        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.Dispose"/> waits until the on-enqueue callback (and the consumer task)
        /// are finished before returning to the caller.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_DelegateDequeuer_DisposeCancelsAndWaitsEnqueueAsync()
        {
            bool signal = true;

            var asyncQueue = this.asyncProvider.CreateAndRunAsyncDelegateDequeuer<int>(this.GetType().Name, async (item, cancellation) =>
            {
                // We only set the signal if the wait is finished.
                await Task.Delay(250);
                signal = false;
            });

            // Enqueue an item, which should trigger the callback.
            asyncQueue.Enqueue(1);

            // Wait a bit and dispose the queue, which should trigger the cancellation.
            await Task.Delay(100);
            asyncQueue.Dispose();

            Assert.False(signal);
        }

        /// <summary>
        /// Tests the guarantee of <see cref="AsyncQueue{T}"/> that only one instance of the callback is executed at the moment
        /// regardless of how many enqueue operations occur.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_DelegateDequeuer_OnlyOneInstanceOfCallbackExecutesAsync()
        {
            bool executingCallback = false;

            int itemsToProcess = 20;
            int itemsProcessed = 0;
            var allItemsProcessed = new ManualResetEventSlim();

            var asyncQueue = this.asyncProvider.CreateAndRunAsyncDelegateDequeuer<int>(this.GetType().Name, async (item, cancellation) =>
            {
                // Mark the callback as executing and wait a bit to make sure other callback operations can happen in the meantime.
                Assert.False(executingCallback);

                executingCallback = true;
                await Task.Delay(this.random.Next(100));

                itemsProcessed++;

                if (itemsProcessed == itemsToProcess) allItemsProcessed.Set();

                executingCallback = false;
            });

            // Adds items quickly so that next item is likely to be enqueued before the previous callback finishes.
            // We make small delays between enqueue operations, to make sure not all items are processed in one batch.
            for (int i = 0; i < itemsToProcess; i++)
            {
                asyncQueue.Enqueue(i);
                await Task.Delay(this.random.Next(10));
            }

            // Wait for all items to be processed.
            allItemsProcessed.Wait();

            Assert.Equal(itemsToProcess, itemsProcessed);

            allItemsProcessed.Dispose();

            asyncQueue.Dispose();
        }

        /// <summary>
        /// Tests that the order of enqueue operations is preserved in callbacks.
        /// </summary>
        [Fact]
        public void AsyncProvider_DelegateDequeuer_EnqueueOrderPreservedInCallbacks()
        {
            int itemsToProcess = 30;
            int itemPrevious = -1;
            var signal = new ManualResetEventSlim();

            var asyncQueue = this.asyncProvider.CreateAndRunAsyncDelegateDequeuer<int>(this.GetType().Name, async (item, cancellation) =>
            {
                // Wait a bit to make sure other enqueue operations can happen in the meantime.
                await Task.Delay(this.random.Next(50));
                Assert.Equal(itemPrevious + 1, item);
                itemPrevious = item;

                if (item + 1 == itemsToProcess) signal.Set();
            });

            // Enqueue items quickly, so that next item is likely to be enqueued before the previous callback finishes.
            for (int i = 0; i < itemsToProcess; i++)
                asyncQueue.Enqueue(i);

            // Wait for all items to be processed.
            signal.Wait();
            signal.Dispose();

            asyncQueue.Dispose();
        }

        /// <summary>
        /// Tests that if the queue is disposed, not all items are necessarily processed.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_DelegateDequeuer_DisposeCanDiscardItemsAsync()
        {
            int itemsToProcess = 100;
            int itemsProcessed = 0;

            var asyncQueue = this.asyncProvider.CreateAndRunAsyncDelegateDequeuer<int>(this.GetType().Name, async (item, cancellation) =>
            {
                // Wait a bit to make sure other enqueue operations can happen in the meantime.
                await Task.Delay(this.random.Next(30));
                itemsProcessed++;
            });

            // Enqueue items quickly, so that next item is likely to be enqueued before the previous callback finishes.
            for (int i = 0; i < itemsToProcess; i++)
                asyncQueue.Enqueue(i);

            // Wait a bit, but not long enough to process all items.
            await Task.Delay(200);

            asyncQueue.Dispose();
            Assert.True(itemsProcessed < itemsToProcess);
        }


        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.DequeueAsync(CancellationToken)"/> throws cancellation exception
        /// when the passed cancellation token is cancelled.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_AsyncQueue_DequeueCancellationAsync()
        {
            int itemsToProcess = 50;
            int itemsProcessed = 0;

            // Create a queue in blocking dequeue mode.
            var asyncQueue = this.asyncProvider.CreateAsyncQueue<int>();

            Task consumer = Task.Run(async () =>
            {
                using (var cts = new CancellationTokenSource(250))
                {
                    while (true)
                    {
                        try
                        {
                            int item = await asyncQueue.DequeueAsync(cts.Token);

                            await Task.Delay(this.random.Next(10));
                            itemsProcessed++;
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }
            });

            for (int i = 0; i < itemsToProcess; i++)
            {
                asyncQueue.Enqueue(i);
                await Task.Delay(this.random.Next(10) + 10);
            }

            // Check that the consumer task ended already.
            Assert.True(consumer.IsCompleted);

            asyncQueue.Dispose();

            // Check that not all items were processed.
            Assert.True(itemsProcessed < itemsToProcess);
        }

        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.DequeueAsync(CancellationToken)"/> provides items in correct order
        /// and that it throws cancellation exception when the queue is disposed.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_AsyncQueue_DequeueAndDisposeAsync()
        {
            int itemsToProcess = 50;

            // Create a queue in blocking dequeue mode.
            var asyncQueue = this.asyncProvider.CreateAsyncQueue<int>();

            // List of items collected by the consumer task.
            var list = new List<int>();

            Task consumer = Task.Run(async () =>
            {
                while (true)
                {
                    try
                    {
                        int item = await asyncQueue.DequeueAsync();

                        await Task.Delay(this.random.Next(10) + 1);

                        list.Add(item);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            });

            // Add half of the items slowly, so that the consumer is able to empty the queue.
            // Add the rest of the items very quickly, so that the consumer won't be able to process all of them.
            for (int i = 0; i < itemsToProcess; i++)
            {
                asyncQueue.Enqueue(i);

                if (i < itemsToProcess / 2)
                    await Task.Delay(this.random.Next(10) + 5);
            }

            // Give the consumer little more time to process couple more items.
            await Task.Delay(20);

            // Dispose the queue, which should cause the first consumer task to terminate.
            asyncQueue.Dispose();

            await consumer;

            // Check that the list contains items in correct order.
            for (int i = 0; i < list.Count - 1; i++)
                Assert.Equal(list[i] + 1, list[i + 1]);

            // Check that not all items were processed.
            Assert.True(list.Count < itemsToProcess);
        }

        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.DequeueAsync(CancellationToken)"/> throws cancellation exception
        /// if it is called after the queue was disposed.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_AsyncQueue_DequeueThrowsAfterDisposeAsync()
        {
            // Create a queue in blocking dequeue mode.
            var asyncQueue = this.asyncProvider.CreateAsyncQueue<int>();

            asyncQueue.Enqueue(1);

            asyncQueue.Dispose();

            await Assert.ThrowsAsync<OperationCanceledException>(async () => await asyncQueue.DequeueAsync());
        }

        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.DequeueAsync(CancellationToken)"/> blocks when the queue is empty.
        /// </summary>
        [Fact]
        public void AsyncProvider_AsyncQueue_DequeueBlocksOnEmptyQueue()
        {
            // Create a queue in blocking dequeue mode.
            var asyncQueue = this.asyncProvider.CreateAsyncQueue<int>();

            Assert.False(asyncQueue.DequeueAsync().Wait(100));
        }

        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.DequeueAsync(CancellationToken)"/> can be used by
        /// two different threads safely.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_AsyncQueue_DequeueParallelAsync()
        {
            int itemsToProcess = 50;

            // Create a queue in blocking dequeue mode.
            var asyncQueue = this.asyncProvider.CreateAsyncQueue<int>();

            // List of items collected by the consumer tasks.
            var list1 = new List<int>();
            var list2 = new List<int>();

            using (var cts = new CancellationTokenSource())
            {
                // We create two consumer tasks that compete for getting items from the queue.
                Task consumer1 = Task.Run(async () => await this.AsyncQueue_DequeueParallelAsync_WorkerAsync(asyncQueue, list1, itemsToProcess - 1, cts));
                Task consumer2 = Task.Run(async () => await this.AsyncQueue_DequeueParallelAsync_WorkerAsync(asyncQueue, list2, itemsToProcess - 1, cts));

                // Start adding the items.
                for (int i = 0; i < itemsToProcess; i++)
                {
                    asyncQueue.Enqueue(i);
                    await Task.Delay(this.random.Next(10));
                }

                // Wait until both consumers are finished.
                Task.WaitAll(consumer1, consumer2);
            }

            asyncQueue.Dispose();

            // Check that the lists contain items in correct order.
            for (int i = 0; i < list1.Count - 1; i++)
                Assert.True(list1[i] < list1[i + 1]);

            for (int i = 0; i < list2.Count - 1; i++)
                Assert.True(list2[i] < list2[i + 1]);

            // Check that the lists contain all items when merged.
            list1.AddRange(list2);
            list1.Sort();

            for (int i = 0; i < list1.Count - 1; i++)
                Assert.Equal(list1[i] + 1, list1[i + 1]);

            // Check that all items were processed.
            Assert.Equal(list1.Count, itemsToProcess);
        }

        /// <summary>
        /// Worker of <see cref="AsyncQueue_DequeueParallelAsync"/> test that tries to consume items from the queue
        /// until the last item is reached or cancellation is triggered.
        /// </summary>
        /// <param name="asyncQueue">Queue to consume items from.</param>
        /// <param name="list">List to add consumed items to.</param>
        /// <param name="lastItem">Value of the last item that will be added to the queue.</param>
        /// <param name="cts">Cancellation source to cancel when we are done.</param>
        /// <returns>The asynchronous task.</returns>
        private async Task AsyncQueue_DequeueParallelAsync_WorkerAsync(IAsyncQueue<int> asyncQueue, List<int> list, int lastItem, CancellationTokenSource cts)
        {
            while (true)
            {
                try
                {
                    int item = await asyncQueue.DequeueAsync(cts.Token);

                    await Task.Delay(this.random.Next(10));

                    list.Add(item);

                    // If we reached the last item, signal cancel to the other worker and finish.
                    if (item == lastItem)
                    {
                        cts.Cancel();
                        break;
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        /// <summary>
        /// Tests that <see cref="AsyncQueue{T}.Dispose"/> can be called from a callback.
        /// </summary>
        /// <returns>The asynchronous task.</returns>
        [Fact]
        public async Task AsyncProvider_AsyncQueue_CanDisposeFromCallback_Async()
        {
            bool firstRun = true;
            bool shouldBeFalse = false;

            var asyncQueue = this.asyncProvider.CreateAndRunAsyncDelegateDequeuer<IDisposable>(this.GetType().Name, (item, cancellation) =>
            {
                if (firstRun)
                {
                    item.Dispose();
                    firstRun = false;
                }
                else
                {
                    // This should not happen.
                    shouldBeFalse = true;
                }

                return Task.CompletedTask;
            });

            asyncQueue.Enqueue(asyncQueue);

            // We wait until the queue callback calling consumer is finished.
            await Task.Delay(this.random.Next(500));

            // Now enqueuing another item should not invoke the callback because the queue should be disposed.
            asyncQueue.Enqueue(asyncQueue);

            await Task.Delay(500);
            Assert.False(shouldBeFalse);
        }


        [Fact]
        public async Task AsyncProvider_AsyncLoop_ExceptionInLoopThrowsCriticalExceptionAsync()
        {
            var asyncLoop = this.asyncProvider.CreateAndRunAsyncLoop("TestLoop", token =>
            {
                throw new Exception("Exception Test.");
            }, CancellationToken.None);

            await asyncLoop.RunningTask;

            this.AssertLog<Exception>(this.mockLogger, LogLevel.Critical, "Exception Test.", "TestLoop threw an unhandled exception");
        }

        protected void AssertLog<T>(Mock<ILogger> logger, LogLevel logLevel, string exceptionMessage, string message) where T : Exception
        {
            logger
                .Setup(f => f.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), (Func<It.IsAnyType, Exception, string>)It.IsAny<object>()))
                .Callback(new InvocationAction(invocation =>
                {
                    if ((LogLevel)invocation.Arguments[0] == logLevel)
                    {
                        invocation.Arguments[2].ToString().Should().EndWith(message);
                        ((T)invocation.Arguments[3]).Message.Should().Be(exceptionMessage);
                    }
                }));
        }
    }
}
