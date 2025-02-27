﻿using NBitcoin;
using NBitcoin.DataEncoders;
using Xels.Bitcoin.Features.SmartContracts.PoW;

namespace Xels.SmartContracts.Networks
{
    public static class SmartContractNetworkUtils
    {
        /// <summary> The name of the root folder containing the different Xels blockchains (XelsMain, XelsTest, XelsRegTest). </summary>
        public const string XelsRootFolderName = "xels";

        /// <summary> The default name used for the Xels configuration file. </summary>
        public const string XelsDefaultConfigFilename = "xels.conf";

        /// <summary> Bitcoin default value for the maximum tip age in seconds to consider the node in initial block download (24 hours). </summary>
        public const int BitcoinDefaultMaxTipAgeInSeconds = 24 * 60 * 60;

        public static NBitcoin.Block CreateGenesis(ConsensusFactory consensusFactory, uint genesisTime, uint nonce, uint bits, int version, Money reward)
        {
            string timeStamp = "The Times 03/Jan/2009 Chancellor on brink of second bailout for banks";
            var genesisOutputScript = new Script(Op.GetPushOp(Encoders.Hex.DecodeData("04678afdb0fe5548271967f1a67130b7105cd6a828e03909a67962e0ea1f61deb649f6bc3f4cef38c4f35504e51ec112de5c384df7ba0b8d578a4c702b6bf11d5f")), OpcodeType.OP_CHECKSIG);

            NBitcoin.Transaction genesisTransaction = consensusFactory.CreateTransaction();
            genesisTransaction.Version = 1;
            genesisTransaction.AddInput(new TxIn()
            {
                ScriptSig = new Script(Op.GetPushOp(486604799), new Op()
                {
                    Code = (OpcodeType)0x1,
                    PushData = new[] { (byte)4 }
                }, Op.GetPushOp(Encoders.ASCII.DecodeData(timeStamp)))
            });

            genesisTransaction.AddOutput(new TxOut()
            {
                Value = reward,
                ScriptPubKey = genesisOutputScript
            });

            NBitcoin.Block genesis = consensusFactory.CreateBlock();
            genesis.Header.BlockTime = Utils.UnixTimeToDateTime(genesisTime);
            genesis.Header.Bits = bits;
            genesis.Header.Nonce = nonce;
            genesis.Header.Version = version;
            genesis.Transactions.Add(genesisTransaction);
            genesis.Header.HashPrevBlock = uint256.Zero;
            genesis.UpdateMerkleRoot();

            ((ISmartContractBlockHeader)genesis.Header).HashStateRoot = SmartContractBlockDefinition.StateRootEmptyTrie;

            return genesis;
        }
    }
}