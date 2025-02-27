﻿using System.Collections.Generic;
using Newtonsoft.Json;

namespace Xels.Features.FederatedPeg.Models
{
    public sealed class FederationMemberInfoModel
    {
        public FederationMemberInfoModel()
        {
            this.FederationMemberConnections = new List<FederationMemberConnectionInfo>();
        }

        [JsonProperty(PropertyName = "asyncLoopState")]
        public string AsyncLoopState { get; set; }

        [JsonProperty(PropertyName = "consensusHeight")]
        public int ConsensusHeight { get; set; }

        [JsonProperty(PropertyName = "cctsHeight")]
        public int CrossChainStoreHeight { get; set; }

        [JsonProperty(PropertyName = "cctsNextDepositHeight")]
        public int CrossChainStoreNextDepositHeight { get; set; }

        [JsonProperty(PropertyName = "cctsPartials")]
        public int CrossChainStorePartialTxs { get; set; }

        [JsonProperty(PropertyName = "cctsSuspended")]
        public int CrossChainStoreSuspendedTxs { get; set; }

        [JsonProperty(PropertyName = "federationWalletActive")]
        public bool FederationWalletActive { get; set; }

        [JsonProperty(PropertyName = "federationWalletHeight")]
        public int FederationWalletHeight { get; set; }

        [JsonProperty(PropertyName = "nodeVersion")]
        public string NodeVersion { get; set; }

        [JsonProperty(PropertyName = "pubKey")]
        public string PubKey { get; set; }

        [JsonProperty(PropertyName = "federationConnectionState")]
        public string FederationConnectionState { get; internal set; }

        public List<FederationMemberConnectionInfo> FederationMemberConnections { get; set; }
    }

    public sealed class FederationMemberConnectionInfo
    {
        [JsonProperty(PropertyName = "federationMemberIp")]
        public string FederationMemberIp { get; set; }

        [JsonProperty(PropertyName = "isConnected")]
        public bool Connected { get; set; }

        [JsonProperty(PropertyName = "isBanned")]
        public bool IsBanned { get; set; }
    }
}