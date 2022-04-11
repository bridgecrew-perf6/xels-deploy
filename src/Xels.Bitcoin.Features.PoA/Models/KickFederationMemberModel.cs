using Newtonsoft.Json;

namespace Xels.Bitcoin.Features.PoA.Models
{
    public sealed class KickFederationMemberModel
    {
        [JsonProperty("pubkey")]
        public string PubKey { get; set; }
    }
}