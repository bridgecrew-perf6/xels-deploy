﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NBitcoin.Crypto;
using NBitcoin.DataEncoders;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace NBitcoin.Tests
{
    public class key_tests
    {
        private const string strSecret1 = ("5HxWvvfubhXpYYpS3tJkw6fq9jE9j18THftkZjHHfmFiWtmAbrj");
        private const string strSecret2 = ("5KC4ejrDjv152FGwP386VD1i2NYc5KkfSMyv1nGy1VGDxGHqVY3");
        private const string strSecret1C = ("Kwr371tjA9u2rFSMZjTNun2PXXP3WPZu2afRHTcta6KxEUdm1vEw");
        private const string strSecret2C = ("L3Hq7a8FEQwJkW1M2GNKDW28546Vp5miewcCzSqUD9kCAXrJdS3g");
        private const string strAddressBad = ("1HV9Lc3sNHZxwj4Zk6fB38tEmBryq2cBiF");

        private BitcoinPubKeyAddress addr1;
        private BitcoinPubKeyAddress addr2;
        private BitcoinPubKeyAddress addr1C;
        private BitcoinPubKeyAddress addr2C;

        private BitcoinAddress addrLocal;
        private uint256 msgLocal = Hashes.Hash256(TestUtils.ToBytes("Localbitcoins.com will change the world"));
        private byte[] signatureLocal = Convert.FromBase64String("IJ/17TjGGUqmEppAliYBUesKHoHzfY4gR4DW0Yg7QzrHUB5FwX1uTJ/H21CF8ncY8HHNB5/lh8kPAOeD5QxV8Xc=");

        private readonly Network networkMain;

        public key_tests()
        {
            this.networkMain = KnownNetworks.Main;

            this.addr1 = (BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress("1QFqqMUD55ZV3PJEJZtaKCsQmjLT6JkjvJ");
            this.addr2 = (BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress("1F5y5E5FMc5YzdJtB9hLaUe43GDxEKXENJ");
            this.addr1C = (BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress("1NoJrossxPBKfCHuJXT4HadJrXRE9Fxiqs");
            this.addr2C = (BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress("1CRj2HyM1CXWzHAXLQtiGLyggNT9WQqsDs");

            this.addrLocal = this.networkMain.CreateBitcoinAddress("1Q1wVsNNiUo68caU7BfyFFQ8fVBqxC2DSc");
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanVerifySignature()
        {
            var tests = new[]
            {
                new
                {
                    Address = "15jZVzLc9cXz5PUFFda5A4Z7kZDYPg2NnL",
                    PrivateKey = "L3TiCqmvPkXJpzCCZJuhy6wQtJZWDkR1AuqFY4Utib5J5XLuvLdZ",
                    Message = "This is an example of a signed message.",
                    Signature = "H6sliOnVrD9r+J8boZAKHZwBIW2zLiD72IfTIF94bfZhBI0JdMu9AM9rrF7P6eH+866YvM4H9xWGVN4jMJZycFU="
                },
                new
                {
                    Address = "1QFqqMUD55ZV3PJEJZtaKCsQmjLT6JkjvJ",
                    PrivateKey = "5HxWvvfubhXpYYpS3tJkw6fq9jE9j18THftkZjHHfmFiWtmAbrj",
                    Message = "hello world",
                    Signature = "G+dnSEywl3v1ijlWXvpY6zpu+AKNNXJcVmrdE35m0mMlzwFzXDiNg+uZrG9k8mpQL6sjHKrlBoDNSA+yaPW7PEA="
                },
                new
                {
                    Address = "1Q1wVsNNiUo68caU7BfyFFQ8fVBqxC2DSc",
                    PrivateKey = null as string,
                    Message = "Localbitcoins.com will change the world",
                    Signature = "IJ/17TjGGUqmEppAliYBUesKHoHzfY4gR4DW0Yg7QzrHUB5FwX1uTJ/H21CF8ncY8HHNB5/lh8kPAOeD5QxV8Xc="
                },
                new
                {
                    Address = "1GvPJp7H8UYsYDvE4GFoV4f2gSCNZzGF48",
                    PrivateKey = "5JEeah4w29axvf5Yg9v9PKv86zcCN9qVbizJDMHmiSUxBqDFoUT",
                    Message = "This is an example of a signed message2",
                    Signature = "G8YNwlo+I36Ct+hZKGSBFl3q8Kbx1pxPpwQmwdsG85io76+DUOHXqh/DfBq+Cn2R3C3dI//g3koSjxy7yNxJ9m8="
                },
                new
                {
                    Address = "1GvPJp7H8UYsYDvE4GFoV4f2gSCNZzGF48",
                    PrivateKey = "5JEeah4w29axvf5Yg9v9PKv86zcCN9qVbizJDMHmiSUxBqDFoUT",
                    Message = "this is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long messagethis is a very long message",
                    Signature = "HFKBHewleUsotk6fWG0OvWS/E2pP4o5hixdD6ui60in/x4376FBI4DvtJYrljXLNJTG1pBOZG+qRT/7S9WiIBfQ="
                },
            };


            foreach(var test in tests)
            {
                if(test.PrivateKey != null)
                {
                    BitcoinSecret secret = this.networkMain.CreateBitcoinSecret(test.PrivateKey);
                    string signature = secret.PrivateKey.SignMessage(test.Message);
                    Assert.True(((BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress(test.Address)).VerifyMessage(test.Message, signature));
                    Assert.True(secret.PubKey.VerifyMessage(test.Message, signature));
                }
                var address = (BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress(test.Address);
                Assert.True(address.VerifyMessage(test.Message, test.Signature));
                Assert.True(!address.VerifyMessage("bad message", test.Signature));
            }
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanVerifyTrezorSignature()
        {
            string visual_challenge = "2015-03-23 17:39:22";
            byte[] random_challenge = Encoders.Hex.DecodeData("cd8552569d6e4509266ef137584d1e62c7579b5b8ed69bbafa4b864c6521e7c2");
            byte[] signature = Encoders.Hex.DecodeData("20f2d1a42d08c3a362be49275c3ffeeaa415fc040971985548b9f910812237bb41770bf2c8d488428799fbb7e52c11f1a3404011375e4080e077e0e42ab7a5ba02");

            byte[] hiddenChallenge_Sha = Hashes.SHA256(random_challenge);
            byte[] visualChallenge_Sha = Hashes.SHA256(Encoding.ASCII.GetBytes(visual_challenge));

            var pubKey = new PubKey("023a472219ad3327b07c18273717bb3a40b39b743756bf287fbd5fa9d263237f45");
            bool verified = pubKey.VerifyMessage(hiddenChallenge_Sha.Concat(visualChallenge_Sha).ToArray(), Encoders.Base64.EncodeData(signature));
            Assert.True(verified);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanGeneratePubKeysAndAddress()
        {
            //Took from http://brainwallet.org/ and http://procbits.com/2013/08/27/generating-a-bitcoin-address-with-javascript
            var tests = new[]
            {
                new
                {
                    PrivateKeyWIF = "5Hx15HFGyep2CfPxsJKe2fXJsCVn5DEiyoeGGF6JZjGbTRnqfiD",
                    CompressedPrivateKeyWIF = "KwomKti1X3tYJUUMb1TGSM2mrZk1wb1aHisUNHCQXTZq5auC2qc3",
                    PubKey = "04d0988bfa799f7d7ef9ab3de97ef481cd0f75d2367ad456607647edde665d6f6fbdd594388756a7beaf73b4822bc22d36e9bda7db82df2b8b623673eefc0b7495",
                    CompressedPubKey = "03d0988bfa799f7d7ef9ab3de97ef481cd0f75d2367ad456607647edde665d6f6f",
                    Address =           "16UjcYNBG9GTK4uq2f7yYEbuifqCzoLMGS",
                    CompressedAddress = "1FkKMsKNJqWSDvTvETqcCeHcUQQ64kSC6s",
                    Hash160 = "3c176e659bea0f29a3e9bf7880c112b1b31b4dc8",
                    CompressedHash160 = "a1c2f92a9dacbd2991c3897724a93f338e44bdc1"
                },
                new
                {
                    PrivateKeyWIF = "5J7WTMRn1vjZ9udUxNCLq7F9DYEJiqRCjstiBrY6mDjnaomd6kZ",
                    CompressedPrivateKeyWIF = "KxXj1KAMh6ApvKJ2PNZ4XLZRGLqjDehppFdEnueGSBDrC2Hfe7vt",
                    PubKey = "0493e5d305cad2588d5fb254065fe48ce446028ba380e6ee663baea9cd105500897eb030c033cdab160f31c36df0ea38330fdd69677df49cd14826902022d17f3f",
                    CompressedPubKey = "0393e5d305cad2588d5fb254065fe48ce446028ba380e6ee663baea9cd10550089",
                    Address =           "1MZmwgyMyjM11uA6ZSpgn1uK3LBWCzvV6e",
                    CompressedAddress = "1AECNr2TDye8dpC1TeDH3eJpGoZ7dNPy4g",
                    Hash160 = "e19557c8f8fb53a964c5dc7bfde86d806709f7c5",
                    CompressedHash160 = "6538094af65453ea279f14d1a04b408e3adfebd7"
                }
            };

            foreach(var test in tests)
            {
                BitcoinSecret secret = this.networkMain.CreateBitcoinSecret(test.PrivateKeyWIF);
                Assert.Equal(test.PubKey, secret.PrivateKey.PubKey.ToHex());

                var address = (BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress(test.Address);
                Assert.Equal(new KeyId(test.Hash160), address.Hash);
                Assert.Equal(new KeyId(test.Hash160), secret.PrivateKey.PubKey.Hash);
                Assert.Equal(address.Hash, secret.PrivateKey.PubKey.GetAddress(KnownNetworks.Main).Hash);

                BitcoinSecret compressedSec = secret.Copy(true);

                PubKey a = secret.PrivateKey.PubKey;
                PubKey b = compressedSec.PrivateKey.PubKey;

                Assert.Equal(test.CompressedPrivateKeyWIF, compressedSec.ToWif());
                Assert.Equal(test.CompressedPubKey, compressedSec.PrivateKey.PubKey.ToHex());
                Assert.True(compressedSec.PrivateKey.PubKey.IsCompressed);

                var compressedAddr = (BitcoinPubKeyAddress)this.networkMain.CreateBitcoinAddress(test.CompressedAddress);
                Assert.Equal(new KeyId(test.CompressedHash160), compressedAddr.Hash);
                Assert.Equal(new KeyId(test.CompressedHash160), compressedSec.PrivateKey.PubKey.Hash);


            }
        }

        [Fact]
        [Trait("Core", "Core")]
        public void key_test1()
        {
            BitcoinSecret bsecret1 = this.networkMain.CreateBitcoinSecret(strSecret1);
            BitcoinSecret bsecret2 = this.networkMain.CreateBitcoinSecret(strSecret2);
            BitcoinSecret bsecret1C = this.networkMain.CreateBitcoinSecret(strSecret1C);
            BitcoinSecret bsecret2C = this.networkMain.CreateBitcoinSecret(strSecret2C);
            Assert.Throws<FormatException>(() => this.networkMain.CreateBitcoinSecret(strAddressBad));

            Key key1 = bsecret1.PrivateKey;
            Assert.True(key1.IsCompressed == false);
            Assert.True(bsecret1.Copy(true).PrivateKey.IsCompressed == true);
            Assert.True(bsecret1.Copy(true).Copy(false).IsCompressed == false);
            Assert.True(bsecret1.Copy(true).Copy(false).ToString() == bsecret1.ToString());
            Key key2 = bsecret2.PrivateKey;
            Assert.True(key2.IsCompressed == false);
            Key key1C = bsecret1C.PrivateKey;
            Assert.True(key1C.IsCompressed == true);
            Key key2C = bsecret2C.PrivateKey;
            Assert.True(key1C.IsCompressed == true);

            PubKey pubkey1 = key1.PubKey;
            PubKey pubkey2 = key2.PubKey;
            PubKey pubkey1C = key1C.PubKey;
            PubKey pubkey2C = key2C.PubKey;

            Assert.True(this.addr1.Hash == pubkey1.Hash);
            Assert.True(this.addr2.Hash == pubkey2.Hash);
            Assert.True(this.addr1C.Hash == pubkey1C.Hash);
            Assert.True(this.addr2C.Hash == pubkey2C.Hash);



            for(int n = 0; n < 16; n++)
            {
                string strMsg = String.Format("Very secret message {0}: 11", n);
                if(n == 10)
                {
                    //Test one long message
                    strMsg = String.Join(",", Enumerable.Range(0, 2000).Select(i => i.ToString()).ToArray());
                }
                uint256 hashMsg = Hashes.Hash256(TestUtils.ToBytes(strMsg));

                // normal signatures

                ECDSASignature sign1 = null, sign2 = null, sign1C = null, sign2C = null;
                var tasks = new List<Task>();
                tasks.Add(Task.Run(() => sign1 = key1.Sign(hashMsg)));
                tasks.Add(Task.Run(() => sign2 = key2.Sign(hashMsg)));
                tasks.Add(Task.Run(() => sign1C = key1C.Sign(hashMsg)));
                tasks.Add(Task.Run(() => sign2C = key2C.Sign(hashMsg)));
                Task.WaitAll(tasks.ToArray());
                tasks.Clear();

                tasks.Add(Task.Run(() => Assert.True(pubkey1.Verify(hashMsg, sign1))));
                tasks.Add(Task.Run(() => Assert.True(pubkey2.Verify(hashMsg, sign2))));
                tasks.Add(Task.Run(() => Assert.True(pubkey1C.Verify(hashMsg, sign1C))));
                tasks.Add(Task.Run(() => Assert.True(pubkey2C.Verify(hashMsg, sign2C))));
                Task.WaitAll(tasks.ToArray());
                tasks.Clear();

                tasks.Add(Task.Run(() => Assert.True(pubkey1.Verify(hashMsg, sign1))));
                tasks.Add(Task.Run(() => Assert.True(!pubkey1.Verify(hashMsg, sign2))));
                tasks.Add(Task.Run(() => Assert.True(pubkey1.Verify(hashMsg, sign1C))));
                tasks.Add(Task.Run(() => Assert.True(!pubkey1.Verify(hashMsg, sign2C))));

                tasks.Add(Task.Run(() => Assert.True(!pubkey2.Verify(hashMsg, sign1))));
                tasks.Add(Task.Run(() => Assert.True(pubkey2.Verify(hashMsg, sign2))));
                tasks.Add(Task.Run(() => Assert.True(!pubkey2.Verify(hashMsg, sign1C))));
                tasks.Add(Task.Run(() => Assert.True(pubkey2.Verify(hashMsg, sign2C))));

                tasks.Add(Task.Run(() => Assert.True(pubkey1C.Verify(hashMsg, sign1))));
                tasks.Add(Task.Run(() => Assert.True(!pubkey1C.Verify(hashMsg, sign2))));
                tasks.Add(Task.Run(() => Assert.True(pubkey1C.Verify(hashMsg, sign1C))));
                tasks.Add(Task.Run(() => Assert.True(!pubkey1C.Verify(hashMsg, sign2C))));

                tasks.Add(Task.Run(() => Assert.True(!pubkey2C.Verify(hashMsg, sign1))));
                tasks.Add(Task.Run(() => Assert.True(pubkey2C.Verify(hashMsg, sign2))));
                tasks.Add(Task.Run(() => Assert.True(!pubkey2C.Verify(hashMsg, sign1C))));
                tasks.Add(Task.Run(() => Assert.True(pubkey2C.Verify(hashMsg, sign2C))));

                Task.WaitAll(tasks.ToArray());
                tasks.Clear();

                // compact signatures (with key recovery)

                byte[] csign1 = null, csign2 = null, csign1C = null, csign2C = null;

                tasks.Add(Task.Run(() => csign1 = key1.SignCompact(hashMsg)));
                tasks.Add(Task.Run(() => csign2 = key2.SignCompact(hashMsg)));
                tasks.Add(Task.Run(() => csign1C = key1C.SignCompact(hashMsg)));
                tasks.Add(Task.Run(() => csign2C = key2C.SignCompact(hashMsg)));
                Task.WaitAll(tasks.ToArray());
                tasks.Clear();

                PubKey rkey1 = null, rkey2 = null, rkey1C = null, rkey2C = null;
                tasks.Add(Task.Run(() => rkey1 = PubKey.RecoverCompact(hashMsg, csign1)));
                tasks.Add(Task.Run(() => rkey2 = PubKey.RecoverCompact(hashMsg, csign2)));
                tasks.Add(Task.Run(() => rkey1C = PubKey.RecoverCompact(hashMsg, csign1C)));
                tasks.Add(Task.Run(() => rkey2C = PubKey.RecoverCompact(hashMsg, csign2C)));
                Task.WaitAll(tasks.ToArray());
                tasks.Clear();

                Assert.True(rkey1.ToHex() == pubkey1.ToHex());
                Assert.True(rkey2.ToHex() == pubkey2.ToHex());
                Assert.True(rkey1C.ToHex() == pubkey1C.ToHex());
                Assert.True(rkey2C.ToHex() == pubkey2C.ToHex());
            }
        }


        [Fact]
        [Trait("Core", "Core")]
        public void key_test_from_bytes()
        {
            //Example private key taken from https://en.bitcoin.it/wiki/Private_key
            var privateKey = new Byte[32] { 0xE9, 0x87, 0x3D, 0x79, 0xC6, 0xD8, 0x7D, 0xC0, 0xFB, 0x6A, 0x57, 0x78, 0x63, 0x33, 0x89, 0xF4, 0x45, 0x32, 0x13, 0x30, 0x3D, 0xA6, 0x1F, 0x20, 0xBD, 0x67, 0xFC, 0x23, 0x3A, 0xA3, 0x32, 0x62 };
            var key1 = new Key(privateKey, -1, false);

            ISecret wifKey = key1.GetWif(KnownNetworks.Main);

            //Example wif private key taken from https://en.bitcoin.it/wiki/Private_key
            const String expected = "5Kb8kLf9zgWQnogidDA76MzPL6TsZZY36hWXMssSzNydYXYB9KF";
            Assert.True(wifKey.ToString() == expected);
        }

        [Fact]
        [Trait("Core", "Core")]
        public void can_always_recover_pubkey_from_signature_when_stopping_at_null()
        {
            var rand = new Random();
            for (int i = 0; i < 1000; i++)
            {
                var key = new Key();
                PosBlock block = new PosBlock(new BlockHeader() { Time = (uint)rand.Next() });
                ECDSASignature signature = key.Sign(block.GetHash());
                block.BlockSignature = new BlockSignature { Signature = signature.ToDER() };
                Assert.True(key.PubKey.Verify(block.GetHash(), new ECDSASignature(block.BlockSignature.Signature)));
                signature = ECDSASignature.FromDER(block.BlockSignature.Signature);
                bool match = false;
                for (int recId = 0; !match; recId++)
                {
                    PubKey pubKeyForSig = PubKey.RecoverFromSignature(recId, signature, block.GetHash(), true);
                    if (pubKeyForSig == null)
                        break;

                    match = pubKeyForSig == key.PubKey;
                }
                Assert.True(match);
            }
        }
    }
}
