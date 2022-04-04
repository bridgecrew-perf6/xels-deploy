namespace Xels.Bitcoin.Features.ExternalApi.Models
{
    /// <summary>
    /// CoinGecko price data as included in <see cref="CoinGeckoResponse"/>.
    /// </summary>
    public class CoinGeckoPriceData
    {
        /// <summary>
        /// The price in usd.
        /// </summary>
        public decimal usd { get; set; }
    }

    /// <summary>
    /// CoinGecko response.
    /// </summary>
    public class CoinGeckoResponse
    {
        /// <summary>
        /// The Xels price.
        /// </summary>
        public CoinGeckoPriceData xels { get; set; }

        /// <summary>
        /// The Ethereum price.
        /// </summary>
        public CoinGeckoPriceData ethereum { get; set; }
    }
}
