﻿using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.Policy;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Networks;
using Xels.Features.Collateral.CounterChain;
using Xels.Sidechains.Networks;
using Xunit;

namespace Xels.Features.FederatedPeg.Tests
{
    public class TestStraxRegTest : StraxRegTest
    {
        internal TestStraxRegTest(Federation federation) : base()
        {
            this.Federations.RegisterFederation(federation);
            this.Name = "TestStraxRegTest";
        }
    }

    public class MofNMultiSigTests
    {
        [Fact]
        public void Eight_Of_Fifteen_SufficientlyFunded_With_Federation()
        {
            const int n = 15;
            const int m = 8;

            Key[] keys = new Key[n];

            Key ultimateReceiver = new Key();

            for (int i = 0; i < n; i++)
            {
                keys[i] = new Key();
            }

            Federation federation = new Federation(keys.Select(x => x.PubKey).ToArray());
            Network network = new TestStraxRegTest(federation);
            Script redeemScript = PayToFederationTemplate.Instance.GenerateScriptPubKey(federation.Id);

            const int inputCount = 50;
            const decimal fundingInputAmount = 100;
            const decimal fundingAmount = 99; // Must be less than fundingInputAmount.

            var multiSigCoins = new List<ICoin>();

            for (int i = 0; i < inputCount; i++)
            {
                var builder = new TransactionBuilder(network);

                // Build transactions to fund the multisig
                Transaction funding = builder
                    .AddCoins(GetCoinSource(keys[0], new[] { Money.Coins(fundingInputAmount) }))
                    .AddKeys(keys[0])
                    .Send(redeemScript.Hash, Money.Coins(fundingAmount))
                    .SetChange(keys[0].PubKey.Hash)
                    .SendFees(Money.Satoshis(5000))
                    .BuildTransaction(true);

                multiSigCoins.Add(ScriptCoin.Create(network, funding, funding.Outputs.To(redeemScript.Hash).First(), redeemScript));
            }

            var fedPegSettings = new FederatedPegSettings(new NodeSettings(network, args: new string[]
            {
                "mainchain",
                "redeemscript=" + redeemScript.ToString(),
                "publickey=" + keys[0].PubKey.ToHex(),
                "federationips=0.0.0.0"
            }), new CounterChainNetworkWrapper(CirrusNetwork.NetworksSelector.Regtest()));

            // Construct the withdrawal tx
            var txBuilder = new TransactionBuilder(network);
            Transaction tx = txBuilder
                .AddCoins(multiSigCoins)
                .AddKeys(keys.Take(m).ToArray())
                .Send(ultimateReceiver.PubKey.Hash, Money.Coins(inputCount * fundingAmount - 1))
                .SetChange(redeemScript.Hash)
                .SendFees(Money.Satoshis(400000))
                .BuildTransaction(true);

            bool verify = txBuilder.Verify(tx, out TransactionPolicyError[] errors);

            Assert.True(verify);
        }

        private static ICoin[] GetCoinSource(Key destination, params Money[] amounts)
        {
            return amounts
                .Select(a => new Coin(RandOutpoint(), new TxOut(a, destination.PubKey.Hash)))
                .ToArray();
        }

        private static OutPoint RandOutpoint()
        {
            return new OutPoint(new uint256(RandomUtils.GetBytes(32)), 0);
        }


        [Fact]
        public void Eight_Of_Fifteen_SufficientlyFunded()
        {
            var network = new StraxMain();

            const int n = 15;
            const int m = 8;

            Key[] keys = new Key[n];

            Key ultimateReceiver = new Key();

            for (int i = 0; i < n; i++)
            {
                keys[i] = new Key();
            }

            Script redeemScript = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(m, keys.Select(x => x.PubKey).ToArray());

            const int inputCount = 50;
            const decimal fundingInputAmount = 100;
            const decimal fundingAmount = 99; // Must be less than fundingInputAmount.

            var multiSigCoins = new List<ICoin>();

            for (int i = 0; i < inputCount; i++)
            {
                var builder = new TransactionBuilder(network);

                // Build transactions to fund the multisig
                Transaction funding = builder
                    .AddCoins(GetCoinSource(keys[0], new[] { Money.Coins(fundingInputAmount) }))
                    .AddKeys(keys[0])
                    .Send(redeemScript.Hash, Money.Coins(fundingAmount))
                    .SetChange(keys[0].PubKey.Hash)
                    .SendFees(Money.Satoshis(5000))
                    .BuildTransaction(true);

                multiSigCoins.Add(ScriptCoin.Create(network, funding, funding.Outputs.To(redeemScript.Hash).First(), redeemScript));
            }

            var fedPegSettings = new FederatedPegSettings(new NodeSettings(network, args: new string[]
            {
                "mainchain",
                "redeemscript=" + redeemScript.ToString(),
                "publickey=" + keys[0].PubKey.ToHex(),
                "federationips=0.0.0.0"
            }), new CounterChainNetworkWrapper(CirrusNetwork.NetworksSelector.Regtest()));

            // Construct the withdrawal tx
            var txBuilder = new TransactionBuilder(network);
            Transaction tx = txBuilder
                .AddCoins(multiSigCoins)
                .AddKeys(keys.Take(m).ToArray())
                .Send(ultimateReceiver.PubKey.Hash, Money.Coins(inputCount * fundingAmount - 1))
                .SetChange(redeemScript.Hash)
                .SendFees(Money.Coins(0.01m))
                .BuildTransaction(true);

            bool verify = txBuilder.Verify(tx, out TransactionPolicyError[] errors);

            Assert.True(verify);
        }
    }
}