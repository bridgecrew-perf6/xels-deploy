﻿using System;
using System.Collections.Generic;
using System.Linq;
using NBitcoin.DataEncoders;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace NBitcoin.Tests
{
    [Trait("Core", "Core")]
    public class base58_tests
    {
        public static IEnumerable<object[]> DataSet
        {
            get
            {
                return new[] {
                    new object[]{string.Empty, ""},
                    new object[]{"61", "2g"},
                    new object[]{"626262", "a3gV"},
                    new object[]{"636363", "aPEr"},
                    new object[]{"73696d706c792061206c6f6e6720737472696e67", "2cFupjhnEsSn59qHXstmK2ffpLv2"},
                    new object[]{"00eb15231dfceb60925886b67d065299925915aeb172c06647", "1NS17iag9jJgTHD1VXjvLCEnZuQ3rJDE9L"},
                    new object[]{"516b6fcd0f", "ABnLTmg"},
                    new object[]{"bf4f89001e670274dd", "3SEo3LWLoPntC"},
                    new object[]{"572e4794", "3EFU7m"},
                    new object[]{"ecac89cad93923c02321", "EJDM8drfXA6uyA"},
                    new object[]{"10c8511e", "Rt5zm"},
                    new object[]{"00000000000000000000", "1111111111"}
                };
            }
        }

        private readonly Network networkMain;

        public base58_tests()
        {
            this.networkMain = KnownNetworks.Main;
        }

        [Fact]
        public void ShouldEncodeProperly()
        {
            foreach (object[] i in DataSet)
            {
                string data = (string)i[0];
                string encoded = (string)i[1];
                byte[] testBytes = Encoders.Hex.DecodeData(data);
                Assert.Equal(encoded, Encoders.Base58.EncodeData(testBytes));
            }
        }

        [Fact]
        public void ShouldDecodeProperly()
        {
            foreach (object[] i in DataSet)
            {
                string data = (string)i[0];
                string encoded = (string)i[1];
                byte[] testBytes = Encoders.Base58.DecodeData(encoded);
                AssertEx.CollectionEquals(Encoders.Hex.DecodeData(data), testBytes);
            }
        }

        [Fact]
        public void ShouldThrowFormatExceptionOnInvalidBase58()
        {
            Assert.Throws<FormatException>(() => Encoders.Base58.DecodeData("invalid"));
            Encoders.Base58.DecodeData(" ");

            // check that DecodeBase58 skips whitespace, but still fails with unexpected non-whitespace at the end.
            Assert.Throws<FormatException>(() => Encoders.Base58.DecodeData(" \t\n\v\f\r skip \r\f\v\n\t a"));
            byte[] result = Encoders.Base58.DecodeData(" \t\n\v\f\r skip \r\f\v\n\t ");
            byte[] expected2 = Encoders.Hex.DecodeData("971a55");
            AssertEx.CollectionEquals(result, expected2);
        }


        [Fact]
        [Trait("Core", "Core")]
        public void base58_keys_valid_parse()
        {
            TestCase[] tests = TestCase.read_json(TestDataLocations.GetFileFromDataFolder("base58_keys_valid.json"));
            Network network;
            foreach (TestCase test in tests)
            {
                string strTest = test.ToString();
                if (test.Count < 3) // Allow for extra stuff (useful for comments)
                {
                    Assert.True(false, "Bad test " + strTest);
                    continue;
                }

                string exp_base58string = (string)test[0];
                byte[] exp_payload = TestUtils.ParseHex((string)test[1]);
                //const Object &metadata = test[2].get_obj();
                bool isPrivkey = (bool)test.GetDynamic(2).isPrivkey;
                bool isTestnet = (bool)test.GetDynamic(2).isTestnet;
                if (isTestnet)
                    network = KnownNetworks.TestNet;
                else
                    network = KnownNetworks.Main;

                if (isPrivkey)
                {
                    bool isCompressed = (bool)test.GetDynamic(2).isCompressed;

                    // Must be valid private key
                    // Note: CBitcoinSecret::SetString tests isValid, whereas CBitcoinAddress does not!
                    BitcoinSecret secret = network.CreateBitcoinSecret(exp_base58string);
                    //If not valid exception would throw

                    Key privkey = secret.PrivateKey;
                    Assert.True(privkey.IsCompressed == isCompressed, "compressed mismatch:" + strTest);
                    Assert.True(Utils.ArrayEqual(privkey.ToBytes(), exp_payload), "key mismatch:" + strTest);

                    // Private key must be invalid public key
                    Assert.Throws<FormatException>(() => network.CreateBitcoinAddress(exp_base58string));
                }
                else
                {
                    string exp_addrType = (string)test.GetDynamic(2).addrType; // "script" or "pubkey"
                                                                               // Must be valid public key
                    BitcoinAddress addr = network.CreateBitcoinAddress(exp_base58string);
                    Assert.True((addr is BitcoinScriptAddress) == (exp_addrType == "script"), "isScript mismatch" + strTest);

                    if (exp_addrType == "script")
                        Assert.True(addr.GetType() == typeof(BitcoinScriptAddress));
                    if (exp_addrType == "pubkey")
                        Assert.True(addr.GetType() == typeof(BitcoinPubKeyAddress));

                    Assert.Throws<FormatException>(() => network.CreateBitcoinSecret(exp_base58string));
                }
            }
        }


        // Goal: check that generated keys match test vectors
        [Fact]
        [Trait("Core", "Core")]
        public void base58_keys_valid_gen()
        {
            TestCase[] tests = TestCase.read_json(TestDataLocations.GetFileFromDataFolder("base58_keys_valid.json"));
            tests = tests.Concat(TestCase.read_json(TestDataLocations.GetFileFromDataFolder("base58_keys_valid2.json"))).ToArray();
            Network network = null;

            foreach (TestCase test in tests)
            {
                string strTest = test.ToString();
                if (test.Count < 3) // Allow for extra stuff (useful for comments)
                {
                    Assert.False(true, "Bad test: " + strTest);
                    continue;
                }
                string exp_base58string = (string)test[0];
                byte[] exp_payload = TestUtils.ParseHex((string)test[1]);
                dynamic metadata = test.GetDynamic(2);
                bool isPrivkey = (bool)metadata.isPrivkey;
                bool isTestnet = (bool)metadata.isTestnet;

                if (isTestnet)
                    network = KnownNetworks.TestNet;
                else
                    network = KnownNetworks.Main;
                if (isPrivkey)
                {
                    bool isCompressed = metadata.isCompressed;
                    var key = new Key(exp_payload, fCompressedIn: isCompressed);
                    BitcoinSecret secret = network.CreateBitcoinSecret(key);
                    Assert.True(secret.ToString() == exp_base58string, "result mismatch: " + strTest);
                }
                else
                {
                    string exp_addrType = (string)metadata.addrType;
                    TxDestination dest;
                    if (exp_addrType == "pubkey")
                    {
                        dest = new KeyId(new uint160(exp_payload));
                    }
                    else if (exp_addrType == "script")
                    {
                        dest = new ScriptId(new uint160(exp_payload));
                    }
                    else if (exp_addrType == "p2wpkh")
                    {
                        dest = new WitKeyId(new uint160(exp_payload));
                    }
                    else if (exp_addrType == "p2wsh")
                    {
                        dest = new WitScriptId(exp_payload);
                    }
                    else if (exp_addrType == "none")
                    {
                        continue;
                    }
                    else
                    {
                        Assert.True(false, "Bad addrtype: " + strTest);
                        continue;
                    }
                    try
                    {
                        BitcoinAddress addrOut = dest.GetAddress(network);
                        Assert.True(addrOut.ToString() == exp_base58string, "mismatch: " + strTest);
                        Assert.True(addrOut.ScriptPubKey == dest.ScriptPubKey);
                        Assert.True(dest.ScriptPubKey.GetDestination(KnownNetworks.Main) == dest);
                    }
                    catch (ArgumentException)
                    {
                        Assert.True(dest.GetType() == typeof(TxDestination));
                    }
                }
            }
        }

        public static IEnumerable<object[]> InvalidKeys
        {
            get
            {
                TestCase[] dataset = TestCase.read_json(TestDataLocations.GetFileFromDataFolder("base58_keys_invalid.json"));
                return dataset.Select(x => x.ToArray());
            }
        }

        // Goal: check that base58 parsing code is robust against a variety of corrupted data
        [Fact]
        public void base58_keys_invalid()
        {
            foreach (object[] i in InvalidKeys)
            {
                string data = (string)i[0];
                // must be invalid as public and as private key
                Assert.Throws<FormatException>(() => this.networkMain.CreateBitcoinAddress(data));
                Assert.Throws<FormatException>(() => this.networkMain.CreateBitcoinSecret(data));
            }
        }
    }
}
