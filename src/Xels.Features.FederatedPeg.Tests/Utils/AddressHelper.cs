﻿using System.Linq;
using NBitcoin;

namespace Xels.Features.FederatedPeg.Tests.Utils
{
    public class AddressHelper
    {
        public Network TargetChainNetwork { get; }

        public Network SourceChainNetwork { get; }

        public AddressHelper(Network targetChainNetwork, Network sourceChainNetwork)
        {
            this.TargetChainNetwork = targetChainNetwork;
            this.SourceChainNetwork = sourceChainNetwork;
        }

        public BitcoinPubKeyAddress GetNewSourceChainPubKeyAddress()
        {
            var key = new Key();
            BitcoinPubKeyAddress newAddress = this.TargetChainNetwork.CreateBitcoinSecret(key).GetAddress();
            return newAddress;
        }

        public BitcoinPubKeyAddress GetNewTargetChainPubKeyAddress()
        {
            var key = new Key();
            BitcoinPubKeyAddress newAddress = this.SourceChainNetwork.CreateBitcoinSecret(key).GetAddress();
            return newAddress;
        }

        public Script GetNewTargetChainPaymentScript()
        {
            var key = new Key();
            Script script = this.TargetChainNetwork.CreateBitcoinSecret(key).ScriptPubKey;
            return script;
        }

        public Script GetNewSourceChainPaymentScript()
        {
            var key = new Key();
            Script script = this.SourceChainNetwork.CreateBitcoinSecret(key).ScriptPubKey;
            return script;
        }
    }

    public class MultisigAddressHelper : AddressHelper
    {
        public const string Passphrase = "password";

        public Key[] MultisigPrivateKeys { get; }

        public Mnemonic[] MultisigMnemonics { get; }

        public Script PayToMultiSig { get; }

        public Script SourceChainPayToScriptHash { get; }

        public Script TargetChainPayToScriptHash { get; }

        public BitcoinAddress SourceChainMultisigAddress { get; }

        public BitcoinAddress TargetChainMultisigAddress { get; }

        public MultisigAddressHelper(Network targetChainNetwork, Network sourceChainNetwork, int quorum = 2, int sigCount = 3)
            : base(targetChainNetwork, sourceChainNetwork)
        {
            // TODO: This still needs some work.

            this.MultisigMnemonics = Enumerable.Range(0, sigCount)
                .Select(i => new Mnemonic(Wordlist.English, WordCount.Twelve))
                .ToArray();

            this.MultisigPrivateKeys = this.MultisigMnemonics
                .Select(m => m.DeriveExtKey(Passphrase).PrivateKey)
                .ToArray();

            FederationId federationId = targetChainNetwork.Federations.GetOnlyFederation().Id;
            this.PayToMultiSig = targetChainNetwork.Federations.GetOnlyFederation().MultisigScript;
            //PayToMultiSigTemplate.Instance.GenerateScriptPubKey(
            //quorum, this.MultisigPrivateKeys.Select(k => k.PubKey).ToArray());

            this.SourceChainMultisigAddress = this.PayToMultiSig.Hash.GetAddress(this.SourceChainNetwork);
            this.SourceChainPayToScriptHash = this.SourceChainMultisigAddress.ScriptPubKey;

            this.TargetChainMultisigAddress = this.PayToMultiSig.Hash.GetAddress(this.TargetChainNetwork);
            this.TargetChainPayToScriptHash = this.TargetChainMultisigAddress.ScriptPubKey;
        }
    }
}
