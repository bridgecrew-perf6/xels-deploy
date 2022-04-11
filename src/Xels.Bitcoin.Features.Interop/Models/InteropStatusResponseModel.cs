using System.Collections.Generic;
using Newtonsoft.Json;

namespace Xels.Bitcoin.Features.Interop.Models
{
    public class InteropStatusResponseModel
    {
        [JsonProperty(PropertyName="mintRequests")]
        public List<ConversionRequestModel> MintRequests { get; set; }

        [JsonProperty(PropertyName = "burnRequests")]
        public List<ConversionRequestModel> BurnRequests { get; set; }

        [JsonProperty(PropertyName = "receivedVotes")]
        public Dictionary<string, List<string>> ReceivedVotes { get; set; }
    }
}
