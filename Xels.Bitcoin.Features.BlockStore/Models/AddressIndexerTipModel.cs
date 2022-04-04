using NBitcoin;
using Newtonsoft.Json;
using Xels.Bitcoin.Utilities.JsonConverters;

namespace Xels.Bitcoin.Features.BlockStore.Models
{
    public sealed class AddressIndexerTipModel
    {
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 TipHash { get; set; }

        public int? TipHeight { get; set; }
    }
}