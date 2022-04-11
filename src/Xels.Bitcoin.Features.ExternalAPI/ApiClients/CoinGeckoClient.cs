using System;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Xels.Bitcoin.Features.ExternalApi.Models;

namespace Xels.Bitcoin.Features.ExternalApi.ApiClients
{
    /// <summary>
    /// Retrieves price data from Coin Gecko.
    /// </summary>
    public class CoinGeckoClient : IDisposable
    {
        /// <summary>
        /// The user-agent used by this class when retrieving price data.
        /// </summary>
        public const string DummyUserAgent = "Mozilla/5.0 (Windows NT 6.3; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/81.0.4044.129 Safari/537.36";

        private readonly ExternalApiSettings externalApiSettings;
        private readonly HttpClient client;

        private decimal xelsPrice = -1;
        private decimal ethereumPrice = -1;

        /// <summary>
        /// Class constructor.
        /// </summary>
        /// <param name="externalApiSettings">The <see cref="ExternalApiSettings"/>.</param>
        public CoinGeckoClient(ExternalApiSettings externalApiSettings)
        {
            this.externalApiSettings = externalApiSettings;

            this.client = new HttpClient();
        }

        /// <summary>
        /// Gets the most recently retrieved Xels price.
        /// </summary>
        /// <returns>The Xels price.</returns>
        public decimal GetXelsPrice()
        {
            return this.xelsPrice;
        }

        /// <summary>
        /// Gets the most recently retrieved Ethereum price.
        /// </summary>
        /// <returns>The Ethereum price.</returns>
        public decimal GetEthereumPrice()
        {
            return this.ethereumPrice;
        }

        /// <summary>
        /// Retrieves price data for Xels and Ethereum from Coin Gecko.
        /// </summary>
        /// <returns>The <see cref="CoinGeckoResponse"/>.</returns>
        public async Task<CoinGeckoResponse> PriceDataRetrievalAsync()
        {
            var targetUri = new Uri(this.externalApiSettings.PriceUrl);
            var requestMessage = new HttpRequestMessage(HttpMethod.Get, targetUri);
            requestMessage.Headers.TryAddWithoutValidation("User-Agent", DummyUserAgent);

            HttpResponseMessage resp = await this.client.SendAsync(requestMessage).ConfigureAwait(false);
            string content = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);

            CoinGeckoResponse response = JsonConvert.DeserializeObject<CoinGeckoResponse>(content);

            if (response?.xels == null || response?.ethereum == null)
            {
                return null;
            }

            this.xelsPrice = response.xels.usd;
            this.ethereumPrice = response.ethereum.usd;

            return response;
        }

        /// <summary>
        /// Disposes instances of this class.
        /// </summary>
        public void Dispose()
        {
            this.client?.Dispose();
        }
    }
}
