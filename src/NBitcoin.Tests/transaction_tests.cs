﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using NBitcoin.BitcoinCore;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using NBitcoin.OpenAsset;
using NBitcoin.Policy;
using NBitcoin.Stealth;
using Newtonsoft.Json.Linq;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace NBitcoin.Tests
{
    public class Transaction_tests
    {
        private readonly Network networkMain;
        private readonly ConsensusFactory consensusFactory;

        public Transaction_tests()
        {
            this.networkMain = KnownNetworks.Main;
            this.consensusFactory = this.networkMain.Consensus.ConsensusFactory;
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanParseOutpoint()
        {
            OutPoint outpoint = RandOutpoint();
            OutPoint actualOutpoint = CanParseOutpointCore(outpoint.ToString(), true);
            Assert.Equal(outpoint.Hash, actualOutpoint.Hash);
            Assert.Equal(outpoint.N, actualOutpoint.N);
            CanParseOutpointCore("abc-6", false);
            CanParseOutpointCore("bdaea31696b464c678c4bcc5d0565d58c86bb00c29f96bb86d1278c510d50aet-6", false);
            CanParseOutpointCore("bdaea31696b464c678c4bcc5d0565d58c86bb00c29f96bb86d1278c510d50aea-6", true);
            CanParseOutpointCore("bdaea31696b464c678c4bcc5d0565d58c86bb00c29f96bb86d1278c510d50aeaf-6", false);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanGetMedianBlock()
        {
            var chain = new ChainIndexer(this.networkMain);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            chain.SetTip(CreateBlock(now, 0, chain));
            chain.SetTip(CreateBlock(now, -1, chain));
            chain.SetTip(CreateBlock(now, 1, chain));
            Assert.Equal(CreateBlock(now, 0).Header.BlockTime, chain.Tip.GetMedianTimePast()); // x -1 0 1
            chain.SetTip(CreateBlock(now, 2, chain));
            Assert.Equal(CreateBlock(now, 0).Header.BlockTime, chain.Tip.GetMedianTimePast()); // x -1 0 1 2
            chain.SetTip(CreateBlock(now, 3, chain));
            Assert.Equal(CreateBlock(now, 1).Header.BlockTime, chain.Tip.GetMedianTimePast()); // x -1 0 1 2 3
            chain.SetTip(CreateBlock(now, 4, chain));
            chain.SetTip(CreateBlock(now, 5, chain));
            chain.SetTip(CreateBlock(now, 6, chain));
            chain.SetTip(CreateBlock(now, 7, chain));
            chain.SetTip(CreateBlock(now, 8, chain));

            Assert.Equal(CreateBlock(now, 3).Header.BlockTime, chain.Tip.GetMedianTimePast()); // x -1 0 1 2 3 4 5 6 7 8

            chain.SetTip(CreateBlock(now, 9, chain));
            Assert.Equal(CreateBlock(now, 4).Header.BlockTime, chain.Tip.GetMedianTimePast()); // x -1 0 1 2 3 4 5 6 7 8 9
            chain.SetTip(CreateBlock(now, 10, chain));
            Assert.Equal(CreateBlock(now, 5).Header.BlockTime, chain.Tip.GetMedianTimePast()); // x -1 0 1 2 3 4 5 6 7 8 9 10
        }

        private ChainedHeader CreateBlock(DateTimeOffset now, int offset, ChainIndexer chain = null)
        {
            Block block = this.networkMain.Consensus.ConsensusFactory.CreateBlock();
            block.Header.BlockTime = now + TimeSpan.FromMinutes(offset);

            if (chain != null)
            {
                block.Header.HashPrevBlock = chain.Tip.HashBlock;
                return new ChainedHeader(block.Header, block.Header.GetHash(), chain.Tip);
            }
            else
                return new ChainedHeader(block.Header, block.Header.GetHash(), 0);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanDetectFinalTransaction()
        {
            var tx = new Transaction();
            tx.Inputs.Add(new TxIn());
            tx.Inputs[0].Sequence = 1;
            Assert.True(tx.IsFinal(null));

            //Test on date, normal case
            tx.LockTime = new LockTime(new DateTimeOffset(2012, 8, 18, 0, 0, 0, TimeSpan.Zero));
            DateTimeOffset time = tx.LockTime.Date;
            Assert.False(tx.IsFinal(null));
            Assert.True(tx.IsFinal(time + TimeSpan.FromSeconds(1), 0));
            Assert.False(tx.IsFinal(time, 0));
            Assert.False(tx.IsFinal(time - TimeSpan.FromSeconds(1), 0));
            tx.Inputs[0].Sequence = uint.MaxValue;
            Assert.True(tx.IsFinal(time, 0));
            Assert.True(tx.IsFinal(time - TimeSpan.FromSeconds(1), 0));
            tx.Inputs[0].Sequence = 1;
            //////////

            //Test on heigh, normal case
            tx.LockTime = new LockTime(400);
            DateTimeOffset zero = Utils.UnixTimeToDateTime(0);
            Assert.False(tx.IsFinal(zero, 0));
            Assert.False(tx.IsFinal(zero, 400));
            Assert.True(tx.IsFinal(zero, 401));
            Assert.False(tx.IsFinal(zero, 399));
            //////////

            //Edge
            tx.LockTime = new LockTime(LockTime.LOCKTIME_THRESHOLD);
            time = tx.LockTime.Date;
            Assert.False(tx.IsFinal(null));
            Assert.True(tx.IsFinal(time + TimeSpan.FromSeconds(1), 0));
            Assert.False(tx.IsFinal(time, 0));
            Assert.False(tx.IsFinal(time - TimeSpan.FromSeconds(1), 0));
            tx.Inputs[0].Sequence = uint.MaxValue;
            Assert.True(tx.IsFinal(time, 0));
            Assert.True(tx.IsFinal(time - TimeSpan.FromSeconds(1), 0));
            tx.Inputs[0].Sequence = 1;
            //////////
        }

        private OutPoint CanParseOutpointCore(string str, bool valid)
        {
            try
            {
                OutPoint result = OutPoint.Parse(str);
                Assert.True(valid);
                return result;
            }
            catch
            {
                Assert.False(valid);
                return null;
            }
        }
        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanExtractTxOutDestinationEasily()
        {
            var secret = new BitcoinSecret("KyJTjvFpPF6DDX4fnT56d2eATPfxjdUPXFFUb85psnCdh34iyXRQ");

            var tx = new Transaction();
            var p2pkh = new TxOut(new Money((ulong)45000000), secret.GetAddress());
            var p2pk = new TxOut(new Money((ulong)80000000), secret.PrivateKey.PubKey);

            tx.AddOutput(p2pkh);
            tx.AddOutput(p2pk);

            Assert.False(p2pkh.IsTo(secret.PrivateKey.PubKey));
            Assert.True(p2pkh.IsTo(secret.GetAddress()));
            Assert.True(p2pk.IsTo(secret.PrivateKey.PubKey));
            Assert.False(p2pk.IsTo(secret.GetAddress()));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSignTransaction()
        {
            var key = new Key();
            Script scriptPubKey = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(key.PubKey);

            Transaction tx = this.networkMain.CreateTransaction();
            tx.AddInput(new TxIn(new OutPoint(tx.GetHash(), 0))
            {
                ScriptSig = scriptPubKey
            });
            tx.AddInput(new TxIn(new OutPoint(tx.GetHash(), 1))
            {
                ScriptSig = scriptPubKey
            });

            tx.AddOutput(new TxOut("21", key.PubKey.Hash));

            Transaction clone = this.networkMain.CreateTransaction(tx.ToBytes());
            tx.Sign(this.networkMain, key, false);
            AssertCorrectlySigned(tx, scriptPubKey);
            clone.Sign(this.networkMain, key, true);
            AssertCorrectlySigned(clone, scriptPubKey.Hash.ScriptPubKey);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSelectCoin()
        {
            var selector = new DefaultCoinSelector(0);
            Assert.Null(selector.Select(new ICoin[] { CreateCoin("9") }, Money.Parse("10.0")));
            Assert.NotNull(selector.Select(new ICoin[] { CreateCoin("9"), CreateCoin("1") }, Money.Parse("10.0")));
            Assert.NotNull(selector.Select(new ICoin[] { CreateCoin("10.0") }, Money.Parse("10.0")));
            Assert.NotNull(selector.Select(new ICoin[]
            {
                CreateCoin("5.0"),
                CreateCoin("4.0"),
                CreateCoin("11.0"),
            }, Money.Parse("10.0")));

            Assert.NotNull(selector.Select(new ICoin[]
            {
                CreateCoin("3.0"),
                CreateCoin("3.0"),
                CreateCoin("3.0"),
                CreateCoin("3.0"),
                CreateCoin("3.0")
            }, Money.Parse("10.0")));

            // Should spend all coins belonging to same scriptPubKey
            Script bob = new Key().ScriptPubKey;
            Script alice = new Key().ScriptPubKey;
            ICoin[] selected = selector.Select(new ICoin[] { CreateCoin("5", bob), CreateCoin("5", bob) }, Money.Parse("2.0")).ToArray();
            Assert.Equal(2, selected.Length);

            selected = selector.Select(new ICoin[] { CreateCoin("5", alice), CreateCoin("5", bob) }, Money.Parse("2.0")).ToArray();
            Assert.Single(selected);
        }

        // These 2 next tests cater for the specific situation where the DefaultCoinSelector
        // performs a random sampling of coins in an attempt to get as close as possible to 
        // the required total.
        // Previously this code wasn't functioning correctly and running virtually forever
        // for large input sizes.

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSelectCoinsInReasonableTimeDifferentScriptPubKeys()
        {
            var selector = new DefaultCoinSelector(0);

            var coins = new ICoin[2000];

            for(int i = 0; i < coins.Length; i++)
            {
                coins[i] = this.CreateCoin("1.0", new Key().ScriptPubKey);
            }

            IEnumerable<ICoin> result = selector.Select(coins, Money.Parse("1.5"));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSelectCoinsInReasonableTimeNoGroupByScriptPubKey()
        {
            var selector = new DefaultCoinSelector(0)
            {
                GroupByScriptPubKey = false
            };

            var testCoins = new ICoin[2000];

            for (int i = 0; i < testCoins.Length; i++)
            {
                testCoins[i] = this.CreateCoin("1.0");
            }

            IEnumerable<ICoin> result = selector.Select(testCoins, Money.Parse("1.5"));
        }

        private Coin CreateCoin(Money amount, Script scriptPubKey = null)
        {
            return new Coin(new OutPoint(Rand(), 0), new TxOut()
            {
                Value = amount,
                ScriptPubKey = scriptPubKey
            });
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanBuildIssueColoredCoinWithMultiSigP2SH()
        {
            var satoshi = new Key();
            var bob = new Key();
            var alice = new Key();

            Script goldRedeem = PayToMultiSigTemplate.Instance
                                    .GenerateScriptPubKey(2, new[] { satoshi.PubKey, bob.PubKey, alice.PubKey });

            Script goldScriptPubKey = goldRedeem.Hash.ScriptPubKey;
            AssetId goldAssetId = goldScriptPubKey.Hash.ToAssetId();

            var issuanceCoin = new IssuanceCoin(
                new ScriptCoin(RandOutpoint(), new TxOut(new Money(2880), goldScriptPubKey), goldRedeem));

            var nico = new Key();

            Transaction bobSigned =
                new TransactionBuilder(this.networkMain)
                .AddCoins(issuanceCoin)
                .AddKeys(bob)
                .IssueAsset(nico.PubKey, new AssetMoney(goldAssetId, 1000))
                .SetChange(bob)
                .BuildTransaction(true);

            Transaction aliceSigned =
                new TransactionBuilder(this.networkMain)
                    .AddCoins(issuanceCoin)
                    .AddKeys(alice)
                    .SignTransaction(bobSigned);

            Assert.True(
                new TransactionBuilder(this.networkMain)
                {
                    StandardTransactionPolicy = EasyPolicy
                }
                    .AddCoins(issuanceCoin)
                    .Verify(aliceSigned));

            //In one two one line

            var builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = RelayPolicy.Clone();
            builder.StandardTransactionPolicy.CheckFee = false;
            Transaction tx =
                builder
                .AddCoins(issuanceCoin)
                .AddKeys(alice, satoshi)
                .IssueAsset(nico.PubKey, new AssetMoney(goldAssetId, 1000))
                .SetChange(alice)
                .BuildTransaction(true);
            Assert.True(builder.Verify(tx));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanGuessRedeemScriptWithInputKeys()
        {
            var k = new Key();

            //This gives you a Bech32 address (currently not really interoperable in wallets, so you need to convert it into P2SH)
            BitcoinAddress address = k.PubKey.WitHash.GetAddress(this.networkMain);
            BitcoinScriptAddress p2sh = address.GetScriptAddress();
            //p2sh is now an interoperable P2SH segwit address

            //For spending, it works the same as a a normal P2SH
            //You need to get the ScriptCoin, the RedeemScript of you script coin should be k.PubKey.WitHash.ScriptPubKey.

            Coin[] coins =
                //Get coins from any block explorer.
                GetCoins(p2sh)
                    //Nobody knows your redeem script, so you add here the information
                    //This line is actually optional since 4.0.0.38, as the TransactionBuilder is smart enough to figure out
                    //the redeems from the keys added by AddKeys.
                    //However, explicitely having the redeem will make code more easy to update to other payment like 2-2
                    //.Select(c => c.ToScriptCoin(k.PubKey.WitHash.ScriptPubKey))
                    .ToArray();

            var builder = new TransactionBuilder(this.networkMain);
            builder.AddCoins(coins);
            builder.AddKeys(k);
            builder.Send(new Key().ScriptPubKey, Money.Coins(1));
            builder.SendFees(Money.Coins(0.001m));
            builder.SetChange(p2sh);
            Transaction signedTx = builder.BuildTransaction(true);
            Assert.True(builder.Verify(signedTx));
        }

        private Coin[] GetCoins(BitcoinScriptAddress p2sh)
        {
            return new Coin[] { new Coin(new uint256(Enumerable.Range(0, 32).Select(i => (byte)0xaa).ToArray()), 0, Money.Coins(2.0m), p2sh.ScriptPubKey) };
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        //https://github.com/NicolasDorier/NBitcoin/issues/34
        public void CanBuildAnyoneCanPayTransaction()
        {
            //Carla is buying from Alice. Bob is acting as a mediator between Alice and Carla.
            var aliceKey = new Key();
            var bobKey = new Key();
            var carlaKey = new Key();

            // Alice + Bob 2 of 2 multisig "wallet"
            Script aliceBobRedeemScript = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, new PubKey[] { aliceKey.PubKey, bobKey.PubKey });

            var txBuilder = new TransactionBuilder(this.networkMain);
            Transaction funding = txBuilder
                .AddCoins(GetCoinSource(aliceKey))
                .AddKeys(aliceKey)
                .Send(aliceBobRedeemScript.Hash, "0.5")
                .SetChange(aliceKey.PubKey.Hash)
                .SendFees(Money.Satoshis(5000))
                .BuildTransaction(true);

            Assert.True(txBuilder.Verify(funding));

            var aliceBobCoins = new List<ICoin>();
            aliceBobCoins.Add(new ScriptCoin(funding, funding.Outputs.To(aliceBobRedeemScript.Hash).First(), aliceBobRedeemScript));

            // first Bob constructs the TX
            txBuilder = new TransactionBuilder(this.networkMain);
            Transaction unsigned = txBuilder
                // spend from the Alice+Bob wallet to Carla
                .AddCoins(aliceBobCoins)
                .Send(carlaKey.PubKey.Hash, "0.01")
                //and Carla pays Alice
                .Send(aliceKey.PubKey.Hash, "0.02")
                .CoverOnly("0.01")
                .SetChange(aliceBobRedeemScript.Hash)
                // Bob does not sign anything yet
                .BuildTransaction(false);

            Assert.True(unsigned.Outputs.Count == 3);
            Assert.True(unsigned.Outputs[0].IsTo(aliceBobRedeemScript.Hash));
            //Only 0.01 should be covered, not 0.03 so 0.49 goes back to Alice+Bob
            Assert.True(unsigned.Outputs[0].Value == Money.Parse("0.49"));


            Assert.True(unsigned.Outputs[1].IsTo(carlaKey.PubKey.Hash));
            Assert.True(unsigned.Outputs[1].Value == Money.Parse("0.01"));

            Assert.True(unsigned.Outputs[2].IsTo(aliceKey.PubKey.Hash));
            Assert.True(unsigned.Outputs[2].Value == Money.Parse("0.02"));

            //Alice signs    
            txBuilder = new TransactionBuilder(this.networkMain);
            Transaction aliceSigned = txBuilder
                    .AddCoins(aliceBobCoins)
                    .AddKeys(aliceKey)
                    .SignTransaction(unsigned, SigHash.All | SigHash.AnyoneCanPay);

            ICoin[] carlaCoins = GetCoinSource(carlaKey, "1.0", "0.8", "0.6", "0.2", "0.05");

            //Scenario 1 : Carla knows aliceBobCoins so she can calculate how much coin she need to complete the transaction
            //Carla fills and signs
            txBuilder = new TransactionBuilder(this.networkMain);
            ((DefaultCoinSelector)txBuilder.CoinSelector).GroupByScriptPubKey = false;
            Transaction carlaSigned = txBuilder
                .AddCoins(aliceBobCoins)
                .Then()
                .AddKeys(carlaKey)
                //Carla should complete 0.02, but with 0.03 of fees, she should have a coins of 0.05
                .AddCoins(carlaCoins)
                .ContinueToBuild(aliceSigned)
                .SendFees("0.03")
                .CoverTheRest()
                .BuildTransaction(true);


            //Bob review and signs
            txBuilder = new TransactionBuilder(this.networkMain);
            Transaction bobSigned = txBuilder
                .AddCoins(aliceBobCoins)
                .AddKeys(bobKey)
                .SignTransaction(carlaSigned);

            txBuilder.AddCoins(carlaCoins);
            Assert.True(txBuilder.Verify(bobSigned));


            //Scenario 2 : Carla is told by Bob to complete 0.05 BTC
            //Carla fills and signs
            txBuilder = new TransactionBuilder(this.networkMain);
            ((DefaultCoinSelector)txBuilder.CoinSelector).GroupByScriptPubKey = false;
            carlaSigned = txBuilder
                .AddKeys(carlaKey)
                .AddCoins(carlaCoins)
                //Carla should complete 0.02, but with 0.03 of fees, she should have a coins of 0.05
                .ContinueToBuild(aliceSigned)
                .CoverOnly("0.05")
                .BuildTransaction(true);


            //Bob review and signs
            txBuilder = new TransactionBuilder(this.networkMain);
            bobSigned = txBuilder
                .AddCoins(aliceBobCoins)
                .AddKeys(bobKey)
                .SignTransaction(carlaSigned);

            txBuilder.AddCoins(carlaCoins);
            Assert.True(txBuilder.Verify(bobSigned));
        }

        private ICoin[] GetCoinSource(Key destination, params Money[] amounts)
        {
            if (amounts.Length == 0)
                amounts = new[] { Money.Parse("100.0") };

            return amounts
                .Select(a => new Coin(RandOutpoint(), new TxOut(a, destination.PubKey.Hash)))
                .ToArray();
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanPrecomputeHashes()
        {
            Transaction tx = this.networkMain.CreateTransaction();
            tx.AddInput(new TxIn(RandomCoin(Money.Coins(1.0m), new Key()).Outpoint, Script.Empty));
            tx.Inputs[0].WitScript = new WitScript(Op.GetPushOp(3));
            tx.AddOutput(RandomCoin(Money.Coins(1.0m), new Key()).TxOut);
            Transaction template = this.networkMain.CreateTransaction(tx.ToBytes());

            // If lazy is true, then the cache will be calculated later
            tx = this.networkMain.CreateTransaction(template.ToBytes());
            var initialHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            tx.PrecomputeHash(false, true);
            tx.Outputs[0].Value = Money.Coins(1.1m);
            var afterHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            Assert.NotEqual(initialHashes[0], afterHashes[0]);
            Assert.NotEqual(initialHashes[1], afterHashes[1]);
            Assert.NotEqual(afterHashes[1], afterHashes[0]);
            /////

            // If lazy is false, then the cache is calculated now
            tx = this.networkMain.CreateTransaction(template.ToBytes());
            initialHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            tx.PrecomputeHash(false, false);
            tx.Outputs[0].Value = Money.Coins(1.1m);
            afterHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            // The hash should be outdated, because they were not calculated during second call to tx.GetHash()
            Assert.Equal(initialHashes[0], afterHashes[0]);
            Assert.Equal(initialHashes[1], afterHashes[1]);
            Assert.NotEqual(afterHashes[1], afterHashes[0]);
            /////

            // If invalidExisting is false, then the cache is not recalculated
            tx = this.networkMain.CreateTransaction(template.ToBytes());
            initialHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            tx.PrecomputeHash(false, false);
            tx.Outputs[0].Value = Money.Coins(1.1m);
            afterHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            // Out of date...
            Assert.Equal(initialHashes[0], afterHashes[0]);
            Assert.Equal(initialHashes[1], afterHashes[1]);
            tx.PrecomputeHash(false, false);
            // Always out of date...
            var afterHashes2 = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            Assert.Equal(afterHashes2[0], afterHashes[0]);
            Assert.Equal(afterHashes2[1], afterHashes[1]);
            Assert.NotEqual(afterHashes2[1], afterHashes2[0]);
            ///////

            // If invalidExisting is true, then the cache is recalculated
            tx = this.networkMain.CreateTransaction(template.ToBytes());
            initialHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            tx.PrecomputeHash(false, false);
            tx.Outputs[0].Value = Money.Coins(1.1m);
            afterHashes = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            // Out of date...
            Assert.Equal(initialHashes[0], afterHashes[0]);
            Assert.Equal(initialHashes[1], afterHashes[1]);
            tx.PrecomputeHash(true, false);
            // Not out of date anymore...
            afterHashes2 = new uint256[] { tx.GetHash(), tx.GetWitHash() };
            Assert.NotEqual(afterHashes2[0], afterHashes[0]);
            Assert.NotEqual(afterHashes2[1], afterHashes[1]);
            ///////
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanBuildShuffleColoredTransaction()
        {
            var gold = new Key();
            var silver = new Key();
            AssetId goldId = gold.PubKey.ScriptPubKey.Hash.ToAssetId();
            AssetId silverId = silver.PubKey.ScriptPubKey.Hash.ToAssetId();

            var satoshi = new Key();
            var bob = new Key();

            var repo = new NoSqlColoredTransactionRepository(this.networkMain);

            var init = new Transaction()
            {
                Outputs =
                {
                    new TxOut("1.0", gold.PubKey),
                    new TxOut("1.0", silver.PubKey),
                    new TxOut("1.0", satoshi.PubKey)
                }
            };
            repo.Transactions.Put(init.GetHash(), init);

            ICoin[] issuanceCoins =
                init
                .Outputs
                .Take(2)
                .Select((o, i) => new IssuanceCoin(new OutPoint(init.GetHash(), i), init.Outputs[i]))
                .OfType<ICoin>().ToArray();

            var satoshiBTC = new Coin(new OutPoint(init.GetHash(), 2), init.Outputs[2]);

            var coins = new List<ICoin>();
            coins.AddRange(issuanceCoins);
            var txBuilder = new TransactionBuilder(1, this.networkMain);
            txBuilder.StandardTransactionPolicy = RelayPolicy;
            //Can issue gold to satoshi and bob
            Transaction tx = txBuilder
                .AddCoins(coins.ToArray())
                .AddKeys(gold)
                .IssueAsset(satoshi.PubKey, new AssetMoney(goldId, 1000))
                .IssueAsset(bob.PubKey, new AssetMoney(goldId, 500))
                .SendFees("0.1")
                .SetChange(gold.PubKey)
                .BuildTransaction(true);
            Assert.True(txBuilder.Verify(tx, "0.1"));

            //Ensure BTC from the IssuanceCoin are returned
            Assert.Equal(Money.Parse("0.89998848"), tx.Outputs[2].Value);
            Assert.Equal(gold.PubKey.ScriptPubKey, tx.Outputs[2].ScriptPubKey);

            //Can issue and send in same transaction
            repo.Transactions.Put(tx.GetHash(), tx);


            IEnumerable<ColoredCoin> cc = ColoredCoin.Find(tx, repo);
            for (int i = 0; i < 20; i++)
            {
                txBuilder = new TransactionBuilder(i, this.networkMain);
                txBuilder.StandardTransactionPolicy = RelayPolicy;
                tx = txBuilder
                    .AddCoins(satoshiBTC)
                    .AddCoins(cc)
                    .AddKeys(satoshi)
                    .SendAsset(gold, new AssetMoney(goldId, 10))
                    .SetChange(satoshi)
                    .Then()
                    .AddKeys(gold)
                    .AddCoins(issuanceCoins)
                    .IssueAsset(bob, new AssetMoney(goldId, 1))
                    .SetChange(gold)
                    .Shuffle()
                    .BuildTransaction(true);

                repo.Transactions.Put(tx.GetHash(), tx);

                ColoredTransaction ctx = tx.GetColoredTransaction(repo);
                Assert.Single(ctx.Issuances);
                Assert.Equal(2, ctx.Transfers.Count);
            }
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanBuildColoredTransaction()
        {
            var gold = new Key();
            var silver = new Key();
            AssetId goldId = gold.PubKey.ScriptPubKey.Hash.ToAssetId();
            AssetId silverId = silver.PubKey.ScriptPubKey.Hash.ToAssetId();

            var satoshi = new Key();
            var bob = new Key();
            var alice = new Key();

            var repo = new NoSqlColoredTransactionRepository(this.networkMain);

            var init = new Transaction()
            {
                Outputs =
                {
                    new TxOut("1.0", gold.PubKey),
                    new TxOut("1.0", silver.PubKey),
                    new TxOut("1.0", satoshi.PubKey)
                }
            };

            repo.Transactions.Put(init);

            ICoin[] issuanceCoins =
                init
                .Outputs
                .AsCoins()
                .Take(2)
                .Select((c, i) => new IssuanceCoin(c))
                .OfType<ICoin>().ToArray();

            Coin satoshiBTC = init.Outputs.AsCoins().Last();

            var coins = new List<ICoin>();
            coins.AddRange(issuanceCoins);
            var txBuilder = new TransactionBuilder(this.networkMain);
            txBuilder.StandardTransactionPolicy = RelayPolicy;
            //Can issue gold to satoshi and bob
            Transaction tx = txBuilder
                .AddCoins(coins.ToArray())
                .AddKeys(gold)
                .IssueAsset(satoshi.PubKey, new AssetMoney(goldId, 1000))
                .IssueAsset(bob.PubKey, new AssetMoney(goldId, 500))
                .SendFees("0.1")
                .SetChange(gold.PubKey)
                .BuildTransaction(true);
            Assert.True(txBuilder.Verify(tx, "0.1"));

            //Ensure BTC from the IssuanceCoin are returned
            Assert.Equal(Money.Parse("0.89998848"), tx.Outputs[2].Value);
            Assert.Equal(gold.PubKey.ScriptPubKey, tx.Outputs[2].ScriptPubKey);

            repo.Transactions.Put(tx);

            ColoredTransaction colored = tx.GetColoredTransaction(repo);
            Assert.Equal(2, colored.Issuances.Count);
            Assert.True(colored.Issuances.All(i => i.Asset.Id == goldId));
            AssertHasAsset(tx, colored, colored.Issuances[0], goldId, 500, bob.PubKey);
            AssertHasAsset(tx, colored, colored.Issuances[1], goldId, 1000, satoshi.PubKey);

            ColoredCoin[] coloredCoins = ColoredCoin.Find(tx, colored).ToArray();
            Assert.Equal(2, coloredCoins.Length);

            //Can issue silver to bob, and send some gold to satoshi
            coins.Add(coloredCoins.First(c => c.ScriptPubKey == bob.PubKey.ScriptPubKey));
            txBuilder = new TransactionBuilder(this.networkMain);
            txBuilder.StandardTransactionPolicy = EasyPolicy;
            tx = txBuilder
                .AddCoins(coins.ToArray())
                .AddKeys(silver, bob)
                .SetChange(bob.PubKey)
                .IssueAsset(bob.PubKey, new AssetMoney(silverId, 10))
                .SendAsset(satoshi.PubKey, new AssetMoney(goldId, 30))
                .BuildTransaction(true);

            Assert.True(txBuilder.Verify(tx));
            colored = tx.GetColoredTransaction(repo);
            Assert.Single(colored.Inputs);
            Assert.Equal(goldId, colored.Inputs[0].Asset.Id);
            Assert.Equal(500, colored.Inputs[0].Asset.Quantity);

            Assert.Single(colored.Issuances);
            Assert.Equal(2, colored.Transfers.Count);
            AssertHasAsset(tx, colored, colored.Transfers[0], goldId, 470, bob.PubKey);
            AssertHasAsset(tx, colored, colored.Transfers[1], goldId, 30, satoshi.PubKey);

            repo.Transactions.Put(tx);


            //Can swap : 
            //satoshi wants to send 100 gold to bob 
            //bob wants to send 200 silver, 5 gold and 0.9 BTC to satoshi

            //Satoshi receive gold
            txBuilder = new TransactionBuilder(this.networkMain);
            txBuilder.StandardTransactionPolicy = RelayPolicy;
            tx = txBuilder
                    .AddKeys(gold)
                    .AddCoins(issuanceCoins)
                    .IssueAsset(satoshi.PubKey, new AssetMoney(goldId, 1000UL))
                    .SetChange(gold.PubKey)
                    .SendFees(Money.Coins(0.0004m))
                    .BuildTransaction(true);
            Assert.True(txBuilder.Verify(tx));
            repo.Transactions.Put(tx);
            ColoredCoin satoshiCoin = ColoredCoin.Find(tx, repo).First();


            //Gold receive 2.5 BTC
            tx = new Transaction()
            {
                Outputs =
                {
                    new TxOut("2.5",gold.PubKey)
                }
            };
            repo.Transactions.Put(tx.GetHash(), tx);

            //Bob receive silver and 2 btc
            txBuilder = new TransactionBuilder(this.networkMain);
            txBuilder.StandardTransactionPolicy = RelayPolicy;
            tx = txBuilder
                    .AddKeys(silver, gold)
                    .AddCoins(issuanceCoins)
                    .AddCoins(new Coin(new OutPoint(tx.GetHash(), 0), new TxOut("2.5", gold.PubKey.ScriptPubKey)))
                    .IssueAsset(bob.PubKey, new AssetMoney(silverId, 300UL))
                    .Send(bob.PubKey, "2.00")
                    .SendFees(Money.Coins(0.0004m))
                    .SetChange(gold.PubKey)
                    .BuildTransaction(true);
            Assert.True(txBuilder.Verify(tx));
            repo.Transactions.Put(tx);

            ColoredCoin bobSilverCoin = ColoredCoin.Find(tx, repo).First();
            var bobBitcoin = new Coin(new OutPoint(tx.GetHash(), 2), tx.Outputs[2]);

            //Bob receive gold
            txBuilder = new TransactionBuilder(this.networkMain);
            txBuilder.StandardTransactionPolicy = RelayPolicy;
            tx = txBuilder
                    .AddKeys(gold)
                    .AddCoins(issuanceCoins)
                    .IssueAsset(bob.PubKey, new AssetMoney(goldId, 50UL))
                    .SetChange(gold.PubKey)
                    .SendFees(Money.Coins(0.0004m))
                    .BuildTransaction(true);
            Assert.True(txBuilder.Verify(tx));
            repo.Transactions.Put(tx.GetHash(), tx);

            ColoredCoin bobGoldCoin = ColoredCoin.Find(tx, repo).First();

            txBuilder = new TransactionBuilder(this.networkMain);
            txBuilder.StandardTransactionPolicy = RelayPolicy;
            tx = txBuilder
                .AddCoins(satoshiCoin)
                .AddCoins(satoshiBTC)
                .SendAsset(bob.PubKey, new AssetMoney(goldId, 100))
                .SendFees(Money.Coins(0.0004m))
                .SetChange(satoshi.PubKey)
                .Then()
                .AddCoins(bobSilverCoin, bobGoldCoin, bobBitcoin)
                .SendAsset(satoshi.PubKey, new AssetMoney(silverId, 200))
                .Send(satoshi.PubKey, "0.9")
                .SendAsset(satoshi.PubKey, new AssetMoney(goldId, 5))
                .SetChange(bob.PubKey)
                .BuildTransaction(false);

            colored = tx.GetColoredTransaction(repo);

            AssertHasAsset(tx, colored, colored.Inputs[0], goldId, 1000, null);
            AssertHasAsset(tx, colored, colored.Inputs[1], silverId, 300, null);

            AssertHasAsset(tx, colored, colored.Transfers[0], goldId, 900, satoshi.PubKey);
            AssertHasAsset(tx, colored, colored.Transfers[1], goldId, 100, bob.PubKey);

            AssertHasAsset(tx, colored, colored.Transfers[2], silverId, 100, bob.PubKey);
            AssertHasAsset(tx, colored, colored.Transfers[3], silverId, 200, satoshi.PubKey);

            AssertHasAsset(tx, colored, colored.Transfers[4], goldId, 45, bob.PubKey);
            AssertHasAsset(tx, colored, colored.Transfers[5], goldId, 5, satoshi.PubKey);

            Assert.True(tx.Outputs[8].Value == Money.Parse("1.09998848"));
            Assert.True(tx.Outputs[8].ScriptPubKey == bob.PubKey.ScriptPubKey);
            Assert.True(tx.Outputs[9].Value == Money.Parse("0.9"));
            Assert.True(tx.Outputs[9].ScriptPubKey == satoshi.PubKey.ScriptPubKey);

            tx = txBuilder.AddKeys(satoshi, bob).SignTransaction(tx);
            Assert.True(txBuilder.Verify(tx));


            //Bob send coins to Satoshi, but alice pay for the dust
            Transaction funding =
                new TransactionBuilder(this.networkMain)
                {
                    StandardTransactionPolicy = RelayPolicy
                }
                .AddCoins(issuanceCoins)
                .AddKeys(gold)
                .IssueAsset(bob.PubKey.Hash, new AssetMoney(goldId, 100UL))
                .SetChange(gold.PubKey.Hash)
                .SendFees(Money.Coins(0.0004m))
                .BuildTransaction(true);

            repo.Transactions.Put(funding);

            ColoredCoin[] bobGold = ColoredCoin.Find(funding, repo).ToArray();

            Transaction transfer = null;
            try
            {
                transfer =
                    new TransactionBuilder(this.networkMain)
                    {
                        StandardTransactionPolicy = RelayPolicy
                    }
                    .AddCoins(bobGold)
                    .SendAsset(alice.PubKey.Hash, new AssetMoney(goldId, 40UL))
                    .SetChange(bob.PubKey.Hash)
                    .BuildTransaction(true);
                Assert.False(true, "Should have thrown");
            }
            catch (NotEnoughFundsException ex) //Not enough dust to send the change
            {
                Assert.True(((Money)ex.Missing).Satoshi == 546);
                var rate = new FeeRate(Money.Coins(0.0004m));
                txBuilder = new TransactionBuilder(this.networkMain);
                txBuilder.StandardTransactionPolicy = RelayPolicy;
                transfer =
                    txBuilder
                    .AddCoins(bobGold)
                    .AddCoins(((IssuanceCoin)issuanceCoins[0]).Bearer)
                    .AddKeys(gold, bob)
                    .SendAsset(alice.PubKey, new AssetMoney(goldId, 40UL))
                    .SetChange(bob.PubKey, ChangeType.Colored)
                    .SetChange(gold.PubKey.Hash, ChangeType.Uncolored)
                    .SendEstimatedFees(rate)
                    .BuildTransaction(true);
                Money fee = transfer.GetFee(txBuilder.FindSpentCoins(transfer));
                Assert.True(txBuilder.Verify(transfer, fee));

                repo.Transactions.Put(funding.GetHash(), funding);

                colored = ColoredTransaction.FetchColors(transfer, repo);
                AssertHasAsset(transfer, colored, colored.Transfers[0], goldId, 60, bob.PubKey);
                AssertHasAsset(transfer, colored, colored.Transfers[1], goldId, 40, alice.PubKey);

                TxOut change = transfer.Outputs.Last(o => o.ScriptPubKey == gold.PubKey.Hash.ScriptPubKey);
                Assert.Equal(Money.Coins(0.99982874m), change.Value);

                Assert.Equal(gold.PubKey.Hash, change.ScriptPubKey.GetDestination(this.networkMain));

                //Verify issuancecoin can have an url
                var issuanceCoin = (IssuanceCoin)issuanceCoins[0];
                issuanceCoin.DefinitionUrl = new Uri("http://toto.com/");
                txBuilder = new TransactionBuilder(this.networkMain);
                tx = txBuilder
                    .AddKeys(gold)
                    .AddCoins(issuanceCoin)
                    .IssueAsset(bob, new AssetMoney(gold.PubKey, 10))
                    .SetChange(gold)
                    .BuildTransaction(true);

                Assert.Equal("http://toto.com/", tx.GetColoredMarker().GetMetadataUrl().AbsoluteUri);

                //Sending 0 asset should be a no op
                txBuilder = new TransactionBuilder(this.networkMain);
                transfer =
                    txBuilder
                    .AddCoins(bobGold)
                    .AddCoins(((IssuanceCoin)issuanceCoins[0]).Bearer)
                    .AddKeys(gold, bob)
                    .SendAsset(alice.PubKey, new AssetMoney(goldId, 0UL))
                    .Send(alice.PubKey, Money.Coins(0.01m))
                    .SetChange(bob.PubKey)
                    .BuildTransaction(true);

                foreach (TxOut output in transfer.Outputs)
                {
                    Assert.False(TxNullDataTemplate.Instance.CheckScriptPubKey(output.ScriptPubKey));
                    Assert.False(output.Value == output.GetDustThreshold(txBuilder.StandardTransactionPolicy.MinRelayTxFee));
                }
            }
        }

        private void AssertHasAsset(Transaction tx, ColoredTransaction colored, ColoredEntry entry, AssetId assetId, int quantity, PubKey destination)
        {
            TxOut txout = tx.Outputs[entry.Index];
            Assert.True(entry.Asset.Id == assetId);
            Assert.True(entry.Asset.Quantity == quantity);
            if (destination != null)
                Assert.True(txout.ScriptPubKey == destination.ScriptPubKey);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanBuildStealthTransaction()
        {
            Key[] stealthKeys = Enumerable.Range(0, 3).Select(_ => new Key()).ToArray();
            var scanKey = new Key();

            var darkSatoshi = new BitcoinStealthAddress(scanKey.PubKey, stealthKeys.Select(k => k.PubKey).ToArray(), 2, new BitField(3, 5), this.networkMain);

            var bob = new Key();
            var coins = new Coin[] {
                new Coin()
                {
                    Outpoint = RandOutpoint(),
                    TxOut = new TxOut("1.00",bob.PubKey.Hash)
                } };

            //Bob sends money to satoshi
            var builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy;
            Transaction tx =
                builder
                .AddCoins(coins)
                .AddKeys(bob)
                .Send(darkSatoshi, "1.00")
                .BuildTransaction(true);
            Assert.True(builder.Verify(tx));

            //Satoshi scans a StealthCoin in the transaction with his scan key
            StealthCoin stealthCoin = StealthCoin.Find(tx, darkSatoshi, scanKey);
            Assert.NotNull(stealthCoin);

            //Satoshi sends back the money to Bob
            builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy;
            tx =
                builder
                    .AddCoins(stealthCoin)
                    .AddKeys(stealthKeys)
                    .AddKeys(scanKey)
                    .Send(bob.PubKey.Hash, "1.00")
                    .BuildTransaction(true);

            Assert.True(builder.Verify(tx)); //Signed !


            //Same scenario, Satoshi wants to send money back to Bob
            //However, his keys are spread on two machines
            //He partially signs on the 1st machine
            builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy;
            tx =
                builder
                    .AddCoins(stealthCoin)
                    .AddKeys(stealthKeys.Skip(2).ToArray()) //Only one Stealth Key
                    .AddKeys(scanKey)
                    .Send(bob.PubKey.Hash, "1.00")
                    .BuildTransaction(true);

            Assert.False(builder.Verify(tx)); //Not fully signed

            //Then he partially signs on the 2nd machine
            builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy;
            tx =
                builder
                    .AddCoins(stealthCoin)
                    .AddKeys(stealthKeys[0]) //Other key
                    .AddKeys(scanKey)
                    .SignTransaction(tx);

            Assert.True(builder.Verify(tx)); //Fully signed !
        }

        private OutPoint RandOutpoint()
        {
            return new OutPoint(Rand(), 0);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSwitchGroup()
        {
            var satoshi = new Key();
            var alice = new Key();
            var bob = new Key();

            var aliceCoins = new ICoin[] { RandomCoin("0.4", alice), RandomCoin("0.6", alice) };
            var bobCoins = new ICoin[] { RandomCoin("0.2", bob), RandomCoin("0.3", bob) };

            var builder = new TransactionBuilder(this.networkMain);
            var rate = new FeeRate(Money.Coins(0.0004m));
            Transaction tx1 = builder
                .AddCoins(aliceCoins)
                .AddKeys(alice)
                .Send(satoshi, Money.Coins(0.1m))
                .SetChange(alice)
                .Then()
                .AddCoins(bobCoins)
                .AddKeys(bob)
                .Send(satoshi, Money.Coins(0.01m))
                .SetChange(bob)
                .SendEstimatedFeesSplit(rate)
                .BuildTransaction(true);

            builder = new TransactionBuilder(this.networkMain);
            Transaction tx2 = builder
                .Then("Alice")
                .AddCoins(aliceCoins)
                .AddKeys(alice)
                .Send(satoshi, Money.Coins(0.1m))
                .Then("Bob")
                .AddCoins(bobCoins)
                .AddKeys(bob)
                .Send(satoshi, Money.Coins(0.01m))
                .SetChange(bob)
                .Then("Alice")
                .SetChange(alice)
                .SendEstimatedFeesSplit(rate)
                .BuildTransaction(true);

            Assert.Equal(tx1.ToString(), tx2.ToString());
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSplitFees()
        {
            var satoshi = new Key();
            var alice = new Key();
            var bob = new Key();

            var aliceCoins = new ICoin[] { RandomCoin("0.4", alice), RandomCoin("0.6", alice) };
            var bobCoins = new ICoin[] { RandomCoin("0.2", bob), RandomCoin("0.3", bob) };

            var builder = new TransactionBuilder(this.networkMain);
            var rate = new FeeRate(Money.Coins(0.0004m));
            Transaction tx = builder
                .AddCoins(aliceCoins)
                .AddKeys(alice)
                .Send(satoshi, Money.Coins(0.1m))
                .SetChange(alice)
                .Then()
                .AddCoins(bobCoins)
                .AddKeys(bob)
                .Send(satoshi, Money.Coins(0.01m))
                .SetChange(bob)
                .SendEstimatedFeesSplit(rate)
                .BuildTransaction(true);

            Money estimated = builder.EstimateFees(tx, rate);

            Assert.True(builder.Verify(tx, estimated));

            // Alice should pay two times more fee than bob
            builder = new TransactionBuilder(this.networkMain);
            tx = builder
                .AddCoins(aliceCoins)
                .AddKeys(alice)
                .SetFeeWeight(2.0m)
                .Send(satoshi, Money.Coins(0.1m))
                .SetChange(alice)
                .Then()
                .AddCoins(bobCoins)
                .AddKeys(bob)
                .Send(satoshi, Money.Coins(0.01m))
                .SetChange(bob)
                .SendFeesSplit(Money.Coins(0.6m))
                .BuildTransaction(true);

            Money spentAlice = builder.FindSpentCoins(tx).Where(c => aliceCoins.Contains(c)).OfType<Coin>().Select(c => c.Amount).Sum();
            Money receivedAlice = tx.Outputs.AsCoins().Where(c => c.ScriptPubKey == alice.PubKey.Hash.ScriptPubKey).Select(c => c.Amount).Sum();
            Assert.Equal(Money.Coins(0.1m + 0.4m), spentAlice - receivedAlice);

            Money spentBob = builder.FindSpentCoins(tx).Where(c => bobCoins.Contains(c)).OfType<Coin>().Select(c => c.Amount).Sum();
            Money receivedBob = tx.Outputs.AsCoins().Where(c => c.ScriptPubKey == bob.PubKey.Hash.ScriptPubKey).Select(c => c.Amount).Sum();
            Assert.Equal(Money.Coins(0.01m + 0.2m), spentBob - receivedBob);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanVerifySequenceLock()
        {
            var now = new DateTimeOffset(1988, 7, 18, 0, 0, 0, TimeSpan.Zero);
            TimeSpan step = TimeSpan.FromMinutes(10.0);
            TimeSpan smallStep = new Sequence(step).LockPeriod;
            CanVerifySequenceLockCore(new[] { new Sequence(1) }, new[] { 1 }, 2, now, true, new SequenceLock(1, -1));
            CanVerifySequenceLockCore(new[] { new Sequence(1) }, new[] { 1 }, 1, now, false, new SequenceLock(1, -1));
            CanVerifySequenceLockCore(
                new[]
                {
                    new Sequence(1),
                    new Sequence(5),
                    new Sequence(11),
                    new Sequence(8)
                },
                new[] { 1, 5, 7, 9 }, 10, DateTimeOffset.UtcNow, false, new SequenceLock(17, -1));

            CanVerifySequenceLockCore(
                new[]
                {
                    new Sequence(smallStep), //MTP(block[11] is +60min) 
                },
                new[] { 12 }, 13, now, true, new SequenceLock(-1, now + TimeSpan.FromMinutes(60.0) + smallStep - TimeSpan.FromSeconds(1)));

            CanVerifySequenceLockCore(
                new[]
                {
                    new Sequence(smallStep), //MTP(block[11] is +60min) 
                },
                new[] { 12 }, 12, now, false, new SequenceLock(-1, now + TimeSpan.FromMinutes(60.0) + smallStep - TimeSpan.FromSeconds(1)));
        }

        private void CanVerifySequenceLockCore(Sequence[] sequences, int[] prevHeights, int currentHeight, DateTimeOffset first, bool expected, SequenceLock expectedLock)
        {
            Network network = KnownNetworks.Main;

            var chain = new ChainIndexer(this.networkMain);

            first = first + TimeSpan.FromMinutes(10);

            while (currentHeight != chain.Height)
            {
                BlockHeader header = network.Consensus.ConsensusFactory.CreateBlockHeader();
                header.BlockTime = first;
                header.HashPrevBlock = chain.Tip.HashBlock;

                chain.SetTip(header);
                first = first + TimeSpan.FromMinutes(10);
            }

            Transaction tx = network.CreateTransaction();
            tx.Version = 2;
            for (int i = 0; i < sequences.Length; i++)
            {
                var input = new TxIn();
                input.Sequence = sequences[i];
                tx.Inputs.Add(input);
            }
            Assert.Equal(expected, tx.CheckSequenceLocks(prevHeights, chain.Tip));
            SequenceLock actualLock = tx.CalculateSequenceLocks(prevHeights, chain.Tip);
            Assert.Equal(expectedLock.MinTime, actualLock.MinTime);
            Assert.Equal(expectedLock.MinHeight, actualLock.MinHeight);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanEstimateFees()
        {
            var alice = new Key();
            var bob = new Key();
            var satoshi = new Key();
            Script bobAlice = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, alice.PubKey, bob.PubKey);

            //Alice sends money to bobAlice
            //Bob sends money to bobAlice
            //bobAlice sends money to satoshi

            var aliceCoins = new ICoin[] { RandomCoin("0.4", alice), RandomCoin("0.6", alice) };
            var bobCoins = new ICoin[] { RandomCoin("0.2", bob), RandomCoin("0.3", bob) };
            var bobAliceCoins = new ICoin[] { RandomCoin("1.5", bobAlice, false), RandomCoin("0.25", bobAlice, true) };

            var builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy;
            Transaction unsigned = builder
                .AddCoins(aliceCoins)
                .Send(bobAlice, "1.0")
                .Then()
                .AddCoins(bobCoins)
                .Send(bobAlice, "0.5")
                .Then()
                .AddCoins(bobAliceCoins)
                .Send(satoshi.PubKey, "1.74")
                .SetChange(bobAlice)
                .BuildTransaction(false);

            builder.AddKeys(alice, bob, satoshi);
            Transaction signed = builder.BuildTransaction(true);
            Assert.True(builder.Verify(signed));

            Assert.True(Math.Abs(signed.ToBytes().Length - builder.EstimateSize(unsigned)) < 20);

            var rate = new FeeRate(Money.Coins(0.0004m));
            Money estimatedFees = builder.EstimateFees(unsigned, rate);
            builder.SendEstimatedFees(rate);
            signed = builder.BuildTransaction(true);
            Assert.True(builder.Verify(signed, estimatedFees));
        }

        private Coin RandomCoin(Money amount, Script scriptPubKey, bool p2sh)
        {
            OutPoint outpoint = RandOutpoint();
            if (!p2sh)
                return new Coin(outpoint, new TxOut(amount, scriptPubKey));
            return new ScriptCoin(outpoint, new TxOut(amount, scriptPubKey.Hash), scriptPubKey);
        }

        private Coin RandomCoin(Money amount, Key receiver)
        {
            return RandomCoin(amount, receiver.PubKey.GetAddress(this.networkMain));
        }

        private Coin RandomCoin(Money amount, IDestination receiver)
        {
            OutPoint outpoint = RandOutpoint();
            return new Coin(outpoint, new TxOut(amount, receiver));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void BigUIntCoverage()
        {
            Assert.True(new uint160("0102030405060708090102030405060708090102") == new uint160("0102030405060708090102030405060708090102"));
            Assert.True(new uint160("0102030405060708090102030405060708090102") == new uint160(new uint160("0102030405060708090102030405060708090102")));
            Assert.True(new uint160("0102030405060708090102030405060708090102") != new uint160(new uint160("0102030405060708090102030405060708090101")));
            Assert.False(new uint160("0102030405060708090102030405060708090102") != new uint160(new uint160("0102030405060708090102030405060708090102")));
            Assert.True(new uint160("0102030405060708090102030405060708090102").Equals(new uint160("0102030405060708090102030405060708090102")));
            Assert.True(new uint160("0102030405060708090102030405060708090102").GetHashCode() == new uint160("0102030405060708090102030405060708090102").GetHashCode());
            Assert.True(new uint160("0102030405060708090102030405060708090102") == uint160.Parse("0102030405060708090102030405060708090102"));
            uint160 a = null;
            Assert.True(uint160.TryParse("0102030405060708090102030405060708090102", out a));
            Assert.True(a == uint160.Parse("0102030405060708090102030405060708090102"));
            Assert.False(uint160.TryParse("01020304050607080901020304050607080901020", out a));
            Assert.True(new uint160("0102030405060708090102030405060708090102") > uint160.Parse("0102030405060708090102030405060708090101"));
            Assert.True(new uint160("0102030405060708090102030405060708090101") < uint160.Parse("0102030405060708090102030405060708090102"));
            Assert.True(new uint160("0102030405060708090102030405060708090101") <= uint160.Parse("0102030405060708090102030405060708090101"));
            Assert.True(new uint160("0102030405060708090102030405060708090101") >= uint160.Parse("0102030405060708090102030405060708090101"));
            Assert.True(new uint160("0102030405060708090102030405060708090101") <= uint160.Parse("0102030405060708090102030405060708090102"));
            Assert.True(new uint160("0102030405060708090102030405060708090102") >= uint160.Parse("0102030405060708090102030405060708090101"));

            var bytes = new List<byte>();
            a = new uint160("0102030405060708090102030405060708090102");
            for (int i = 0; i < 20; i++)
            {
                bytes.Add(a.GetByte(i));
            }
            bytes.Reverse();
            AssertEx.CollectionEquals(Encoders.Hex.DecodeData("0102030405060708090102030405060708090102"), bytes.ToArray());

            bytes = new List<byte>();
            var b = new uint256("0102030405060708090102030405060708090102030405060708090102030405");
            for (int i = 0; i < 32; i++)
            {
                bytes.Add(b.GetByte(i));
            }
            bytes.Reverse();
            AssertEx.CollectionEquals(Encoders.Hex.DecodeData("0102030405060708090102030405060708090102030405060708090102030405"), bytes.ToArray());
            Assert.True(new uint256("0102030405060708090102030405060708090102030405060708090102030405") == new uint256(new uint256("0102030405060708090102030405060708090102030405060708090102030405")));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void OtherCoverage()
        {
            Assert.Equal(System.Net.IPAddress.Parse("127.0.0.1").MapToIPv6(), Utils.MapToIPv6(System.Net.IPAddress.Parse("127.0.0.1")));
            Assert.False(Utils.IsIPv4MappedToIPv6(System.Net.IPAddress.Parse("127.0.0.1")));
            Assert.True(Utils.IsIPv4MappedToIPv6(Utils.MapToIPv6(System.Net.IPAddress.Parse("127.0.0.1"))));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void BitcoinStreamCoverage()
        {
            BitcoinStreamCoverageCore(new ulong[] { 1, 2, 3, 4 }, (BitcoinStream bs, ref ulong[] items) =>
            {
                bs.ReadWrite(ref items);
            });
            BitcoinStreamCoverageCore(new ushort[] { 1, 2, 3, 4 }, (BitcoinStream bs, ref ushort[] items) =>
            {
                bs.ReadWrite(ref items);
            });
            BitcoinStreamCoverageCore(new uint[] { 1, 2, 3, 4 }, (BitcoinStream bs, ref uint[] items) =>
            {
                bs.ReadWrite(ref items);
            });
            BitcoinStreamCoverageCore(new short[] { -1, 1, 2, 3, 4 }, (BitcoinStream bs, ref short[] items) =>
            {
                bs.ReadWrite(ref items);
            });
            BitcoinStreamCoverageCore(new long[] { -1, 1, 2, 3, 4 }, (BitcoinStream bs, ref long[] items) =>
            {
                bs.ReadWrite(ref items);
            });
            BitcoinStreamCoverageCore(new byte[] { 1, 2, 3, 4 }, (BitcoinStream bs, ref byte[] items) =>
            {
                bs.ReadWrite(ref items);
            });
            BitcoinStreamCoverageCore(new uint160[] { new uint160(1), new uint160(2), new uint160(3), new uint160(4) }, (BitcoinStream bs, ref uint160[] items) =>
            {
                List<uint160> l = items.ToList();
                bs.ReadWrite(ref l);
                items = l.ToArray();
            });
        }

        private delegate void BitcoinStreamCoverageCoreDelegate<TItem>(BitcoinStream bs, ref TItem[] items);

        private void BitcoinStreamCoverageCore<TItem>(TItem[] input, BitcoinStreamCoverageCoreDelegate<TItem> roundTrip)
        {
            TItem[] before = input.ToArray();
            var ms = new MemoryStream();
            var bs = new BitcoinStream(ms, true);

            bs.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;
            TItem[] before2 = input;
            roundTrip(bs, ref input);
            Array.Clear(input, 0, input.Length);
            ms.Position = 0;
            bs = new BitcoinStream(ms, false);
            bs.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;
            roundTrip(bs, ref input);
            if (!(input is byte[])) //Byte serialization reuse the input array
                Assert.True(before2 != input);
            AssertEx.CollectionEquals(before, input);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSerializeInvalidTransactionsBackAndForth()
        {
            Transaction before = this.networkMain.CreateTransaction();
            uint versionBefore = before.Version;
            before.Outputs.Add(new TxOut());
            Transaction after = AssertClone(before);
            Assert.Equal(before.Version, after.Version);
            Assert.Equal(versionBefore, after.Version);
            Assert.True(after.Outputs.Count == 1);

            before = this.networkMain.CreateTransaction();
            after = AssertClone(before);
            Assert.Equal(before.Version, versionBefore);
        }

        private Transaction AssertClone(Transaction before)
        {
            Transaction after = this.networkMain.CreateTransaction(before.ToBytes());
            Transaction after2 = null;

            var ms = new MemoryStream();
            var stream = new BitcoinStream(ms, true);
            stream.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;
            stream.TransactionOptions = TransactionOptions.Witness;
            stream.ReadWrite(before);

            ms.Position = 0;

            stream = new BitcoinStream(ms, false);
            stream.TransactionOptions = TransactionOptions.Witness;
            stream.ConsensusFactory = this.networkMain.Consensus.ConsensusFactory;
            stream.ReadWrite(ref after2);

            Assert.Equal(after2.GetHash(), after.GetHash());
            Assert.Equal(before.GetHash(), after.GetHash());

            return after;
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CantAskScriptCodeOnIncompleteCoin()
        {
            var k = new Key();
            Coin coin = RandomCoin(Money.Zero, k);
            Assert.True(coin.CanGetScriptCode(this.networkMain));
            coin.ScriptPubKey = k.PubKey.ScriptPubKey.Hash.ScriptPubKey;
            Assert.False(coin.CanGetScriptCode(this.networkMain));
            Assert.Throws<InvalidOperationException>(() => coin.GetScriptCode(this.networkMain));
            Assert.True(coin.ToScriptCoin(k.PubKey.ScriptPubKey).CanGetScriptCode(this.networkMain));

            coin.ScriptPubKey = k.PubKey.ScriptPubKey.WitHash.ScriptPubKey;
            Assert.False(coin.CanGetScriptCode(this.networkMain));
            Assert.Throws<InvalidOperationException>(() => coin.GetScriptCode(this.networkMain));
            Assert.True(coin.ToScriptCoin(k.PubKey.ScriptPubKey).CanGetScriptCode(this.networkMain));

            coin.ScriptPubKey = k.PubKey.ScriptPubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey;
            Assert.False(coin.CanGetScriptCode(this.networkMain));
            Assert.Throws<InvalidOperationException>(() => coin.GetScriptCode(this.networkMain));

            ScriptCoin badCoin = coin.ToScriptCoin(k.PubKey.ScriptPubKey);
            badCoin.Redeem = k.PubKey.ScriptPubKey.WitHash.ScriptPubKey;
            Assert.False(badCoin.CanGetScriptCode(this.networkMain));
            Assert.Throws<InvalidOperationException>(() => badCoin.GetScriptCode(this.networkMain));
            Assert.True(coin.ToScriptCoin(k.PubKey.ScriptPubKey).CanGetScriptCode(this.networkMain));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanBuildWitTransaction()
        {
            Action<Transaction, TransactionBuilder> AssertEstimatedSize = (tx, b) =>
            {
                int expectedVSize = tx.GetVirtualSize(KnownNetworks.Main.Consensus.Options.WitnessScaleFactor);
                int actualVSize = b.EstimateSize(tx, true);
                int expectedSize = tx.GetSerializedSize();
                int actualSize = b.EstimateSize(tx, false);
                Assert.True(Math.Abs(expectedVSize - actualVSize) < Math.Abs(expectedVSize - actualSize));
                Assert.True(Math.Abs(expectedSize - actualSize) < Math.Abs(expectedSize - actualVSize));
                Assert.True(Math.Abs(expectedVSize - actualVSize) < Math.Abs(expectedSize - actualVSize));
                Assert.True(Math.Abs(expectedSize - actualSize) < Math.Abs(expectedVSize - actualSize));

                decimal error = (decimal)Math.Abs(expectedVSize - actualVSize) / Math.Min(expectedVSize, actualSize);
                Assert.True(error < 0.01m);
            };
            var alice = new Key();
            var bob = new Key();
            Transaction previousTx = null;
            Coin previousCoin = null;
            ScriptCoin witnessCoin = null;
            TransactionBuilder builder = null;
            Transaction signedTx = null;
            ScriptCoin scriptCoin = null;

            //P2WPKH
            previousTx = this.networkMain.CreateTransaction();
            previousTx.Outputs.Add(new TxOut(Money.Coins(1.0m), alice.PubKey.WitHash));
            previousCoin = previousTx.Outputs.AsCoins().First();

            builder = new TransactionBuilder(this.networkMain);
            builder.AddKeys(alice);
            builder.AddCoins(previousCoin);
            builder.Send(bob, Money.Coins(0.4m));
            builder.SendFees(Money.Satoshis(30000));
            builder.SetChange(alice);
            signedTx = builder.BuildTransaction(true);
            AssertEstimatedSize(signedTx, builder);
            Assert.True(builder.Verify(signedTx));
            Assert.Equal(previousCoin.ScriptPubKey, signedTx.Inputs[0].GetSigner(this.networkMain).ScriptPubKey);

            //P2WSH
            previousTx = this.networkMain.CreateTransaction();
            previousTx.Outputs.Add(new TxOut(Money.Coins(1.0m), alice.PubKey.ScriptPubKey.WitHash));
            previousCoin = previousTx.Outputs.AsCoins().First();

            witnessCoin = new ScriptCoin(previousCoin, alice.PubKey.ScriptPubKey);
            builder = new TransactionBuilder(this.networkMain);
            builder.AddKeys(alice);
            builder.AddCoins(witnessCoin);
            builder.Send(bob, Money.Coins(0.4m));
            builder.SendFees(Money.Satoshis(30000));
            builder.SetChange(alice);
            signedTx = builder.BuildTransaction(true);
            AssertEstimatedSize(signedTx, builder);
            Assert.True(builder.Verify(signedTx));
            Assert.Equal(witnessCoin.ScriptPubKey, signedTx.Inputs[0].GetSigner(this.networkMain).ScriptPubKey);


            //P2SH(P2WPKH)
            previousTx = this.networkMain.CreateTransaction();
            previousTx.Outputs.Add(new TxOut(Money.Coins(1.0m), alice.PubKey.WitHash.ScriptPubKey.Hash));
            previousCoin = previousTx.Outputs.AsCoins().First();

            scriptCoin = new ScriptCoin(previousCoin, alice.PubKey.WitHash.ScriptPubKey);
            builder = new TransactionBuilder(this.networkMain);
            builder.AddKeys(alice);
            builder.AddCoins(scriptCoin);
            builder.Send(bob, Money.Coins(0.4m));
            builder.SendFees(Money.Satoshis(30000));
            builder.SetChange(alice);
            signedTx = builder.BuildTransaction(true);
            AssertEstimatedSize(signedTx, builder);
            Assert.True(builder.Verify(signedTx));
            Assert.Equal(scriptCoin.ScriptPubKey, signedTx.Inputs[0].GetSigner(this.networkMain).ScriptPubKey);

            //P2SH(P2WSH)
            previousTx = this.networkMain.CreateTransaction();
            previousTx.Outputs.Add(new TxOut(Money.Coins(1.0m), alice.PubKey.ScriptPubKey.WitHash.ScriptPubKey.Hash));
            previousCoin = previousTx.Outputs.AsCoins().First();

            witnessCoin = new ScriptCoin(previousCoin, alice.PubKey.ScriptPubKey);
            builder = new TransactionBuilder(this.networkMain);
            builder.AddKeys(alice);
            builder.AddCoins(witnessCoin);
            builder.Send(bob, Money.Coins(0.4m));
            builder.SendFees(Money.Satoshis(30000));
            builder.SetChange(alice);
            signedTx = builder.BuildTransaction(true);
            AssertEstimatedSize(signedTx, builder);
            Assert.True(builder.Verify(signedTx));
            Assert.Equal(witnessCoin.ScriptPubKey, signedTx.Inputs[0].GetSigner(this.networkMain).ScriptPubKey);

            //Can remove witness data from tx
            Transaction signedTx2 = signedTx.WithOptions(TransactionOptions.None, this.networkMain.Consensus.ConsensusFactory);
            Assert.Equal(signedTx.GetHash(), signedTx2.GetHash());
            Assert.True(signedTx2.GetSerializedSize() < signedTx.GetSerializedSize());
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanFilterUneconomicalCoins()
        {
            var builder = new TransactionBuilder(this.networkMain);
            var alice = new Key();
            var bob = new Key();
            //P2SH(P2WSH)
            Transaction previousTx = this.networkMain.CreateTransaction();
            previousTx.Outputs.Add(new TxOut(Money.Coins(1.0m), alice.PubKey.ScriptPubKey.WitHash.ScriptPubKey.Hash));
            Coin previousCoin = previousTx.Outputs.AsCoins().First();

            var witnessCoin = new ScriptCoin(previousCoin, alice.PubKey.ScriptPubKey);
            builder = new TransactionBuilder(this.networkMain);
            builder.AddKeys(alice);
            builder.AddCoins(witnessCoin);
            builder.Send(bob, Money.Coins(0.4m));
            builder.SendFees(Money.Satoshis(30000));
            builder.SetChange(alice);
            builder.BuildTransaction(true);
            builder.FilterUneconomicalCoinsRate = new FeeRate(Money.Coins(1m), 1);
            Assert.Throws<NotEnoughFundsException>(() => builder.BuildTransaction(true));
            builder.FilterUneconomicalCoins = false;
            builder.BuildTransaction(true);
            builder.FilterUneconomicalCoins = true;
            Assert.Throws<NotEnoughFundsException>(() => builder.BuildTransaction(true));
            builder.FilterUneconomicalCoinsRate = new FeeRate(Money.Satoshis(1m), 1);
            builder.BuildTransaction(true);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanCheckSegwitPubkey()
        {
            var a = new Script("OP_DUP 033fbe0a2aa8dc28ee3b2e271e3fedc7568529ffa20df179b803bf9073c11b6a8b OP_CHECKSIG OP_IF OP_DROP 0382fdfb0a3898bc6504f63204e7d15a63be82a3b910b5b865690dc96d1249f98c OP_ELSE OP_CODESEPARATOR 033fbe0a2aa8dc28ee3b2e271e3fedc7568529ffa20df179b803bf9073c11b6a8b OP_ENDIF OP_CHECKSIG");
            Assert.False(PayToWitTemplate.Instance.CheckScriptPubKey(a));
            a = new Script("1 033fbe0a2aa8dc28ee3b2e271e3fedc7568529ffa20df179b803bf9073c1");
            Assert.True(PayToWitTemplate.Instance.CheckScriptPubKey(a));

            foreach (int pushSize in new[] { 2, 10, 20, 32 })
            {
                a = new Script("1 " + string.Concat(Enumerable.Range(0, pushSize * 2).Select(_ => "0").ToArray()));
                Assert.True(PayToWitTemplate.Instance.CheckScriptPubKey(a));
            }
            a = new Script("1 " + string.Concat(Enumerable.Range(0, 33 * 2).Select(_ => "0").ToArray()));
            Assert.False(PayToWitTemplate.Instance.CheckScriptPubKey(a));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanEstimatedFeesCorrectlyIfFeesChangeTransactionSize()
        {
            Script redeem = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, new Key().PubKey, new Key().PubKey, new Key().PubKey);
            var transactionBuilder = new TransactionBuilder(this.networkMain);
            transactionBuilder.AddCoins(new Coin(new OutPoint(uint256.Parse("75425c904289f21feef0cffab2081ba22030b633623115adf0780edad443e6c7"), 1), new TxOut("0.00010000", PayToScriptHashTemplate.Instance.GenerateScriptPubKey(redeem).GetDestinationAddress(this.networkMain))).ToScriptCoin(redeem));
            transactionBuilder.AddCoins(new Coin(new OutPoint(uint256.Parse("75425c904289f21feef0cffab2081ba22030b633623115adf0780edad443e6c7"), 2), new TxOut("0.00091824", PayToScriptHashTemplate.Instance.GenerateScriptPubKey(redeem).GetDestinationAddress(this.networkMain))).ToScriptCoin(redeem));
            transactionBuilder.AddCoins(new Coin(new OutPoint(uint256.Parse("75425c904289f21feef0cffab2081ba22030b633623115adf0780edad443e6c7"), 3), new TxOut("0.00100000", PayToScriptHashTemplate.Instance.GenerateScriptPubKey(redeem).GetDestinationAddress(this.networkMain))).ToScriptCoin(redeem));
            transactionBuilder.AddCoins(new Coin(new OutPoint(uint256.Parse("75425c904289f21feef0cffab2081ba22030b633623115adf0780edad443e6c7"), 4), new TxOut("0.00100000", PayToScriptHashTemplate.Instance.GenerateScriptPubKey(redeem).GetDestinationAddress(this.networkMain))).ToScriptCoin(redeem));
            transactionBuilder.AddCoins(new Coin(new OutPoint(uint256.Parse("75425c904289f21feef0cffab2081ba22030b633623115adf0780edad443e6c7"), 5), new TxOut("0.00246414", PayToScriptHashTemplate.Instance.GenerateScriptPubKey(redeem).GetDestinationAddress(this.networkMain))).ToScriptCoin(redeem));
            transactionBuilder.AddCoins(new Coin(new OutPoint(uint256.Parse("75425c904289f21feef0cffab2081ba22030b633623115adf0780edad443e6c7"), 6), new TxOut("0.00250980", PayToScriptHashTemplate.Instance.GenerateScriptPubKey(redeem).GetDestinationAddress(this.networkMain))).ToScriptCoin(redeem));
            transactionBuilder.AddCoins(new Coin(new OutPoint(uint256.Parse("75425c904289f21feef0cffab2081ba22030b633623115adf0780edad443e6c7"), 7), new TxOut("0.01000000", PayToScriptHashTemplate.Instance.GenerateScriptPubKey(redeem).GetDestinationAddress(this.networkMain))).ToScriptCoin(redeem));
            transactionBuilder.Send(new Key().PubKey.GetAddress(this.networkMain), "0.01000000");
            transactionBuilder.SetChange(new Key().PubKey.GetAddress(this.networkMain));

            var feeRate = new FeeRate((long)32563);
            Money estimatedFeeBefore = transactionBuilder.EstimateFees(feeRate);
            //Adding the estimated fees will cause 6 more coins to be included, so let's verify the actual sent fees take that into account
            transactionBuilder.SendEstimatedFees(feeRate);
            Transaction tx = transactionBuilder.BuildTransaction(false);
            Money estimation = transactionBuilder.EstimateFees(tx, feeRate);
            Assert.Equal(estimation, tx.GetFee(transactionBuilder.FindSpentCoins(tx)));
            Assert.Equal(estimatedFeeBefore, estimation);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanSubstractFees()
        {
            var alice = new Key();
            var bob = new Key();
            Transaction tx =
                new TransactionBuilder(this.networkMain)
                .AddCoins(new Coin(new OutPoint(Rand(), 1), new TxOut(Money.Coins(1.0m), alice.ScriptPubKey)))
                .AddKeys(alice)
                .Send(bob, Money.Coins(0.6m))
                .SubtractFees()
                .SendFees(Money.Coins(0.01m))
                .SetChange(alice)
                .BuildTransaction(true);
            Assert.Contains(tx.Outputs, o => o.Value == Money.Coins(0.59m));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanBuildWithKnownSignatures()
        {
            var k = new Key();
            var tx = new Transaction();

            var coin = new Coin(new OutPoint(Rand(), 0), new TxOut(Money.Coins(1.0m), k.PubKey.Hash));
            tx.Inputs.Add(new TxIn(coin.Outpoint));
            TransactionSignature signature = tx.SignInput(KnownNetworks.Main, k, coin);

            var txBuilder = new TransactionBuilder(this.networkMain);
            txBuilder.AddCoins(coin);
            txBuilder.AddKnownSignature(k.PubKey, signature);
            txBuilder.SignTransactionInPlace(tx);

            Assert.True(tx.Inputs.AsIndexedInputs().First().VerifyScript(KnownNetworks.Main, coin));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanBuildTransaction()
        {
            Key[] keys = Enumerable.Range(0, 5).Select(i => new Key()).ToArray();

            Script multiSigPubKey = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, keys.Select(k => k.PubKey).Take(3).ToArray());
            Script pubKeyPubKey = PayToPubkeyTemplate.Instance.GenerateScriptPubKey(keys[4].PubKey);
            Script pubKeyHashPubKey = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(keys[4].PubKey.Hash);
            Script scriptHashPubKey1 = PayToScriptHashTemplate.Instance.GenerateScriptPubKey(multiSigPubKey.Hash);
            Script scriptHashPubKey2 = PayToScriptHashTemplate.Instance.GenerateScriptPubKey(pubKeyPubKey.Hash);
            Script scriptHashPubKey3 = PayToScriptHashTemplate.Instance.GenerateScriptPubKey(pubKeyHashPubKey.Hash);


            List<Coin> coins = new[] { multiSigPubKey, pubKeyPubKey, pubKeyHashPubKey }.Select((script, i) =>
                new Coin
                    (
                    new OutPoint(Rand(), i),
                    new TxOut(new Money((i + 1) * Money.COIN), script)
                    )).ToList();

            List<ScriptCoin> scriptCoins =
                new[] { scriptHashPubKey1, scriptHashPubKey2, scriptHashPubKey3 }
                .Zip(new[] { multiSigPubKey, pubKeyPubKey, pubKeyHashPubKey },
                    (script, redeem) => new
                    {
                        script,
                        redeem
                    })
                .Select((_, i) =>
                new ScriptCoin
                    (
                    new OutPoint(Rand(), i),
                    new TxOut(new Money((i + 1) * Money.COIN), _.script), _.redeem
                    )).ToList();

            List<ScriptCoin> witCoins =
            new[] { scriptHashPubKey1, scriptHashPubKey2, scriptHashPubKey3 }
            .Zip(new[] { multiSigPubKey, pubKeyPubKey, pubKeyHashPubKey },
                (script, redeem) => new
                {
                    script,
                    redeem
                })
            .Select((_, i) =>
            new ScriptCoin
                (
                new OutPoint(Rand(), i),
                new TxOut(new Money((i + 1) * Money.COIN), _.redeem.WitHash.ScriptPubKey.Hash),
                _.redeem
                )).ToList();
            Money a = witCoins.Select(c => c.Amount).Sum();
            Coin[] allCoins = coins.Concat(scriptCoins).Concat(witCoins).ToArray();
            BitcoinPubKeyAddress[] destinations = keys.Select(k => k.PubKey.GetAddress(this.networkMain)).ToArray();

            var txBuilder = new TransactionBuilder(0, this.networkMain);
            txBuilder.StandardTransactionPolicy = EasyPolicy;
            Transaction tx = txBuilder
                .AddCoins(allCoins)
                .AddKeys(keys)
                .Send(destinations[0], Money.Parse("6") * 2)
                .Send(destinations[2], Money.Parse("5"))
                .Send(destinations[2], Money.Parse("0.9999"))
                .SendFees(Money.Parse("0.0001"))
                .SetChange(destinations[3])
                .BuildTransaction(true);
            Assert.True(txBuilder.Verify(tx, "0.0001"));

            //Verify that we can detect malleability
            txBuilder.StandardTransactionPolicy = EasyPolicy.Clone();
            txBuilder.StandardTransactionPolicy.CheckMalleabilitySafe = true;
            Assert.False(txBuilder.Verify(tx, "0.0001"));
            Assert.Equal(3, tx.Outputs.Count);
            TransactionPolicyError[] errors = txBuilder.Check(tx);
            Assert.True(errors.Length > 0);
            Assert.Equal(witCoins.Count, tx.Inputs.Count - errors.Length);

            txBuilder = new TransactionBuilder(0, this.networkMain);
            txBuilder.StandardTransactionPolicy = EasyPolicy;
            tx = txBuilder
               .AddCoins(allCoins)
               .AddKeys(keys)
               .SetGroupName("test")
               .Send(destinations[0], Money.Parse("6") * 2)
               .Send(destinations[2], Money.Parse("5"))
               .Send(destinations[2], Money.Parse("0.9998"))
               .SendFees(Money.Parse("0.0001"))
               .SetChange(destinations[3])
               .BuildTransaction(true);

            Assert.Equal(4, tx.Outputs.Count); //+ Change

            txBuilder.Send(destinations[4], Money.Parse("1"));
            NotEnoughFundsException ex = Assert.Throws<NotEnoughFundsException>(() => txBuilder.BuildTransaction(true));
            Assert.True(ex.Group == "test");
            Assert.True((Money)ex.Missing == Money.Parse("0.9999"));
            //Can sign partially
            txBuilder = new TransactionBuilder(0, this.networkMain);
            txBuilder.StandardTransactionPolicy = EasyPolicy;
            tx = txBuilder
                    .AddCoins(allCoins)
                    .AddKeys(keys.Skip(2).ToArray())  //One of the multi key missing
                    .Send(destinations[0], Money.Parse("6") * 2)
                    .Send(destinations[2], Money.Parse("5"))
                    .Send(destinations[2], Money.Parse("0.9998"))
                    .SendFees(Money.Parse("0.0001"))
                    .SetChange(destinations[3])
                    .Shuffle()
                    .BuildTransaction(true);
            Assert.False(txBuilder.Verify(tx, "0.0001"));

            Transaction partiallySigned = this.networkMain.CreateTransaction(tx.ToBytes(this.consensusFactory));

            txBuilder = new TransactionBuilder(0, this.networkMain);
            tx = txBuilder
                    .AddKeys(keys[0])
                    .AddCoins(allCoins)
                    .SignTransaction(tx);
            Assert.True(txBuilder.Verify(tx));

            txBuilder = new TransactionBuilder(0, this.networkMain)
                        .AddCoins(allCoins);
            //Trying with known signature
            foreach (Coin coin in allCoins)
            {
                TransactionSignature sig = partiallySigned.SignInput(KnownNetworks.Main, keys[0], coin);
                txBuilder.AddKnownSignature(keys[0].PubKey, sig);
            }
            tx = txBuilder
                .SignTransaction(partiallySigned);
            Assert.True(txBuilder.Verify(tx));

            //Test if signing separatly
            txBuilder = new TransactionBuilder(0, this.networkMain);
            txBuilder.StandardTransactionPolicy = EasyPolicy;
            tx = txBuilder
                    .AddCoins(allCoins)
                    .AddKeys(keys.Skip(2).ToArray())  //One of the multi key missing
                    .Send(destinations[0], Money.Parse("6") * 2)
                    .Send(destinations[2], Money.Parse("5"))
                    .Send(destinations[2], Money.Parse("0.9998"))
                    .SendFees(Money.Parse("0.0001"))
                    .SetChange(destinations[3])
                    .Shuffle()
                    .BuildTransaction(false);

            Transaction signed1 = txBuilder.SignTransaction(tx);

            txBuilder = new TransactionBuilder(0, this.networkMain);
            Transaction signed2 = txBuilder
                    .AddKeys(keys[0])
                    .AddCoins(allCoins)
                    .SignTransaction(tx);

            Assert.False(txBuilder.Verify(signed1));
            Assert.False(txBuilder.Verify(signed2));

            txBuilder = new TransactionBuilder(0, this.networkMain);
            txBuilder.StandardTransactionPolicy = EasyPolicy;
            tx = txBuilder
                .AddCoins(allCoins)
                .CombineSignatures(signed1, signed2);
            Assert.True(txBuilder.Verify(tx));

            //Check if can deduce scriptPubKey from P2SH and P2SPKH scriptSig
            allCoins = new[]
                {
                    RandomCoin(Money.Parse("1.0"), keys[0].PubKey.Hash.ScriptPubKey, false),
                    RandomCoin(Money.Parse("1.0"), keys[0].PubKey.Hash.ScriptPubKey, false),
                    RandomCoin(Money.Parse("1.0"), keys[1].PubKey.Hash.ScriptPubKey, false)
                };

            txBuilder = new TransactionBuilder(0, this.networkMain);
            txBuilder.StandardTransactionPolicy = EasyPolicy;
            tx =
                txBuilder.AddCoins(allCoins)
                     .Send(destinations[0], Money.Parse("3.0"))
                     .BuildTransaction(false);

            signed1 = new TransactionBuilder(0, this.networkMain)
                        .AddCoins(allCoins)
                        .AddKeys(keys[0])
                        .SignTransaction(tx);

            signed2 = new TransactionBuilder(0, this.networkMain)
                        .AddCoins(allCoins)
                        .AddKeys(keys[1])
                        .SignTransaction(tx);

            Assert.False(txBuilder.Verify(signed1));
            Assert.False(txBuilder.Verify(signed2));

            tx = new TransactionBuilder(0, this.networkMain)
                .CombineSignatures(signed1, signed2);

            Assert.True(txBuilder.Verify(tx));

            //Using the same set of coin in 2 group should not use two times the sames coins
            for (int i = 0; i < 3; i++)
            {
                txBuilder = new TransactionBuilder(this.networkMain);
                txBuilder.StandardTransactionPolicy = EasyPolicy;
                tx =
                    txBuilder
                    .AddCoins(allCoins)
                    .AddKeys(keys)
                    .Send(destinations[0], Money.Parse("2.0"))
                    .Then()
                    .AddCoins(allCoins)
                    .AddKeys(keys)
                    .Send(destinations[0], Money.Parse("1.0"))
                    .BuildTransaction(true);
                Assert.True(txBuilder.Verify(tx));
            }
        }

        private uint256 Rand()
        {
            return new uint256(RandomUtils.GetBytes(32));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        //https://gist.github.com/gavinandresen/3966071
        public void CanBuildTransactionWithDustPrevention()
        {
            var bob = new Key();
            var alice = new Key();
            var tx = new Transaction()
            {
                Outputs =
                {
                    new TxOut(Money.Coins(1.0m), bob)
                }
            };
            Coin[] coins = tx.Outputs.AsCoins().ToArray();

            var builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy.Clone();
            builder.StandardTransactionPolicy.MinRelayTxFee = new FeeRate(new Money(1000));

            Func<Transaction> create = () => builder
                .AddCoins(coins)
                .AddKeys(bob)
                .Send(alice, Money.Coins(0.99m))
                .Send(alice, Money.Satoshis(500))
                .Send(TxNullDataTemplate.Instance.GenerateScriptPubKey(new byte[] { 1, 2 }), Money.Zero)
                .SendFees(Money.Coins(0.0001m))
                .SetChange(bob)
                .BuildTransaction(true);

            Transaction signed = create();

            Assert.True(signed.Outputs.Count == 3);
            Assert.True(builder.Verify(signed, Money.Coins(0.0001m)));
            builder.DustPrevention = false;

            TransactionPolicyError[] errors;
            Assert.False(builder.Verify(signed, Money.Coins(0.0001m), out errors));
            var ex = (NotEnoughFundsPolicyError)errors.Single();
            Assert.True((Money)ex.Missing == Money.Parse("-0.00000500"));

            builder = new TransactionBuilder(this.networkMain);
            builder.DustPrevention = false;
            builder.StandardTransactionPolicy = EasyPolicy.Clone();
            builder.StandardTransactionPolicy.MinRelayTxFee = new FeeRate(new Money(1000));
            signed = create();
            Assert.True(signed.Outputs.Count == 4);
            Assert.False(builder.Verify(signed, out errors));
            Assert.True(errors.Length == 1);
            Assert.True(errors[0] is DustPolicyError);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        //https://gist.github.com/gavinandresen/3966071
        public void CanPartiallySignTransaction()
        {
            Key[] privKeys = new[]{"5JaTXbAUmfPYZFRwrYaALK48fN6sFJp4rHqq2QSXs8ucfpE4yQU",
                        "5Jb7fCeh1Wtm4yBBg3q3XbT6B525i17kVhy3vMC9AqfR6FH2qGk",
                        "5JFjmGo5Fww9p8gvx48qBYDJNAzR9pmH5S389axMtDyPT8ddqmw"}
                        .Select(k => new BitcoinSecret(k).PrivateKey).ToArray();

            //First: combine the three keys into a multisig address
            Script redeem = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, privKeys.Select(k => k.PubKey).ToArray());
            BitcoinAddress scriptAddress = redeem.Hash.GetAddress(this.networkMain);
            Assert.Equal("3QJmV3qfvL9SuYo34YihAf3sRCW3qSinyC", scriptAddress.ToString());

            // Next, create a transaction to send funds into that multisig. Transaction d6f72... is
            // an unspent transaction in my wallet (which I got from the 'listunspent' RPC call):
            // Taken from example
            Transaction fundingTransaction = this.networkMain.CreateTransaction("010000000189632848f99722915727c5c75da8db2dbf194342a0429828f66ff88fab2af7d6000000008b483045022100abbc8a73fe2054480bda3f3281da2d0c51e2841391abd4c09f4f908a2034c18d02205bc9e4d68eafb918f3e9662338647a4419c0de1a650ab8983f1d216e2a31d8e30141046f55d7adeff6011c7eac294fe540c57830be80e9355c83869c9260a4b8bf4767a66bacbd70b804dc63d5beeb14180292ad7f3b083372b1d02d7a37dd97ff5c9effffffff0140420f000000000017a914f815b036d9bbbce5e9f2a00abd1bf3dc91e955108700000000");

            // Create the spend-from-multisig transaction. Since the fund-the-multisig transaction
            // hasn't been sent yet, I need to give txid, scriptPubKey and redeemScript:
            Transaction spendTransaction = this.networkMain.CreateTransaction();
            spendTransaction.Inputs.Add(new TxIn()
            {
                PrevOut = new OutPoint(fundingTransaction.GetHash(), 0),
            });
            spendTransaction.Outputs.Add(new TxOut()
            {
                Value = "0.01000000",
                ScriptPubKey = new Script("OP_DUP OP_HASH160 ae56b4db13554d321c402db3961187aed1bbed5b OP_EQUALVERIFY OP_CHECKSIG")
            });

            spendTransaction.Inputs[0].ScriptSig = redeem; //The redeem should be in the scriptSig before signing

            Transaction partiallySigned = this.networkMain.CreateTransaction(spendTransaction.ToBytes());
            //... Now I can partially sign it using one private key:

            partiallySigned.Sign(KnownNetworks.Main, privKeys[0], true);

            //the other private keys (note the "hex" result getting longer):
            partiallySigned.Sign(KnownNetworks.Main, privKeys[1], true);


            AssertCorrectlySigned(partiallySigned, fundingTransaction.Outputs[0].ScriptPubKey, this.allowHighS);

            //Verify the transaction from the gist is also correctly signed
            Transaction gistTransaction = this.networkMain.CreateTransaction("0100000001aca7f3b45654c230e0886a57fb988c3044ef5e8f7f39726d305c61d5e818903c00000000fd5d010048304502200187af928e9d155c4b1ac9c1c9118153239aba76774f775d7c1f9c3e106ff33c0221008822b0f658edec22274d0b6ae9de10ebf2da06b1bbdaaba4e50eb078f39e3d78014730440220795f0f4f5941a77ae032ecb9e33753788d7eb5cb0c78d805575d6b00a1d9bfed02203e1f4ad9332d1416ae01e27038e945bc9db59c732728a383a6f1ed2fb99da7a4014cc952410491bba2510912a5bd37da1fb5b1673010e43d2c6d812c514e91bfa9f2eb129e1c183329db55bd868e209aac2fbc02cb33d98fe74bf23f0c235d6126b1d8334f864104865c40293a680cb9c020e7b1e106d8c1916d3cef99aa431a56d253e69256dac09ef122b1a986818a7cb624532f062c1d1f8722084861c5c3291ccffef4ec687441048d2455d2403e08708fc1f556002f1b6cd83f992d085097f9974ab08a28838f07896fbab08f39495e15fa6fad6edbfb1e754e35fa1c7844c41f322a1863d4621353aeffffffff0140420f00000000001976a914ae56b4db13554d321c402db3961187aed1bbed5b88ac00000000");

            AssertCorrectlySigned(gistTransaction, fundingTransaction.Outputs[0].ScriptPubKey, this.allowHighS); //One sig in the hard code tx is high

            //Can sign out of order
            partiallySigned = this.networkMain.CreateTransaction(spendTransaction.ToBytes());
            partiallySigned.Sign(KnownNetworks.Main, privKeys[2], true);
            partiallySigned.Sign(KnownNetworks.Main, privKeys[0], true);
            AssertCorrectlySigned(partiallySigned, fundingTransaction.Outputs[0].ScriptPubKey);

            //Can sign multiple inputs
            partiallySigned = this.networkMain.CreateTransaction(spendTransaction.ToBytes());
            partiallySigned.Inputs.Add(new TxIn()
            {
                PrevOut = new OutPoint(fundingTransaction.GetHash(), 1),
            });
            partiallySigned.Inputs[1].ScriptSig = redeem; //The redeem should be in the scriptSig before signing
            partiallySigned.Sign(KnownNetworks.Main, privKeys[2], true);
            partiallySigned.Sign(KnownNetworks.Main, privKeys[0], true);
        }

        private void AssertCorrectlySigned(Transaction tx, Script scriptPubKey, ScriptVerify scriptVerify = ScriptVerify.Standard)
        {
            for (int i = 0; i < tx.Inputs.Count; i++)
            {
                Assert.True(Script.VerifyScript(KnownNetworks.Main, scriptPubKey, tx, i, null, scriptVerify));
            }
        }

        private static StandardTransactionPolicy EasyPolicy = new StandardTransactionPolicy(KnownNetworks.Main)
        {
            MaxTransactionSize = null,
            MaxTxFee = null,
            MinRelayTxFee = null,
            ScriptVerify = ScriptVerify.Standard & ~ScriptVerify.LowS
        };

        private static StandardTransactionPolicy RelayPolicy = new StandardTransactionPolicy(KnownNetworks.Main)
        {
            MaxTransactionSize = null,
            MaxTxFee = null,
            MinRelayTxFee = new FeeRate(Money.Satoshis(KnownNetworks.Main.MinRelayTxFee)),
            ScriptVerify = ScriptVerify.Standard & ~ScriptVerify.LowS
        };

        [Trait("UnitTest", "UnitTest")]
        [Fact]
        public void CanMutateSignature()
        {
            Transaction funding = this.networkMain.CreateTransaction("010000000189632848f99722915727c5c75da8db2dbf194342a0429828f66ff88fab2af7d6000000008b483045022100abbc8a73fe2054480bda3f3281da2d0c51e2841391abd4c09f4f908a2034c18d02205bc9e4d68eafb918f3e9662338647a4419c0de1a650ab8983f1d216e2a31d8e30141046f55d7adeff6011c7eac294fe540c57830be80e9355c83869c9260a4b8bf4767a66bacbd70b804dc63d5beeb14180292ad7f3b083372b1d02d7a37dd97ff5c9effffffff0140420f000000000017a914f815b036d9bbbce5e9f2a00abd1bf3dc91e955108700000000");
            Transaction spending = this.networkMain.CreateTransaction("0100000001aca7f3b45654c230e0886a57fb988c3044ef5e8f7f39726d305c61d5e818903c00000000fd5d010048304502200187af928e9d155c4b1ac9c1c9118153239aba76774f775d7c1f9c3e106ff33c0221008822b0f658edec22274d0b6ae9de10ebf2da06b1bbdaaba4e50eb078f39e3d78014730440220795f0f4f5941a77ae032ecb9e33753788d7eb5cb0c78d805575d6b00a1d9bfed02203e1f4ad9332d1416ae01e27038e945bc9db59c732728a383a6f1ed2fb99da7a4014cc952410491bba2510912a5bd37da1fb5b1673010e43d2c6d812c514e91bfa9f2eb129e1c183329db55bd868e209aac2fbc02cb33d98fe74bf23f0c235d6126b1d8334f864104865c40293a680cb9c020e7b1e106d8c1916d3cef99aa431a56d253e69256dac09ef122b1a986818a7cb624532f062c1d1f8722084861c5c3291ccffef4ec687441048d2455d2403e08708fc1f556002f1b6cd83f992d085097f9974ab08a28838f07896fbab08f39495e15fa6fad6edbfb1e754e35fa1c7844c41f322a1863d4621353aeffffffff0140420f00000000001976a914ae56b4db13554d321c402db3961187aed1bbed5b88ac00000000");

            var builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy;
            builder.AddCoins(funding.Outputs.AsCoins());
            Assert.True(builder.Verify(spending));

            foreach (IndexedTxIn input in spending.Inputs.AsIndexedInputs())
            {
                Op[] ops = input.TxIn.ScriptSig.ToOps().ToArray();
                foreach (var sig in ops.Select(o =>
                {
                    try
                    {
                        return new TransactionSignature(o.PushData);
                    }
                    catch
                    {
                        return null;
                    }
                })
                    .Select((sig, i) => new
                    {
                        sig,
                        i
                    })
                    .Where(i => i.sig != null))
                {
                    ops[sig.i] = Op.GetPushOp(sig.sig.MakeCanonical().ToBytes());
                }
                input.TxIn.ScriptSig = new Script(ops);
            }
            Assert.True(builder.Verify(spending));
        }

        private ScriptVerify allowHighS = ScriptVerify.Standard & ~ScriptVerify.LowS;
        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanUseLockTime()
        {
            Transaction tx = this.networkMain.CreateTransaction();
            tx.LockTime = new LockTime(4);
            Transaction clone = this.networkMain.CreateTransaction(tx.ToBytes());
            Assert.Equal(tx.LockTime, clone.LockTime);

            Assert.Equal("Height : 0", new LockTime().ToString());
            Assert.Equal(3, (int)new LockTime(3));
            Assert.Equal((uint)3, (uint)new LockTime(3));
            Assert.Throws<InvalidOperationException>(() => (DateTimeOffset)new LockTime(3));

            DateTimeOffset now = DateTimeOffset.UtcNow;
            Assert.Equal("Date : " + now, new LockTime(now).ToString());
            Assert.Equal((int)Utils.DateTimeToUnixTime(now), (int)new LockTime(now));
            Assert.Equal(Utils.DateTimeToUnixTime(now), (uint)new LockTime(now));
            Assert.Equal(now.ToString(), ((DateTimeOffset)new LockTime(now)).ToString());
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        //http://brainwallet.org/#tx
        public void CanGetTransactionErrors()
        {
            var bob = new Key();
            var alice = new Key();

            Transaction funding = this.networkMain.CreateTransaction();
            funding.Outputs.Add(new TxOut(Money.Coins(1.0m), bob));
            funding.Outputs.Add(new TxOut(Money.Coins(1.1m), bob));
            funding.Outputs.Add(new TxOut(Money.Coins(1.2m), alice));

            Transaction spending = this.networkMain.CreateTransaction();
            spending.Inputs.Add(new TxIn(new OutPoint(funding, 0)));
            spending.Inputs.Add(new TxIn(new OutPoint(funding, 0))); //Duplicate
            spending.Inputs.Add(new TxIn(new OutPoint(funding, 1)));
            spending.Inputs.Add(new TxIn(new OutPoint(funding, 2))); //Alice will not sign

            spending.Outputs.Add(new TxOut(Money.Coins(4.0m), bob));

            TransactionPolicyError[] errors = null;
            var builder = new TransactionBuilder(this.networkMain);
            builder.StandardTransactionPolicy = EasyPolicy;
            builder.AddKeys(bob);
            builder.AddCoins(funding.Outputs.AsCoins());
            builder.SignTransactionInPlace(spending);
            Assert.False(builder.Verify(spending, Money.Coins(1.0m), out errors));

            DuplicateInputPolicyError dup = errors.OfType<DuplicateInputPolicyError>().Single();
            AssertEx.CollectionEquals(new uint[] { 0, 1 }, dup.InputIndices);
            Equals(new OutPoint(funding, 0), dup.OutPoint);

            ScriptPolicyError script = errors.OfType<ScriptPolicyError>().Single();
            Equals(alice.ScriptPubKey, script.ScriptPubKey);
            Equals(3, script.InputIndex);

            NotEnoughFundsPolicyError fees = errors.OfType<NotEnoughFundsPolicyError>().Single();
            Assert.Equal(fees.Missing, Money.Coins(0.7m));

            spending.Inputs.Add(new TxIn(new OutPoint(funding, 3))); //Coins not found
            builder.Verify(spending, Money.Coins(1.0m), out errors);
            CoinNotFoundPolicyError coin = errors.OfType<CoinNotFoundPolicyError>().Single();
            Assert.Equal(4UL, coin.InputIndex);
            Assert.Equal(3UL, coin.OutPoint.N);
        }

        //OP_DEPTH 5 OP_SUB OP_PICK OP_SIZE OP_NIP e 10 OP_WITHIN OP_DEPTH 6 OP_SUB OP_PICK OP_SIZE OP_NIP e 10 OP_WITHIN OP_BOOLAND OP_DEPTH 5 OP_SUB OP_PICK OP_SHA256 1d3a9b978502dbe93364a4ea7b75ae9758fd7683958f0f42c1600e9975d10350 OP_EQUAL OP_DEPTH 6 OP_SUB OP_PICK OP_SHA256 1c5f92551d47cb478129b6ba715f58e9cea74ced4eee866c61fc2ea214197dec OP_EQUAL OP_BOOLAND OP_BOOLAND OP_DEPTH 5 OP_SUB OP_PICK OP_SIZE OP_NIP OP_DEPTH 6 OP_SUB OP_PICK OP_SIZE OP_NIP OP_EQUAL OP_IF 1d3a9b978502dbe93364a4ea7b75ae9758fd7683958f0f42c1600e9975d10350 OP_ELSE 1c5f92551d47cb478129b6ba715f58e9cea74ced4eee866c61fc2ea214197dec OP_ENDIF 1d3a9b978502dbe93364a4ea7b75ae9758fd7683958f0f42c1600e9975d10350 OP_EQUAL OP_DEPTH 1 OP_SUB OP_PICK OP_DEPTH 2 OP_SUB OP_PICK OP_CHECKSIG OP_BOOLAND OP_DEPTH 5 OP_SUB OP_PICK OP_SIZE OP_NIP OP_DEPTH 6 OP_SUB OP_PICK OP_SIZE OP_NIP OP_EQUAL OP_IF 1d3a9b978502dbe93364a4ea7b75ae9758fd7683958f0f42c1600e9975d10350 OP_ELSE 1c5f92551d47cb478129b6ba715f58e9cea74ced4eee866c61fc2ea214197dec OP_ENDIF 1c5f92551d47cb478129b6ba715f58e9cea74ced4eee866c61fc2ea214197dec OP_EQUAL OP_DEPTH 3 OP_SUB OP_PICK OP_DEPTH 4 OP_SUB OP_PICK OP_CHECKSIG OP_BOOLAND OP_BOOLOR OP_BOOLAND OP_DEPTH 1 OP_SUB OP_PICK OP_DEPTH 2 OP_SUB OP_PICK OP_CHECKSIG OP_DEPTH 3 OP_SUB OP_PICK OP_DEPTH 4 OP_SUB OP_PICK OP_CHECKSIG OP_BOOLAND OP_BOOLOR OP_VERIFY

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestWtfScript()
        {
            Transaction source = this.networkMain.CreateTransaction("01000000024c8a18c4d9e623e81272e5c34669d67604637e6d8f64cbf79ba0bcc93c3472f30000000049483045022100bc793ca29e427838cbc24c127733be032178ab623519f796ce0fcdfef58a96f202206174c0f7d7b303e01fbb50052e0f5fcd75a964af5737a9ed2b95c20eef80cd7a01ffffffffd1f8b4607bd6a83db42d6f557f378758d4b03d9d1f2bb3b868bb9c55bbfc8679000000005747304402203424c812ee29c52a39bd6ce41b2a0d8a6210995b4049fa03be65105fc1abdb7402205bccaeb2c0d23d59f70a8175912c49100108d7217ad105bcb001d65be0c25c95010e8856df1cc6b5d55a12704c261963ffffffff01f049020000000000fd76017455947982775e60a57456947982775e60a59a74559479a820555086252d5d479dc034cddb75d6b9e8b6947e3690cec630531203e2f3dfdbfc8774569479a82034ebda879dc394c7522047b9bcc16b985d960b301b9d4f3addb05190b01b1300879a9a745594798277745694798277876320555086252d5d479dc034cddb75d6b9e8b6947e3690cec630531203e2f3dfdbfc672034ebda879dc394c7522047b9bcc16b985d960b301b9d4f3addb05190b01b13006820555086252d5d479dc034cddb75d6b9e8b6947e3690cec630531203e2f3dfdbfc877451947974529479ac9a745594798277745694798277876320555086252d5d479dc034cddb75d6b9e8b6947e3690cec630531203e2f3dfdbfc672034ebda879dc394c7522047b9bcc16b985d960b301b9d4f3addb05190b01b1300682034ebda879dc394c7522047b9bcc16b985d960b301b9d4f3addb05190b01b1300877453947974549479ac9a9b9a7451947974529479ac7453947974549479ac9a9b6900000000");
            Transaction spending = this.networkMain.CreateTransaction("0100000001914853959297db6a5aa0e3945a750e4ee311cf47e723dd81d4e397df04c8f500000000008b483045022100bef86c24185a568ce76a4527a88eda58b6ce531e9549d8135d334a6bd077c0350220398385675415edac18a6e623f3f7f7dc2e6a3b11f3beaa2c2763232e0cbf958f012103b81eecef4a027975ea51e6d1220129ed21b6d97c17b27bbbe32a5b934561ba6400000e89dbf6109a1e40f015dfceb0832c0e8856df1cc6b5d55a12704c261963ffffffff01a086010000000000232103b81eecef4a027975ea51e6d1220129ed21b6d97c17b27bbbe32a5b934561ba64ac00000000");
            var ctx = new ScriptEvaluationContext(this.networkMain);
            ctx.ScriptVerify = ScriptVerify.Mandatory | ScriptVerify.DerSig;
            bool passed = ctx.VerifyScript(spending.Inputs[0].ScriptSig, source.Outputs[0].ScriptPubKey, spending, 0, Money.Zero);
            Assert.True(passed);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestOPNIP()
        {
            // (x1 x2 -- x2)
            var scriptSig = new Script(Op.GetPushOp(1), Op.GetPushOp(2), Op.GetPushOp(3));
            var scriptPubKey = new Script(OpcodeType.OP_NIP);
            var ctx = new ScriptEvaluationContext(this.networkMain);
            ctx.VerifyScript(scriptSig, scriptPubKey, CreateDummy(), 0, Money.Zero);
            Assert.Equal(2, ctx.Stack.Count);
            byte[][] actual = new[] { ctx.Stack.Top(-2), ctx.Stack.Top(-1) };
            byte[][] expected = new[] { Op.GetPushOp(1).PushData, Op.GetPushOp(3).PushData };
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.True(actual[i].SequenceEqual(expected[i]));
            }
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void OP_2ROT()
        {
            // (x1 x2 x3 x4 x5 x6 -- x3 x4 x5 x6 x1 x2)
            var scriptSig = new Script(Op.GetPushOp(1), Op.GetPushOp(2), Op.GetPushOp(3), Op.GetPushOp(4), Op.GetPushOp(5), Op.GetPushOp(6));
            var scriptPubKey = new Script(OpcodeType.OP_2ROT);
            var ctx = new ScriptEvaluationContext(this.networkMain);
            ctx.VerifyScript(scriptSig, scriptPubKey, CreateDummy(), 0, Money.Zero);
            Assert.Equal(6, ctx.Stack.Count);
            byte[][] actual = new[] {
                ctx.Stack.Top(-6),
                ctx.Stack.Top(-5),
                ctx.Stack.Top(-4),
                ctx.Stack.Top(-3) ,
                ctx.Stack.Top(-2),
                ctx.Stack.Top(-1) };
            byte[][] expected = new[]
            {
                Op.GetPushOp(3).PushData,
                Op.GetPushOp(4).PushData,
                Op.GetPushOp(5).PushData,
                Op.GetPushOp(6).PushData,
                Op.GetPushOp(1).PushData,
                Op.GetPushOp(2).PushData,
            };
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.True(actual[i].SequenceEqual(expected[i]));
            }
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestOPTuck()
        {
            // (x1 x2 -- x2 x1 x2)
            var scriptSig = new Script(Op.GetPushOp(1), Op.GetPushOp(2), Op.GetPushOp(3));
            var scriptPubKey = new Script(OpcodeType.OP_TUCK);
            var ctx = new ScriptEvaluationContext(this.networkMain);
            ctx.VerifyScript(scriptSig, scriptPubKey, CreateDummy(), 0, Money.Zero);
            Assert.Equal(4, ctx.Stack.Count);
            byte[][] actual = new[] { ctx.Stack.Top(-3), ctx.Stack.Top(-2), ctx.Stack.Top(-1) };
            byte[][] expected = new[] { Op.GetPushOp(3).PushData, Op.GetPushOp(2).PushData, Op.GetPushOp(3).PushData };
            for (int i = 0; i < actual.Length; i++)
            {
                Assert.True(actual[i].SequenceEqual(expected[i]));
            }
        }

        private Transaction CreateDummy()
        {
            Transaction transaction = this.networkMain.CreateTransaction();
            transaction.AddInput(TxIn.CreateCoinbase(200));
            return transaction;
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanCheckSegwitSig()
        {
            Transaction tx = this.networkMain.CreateTransaction("010000000001015d896079097272b13ed9cb22acfabeca9ce83f586d98cc15a08ea2f9c558013b0300000000ffffffff01605af40500000000160014a8cbb5eca9af499cecaa08457690ab367f23d95b0247304402200b6baba4287f3321ae4ec6ba66420d9a48c3f3bc331603e7dca6b12ca75cce6102207fa582041b025605c0474b99a2d3ab5080d6ea14ae3a50b7de92596abf40fb4b012102cdfc0f4701e0c8db3a0913de5f635d0ea76663a8f80925567358d558603fae3500000000");
            CanCheckSegwitSigCore(tx, 0, Money.Coins(1.0m));

            Transaction toCheck = this.networkMain.CreateTransaction("01000000000103b019e2344634c5b34aeb867f2cd8b09dbbd95b5bf8c5d56d58be1dd9077f9d3a00000000da0047304402201b2be1016abd4df4ca699e0430b97bc8dcd4c1c90b6a6ee382be75f42956566402205ab38fddace15ba4b2c4dbacc6793bb1f35a371aa8386f1348bd65dfeda9657201483045022100db1dbea1a5d05ff7daf6d106931ab701a29d2dddd8cd7781e9eb7fefd31139790220319eb8a238e6c635ebe2960f5960eeb96371f5a38503cf41aa89a33807c8b6a50147522102a96e9843b846b8cc3277ea54638f1454378219854ef89c81a8a4e9217f1f3ca02103d5feb2e2f2fa1403ede18aaac7631dd2c9a893953a9ab338e7d9fa749d91f03b52aeffffffffb019e2344634c5b34aeb867f2cd8b09dbbd95b5bf8c5d56d58be1dd9077f9d3a01000000db00483045022100aec68f5760337efdf425007387f094df284a576e824492597b0d046e038034100220434cb22f056e97cd823a13751c482a9f2d3fb956abcfa69db4dcd2679379070101483045022100c7ce0a9617cbcaa9308758092d336b228f67d358ad25a786711a87a29e2f72d102203d608bf6a4416e9493a5d89552633da300e9a237811e9affea3cda3320a3257c0147522102c4bd91a554815c73814848b311051c43ad6a75810269e1ff0eb9c13d828fc6fb21031035e69a48e04bc4d6315590620f784ab79d8369d122bd45ad7e77c81ac1cb1c52aeffffffffbcf750fad5ddd1909d8b3e2edda94f7ae3c866952932823763291b9467e3b9580000000023220020e0be53749d09a8e2d3843633cf11133e51e73944334d11a147f1ae53f1c3dfe5ffffffff019cbaf0080000000017a9148d52e4999751ec43c07eb371119f8c45047d26dc870000040047304402205bdc03fac6c3be92309e4fdd1572147ca56210dbb4413539874a4e3b0670ac0b02206422cd069e6078bcdc8f698ff77aed65566b6fa1ff028cc322d14d036d2c192401473044022022fa0bda2e8e21716b9d74499665e4f31cbcf2bf49d0b535188e7e196e8e90d8022076ad55655fbd54637c0cf5bbd7f07905446e23a621f82a940cb07677dab2f8fe0147522102d01cf4abc1b6c22cc0e0e43e5277f1a7fb544eca52244cd4cb88bef5943c5563210284a2ffb3e6b6ac0ac9444b0ecd9856f79b53bbd3100894ec6dc80e6e956edbeb52ae00000000");

            ScriptError error;
            Assert.True(toCheck.Inputs.AsIndexedInputs().Skip(0).First().VerifyScript(KnownNetworks.Main, new Script("OP_HASH160 442afa4f034468652c571202da0bf277cb729def OP_EQUAL"), Money.Satoshis(100000), ScriptVerify.Mandatory, out error));
        }

        private static void CanCheckSegwitSigCore(Transaction tx, int input, Money amount, string scriptCodeHex = null)
        {
            Script scriptCode = null;
            if (scriptCodeHex == null)
            {
                PayToWitPubkeyHashScriptSigParameters param1 = PayToWitPubKeyHashTemplate.Instance.ExtractWitScriptParameters(KnownNetworks.Main, tx.Inputs[input].WitScript);
                Assert.NotNull(param1);
                WitKeyId param2 = PayToWitPubKeyHashTemplate.Instance.ExtractScriptPubKeyParameters(KnownNetworks.Main, param1.PublicKey.GetSegwitAddress(KnownNetworks.Main).Hash.ScriptPubKey);
                Assert.Equal(param1.PublicKey.WitHash, param2);
                scriptCode = param1.ScriptPubKey;
            }
            else
            {
                scriptCode = new Script(Encoders.Hex.DecodeData(scriptCodeHex));
            }

            ScriptError err;
            bool r = Script.VerifyScript(KnownNetworks.Main, scriptCode, tx, 0, amount, out err);
            Assert.True(r);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanParseWitTransaction()
        {
            string hex = "010000000001015d896079097272b13ed9cb22acfabeca9ce83f586d98cc15a08ea2f9c558013b0300000000ffffffff01605af40500000000160014a8cbb5eca9af499cecaa08457690ab367f23d95b0247304402200b6baba4287f3321ae4ec6ba66420d9a48c3f3bc331603e7dca6b12ca75cce6102207fa582041b025605c0474b99a2d3ab5080d6ea14ae3a50b7de92596abf40fb4b012102cdfc0f4701e0c8db3a0913de5f635d0ea76663a8f80925567358d558603fae3500000000";
            Transaction tx = this.networkMain.CreateTransaction(hex);
            byte[] bytes = tx.ToBytes();
            Assert.Equal(Encoders.Hex.EncodeData(bytes), hex);

            Assert.Equal("4b3580bbcceb12fee91abc7f9e8e7d092e981d4bb38339204c457a04316d949a", tx.GetHash().ToString());
            Assert.Equal("38331098fb804ef2e6dee7826a74b4af07e631a0f1082ffc063667ccb825d701", tx.GetWitHash().ToString());

            Transaction noWit = tx.WithOptions(TransactionOptions.None, this.networkMain.Consensus.ConsensusFactory);
            Assert.True(noWit.GetSerializedSize() < tx.GetSerializedSize());

            tx = this.networkMain.CreateTransaction("010000000001015d896079097272b13ed9cb22acfabeca9ce83f586d98cc15a08ea2f9c558013b0200000000ffffffff01605af40500000000160014a8cbb5eca9af499cecaa08457690ab367f23d95b02483045022100d3edd272c4ff247c36a1af34a2394859ece319f61ee85f759b94ec0ecd61912402206dbdc7c6ca8f7279405464d2d935b5e171dfd76656872f76399dbf333c0ac3a001fd08020000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000000100000000");

            ScriptError error;
            Assert.False(tx.Inputs.AsIndexedInputs().First().VerifyScript(KnownNetworks.Main, new Script("0 b7854eb547106248b136ca2bf48d8df2f1167588"), out error));
            Assert.Equal(ScriptError.EqualVerify, error);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void bip143Test()
        {
            Transaction tx = this.networkMain.CreateTransaction("0100000002fff7f7881a8099afa6940d42d1e7f6362bec38171ea3edf433541db4e4ad969f0000000000eeffffffef51e1b804cc89d182d279655c3aa89e815b1b309fe287d9b2b55d57b90ec68a0100000000ffffffff02202cb206000000001976a9148280b37df378db99f66f85c95a783a76ac7a6d5988ac9093510d000000001976a9143bde42dbee7e4dbe6a21b2d50ce2f0167faa815988ac11000000");
            uint256 h = Script.SignatureHash(KnownNetworks.Main, new Script(Encoders.Hex.DecodeData("76a9141d0f172a0ecb48aee1be1f2687d2963ae33f71a188ac")), tx, 1, SigHash.All, Money.Satoshis(0x23c34600L), HashVersion.Witness);
            Assert.Equal(new uint256(Encoders.Hex.DecodeData("c37af31116d1b27caf68aae9e3ac82f1477929014d5b917657d0eb49478cb670"), true), h);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void witnessHasPushSizeLimit()
        {
            var bob = new Key();
            Transaction tx = this.networkMain.CreateTransaction();
            tx.Outputs.Add(new TxOut(Money.Coins(1.0m), bob.PubKey.ScriptPubKey.WitHash));
            var coin = new ScriptCoin(tx.Outputs.AsCoins().First(), bob.PubKey.ScriptPubKey);

            Transaction spending = this.networkMain.CreateTransaction();
            spending.AddInput(tx, 0);
            spending.Sign(KnownNetworks.Main, bob, coin);
            ScriptError error;
            Assert.True(spending.Inputs.AsIndexedInputs().First().VerifyScript(KnownNetworks.Main, coin, out error));
            spending.Inputs[0].WitScript = new WitScript(new[] { new byte[521] }.Concat(spending.Inputs[0].WitScript.Pushes).ToArray());
            Assert.False(spending.Inputs.AsIndexedInputs().First().VerifyScript(KnownNetworks.Main, coin, out error));
            Assert.Equal(ScriptError.PushSize, error);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void DoNotThrowsWithSatoshiFormatAndNoOutputs()
        {
            Transaction tx = this.networkMain.CreateTransaction("02000000010000000000000000000000000000000000000000000000000000000000000000ffffffff0401700101ffffffff02" +
                       "00f2052a0100000023210295aefb5b15cd9204f18ceda653ebeaada10c69b6ef7f757450c5d66c0f0ebb8dac0000000000000000266a24aa21a9" +
                       "ede2f61c3f71d1defd3fa999dfa36953755c690689799962b48bebd836974e8cf900000000");

            tx.ToString(KnownNetworks.Main, RawFormat.Satoshi);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        //http://brainwallet.org/#tx
        public void CanParseTransaction()
        {
            TestCase[] tests = TestCase.read_json(TestDataLocations.GetFileFromDataFolder("can_parse_transaction.json"));

            foreach (dynamic test in tests.Select(t => t.GetDynamic(0)))
            {
                string raw = test.Raw;
                Transaction tx = this.networkMain.CreateTransaction(raw);
                Assert.Equal((int)test.JSON.vin_sz, tx.Inputs.Count);
                Assert.Equal((int)test.JSON.vout_sz, tx.Outputs.Count);
                Assert.Equal((uint)test.JSON.lock_time, (uint)tx.LockTime);

                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    TxIn actualVIn = tx.Inputs[i];
                    dynamic expectedVIn = test.JSON.@in[i];
                    Assert.Equal(uint256.Parse((string)expectedVIn.prev_out.hash), actualVIn.PrevOut.Hash);
                    Assert.Equal((uint)expectedVIn.prev_out.n, actualVIn.PrevOut.N);
                    if (expectedVIn.sequence != null)
                        Assert.Equal((uint)expectedVIn.sequence, (uint)actualVIn.Sequence);
                    Assert.Equal((string)expectedVIn.scriptSig, actualVIn.ScriptSig.ToString());
                    //Can parse the string
                    Assert.Equal((string)expectedVIn.scriptSig, (string)expectedVIn.scriptSig.ToString());
                }

                for (int i = 0; i < tx.Outputs.Count; i++)
                {
                    TxOut actualVOut = tx.Outputs[i];
                    dynamic expectedVOut = test.JSON.@out[i];
                    Assert.Equal((string)expectedVOut.scriptPubKey, actualVOut.ScriptPubKey.ToString());
                    Assert.Equal(Money.Parse((string)expectedVOut.value), actualVOut.Value);
                }
                string hash = (string)test.JSON.hash;
                var expectedHash = new uint256(Encoders.Hex.DecodeData(hash), false);
                Assert.Equal(expectedHash, tx.GetHash());
            }
        }

        //[Fact]
        //http://bitcoin.stackexchange.com/questions/25814/ecdsa-signature-and-the-z-value
        //http://www.nilsschneider.net/2013/01/28/recovering-bitcoin-private-keys.html
        private void PlayingWithSignatures()
        {
            var script1 = new Script("30440220d47ce4c025c35ec440bc81d99834a624875161a26bf56ef7fdc0f5d52f843ad1022044e1ff2dfd8102cf7a47c21d5c9fd5701610d04953c6836596b4fe9dd2f53e3e01 04dbd0c61532279cf72981c3584fc32216e0127699635c2789f549e0730c059b81ae133016a69c21e23f1859a95f06d52b7bf149a8f2fe4e8535c8a829b449c5ff");

            var script2 = new Script("30440220d47ce4c025c35ec440bc81d99834a624875161a26bf56ef7fdc0f5d52f843ad102209a5f1c75e461d7ceb1cf3cab9013eb2dc85b6d0da8c3c6e27e3a5a5b3faa5bab01 04dbd0c61532279cf72981c3584fc32216e0127699635c2789f549e0730c059b81ae133016a69c21e23f1859a95f06d52b7bf149a8f2fe4e8535c8a829b449c5ff");

            ECDSASignature sig1 = (PayToPubkeyHashTemplate.Instance.ExtractScriptSigParameters(KnownNetworks.Main, script1).TransactionSignature.Signature);
            ECDSASignature sig2 = (PayToPubkeyHashTemplate.Instance.ExtractScriptSigParameters(KnownNetworks.Main, script2).TransactionSignature.Signature);

            BigInteger n = ECKey.CURVE.N;
            var z1 = new BigInteger(1, Encoders.Hex.DecodeData("c0e2d0a89a348de88fda08211c70d1d7e52ccef2eb9459911bf977d587784c6e"));
            var z2 = new BigInteger(1, Encoders.Hex.DecodeData("17b0f41c8c337ac1e18c98759e83a8cccbc368dd9d89e5f03cb633c265fd0ddc"));

            BigInteger z = z1.Subtract(z2);
            BigInteger s = sig1.S.Subtract(sig2.S);
            BigInteger n2 = BigInteger.Two.Pow(256).Subtract(new BigInteger("432420386565659656852420866394968145599"));

            var expected = new Key(Encoders.Hex.DecodeData("c477f9f65c22cce20657faa5b2d1d8122336f851a508a1ed04e479c34985bf96"), fCompressedIn: false);

            var expectedBigInt = new BigInteger(1, Encoders.Hex.DecodeData("c477f9f65c22cce20657faa5b2d1d8122336f851a508a1ed04e479c34985bf96"));
            BigInteger priv = (z1.Multiply(sig2.S).Subtract(z2.Multiply(sig1.S)).Mod(n)).Divide(sig1.R.Multiply(sig1.S.Subtract(sig2.S)).Mod(n));
            Assert.Equal(expectedBigInt.ToString(), priv.ToString());

        }

        protected virtual BigInteger CalculateE(BigInteger n, byte[] message)
        {
            int messageBitLength = message.Length * 8;
            var trunc = new BigInteger(1, message);

            if (n.BitLength < messageBitLength)
            {
                trunc = trunc.ShiftRight(messageBitLength - n.BitLength);
            }

            return trunc;
        }

        private ECDSASignature ToPositive(ECDSASignature sig)
        {
            return new ECDSASignature(new BigInteger(1, sig.R.ToByteArray()), new BigInteger(1, sig.S.ToByteArray()));
        }

        public enum HashModification
        {
            NoModification,
            Modification,
            Invalid
        }

        private class Combinaison
        {
            public SigHash SigHash
            {
                get;
                set;
            }
            public bool Segwit
            {
                get;
                set;
            }
        }

        private IEnumerable<Combinaison> GetCombinaisons()
        {
            foreach (SigHash sighash in new[] { SigHash.All, SigHash.Single, SigHash.None })
            {
                foreach (bool anyoneCanPay in new[] { false, true })
                {
                    foreach (bool segwit in new[] { false, true })
                    {
                        yield return new Combinaison()
                        {
                            SigHash = anyoneCanPay ? sighash | SigHash.AnyoneCanPay : sighash,
                            Segwit = segwit
                        };
                    }
                }
            }
        }

        [Fact]
        public void Play2()
        {
            var secret = new BitcoinSecret("L5AQtV2HDm4xGsseLokK2VAT2EtYKcTm3c7HwqnJBFt9LdaQULsM");
            Combinaison[] all = GetCombinaisons().ToArray();
            while (true)
            {
                Utils.Shuffle(all);
                if (((uint)all[0].SigHash & 0x1f) != (uint)SigHash.All)
                    break;
            }
            int i = 0;
            Transaction tx = this.networkMain.CreateTransaction();
            var coins = new List<ICoin>();
            foreach (Combinaison combinaison in all)
            {
                Script scriptPubKey = combinaison.Segwit ? secret.PubKey.WitHash.ScriptPubKey : secret.PubKey.Hash.ScriptPubKey;
                ICoin coin = new Coin(
                                new uint256("0000000000000000000000000000000000000000000000000000000000000100"), (uint)i,
                                Money.Satoshis(1000 + i), scriptPubKey);
                tx.Inputs.Add(new TxIn(coin.Outpoint));
                tx.AddOutput(new TxOut(Money.Satoshis(1000 + i), new Script(OpcodeType.OP_TRUE)));
                coins.Add(coin);
                i++;
            }

            var builder2 = new TransactionBuilder(this.networkMain);
            builder2.SetTransactionPolicy(new StandardTransactionPolicy(this.networkMain)
            {
                CheckFee = false,
                MinRelayTxFee = null,
                CheckScriptPubKey = false
            });
            builder2.AddCoins(coins.ToArray());
            builder2.AddKeys(secret);
            builder2.SignTransactionInPlace(tx);
            bool verified = builder2.Verify(tx);

            var output = new StringBuilder();
            output.Append("[\"Transaction mixing all SigHash and segwit and non segwit inputs");

            output.Append("\"],");
            output.AppendLine();
            WriteTest(output, coins.OfType<Coin>().ToList(), tx);


            //var scriptPubKey = new Script(OpcodeType.OP_16, Op.GetPushOp(new byte[] { 0, 1 }));
            //ICoin coin1 = new Coin(
            //                    new uint256("0000000000000000000000000000000000000000000000000000000000000100"), 0,
            //                    Money.Satoshis(1000), scriptPubKey);
            //Transaction tx = new Transaction();
            //tx.Inputs.Add(new TxIn(coin1.Outpoint)
            //{
            //    ScriptSig = new Script(OpcodeType.OP_1)
            //});
            //tx.Outputs.Add(new TxOut(Money.Zero, new Script(OpcodeType.OP_TRUE)));
            //var verified = tx.Inputs.AsIndexedInputs().First().VerifyScript(coin1, ScriptVerify.P2SH | ScriptVerify.Witness);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanCacheHashes()
        {
            Transaction tx = this.networkMain.CreateTransaction();
            uint256 original = tx.GetHash();
            tx.Version = 4;
            Assert.True(tx.GetHash() != original);

            tx.CacheHashes();
            original = tx.GetHash();
            tx.Version = 5;
            Assert.True(tx.GetHash() == original);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CheckScriptCoinIsCoherent()
        {
            var key = new Key();
            Coin c = RandomCoin(Money.Zero, key.PubKey.ScriptPubKey.Hash);

            //P2SH
            var scriptCoin = new ScriptCoin(c, key.PubKey.ScriptPubKey);
            Assert.True(scriptCoin.RedeemType == RedeemType.P2SH);
            Assert.True(scriptCoin.IsP2SH);
            Assert.True(scriptCoin.GetHashVersion(this.networkMain) == HashVersion.Original);

            //P2SH(P2WPKH)
            c.ScriptPubKey = key.PubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey;
            scriptCoin = new ScriptCoin(c, key.PubKey.WitHash.ScriptPubKey);
            Assert.True(scriptCoin.RedeemType == RedeemType.P2SH);
            Assert.True(scriptCoin.IsP2SH);
            Assert.True(scriptCoin.GetHashVersion(this.networkMain) == HashVersion.Witness);

            //P2WSH
            c.ScriptPubKey = key.PubKey.ScriptPubKey.WitHash.ScriptPubKey;
            scriptCoin = new ScriptCoin(c, key.PubKey.ScriptPubKey);
            Assert.True(scriptCoin.RedeemType == RedeemType.WitnessV0);
            Assert.True(!scriptCoin.IsP2SH);
            Assert.True(scriptCoin.GetHashVersion(this.networkMain) == HashVersion.Witness);

            //P2SH(P2WSH)
            c.ScriptPubKey = key.PubKey.ScriptPubKey.WitHash.ScriptPubKey.Hash.ScriptPubKey;
            scriptCoin = new ScriptCoin(c, key.PubKey.ScriptPubKey);
            Assert.True(scriptCoin.RedeemType == RedeemType.WitnessV0);
            Assert.True(scriptCoin.IsP2SH);
            Assert.True(scriptCoin.GetHashVersion(this.networkMain) == HashVersion.Witness);

            Assert.Throws<ArgumentException>(() => ScriptCoin.Create(KnownNetworks.Main, c, key.PubKey.ScriptPubKey.WitHash.ScriptPubKey));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CheckWitnessSize()
        {
            var scriptPubKey = new Script(OpcodeType.OP_DROP, OpcodeType.OP_TRUE);
            ICoin coin1 = new Coin(
                                new uint256("0000000000000000000000000000000000000000000000000000000000000100"), 0,
                                Money.Satoshis(1000), scriptPubKey.WitHash.ScriptPubKey);
            coin1 = new ScriptCoin(coin1, scriptPubKey);
            Transaction tx = this.networkMain.CreateTransaction();
            tx.Inputs.Add(new TxIn(coin1.Outpoint));
            tx.Inputs[0].ScriptSig = tx.Inputs[0].ScriptSig + Op.GetPushOp(new byte[520]);
            tx.Inputs[0].ScriptSig = tx.Inputs[0].ScriptSig + Op.GetPushOp(scriptPubKey.ToBytes());
            tx.Inputs[0].WitScript = tx.Inputs[0].ScriptSig;
            tx.Inputs[0].ScriptSig = Script.Empty;
            tx.Outputs.Add(new TxOut(Money.Zero, new Script(OpcodeType.OP_TRUE)));
            ScriptError error;
            Assert.True(tx.Inputs.AsIndexedInputs().First().VerifyScript(KnownNetworks.Main, coin1, ScriptVerify.Standard, out error));

            tx = this.networkMain.CreateTransaction();
            tx.Inputs.Add(new TxIn(coin1.Outpoint));
            tx.Inputs[0].ScriptSig = tx.Inputs[0].ScriptSig + Op.GetPushOp(new byte[521]);
            tx.Inputs[0].ScriptSig = tx.Inputs[0].ScriptSig + Op.GetPushOp(scriptPubKey.ToBytes());
            tx.Inputs[0].WitScript = tx.Inputs[0].ScriptSig;
            tx.Inputs[0].ScriptSig = Script.Empty;
            tx.Outputs.Add(new TxOut(Money.Zero, new Script(OpcodeType.OP_TRUE)));
            Assert.False(tx.Inputs.AsIndexedInputs().First().VerifyScript(KnownNetworks.Main, coin1, ScriptVerify.Standard, out error));
            Assert.True(error == ScriptError.PushSize);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestSigHashes()
        {
            var secret = new BitcoinSecret("L5AQtV2HDm4xGsseLokK2VAT2EtYKcTm3c7HwqnJBFt9LdaQULsM");
            Key key = secret.PrivateKey;
            var output = new StringBuilder();
            foreach (bool segwit in new[] { false, true })
            {
                foreach (SigHash flag in new[] { SigHash.Single, SigHash.None, SigHash.All })
                {
                    foreach (bool anyoneCanPay in new[] { true, false })
                    {
                        var invalidChanges = new List<string>();
                        SigHash actualFlag = anyoneCanPay ? flag | SigHash.AnyoneCanPay : flag;
                        var signatures = new List<TransactionSignature>();
                        var transactions = new List<Transaction>();
                        foreach (HashModification modification in new[] { HashModification.NoModification, HashModification.Modification, HashModification.Invalid })
                        {
                            var knownCoins = new List<Coin>();

                            var coin1 = new Coin(
                                new uint256("0000000000000000000000000000000000000000000000000000000000000100"), 0,
                                Money.Satoshis(1000), new Script(OpcodeType.OP_TRUE));
                            var signedCoin = new Coin(
                                new uint256("0000000000000000000000000000000000000000000000000000000000000100"), 1,
                                Money.Satoshis(2000), segwit ? key.PubKey.WitHash.ScriptPubKey : key.PubKey.Hash.ScriptPubKey);
                            var coin2 = new Coin(
                                new uint256("0000000000000000000000000000000000000000000000000000000000000100"), 2,
                                Money.Satoshis(3000), new Script(OpcodeType.OP_TRUE));
                            knownCoins.AddRange(new[] { coin1, signedCoin, coin2 });
                            var coin4 = new Coin(
                                new uint256("0000000000000000000000000000000000000000000000000000000000000100"), 3,
                                Money.Satoshis(4000), new Script(OpcodeType.OP_TRUE));

                            Transaction txx = this.networkMain.CreateTransaction();
                            if (anyoneCanPay && modification == HashModification.Modification)
                            {
                                if (flag != SigHash.Single)
                                {
                                    txx.Inputs.Add(new TxIn(coin2.Outpoint));
                                    txx.Inputs.Add(new TxIn(coin1.Outpoint));
                                    txx.Inputs.Add(new TxIn(signedCoin.Outpoint));
                                }
                                else
                                {
                                    txx.Inputs.Add(new TxIn(coin2.Outpoint));
                                    txx.Inputs.Add(new TxIn(signedCoin.Outpoint));
                                    txx.Inputs.Add(new TxIn(coin1.Outpoint));
                                }
                                txx.Inputs.Add(new TxIn(coin4.Outpoint));
                                knownCoins.Add(coin4);
                            }
                            else if (!anyoneCanPay && modification == HashModification.Invalid)
                            {
                                txx.Inputs.Add(new TxIn(coin1.Outpoint));
                                txx.Inputs.Add(new TxIn(signedCoin.Outpoint));
                                txx.Inputs.Add(new TxIn(coin4.Outpoint));
                                knownCoins.Remove(coin2);
                                knownCoins.Add(coin4);
                                invalidChanges.Add("third input replaced");
                            }
                            else
                            {
                                txx.Inputs.Add(new TxIn(coin1.Outpoint));
                                txx.Inputs.Add(new TxIn(signedCoin.Outpoint));
                                txx.Inputs.Add(new TxIn(coin2.Outpoint));
                            }

                            if (flag == SigHash.All)
                            {
                                txx.Outputs.Add(new TxOut(coin1.Amount, new Script(OpcodeType.OP_TRUE)));
                                txx.Outputs.Add(new TxOut(signedCoin.Amount, new Script(OpcodeType.OP_TRUE)));
                                txx.Outputs.Add(new TxOut(coin2.Amount, new Script(OpcodeType.OP_TRUE)));
                                if (modification == HashModification.Invalid)
                                {
                                    txx.Outputs[2].Value = coin2.Amount - Money.Satoshis(100);
                                    invalidChanges.Add("third output value changed");
                                }
                            }
                            else if (flag == SigHash.None)
                            {
                                if (modification == HashModification.Modification)
                                {
                                    Money bump = Money.Satoshis(50);
                                    txx.Outputs.Add(new TxOut(coin1.Amount - bump, new Script(OpcodeType.OP_TRUE)));
                                    txx.Outputs.Add(new TxOut(signedCoin.Amount - bump, new Script(OpcodeType.OP_TRUE)));
                                    txx.Outputs.Add(new TxOut(coin2.Amount - bump, new Script(OpcodeType.OP_FALSE)));
                                    txx.Outputs.Add(new TxOut(3 * bump, new Script(OpcodeType.OP_TRUE)));
                                }
                                else if (modification == HashModification.NoModification)
                                {
                                    txx.Outputs.Add(new TxOut(coin1.Amount, new Script(OpcodeType.OP_TRUE)));
                                    txx.Outputs.Add(new TxOut(signedCoin.Amount, new Script(OpcodeType.OP_TRUE)));
                                    txx.Outputs.Add(new TxOut(coin2.Amount, new Script(OpcodeType.OP_TRUE)));
                                }
                                else if (modification == HashModification.Invalid)
                                {
                                    TxIn input = txx.Inputs.FirstOrDefault(i => i.PrevOut == signedCoin.Outpoint);
                                    input.Sequence = 1;
                                    invalidChanges.Add("input sequence changed");
                                }
                            }
                            else if (flag == SigHash.Single)
                            {
                                int index = txx.Inputs.Select((txin, i) => txin.PrevOut == signedCoin.Outpoint ? i : -1).Where(ii => ii != -1).FirstOrDefault();
                                foreach (Coin coin in knownCoins)
                                {
                                    txx.Outputs.Add(new TxOut(coin.Amount, new Script(OpcodeType.OP_TRUE)));
                                }

                                if (modification == HashModification.Modification)
                                {
                                    TxOut signed = txx.Outputs[index];
                                    TxOut[] outputs = txx.Outputs.ToArray();
                                    Utils.Shuffle(outputs, 50);
                                    int newIndex = Array.IndexOf(outputs, signed);
                                    if (newIndex == index)
                                        throw new InvalidOperationException();
                                    TxOut temp = outputs[index];
                                    outputs[index] = signed;
                                    outputs[newIndex] = temp;
                                    txx.Outputs.Clear();
                                    txx.Outputs.AddRange(outputs);
                                    Money bumps = Money.Zero;
                                    for (int i = 0; i < txx.Outputs.Count; i++)
                                    {
                                        if (i != index)
                                        {
                                            Money bump = Money.Satoshis(100);
                                            bumps += bump;
                                            txx.Outputs[i].Value -= bump;
                                        }
                                    }
                                    txx.Outputs.Add(new TxOut(bumps, new Script(OpcodeType.OP_TRUE)));
                                }
                                else if (modification == HashModification.Invalid)
                                {
                                    txx.Outputs[index].Value -= Money.Satoshis(100);
                                    invalidChanges.Add("same index output value changed");
                                }
                            }

                            if (anyoneCanPay & modification == HashModification.Modification)
                            {
                                foreach (Coin coin in knownCoins)
                                {
                                    if (coin != signedCoin)
                                        coin.Amount += Money.Satoshis(100);
                                }
                            }

                            var builder = new TransactionBuilder(this.networkMain);
                            builder.SetTransactionPolicy(new StandardTransactionPolicy(this.networkMain)
                            {
                                CheckFee = false,
                                CheckScriptPubKey = false,
                                MinRelayTxFee = null
                            });
                            builder.StandardTransactionPolicy.ScriptVerify &= ~ScriptVerify.NullFail;
                            builder.AddKeys(secret);
                            builder.AddCoins(knownCoins);
                            if (txx.Outputs.Count == 0)
                                txx.Outputs.Add(new TxOut(coin1.Amount, new Script(OpcodeType.OP_TRUE)));
                            Transaction result = builder.SignTransaction(txx, actualFlag);
                            Assert.True(builder.Verify(result));

                            if (flag == SigHash.None)
                            {
                                Transaction clone = this.networkMain.CreateTransaction(result.ToBytes());
                                foreach (TxIn input in clone.Inputs)
                                {
                                    if (input.PrevOut != signedCoin.Outpoint)
                                        input.Sequence = 2;
                                }
                                Assert.True(builder.Verify(clone));
                                TxIn signedClone = clone.Inputs.FirstOrDefault(ii => ii.PrevOut == signedCoin.Outpoint);
                                signedClone.Sequence = 2;
                                Assert.False(builder.Verify(clone));
                            }

                            TxIn signedInput = result.Inputs.FirstOrDefault(txin => txin.PrevOut == signedCoin.Outpoint);
                            TransactionSignature sig = PayToPubkeyHashTemplate.Instance.ExtractScriptSigParameters(KnownNetworks.Main, signedInput.WitScript == WitScript.Empty ? signedInput.ScriptSig : signedInput.WitScript.ToScript()).TransactionSignature;
                            if (modification != HashModification.Invalid)
                            {
                                signatures.Add(sig);
                                if (actualFlag != SigHash.All)
                                    Assert.True(transactions.All(s => s.GetHash() != result.GetHash()));
                                transactions.Add(result);
                                Assert.True(signatures.All(s => s.ToBytes().SequenceEqual(sig.ToBytes())));
                            }
                            else
                            {
                                Assert.Contains(signatures, s => !s.ToBytes().SequenceEqual(sig.ToBytes()));
                                TransactionSignature noModifSignature = signatures[0];
                                Script replacement = PayToPubkeyHashTemplate.Instance.GenerateScriptSig(noModifSignature, secret.PubKey);
                                if (signedInput.WitScript != WitScript.Empty)
                                {
                                    signedInput.WitScript = replacement;
                                }
                                else
                                {
                                    signedInput.ScriptSig = replacement;
                                }
                                TransactionPolicyError[] errors;
                                Assert.False(builder.Verify(result, out errors));
                                Assert.Single(errors);
                                var scriptError = (ScriptPolicyError)errors[0];
                                Assert.True(scriptError.ScriptError == ScriptError.EvalFalse);
                            }

                            if (segwit && actualFlag != SigHash.All && modification == HashModification.Invalid)
                            {
                                output.Append("[\"Witness with SigHash " + ToString(anyoneCanPay, flag));
                                if (transactions.Count == 2 && modification != HashModification.Invalid)
                                {
                                    output.Append(" (same signature as previous)");
                                }
                                else if (transactions.Count == 2 && modification == HashModification.Invalid)
                                {
                                    string changes = string.Join(", ", invalidChanges);
                                    output.Append(" (" + changes + ")");
                                }

                                output.Append("\"],");
                                output.AppendLine();
                                WriteTest(output, knownCoins, result);
                            }
                        }
                    }
                }
            }
        }

        private static void WriteTest(StringBuilder output, List<Coin> knownCoins, Transaction result)
        {
            output.Append("[[");

            var coinParts = new List<string>();
            var parts = new List<string>();
            foreach (Coin coin in knownCoins)
            {
                var coinOutput = new StringBuilder();
                coinOutput.Append("[\"");
                coinOutput.Append(coin.Outpoint.Hash);
                coinOutput.Append("\", ");
                coinOutput.Append(coin.Outpoint.N);
                coinOutput.Append(", \"");
                string script = coin.ScriptPubKey.ToString();
                string[] words = script.Split(' ');
                var scriptParts = new List<string>();
                foreach (string word in words)
                {
                    var scriptOutput = new StringBuilder();
                    if (word.StartsWith("OP_"))
                        scriptOutput.Append(word.Substring(3, word.Length - 3));
                    else if (word == "0")
                        scriptOutput.Append("0x00");
                    else if (word == "1")
                        scriptOutput.Append("0x51");
                    else if (word == "16")
                        scriptOutput.Append("0x60");
                    else
                    {
                        int size = word.Length / 2;
                        scriptOutput.Append("0x" + size.ToString("x2"));
                        scriptOutput.Append(" ");
                        scriptOutput.Append("0x" + word);
                    }
                    scriptParts.Add(scriptOutput.ToString());
                }
                coinOutput.Append(string.Join(" ", scriptParts));
                coinOutput.Append("\", ");
                coinOutput.Append(coin.Amount.Satoshi);
                coinOutput.Append("]");
                coinParts.Add(coinOutput.ToString());
            }
            output.Append(string.Join(",\n", coinParts));
            output.Append("],\n\"");
            output.Append(result.ToHex());
            output.Append("\", \"P2SH,WITNESS\"],\n\n");
        }

        private string ToString(bool anyoneCanPay, SigHash flag)
        {
            if (anyoneCanPay)
            {
                return flag.ToString() + "|AnyoneCanPay";
            }
            else
                return flag.ToString();
        }

        [Fact]
        [Trait("Core", "Core")]
        public void Tx_Valid()
        {
            // Read tests from test/data/tx_valid.json
            // Format is an array of arrays
            // Inner arrays are either [ "comment" ]
            // or [[[prevout hash, prevout index, prevout scriptPubKey], [input 2], ...],"], serializedTransaction, enforceP2SH
            // ... where all scripts are stringified scripts.
            TestCase[] tests = TestCase.read_json(TestDataLocations.GetFileFromDataFolder("tx_valid.json"));
            foreach (TestCase test in tests)
            {
                string strTest = test.ToString();
                //Skip comments
                if (!(test[0] is JArray))
                    continue;
                var inputs = (JArray)test[0];
                if (test.Count != 3 || !(test[1] is string) || !(test[2] is string))
                {
                    Assert.False(true, "Bad test: " + strTest);
                    continue;
                }

                var mapprevOutScriptPubKeys = new Dictionary<OutPoint, Script>();
                var mapprevOutScriptPubKeysAmount = new Dictionary<OutPoint, Money>();
                foreach (JToken vinput in inputs)
                {
                    var outpoint = new OutPoint(uint256.Parse(vinput[0].ToString()), int.Parse(vinput[1].ToString()));
                    mapprevOutScriptPubKeys[outpoint] = Script_Tests.ParseScript(vinput[2].ToString());
                    if (vinput.Count() >= 4)
                        mapprevOutScriptPubKeysAmount[outpoint] = Money.Satoshis(vinput[3].Value<long>());
                }

                Transaction tx = this.networkMain.CreateTransaction((string)test[1]);

                for (int i = 0; i < tx.Inputs.Count; i++)
                {
                    if (!mapprevOutScriptPubKeys.ContainsKey(tx.Inputs[i].PrevOut))
                    {
                        Assert.False(true, "Bad test: " + strTest);
                        continue;
                    }

                    bool valid = Script.VerifyScript(KnownNetworks.Main,
                        mapprevOutScriptPubKeys[tx.Inputs[i].PrevOut],
                        tx,
                        i,
                        mapprevOutScriptPubKeysAmount.TryGet(tx.Inputs[i].PrevOut),
                        ParseFlags(test[2].ToString())
                        , 0);
                    Assert.True(valid, strTest + " failed");
                    Assert.True(tx.Check() == TransactionCheckResult.Success);
                }
            }
        }

        private ScriptVerify ParseFlags(string strFlags)
        {
            ScriptVerify flags = 0;
            string[] words = strFlags.Split(',');


            // Note how NOCACHE is not included as it is a runtime-only flag.
            var mapFlagNames = new Dictionary<string, ScriptVerify>();
            if (mapFlagNames.Count == 0)
            {
                mapFlagNames["NONE"] = ScriptVerify.None;
                mapFlagNames["P2SH"] = ScriptVerify.P2SH;
                mapFlagNames["STRICTENC"] = ScriptVerify.StrictEnc;
                mapFlagNames["LOW_S"] = ScriptVerify.LowS;
                mapFlagNames["NULLDUMMY"] = ScriptVerify.NullDummy;
                mapFlagNames["CHECKLOCKTIMEVERIFY"] = ScriptVerify.CheckLockTimeVerify;
                mapFlagNames["CHECKSEQUENCEVERIFY"] = ScriptVerify.CheckSequenceVerify;
                mapFlagNames["DERSIG"] = ScriptVerify.DerSig;
                mapFlagNames["WITNESS"] = ScriptVerify.Witness;
                mapFlagNames["DISCOURAGE_UPGRADABLE_WITNESS_PROGRAM"] = ScriptVerify.DiscourageUpgradableWitnessProgram;
            }

            foreach (string word in words)
            {
                if (!mapFlagNames.ContainsKey(word))
                    Assert.False(true, "Bad test: unknown verification flag '" + word + "'");
                flags |= mapFlagNames[word];
            }

            return flags;
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void SequenceStructParsedCorrectly()
        {
            Assert.True(new Sequence() == 0xFFFFFFFFU);
            Assert.False(new Sequence().IsRelativeLock);
            Assert.False(new Sequence().IsRBF);

            Assert.True(new Sequence(1) == 1U);
            Assert.True(new Sequence(1).IsRelativeLock);
            Assert.True(new Sequence(1).IsRBF);
            Assert.True(new Sequence(1).LockType == SequenceLockType.Height);
            Assert.True(new Sequence(1) == 1U);
            Assert.True(new Sequence(1).LockHeight == 1);
            Assert.Throws<InvalidOperationException>(() => new Sequence(1).LockPeriod);

            Assert.True(new Sequence(0xFFFF).LockHeight == 0xFFFF);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Sequence(0xFFFF + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new Sequence(-1));

            TimeSpan time = TimeSpan.FromSeconds(512 * 0xFF);
            Assert.True(new Sequence(time) == (uint)(0xFF | 1 << 22));
            Assert.True(new Sequence(time).IsRelativeLock);
            Assert.True(new Sequence(time).IsRBF);
            Assert.Throws<ArgumentOutOfRangeException>(() => new Sequence(TimeSpan.FromSeconds(512 * (0xFFFF + 1))));
            new Sequence(TimeSpan.FromSeconds(512 * (0xFFFF)));
            Assert.Throws<InvalidOperationException>(() => new Sequence(time).LockHeight);
        }

        [Fact]
        [Trait("Core", "Core")]
        public void Tx_Invalid()
        {
            // Read tests from test/data/tx_valid.json
            // Format is an array of arrays
            // Inner arrays are either [ "comment" ]
            // or [[[prevout hash, prevout index, prevout scriptPubKey], [input 2], ...],"], serializedTransaction, enforceP2SH
            // ... where all scripts are stringified scripts.
            TestCase[] tests = TestCase.read_json(TestDataLocations.GetFileFromDataFolder("tx_invalid.json"));
            string comment = null;
            foreach (TestCase test in tests)
            {
                string strTest = test.ToString();
                //Skip comments
                if (!(test[0] is JArray))
                {
                    comment = test[0].ToString();
                    continue;
                }
                var inputs = (JArray)test[0];
                if (test.Count != 3 || !(test[1] is string) || !(test[2] is string))
                {
                    Assert.False(true, "Bad test: " + strTest);
                    continue;
                }
                var mapprevOutScriptPubKeys = new Dictionary<OutPoint, Script>();
                var mapprevOutScriptPubKeysAmount = new Dictionary<OutPoint, Money>();
                foreach (JToken vinput in inputs)
                {
                    var outpoint = new OutPoint(uint256.Parse(vinput[0].ToString()), int.Parse(vinput[1].ToString()));
                    mapprevOutScriptPubKeys[new OutPoint(uint256.Parse(vinput[0].ToString()), int.Parse(vinput[1].ToString()))] = Script_Tests.ParseScript(vinput[2].ToString());
                    if (vinput.Count() >= 4)
                        mapprevOutScriptPubKeysAmount[outpoint] = Money.Satoshis(vinput[3].Value<int>());
                }

                Transaction tx = this.networkMain.CreateTransaction((string)test[1]);

                bool fValid = true;
                fValid = tx.Check() == TransactionCheckResult.Success;
                for (int i = 0; i < tx.Inputs.Count && fValid; i++)
                {
                    if (!mapprevOutScriptPubKeys.ContainsKey(tx.Inputs[i].PrevOut))
                    {
                        Assert.False(true, "Bad test: " + strTest);
                        continue;
                    }

                    fValid = Script.VerifyScript(KnownNetworks.Main,
                       mapprevOutScriptPubKeys[tx.Inputs[i].PrevOut],
                       tx,
                       i,
                       mapprevOutScriptPubKeysAmount.TryGet(tx.Inputs[i].PrevOut),
                       ParseFlags(test[2].ToString())
                       , 0);
                }
                if (fValid)
                    Debugger.Break();
                Assert.True(!fValid, strTest + " failed");
            }
        }

        [Fact]
        [Trait("Core", "Core")]
        public void Test_Get()
        {
            byte[] dummyPubKey = TransactionSignature.Empty.ToBytes();

            var dummyPubKey2 = new byte[33];
            dummyPubKey2[0] = 0x02;
            //CBasicKeyStore keystore;
            //CCoinsView coinsDummy;
            var coins = new CoinsView(this.networkMain);//(coinsDummy);
            Transaction[] dummyTransactions = SetupDummyInputs(coins);//(keystore, coins);

            Transaction t1 = this.networkMain.CreateTransaction();
            t1.Inputs.AddRange(Enumerable.Range(0, 3).Select(_ => new TxIn()));
            t1.Inputs[0].PrevOut.Hash = dummyTransactions[0].GetHash();
            t1.Inputs[0].PrevOut.N = 1;
            t1.Inputs[0].ScriptSig += dummyPubKey;
            t1.Inputs[1].PrevOut.Hash = dummyTransactions[1].GetHash();
            t1.Inputs[1].PrevOut.N = 0;
            t1.Inputs[1].ScriptSig = t1.Inputs[1].ScriptSig + dummyPubKey + dummyPubKey2;
            t1.Inputs[2].PrevOut.Hash = dummyTransactions[1].GetHash();
            t1.Inputs[2].PrevOut.N = 1;
            t1.Inputs[2].ScriptSig = t1.Inputs[2].ScriptSig + dummyPubKey + dummyPubKey2;
            t1.Outputs.AddRange(Enumerable.Range(0, 2).Select(_ => new TxOut()));
            t1.Outputs[0].Value = 90 * Money.CENT;
            t1.Outputs[0].ScriptPubKey += OpcodeType.OP_1;

            Assert.True(StandardScripts.AreInputsStandard(KnownNetworks.Main, t1, coins));
            //Assert.Equal(coins.GetValueIn(t1), (50+21+22)*Money.CENT);

            //// Adding extra junk to the scriptSig should make it non-standard:
            t1.Inputs[0].ScriptSig += OpcodeType.OP_11;
            Assert.True(!StandardScripts.AreInputsStandard(KnownNetworks.Main, t1, coins));

            //// ... as should not having enough:
            t1.Inputs[0].ScriptSig = new Script();
            Assert.True(!StandardScripts.AreInputsStandard(KnownNetworks.Main, t1, coins));
        }

        private Transaction[] SetupDummyInputs(CoinsView coinsRet)
        {
            Transaction[] dummyTransactions = Enumerable.Range(0, 2).Select(_ => new Transaction()).ToArray();

            // Add some keys to the keystore:
            Key[] key = Enumerable.Range(0, 4).Select((_, i) => new Key(i % 2 != 0)).ToArray();


            // Create some dummy input transactions
            dummyTransactions[0].Outputs.AddRange(Enumerable.Range(0, 2).Select(_ => new TxOut()));
            dummyTransactions[0].Outputs[0].Value = 11 * Money.CENT;
            dummyTransactions[0].Outputs[0].ScriptPubKey = dummyTransactions[0].Outputs[0].ScriptPubKey + key[0].PubKey.ToBytes() + OpcodeType.OP_CHECKSIG;
            dummyTransactions[0].Outputs[1].Value = 50 * Money.CENT;
            dummyTransactions[0].Outputs[1].ScriptPubKey = dummyTransactions[0].Outputs[1].ScriptPubKey + key[1].PubKey.ToBytes() + OpcodeType.OP_CHECKSIG;
            coinsRet.AddTransaction(this.networkMain.Consensus, dummyTransactions[0], 0);


            dummyTransactions[1].Outputs.AddRange(Enumerable.Range(0, 2).Select(_ => new TxOut()));
            dummyTransactions[1].Outputs[0].Value = 21 * Money.CENT;
            dummyTransactions[1].Outputs[0].ScriptPubKey = key[2].PubKey.GetAddress(this.networkMain).ScriptPubKey;
            dummyTransactions[1].Outputs[1].Value = 22 * Money.CENT;
            dummyTransactions[1].Outputs[1].ScriptPubKey = key[3].PubKey.GetAddress(this.networkMain).ScriptPubKey;
            coinsRet.AddTransaction(this.networkMain.Consensus, dummyTransactions[1], 0);


            return dummyTransactions;
        }

        private class CKeyStore
        {
            internal List<Tuple<Key, PubKey>> _Keys = new List<Tuple<Key, PubKey>>();
            internal List<Script> _Scripts = new List<Script>();
            internal void AddKeyPubKey(Key key, PubKey pubkey)
            {
                this._Keys.Add(Tuple.Create(key, pubkey));
            }

            internal void RemoveKeyPubKey(Key key)
            {
                this._Keys.Remove(this._Keys.First(o => o.Item1 == key));
            }

            internal void AddCScript(Script scriptPubkey)
            {
                this._Scripts.Add(scriptPubkey);
            }
        }

        private void CreateCreditAndSpend(CKeyStore keystore, Script outscript, ref Transaction output, ref Transaction input, bool success = true)
        {
            Transaction outputm = this.networkMain.CreateTransaction();
            outputm.Version = 1;
            outputm.Inputs.Add(new TxIn());
            outputm.Inputs[0].PrevOut = new OutPoint();
            outputm.Inputs[0].ScriptSig = Script.Empty;
            outputm.Inputs[0].WitScript = new WitScript();
            outputm.Outputs.Add(new TxOut());
            outputm.Outputs[0].Value = Money.Satoshis(1);
            outputm.Outputs[0].ScriptPubKey = outscript;

            output = this.networkMain.CreateTransaction(outputm.ToBytes());

            Assert.True(output.Inputs.Count == 1);
            Assert.True(output.Inputs[0].ToBytes().SequenceEqual(outputm.Inputs[0].ToBytes()));
            Assert.True(output.Outputs.Count == 1);
            Assert.True(output.Inputs[0].ToBytes().SequenceEqual(outputm.Inputs[0].ToBytes()));
            Assert.True(!output.HasWitness);

            Transaction inputm = this.networkMain.CreateTransaction();
            inputm.Version = 1;
            inputm.Inputs.Add(new TxIn());
            inputm.Inputs[0].PrevOut.Hash = output.GetHash();
            inputm.Inputs[0].PrevOut.N = 0;
            inputm.Inputs[0].WitScript = new WitScript();
            inputm.Outputs.Add(new TxOut());
            inputm.Outputs[0].Value = Money.Satoshis(1);
            inputm.Outputs[0].ScriptPubKey = Script.Empty;
            bool ret = SignSignature(keystore, output, inputm, 0);
            Assert.True(ret == success);
            input = this.networkMain.CreateTransaction(inputm.ToBytes());
            Assert.True(input.Inputs.Count == 1);
            Assert.True(input.Inputs[0].ToBytes().SequenceEqual(inputm.Inputs[0].ToBytes()));
            Assert.True(input.Outputs.Count == 1);
            Assert.True(input.Outputs[0].ToBytes().SequenceEqual(inputm.Outputs[0].ToBytes()));

            if (!inputm.HasWitness)
            {
                Assert.True(!input.HasWitness);
            }
            else
            {
                Assert.True(input.HasWitness);
                Assert.True(input.Inputs[0].WitScript.ToBytes().SequenceEqual(inputm.Inputs[0].WitScript.ToBytes()));
            }
        }

        private bool SignSignature(CKeyStore keystore, Transaction txFrom, Transaction txTo, int nIn)
        {
            TransactionBuilder builder = CreateBuilder(keystore, txFrom);
            builder.SignTransactionInPlace(txTo);
            return builder.Verify(txTo);
        }

        private void CombineSignatures(CKeyStore keystore, Transaction txFrom, ref Transaction input1, Transaction input2)
        {
            TransactionBuilder builder = CreateBuilder(keystore, txFrom);
            input1 = builder.CombineSignatures(input1, input2);
        }

        private static TransactionBuilder CreateBuilder(CKeyStore keystore, Transaction txFrom)
        {
            Coin[] coins = txFrom.Outputs.AsCoins().ToArray();
            TransactionBuilder builder = new TransactionBuilder(KnownNetworks.Main)
            {
                StandardTransactionPolicy = new StandardTransactionPolicy(KnownNetworks.Main)
                {
                    CheckFee = false,
                    MinRelayTxFee = null,
                    CheckScriptPubKey = false
                }
            }
            .AddCoins(coins)
            .AddKeys(keystore._Keys.Select(k => k.Item1).ToArray())
            .AddKnownRedeems(keystore._Scripts.ToArray());
            return builder;
        }

        private void CheckWithFlag(Transaction output, Transaction input, ScriptVerify flags, bool success)
        {
            Transaction inputi = this.networkMain.CreateTransaction(input.ToBytes());
            var ctx = new ScriptEvaluationContext(this.networkMain);
            ctx.ScriptVerify = flags;
            bool ret = ctx.VerifyScript(inputi.Inputs[0].ScriptSig, output.Outputs[0].ScriptPubKey, new TransactionChecker(inputi, 0, output.Outputs[0].Value));
            Assert.True(ret == success);
        }

        private static Script PushAll(ContextStack<byte[]> values)
        {
            var result = new List<Op>();
            foreach (byte[] v in values.Reverse())
            {
                if (v.Length == 0)
                {
                    result.Add(OpcodeType.OP_0);
                }
                else
                {
                    result.Add(Op.GetPushOp(v));
                }
            }
            return new Script(result.ToArray());
        }

        private void ReplaceRedeemScript(TxIn input, Script redeemScript)
        {
            var ctx = new ScriptEvaluationContext(this.networkMain);
            ctx.ScriptVerify = ScriptVerify.StrictEnc;
            ctx.EvalScript(input.ScriptSig, this.networkMain.CreateTransaction(), 0);
            ContextStack<byte[]> stack = ctx.Stack;
            Assert.True(stack.Count > 0);
            stack.Pop();
            stack.Push(redeemScript.ToBytes());
            input.ScriptSig = PushAll(stack);
        }

        [Fact]
        [Trait("Core", "Core")]
        public void Test_witness()
        {
            var keystore = new CKeyStore();
            var keystore2 = new CKeyStore();
            var key1 = new Key(true);
            var key2 = new Key(true);
            var key3 = new Key(true);
            var key1L = new Key(false);
            var key2L = new Key(false);
            PubKey pubkey1 = key1.PubKey;
            PubKey pubkey2 = key2.PubKey;
            PubKey pubkey3 = key3.PubKey;
            PubKey pubkey1L = key1L.PubKey;
            PubKey pubkey2L = key2L.PubKey;
            keystore.AddKeyPubKey(key1, pubkey1);
            keystore.AddKeyPubKey(key2, pubkey2);
            keystore.AddKeyPubKey(key1L, pubkey1L);
            keystore.AddKeyPubKey(key2L, pubkey2L);
            Script scriptPubkey1, scriptPubkey2, scriptPubkey1L, scriptPubkey2L, scriptMulti;
            scriptPubkey1 = new Script(Op.GetPushOp(pubkey1.ToBytes()), OpcodeType.OP_CHECKSIG);
            scriptPubkey2 = new Script(Op.GetPushOp(pubkey2.ToBytes()), OpcodeType.OP_CHECKSIG);
            scriptPubkey1L = new Script(Op.GetPushOp(pubkey1L.ToBytes()), OpcodeType.OP_CHECKSIG);
            scriptPubkey2L = new Script(Op.GetPushOp(pubkey2L.ToBytes()), OpcodeType.OP_CHECKSIG);
            var oneandthree = new List<PubKey>();
            oneandthree.Add(pubkey1);
            oneandthree.Add(pubkey3);
            scriptMulti = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(2, oneandthree.ToArray());
            keystore.AddCScript(scriptPubkey1);
            keystore.AddCScript(scriptPubkey2);
            keystore.AddCScript(scriptPubkey1L);
            keystore.AddCScript(scriptPubkey2L);
            keystore.AddCScript(scriptMulti);
            keystore.AddCScript(GetScriptForWitness(scriptPubkey1));
            keystore.AddCScript(GetScriptForWitness(scriptPubkey2));
            keystore.AddCScript(GetScriptForWitness(scriptPubkey1L));
            keystore.AddCScript(GetScriptForWitness(scriptPubkey2L));
            keystore.AddCScript(GetScriptForWitness(scriptMulti));
            keystore2.AddCScript(scriptMulti);
            keystore2.AddCScript(GetScriptForWitness(scriptMulti));
            keystore2.AddKeyPubKey(key3, pubkey3);

            Transaction output1, output2;
            output1 = new Transaction();
            output2 = new Transaction();
            Transaction input1, input2;
            input1 = new Transaction();
            input2 = new Transaction();

            // Normal pay-to-compressed-pubkey.
            CreateCreditAndSpend(keystore, scriptPubkey1, ref output1, ref input1);
            CreateCreditAndSpend(keystore, scriptPubkey2, ref output2, ref input2);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, false);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // P2SH pay-to-compressed-pubkey.
            CreateCreditAndSpend(keystore, scriptPubkey1.Hash.ScriptPubKey, ref output1, ref input1);
            CreateCreditAndSpend(keystore, scriptPubkey2.Hash.ScriptPubKey, ref output2, ref input2);
            ReplaceRedeemScript(input2.Inputs[0], scriptPubkey1);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, true);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // Witness pay-to-compressed-pubkey (v0).
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey1), ref output1, ref input1);
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey2), ref output2, ref input2);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, true);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // P2SH witness pay-to-compressed-pubkey (v0).
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey1).Hash.ScriptPubKey, ref output1, ref input1);
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey2).Hash.ScriptPubKey, ref output2, ref input2);
            ReplaceRedeemScript(input2.Inputs[0], GetScriptForWitness(scriptPubkey1));
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, true);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // Normal pay-to-uncompressed-pubkey.
            CreateCreditAndSpend(keystore, scriptPubkey1L, ref output1, ref input1);
            CreateCreditAndSpend(keystore, scriptPubkey2L, ref output2, ref input2);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, false);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // P2SH pay-to-uncompressed-pubkey.
            CreateCreditAndSpend(keystore, scriptPubkey1L.Hash.ScriptPubKey, ref output1, ref input1);
            CreateCreditAndSpend(keystore, scriptPubkey2L.Hash.ScriptPubKey, ref output2, ref input2);
            ReplaceRedeemScript(input2.Inputs[0], scriptPubkey1L);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, true);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // Witness pay-to-uncompressed-pubkey (v1).
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey1L), ref output1, ref input1);
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey2L), ref output2, ref input2);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, true);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // P2SH witness pay-to-uncompressed-pubkey (v1).
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey1L).Hash.ScriptPubKey, ref output1, ref input1);
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptPubkey2L).Hash.ScriptPubKey, ref output2, ref input2);
            ReplaceRedeemScript(input2.Inputs[0], GetScriptForWitness(scriptPubkey1L));
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Witness | ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
            CheckWithFlag(output1, input2, 0, true);
            CheckWithFlag(output1, input2, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input2, ScriptVerify.Witness | ScriptVerify.P2SH, false);
            CheckWithFlag(output1, input2, ScriptVerify.Standard, false);

            // Normal 2-of-2 multisig
            CreateCreditAndSpend(keystore, scriptMulti, ref output1, ref input1, false);
            CheckWithFlag(output1, input1, 0, false);
            CreateCreditAndSpend(keystore2, scriptMulti, ref output2, ref input2, false);
            CheckWithFlag(output2, input2, 0, false);
            Assert.True(output1.ToBytes().SequenceEqual(output2.ToBytes()));
            CombineSignatures(keystore, output1, ref input1, input2);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);

            // P2SH 2-of-2 multisig
            CreateCreditAndSpend(keystore, scriptMulti.Hash.ScriptPubKey, ref output1, ref input1, false);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, false);
            CreateCreditAndSpend(keystore2, scriptMulti.Hash.ScriptPubKey, ref output2, ref input2, false);
            CheckWithFlag(output2, input2, 0, true);
            CheckWithFlag(output2, input2, ScriptVerify.P2SH, false);
            Assert.True(output1.ToBytes().SequenceEqual(output2.ToBytes()));
            CombineSignatures(keystore, output1, ref input1, input2);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);

            // Witness 2-of-2 multisig
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptMulti), ref output1, ref input1, false);
            CheckWithFlag(output1, input1, 0, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH | ScriptVerify.Witness, false);
            CreateCreditAndSpend(keystore2, GetScriptForWitness(scriptMulti), ref output2, ref input2, false);
            CheckWithFlag(output2, input2, 0, true);
            CheckWithFlag(output2, input2, ScriptVerify.P2SH | ScriptVerify.Witness, false);
            Assert.True(output1.ToBytes().SequenceEqual(output2.ToBytes()));
            CombineSignatures(keystore, output1, ref input1, input2);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH | ScriptVerify.Witness, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);

            // P2SH witness 2-of-2 multisig
            CreateCreditAndSpend(keystore, GetScriptForWitness(scriptMulti).Hash.ScriptPubKey, ref output1, ref input1, false);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH, true);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH | ScriptVerify.Witness, false);
            CreateCreditAndSpend(keystore2, GetScriptForWitness(scriptMulti).Hash.ScriptPubKey, ref output2, ref input2, false);
            CheckWithFlag(output2, input2, ScriptVerify.P2SH, true);
            CheckWithFlag(output2, input2, ScriptVerify.P2SH | ScriptVerify.Witness, false);
            Assert.True(output1.ToBytes().SequenceEqual(output2.ToBytes()));
            CombineSignatures(keystore, output1, ref input1, input2);
            CheckWithFlag(output1, input1, ScriptVerify.P2SH | ScriptVerify.Witness, true);
            CheckWithFlag(output1, input1, ScriptVerify.Standard, true);
        }

        private Script GetScriptForWitness(Script scriptPubKey)
        {
            PubKey pubkey = PayToPubkeyTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey);
            if (pubkey != null)
                return new Script(OpcodeType.OP_0, Op.GetPushOp(pubkey.Hash.ToBytes()));
            KeyId pkh = PayToPubkeyHashTemplate.Instance.ExtractScriptPubKeyParameters(scriptPubKey);
            if (pkh != null)
                return new Script(OpcodeType.OP_0, Op.GetPushOp(pkh.ToBytes()));

            return new Script(OpcodeType.OP_0, Op.GetPushOp(scriptPubKey.WitHash.ToBytes()));
        }

        [Fact]
        [Trait("Core", "Core")]
        public void Test_IsStandard()
        {
            var coins = new CoinsView(this.networkMain);
            Transaction[] dummyTransactions = SetupDummyInputs(coins);

            var t = new Transaction();
            t.Inputs.Add(new TxIn());
            t.Inputs[0].PrevOut.Hash = dummyTransactions[0].GetHash();
            t.Inputs[0].PrevOut.N = 1;
            t.Inputs[0].ScriptSig = new Script(Op.GetPushOp(new byte[65]));
            t.Outputs.Add(new TxOut());
            t.Outputs[0].Value = 90 * Money.CENT;
            var key = new Key(true);
            t.Outputs[0].ScriptPubKey = PayToPubkeyHashTemplate.Instance.GenerateScriptPubKey(key.PubKey.Hash);

            Assert.True(StandardScripts.IsStandardTransaction(t, this.networkMain));

            t.Outputs[0].Value = 501; //dust
            Assert.True(!StandardScripts.IsStandardTransaction(t, this.networkMain));

            t.Outputs[0].Value = 2730; // not dust
            Assert.True(StandardScripts.IsStandardTransaction(t, this.networkMain));

            t.Outputs[0].ScriptPubKey = new Script() + OpcodeType.OP_1;
            Assert.True(!StandardScripts.IsStandardTransaction(t, this.networkMain));

            // 80-byte TX_NULL_DATA (standard)
            t.Outputs[0].ScriptPubKey = new Script() + OpcodeType.OP_RETURN + ParseHex("04678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef3804678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38");
            Assert.True(StandardScripts.IsStandardTransaction(t, this.networkMain));

            // 81-byte TX_NULL_DATA (non-standard)
            t.Outputs[0].ScriptPubKey = new Script() + OpcodeType.OP_RETURN + ParseHex("04678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef3804678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef3800");
            Assert.True(!StandardScripts.IsStandardTransaction(t, this.networkMain));

            // TX_NULL_DATA w/o PUSHDATA
            t.Outputs.Clear();
            t.Outputs.Add(new TxOut());
            t.Outputs[0].ScriptPubKey = new Script() + OpcodeType.OP_RETURN;
            Assert.True(StandardScripts.IsStandardTransaction(t, this.networkMain));

            // Only one TX_NULL_DATA permitted in all cases
            t.Outputs.Clear();
            t.Outputs.Add(new TxOut());
            t.Outputs.Add(new TxOut());
            t.Outputs[0].ScriptPubKey = new Script() + OpcodeType.OP_RETURN + ParseHex("04678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38");
            t.Outputs[1].ScriptPubKey = new Script() + OpcodeType.OP_RETURN + ParseHex("04678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38");
            Assert.True(!StandardScripts.IsStandardTransaction(t, this.networkMain));

            t.Outputs[0].ScriptPubKey = new Script() + OpcodeType.OP_RETURN + ParseHex("04678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38");
            t.Outputs[1].ScriptPubKey = new Script() + OpcodeType.OP_RETURN;
            Assert.True(!StandardScripts.IsStandardTransaction(t, this.networkMain));

            t.Outputs[0].ScriptPubKey = new Script() + OpcodeType.OP_RETURN;
            t.Outputs[1].ScriptPubKey = new Script() + OpcodeType.OP_RETURN;
            Assert.True(!StandardScripts.IsStandardTransaction(t, this.networkMain));
        }

        [Fact]
        public void CanDecodeAndEncodeRawTransaction()
        {
            TestCase[] tests = TestCase.read_json(TestDataLocations.GetFileFromDataFolder("tx_raw.json"));
            foreach (TestCase test in tests)
            {
                var format = (RawFormat)Enum.Parse(typeof(RawFormat), (string)test[0], true);
                Network network = ((string)test[1]) == "Main" ? this.networkMain : KnownNetworks.TestNet;
                string testData = ((JObject)test[2]).ToString();

                Transaction raw = Transaction.Parse(testData, format, network);

                Assert.True(JToken.DeepEquals(raw.ToString(network, format), testData));

                Transaction raw3 = Transaction.Parse(raw.ToString(network, format), format);
                Assert.Equal(raw.ToString(network, format), raw3.ToString(network, format));
            }
        }

        private byte[] ParseHex(string data)
        {
            return Encoders.Hex.DecodeData(data);
        }
    }
}
