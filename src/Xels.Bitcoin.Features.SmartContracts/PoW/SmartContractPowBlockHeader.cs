﻿using NBitcoin;

namespace Xels.Bitcoin.Features.SmartContracts.PoW
{
    public sealed class SmartContractPowBlockHeader : BlockHeader, ISmartContractBlockHeader
    {
        /// <summary>
        /// Root of the state trie after execution of this block. 
        /// </summary>
        private uint256 hashStateRoot;
        public uint256 HashStateRoot { get { return this.hashStateRoot; } set { this.hashStateRoot = value; } }

        /// <summary>
        /// Root of the receipt trie after execution of this block.
        /// </summary>
        private uint256 receiptRoot;
        public uint256 ReceiptRoot { get { return this.receiptRoot; } set { this.receiptRoot = value; } }

        /// <summary>
        /// Bitwise-OR of all the blooms generated from all of the smart contract transactions in the block.
        /// </summary>
        private Bloom logsBloom;
        public Bloom LogsBloom { get { return this.logsBloom; } set { this.logsBloom = value; } }

        public SmartContractPowBlockHeader() : base()
        {
            this.hashStateRoot = 0;
            this.receiptRoot = 0;
            this.logsBloom = new Bloom();
        }

        /// <summary>
        /// <see cref="ReadWrite(BitcoinStream)"/> overridden so that we can write the <see cref="hashStateRoot"/>.
        /// </summary>
        public override void ReadWrite(BitcoinStream stream)
        {
            base.ReadWrite(stream);
            stream.ReadWrite(ref this.hashStateRoot);
            stream.ReadWrite(ref this.receiptRoot);
            stream.ReadWrite(ref this.logsBloom);
        }

        /// <summary>
        /// Overridden so we can add these fields to the hash.
        /// </summary>
        protected override void ReadWriteHashingStream(BitcoinStream stream)
        {
            base.ReadWriteHashingStream(stream);
            stream.ReadWrite(ref this.hashStateRoot);
            stream.ReadWrite(ref this.receiptRoot);
            stream.ReadWrite(ref this.logsBloom);
        }
    }
}