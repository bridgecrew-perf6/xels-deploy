﻿using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;

namespace Xels.Features.FederatedPeg.Models
{
    public class FederationGatewayInfoModel
    {
        [JsonProperty(PropertyName = "active")]
        public bool IsActive { get; set; }

        [JsonProperty(PropertyName = "mainchain")]
        public bool IsMainChain { get; set; }

        [JsonProperty(PropertyName = "endpoints")]
        public IEnumerable<string> FederationNodeIpEndPoints { get; set; }

        [JsonProperty(PropertyName = "multisigPubKey")]
        public string MultisigPublicKey { get; set; }

        [JsonProperty(PropertyName = "federationMultisigPubKeys")]
        public IEnumerable<string> FederationMultisigPubKeys { get; set; }

        [JsonProperty(PropertyName = "miningPubKey", NullValueHandling = NullValueHandling.Ignore)]
        public string MiningPublicKey { get; set; }

        [JsonProperty(PropertyName = "federationMiningPubKeys", NullValueHandling = NullValueHandling.Ignore)]
        public IEnumerable<string> FederationMiningPubKeys { get; set; }

        [JsonProperty(PropertyName = "multisigAddress")]
        public BitcoinAddress MultiSigAddress { get; set; }

        [JsonProperty(PropertyName = "multisigRedeemScript")]
        public string MultiSigRedeemScript { get; set; }

        [JsonProperty(PropertyName = "multisigRedeemScriptPaymentScript")]
        public string MultiSigRedeemScriptPaymentScript { get; set; }

        [JsonProperty(PropertyName = "minconfsmalldeposits")]
        public uint MinimumDepositConfirmationsSmallDeposits { get; set; }

        [JsonProperty(PropertyName = "minconfnormaldeposits")]
        public uint MinimumDepositConfirmationsNormalDeposits { get; set; }

        [JsonProperty(PropertyName = "minconflargedeposits")]
        public uint MinimumDepositConfirmationsLargeDeposits { get; set; }

        [JsonProperty(PropertyName = "minconfdistributiondeposits")]
        public uint MinimumDepositConfirmationsDistributionDeposits { get; set; }
    }
}