﻿using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xels.Bitcoin.Controllers;
using Xels.Features.PoA.Collateral.CounterChain;
using Xunit;

namespace Xels.Features.FederatedPeg.Tests.RestClientsTests
{
    public class RestApiClientBaseTests
    {
        private readonly IHttpClientFactory httpClientFactory;

        private readonly ILoggerFactory loggerFactory;

        private readonly ILogger logger;

        public RestApiClientBaseTests()
        {
            this.loggerFactory = Substitute.For<ILoggerFactory>();
            this.logger = Substitute.For<ILogger>();
            this.loggerFactory.CreateLogger(null).ReturnsForAnyArgs(this.logger);
            this.httpClientFactory = new Bitcoin.Controllers.HttpClientFactory();
        }

        [Fact]
        public async Task TestRetriesCountAsync()
        {
            ICounterChainSettings federationSettings = Substitute.For<ICounterChainSettings>();

            var testClient = new TestRestApiClient(this.loggerFactory, federationSettings, this.httpClientFactory);

            HttpResponseMessage result = await testClient.CallThatWillAlwaysFail().ConfigureAwait(false);

            Assert.Equal(RestApiClientBase.RetryCount, testClient.RetriesCount);
            Assert.Equal(HttpStatusCode.InternalServerError, result.StatusCode);
        }
    }

    public class TestRestApiClient : RestApiClientBase
    {
        public int RetriesCount { get; private set; }

        public TestRestApiClient(ILoggerFactory loggerFactory, ICounterChainSettings settings, IHttpClientFactory httpClientFactory)
            : base(httpClientFactory, settings.CounterChainApiPort, "FederationGateway", "http://localhost")
        {
            this.RetriesCount = 0;
        }

        public Task<HttpResponseMessage> CallThatWillAlwaysFail()
        {
            return this.SendPostRequestAsync("stringModel", "nonExistentAPIMethod", CancellationToken.None);
        }

        protected override void OnRetry(Exception exception, TimeSpan delay)
        {
            this.RetriesCount++;
            base.OnRetry(exception, delay);
        }
    }
}
