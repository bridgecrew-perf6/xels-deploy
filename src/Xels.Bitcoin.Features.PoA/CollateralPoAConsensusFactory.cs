﻿using System;
using System.Text;
using NBitcoin;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonConverters;

namespace Xels.Bitcoin.Features.PoA
{
    public class CollateralPoAConsensusFactory : PoAConsensusFactory
    {
        public override IFederationMember DeserializeFederationMember(byte[] serializedBytes)
        {
            string json = Encoding.ASCII.GetString(serializedBytes);

            CollateralFederationMemberModel model = Serializer.ToObject<CollateralFederationMemberModel>(json);

            var member = new CollateralFederationMember(new PubKey(model.PubKeyHex), false, new Money(model.CollateralAmountSatoshis), model.CollateralMainchainAddress);

            return member;
        }

        public override byte[] SerializeFederationMember(IFederationMember federationMember)
        {
            var member = federationMember as CollateralFederationMember;

            if (member == null)
                throw new ArgumentException($"Member of type: '{nameof(CollateralFederationMember)}' should be provided.");

            Guard.Assert(!member.IsMultisigMember);

            var model = new CollateralFederationMemberModel()
            {
                CollateralMainchainAddress = member.CollateralMainchainAddress,
                CollateralAmountSatoshis = member.CollateralAmount,
                PubKeyHex = member.PubKey.ToHex()
            };

            string json = Serializer.ToString(model);

            // Standardize the bytes produced as its often used in poll matching.
            if (Environment.NewLine != "\n")
                json = json.Replace(Environment.NewLine, "\n");

            byte[] jsonBytes = Encoding.ASCII.GetBytes(json);

            return jsonBytes;
        }
    }

    public class CollateralFederationMemberModel
    {
        public string PubKeyHex { get; set; }

        public long CollateralAmountSatoshis { get; set; }

        public string CollateralMainchainAddress { get; set; }
    }
}
