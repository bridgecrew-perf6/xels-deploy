﻿using System.Linq;
using Moq;
using Xels.Bitcoin.Tests.Common;
using Xunit;

namespace NBitcoin.Tests
{
    public class sigopcount_tests
    {
        [Fact]
        [Trait("Core", "Core")]
        public void GetSigOpCount()
        {
            // Test CScript::GetSigOpCount()
            var s1 = new Script();
            Assert.Equal(0U, s1.GetSigOpCount(false));
            Assert.Equal(0U, s1.GetSigOpCount(true));

            var dummy = new uint160(0);
            s1 = s1 + OpcodeType.OP_1 + dummy.ToBytes() + dummy.ToBytes() + OpcodeType.OP_2 + OpcodeType.OP_CHECKMULTISIG;
            Assert.Equal(2U, s1.GetSigOpCount(true));
            s1 = s1 + OpcodeType.OP_IF + OpcodeType.OP_CHECKSIG + OpcodeType.OP_ENDIF;
            Assert.Equal(3U, s1.GetSigOpCount(true));
            Assert.Equal(21U, s1.GetSigOpCount(false));

            Script p2sh = PayToScriptHashTemplate.Instance.GenerateScriptPubKey(s1);
            Script scriptSig = PayToScriptHashTemplate.Instance.GenerateScriptSig(new[] { (Op)OpcodeType.OP_0 }, s1);
            Assert.Equal(3U, p2sh.GetSigOpCount(KnownNetworks.Main, scriptSig));

            PubKey[] keys = Enumerable.Range(0, 3).Select(_ => new Key(true).PubKey).ToArray();

            Script s2 = PayToMultiSigTemplate.Instance.GenerateScriptPubKey(1, keys);
            Assert.Equal(3U, s2.GetSigOpCount(true));
            Assert.Equal(20U, s2.GetSigOpCount(false));

            p2sh = PayToScriptHashTemplate.Instance.GenerateScriptPubKey(s2);
            Assert.Equal(0U, p2sh.GetSigOpCount(true));
            Assert.Equal(0U, p2sh.GetSigOpCount(false));
            var scriptSig2 = new Script();
            scriptSig2 = scriptSig2 + OpcodeType.OP_1 + dummy.ToBytes() + dummy.ToBytes() + s2.ToBytes();
            Assert.Equal(3U, p2sh.GetSigOpCount(KnownNetworks.Main, scriptSig2));
        }

        [Fact]
        [Trait("Core", "Core")]
        public void GetSigOpCountForFederation()
        {
            PubKey[] keys = Enumerable.Range(0, 3).Select(_ => new Key(true).PubKey).ToArray();
            var federations = new Federations();
            federations.RegisterFederation(new Federation(keys.Take(2), 1));
            var network = KnownNetworks.StraxRegTest;
            network.SetPrivatePropertyValue("Federations", federations); 

            // Test CScript::GetSigOpCount()
            var s1 = new Script();
            s1 = s1 + OpcodeType.OP_1 + OpcodeType.OP_FEDERATION + OpcodeType.OP_CHECKMULTISIG;
            Assert.Equal(2U, s1.GetSigOpCount(true, network));
            s1 = s1 + OpcodeType.OP_IF + OpcodeType.OP_CHECKSIG + OpcodeType.OP_ENDIF;
            Assert.Equal(3U, s1.GetSigOpCount(true, network));
            Assert.Equal(21U, s1.GetSigOpCount(false, network));
        }
    }
}
