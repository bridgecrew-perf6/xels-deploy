﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NBitcoin.DataEncoders;
using Newtonsoft.Json.Linq;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace NBitcoin.Tests
{
    public class bip39_tests
    {
        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanGenerateMnemonicOfSpecificLength()
        {
            foreach(WordCount count in new[] { WordCount.Twelve, WordCount.TwentyFour, WordCount.TwentyOne, WordCount.Fifteen, WordCount.Eighteen })
            {
                Assert.Equal((int)count, new Mnemonic(Wordlist.English, count).Words.Length);
            }
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanDetectBadChecksum()
        {
            var mnemonic = new Mnemonic("turtle front uncle idea crush write shrug there lottery flower risk shell", Wordlist.English);
            Assert.True(mnemonic.IsValidChecksum);
            mnemonic = new Mnemonic("front front uncle idea crush write shrug there lottery flower risk shell", Wordlist.English);
            Assert.False(mnemonic.IsValidChecksum);
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void CanNormalizeMnemonicString()
        {
            var mnemonic = new Mnemonic("turtle front uncle idea crush write shrug there lottery flower risk shell", Wordlist.English);
            var mnemonic2 = new Mnemonic("turtle    front	uncle　 idea crush write shrug there lottery flower risk shell", Wordlist.English);
            Assert.Equal(mnemonic.DeriveExtKey().ScriptPubKey, mnemonic2.DeriveExtKey().ScriptPubKey);
            Assert.Equal(mnemonic.ToString(), mnemonic2.ToString());
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void EngTest()
        {
            JObject test = JObject.Parse(File.ReadAllText(TestDataLocations.GetFileFromDataFolder("bip39_vectors.json")));

            foreach(JProperty language in test.Properties())
            {
                Wordlist lang = GetList(language.Name);
                foreach(JArray langTest in ((JArray)language.Value).OfType<JArray>().Take(2))
                {
                    byte[] entropy = Encoders.Hex.DecodeData(langTest[0].ToString());
                    string mnemonicStr = langTest[1].ToString();
                    string seed = langTest[2].ToString();
                    var mnemonic = new Mnemonic(mnemonicStr, lang);
                    Assert.True(mnemonic.IsValidChecksum);
                    Assert.Equal(seed, Encoders.Hex.EncodeData(mnemonic.DeriveSeed("TREZOR")));

                    mnemonic = new Mnemonic(lang, entropy);
                    Assert.True(mnemonic.IsValidChecksum);
                    Assert.Equal(seed, Encoders.Hex.EncodeData(mnemonic.DeriveSeed("TREZOR")));
                }
            }
        }


        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void KDTableCanNormalize()
        {
            string input = "あおぞら";
            string expected = "あおぞら";
            Assert.False(input == expected);
            Assert.Equal(expected, KDTable.NormalizeKD(input));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void JapTest()
        {
            JArray test = JArray.Parse(File.ReadAllText(TestDataLocations.GetFileFromDataFolder("bip39_JP.json"), Encoding.UTF32));

            foreach(JObject unitTest in test.OfType<JObject>())
            {
                byte[] entropy = Encoders.Hex.DecodeData(unitTest["entropy"].ToString());
                string mnemonicStr = unitTest["mnemonic"].ToString();
                string seed = unitTest["seed"].ToString();
                string passphrase = unitTest["passphrase"].ToString();
                var mnemonic = new Mnemonic(mnemonicStr, Wordlist.Japanese);
                Assert.True(mnemonic.IsValidChecksum);
                Assert.Equal(seed, Encoders.Hex.EncodeData(mnemonic.DeriveSeed(passphrase)));
                string bip32 = unitTest["bip32_xprv"].ToString();
                string bip32Actual = mnemonic.DeriveExtKey(passphrase).ToString(KnownNetworks.Main);
                Assert.Equal(bip32, bip32Actual.ToString());
                mnemonic = new Mnemonic(Wordlist.Japanese, entropy);
                Assert.True(mnemonic.IsValidChecksum);
                bip32Actual = mnemonic.DeriveExtKey(passphrase).ToString(KnownNetworks.Main);
                Assert.Equal(bip32, bip32Actual.ToString());
            }
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestKnownEnglish()
        {
            Assert.Equal(Language.English, Wordlist.AutoDetectLanguage(new string[] { "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "abandon", "about" }));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestKnownJapenese()
        {
            Assert.Equal(Language.Japanese, Wordlist.AutoDetectLanguage(new string[] { "あいこくしん", "あいさつ", "あいだ", "あおぞら", "あかちゃん", "あきる", "あけがた", "あける", "あこがれる", "あさい", "あさひ", "あしあと", "あじわう", "あずかる", "あずき", "あそぶ", "あたえる", "あたためる", "あたりまえ", "あたる", "あつい", "あつかう", "あっしゅく", "あつまり", "あつめる", "あてな", "あてはまる", "あひる", "あぶら", "あぶる", "あふれる", "あまい", "あまど", "あまやかす", "あまり", "あみもの", "あめりか" }));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestKnownSpanish()
        {
            Assert.Equal(Language.Spanish, Wordlist.AutoDetectLanguage(new string[] { "yoga", "yogur", "zafiro", "zanja", "zapato", "zarza", "zona", "zorro", "zumo", "zurdo" }));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestKnownFrench()
        {
            Assert.Equal(Language.French, Wordlist.AutoDetectLanguage(new string[] { "abusif", "antidote" }));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestKnownChineseSimplified()
        {
            Assert.Equal(Language.ChineseSimplified, Wordlist.AutoDetectLanguage(new string[] { "的", "一", "是", "在", "不", "了", "有", "和", "人", "这" }));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestKnownChineseTraditional()
        {
            Assert.Equal(Language.ChineseTraditional, Wordlist.AutoDetectLanguage(new string[] { "的", "一", "是", "在", "不", "了", "有", "和", "載" }));
        }

        [Fact]
        [Trait("UnitTest", "UnitTest")]
        public void TestKnownUnknown()
        {
            Assert.Equal(Language.Unknown, Wordlist.AutoDetectLanguage(new string[] { "gffgfg", "khjkjk", "kjkkj" }));
        }


        private Wordlist GetList(string lang)
        {
            if(lang == "english")
                return Wordlist.English;
            throw new NotSupportedException(lang);
        }
    }

    public class bip39_Codegen
    {
        //[Fact]
        private void GenerateHardcodedBIP39Dictionary()
        {
            var builder = new StringBuilder();
            foreach(Language lang in new[] { Language.ChineseSimplified, Language.ChineseTraditional, Language.English, Language.Japanese, Language.Spanish, Language.French })
            {
                string name = Wordlist.GetLanguageFileName(lang);
                builder.AppendLine("dico.Add(\"" + name + "\",\"" + GetLanguage(lang) + "\");");
            }
            string dico = builder.ToString();
        }

        [Fact]
        public void GenerateHardcodedNormalization()
        {
            var builder = new StringBuilder();
            builder.Append("\"");
            var chars = new HashSet<char>();
            var ranges = new List<CharRangeT>();
            ranges.Add(CharRange(0, 1000)); //Some latin language accent
            ranges.Add(CharRange(0x3040, 0x309F)); //Hiragana
            ranges.Add(CharRange(0x30A0, 0x30FF)); //Katakana

            //CJK Unified Ideographs                  4E00-9FFF   Common
            //CJK Unified Ideographs Extension A      3400-4DFF   Rare
            //CJK Unified Ideographs Extension B      20000-2A6DF Rare, historic
            //CJK Compatibility Ideographs            F900-FAFF   Duplicates, unifiable variants, corporate characters
            //CJK Compatibility Ideographs Supplement 2F800-2FA1F Unifiable variants

            ranges.Add(CharRange(0x4e00, 0x9FFF));
            ranges.Add(CharRange(0x3400, 0x4DFF));
            ranges.Add(CharRange(0x20000, 0x2A6DF));
            ranges.Add(CharRange(0xF900, 0xFAFF));
            ranges.Add(CharRange(0x2F800, 0x2FA1F));
            ranges.Add(CharRange(0x3300, 0x33FF));
            ranges.Add(CharRange(0x3000, 0x303F));
            ranges.Add(CharRange(0xFF00, 0xFFFF));
            ranges.Add(CharRange(0x2000, 0x206F));
            ranges.Add(CharRange(0x20A0, 0x20CF));

            foreach(char letter in ranges.SelectMany(c => c).OrderBy(c => c))
            {
                string nonNormal = new String(new[] { letter });
                try
                {
                    string normal = nonNormal.Normalize(NormalizationForm.FormKD);
                    if(nonNormal != normal && chars.Add(letter))
                    {
                        builder.Append(nonNormal + normal + "\\n");
                    }
                }
                catch(ArgumentException)
                {
                }
            }

            builder.Append("\"");
            string substitutionTable = builder.ToString();

            builder = new StringBuilder();
            builder.AppendLine("{");
            foreach(CharRangeT range in ranges)
            {
                builder.AppendLine("new[]{" + range.From + "," + range.To + "},");
            }
            builder.AppendLine("}");
            string rangeTable = builder.ToString();
        }

        private class CharRangeT : IEnumerable<char>
        {
            public int From;
            public int To;

            public IEnumerable<char> Chars;


            public IEnumerator<char> GetEnumerator()
            {
                return this.Chars.GetEnumerator();
            }


            IEnumerator IEnumerable.GetEnumerator()
            {
                return GetEnumerator();
            }
        }

        private CharRangeT CharRange(char[] chars)
        {
            int min = chars.Select(c => (int)c).Min();
            int max = chars.Select(c => (int)c).Max();
            return CharRange(min, max);
        }

        private CharRangeT CharRange(int from, int to)
        {
            var result = new CharRangeT();
            result.From = from;
            result.To = to;
            result.Chars = Enumerable.Range(from, to - from + 1).Select(i => (char)i);
            return result;
        }

        private string GetLanguage(Language lang)
        {
            string name = Wordlist.GetLanguageFileName(lang);
            var client = new System.Net.Http.HttpClient();
            string data = client.GetAsync("https://raw.githubusercontent.com/bitcoin/bips/master/bip-0039/" + name + ".txt").Result.Content.ReadAsStringAsync().Result;
            return data.Replace("\n", "\\n");
        }
    }
}
