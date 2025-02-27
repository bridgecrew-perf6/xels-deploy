﻿using System.Collections.Generic;
using System.Threading.Tasks;
using NBitcoin;
using Xels.Bitcoin.Utilities;

namespace Xels.Bitcoin.Interfaces
{
    /// <summary>
    /// Interface to insert and retrieve <see cref="ProvenBlockHeader"/> items from the database repository.
    /// </summary>
    public interface IProvenBlockHeaderRepository : IProvenBlockHeaderProvider
    {
        /// <summary>
        /// Initializes <see cref="ProvenBlockHeader"/> items database.
        /// </summary>
        Task InitializeAsync();

        /// <summary>
        /// Persists <see cref="ProvenBlockHeader"/> items to the database.
        /// </summary>
        /// <param name="provenBlockHeaders">List of <see cref="ProvenBlockHeader"/> items.</param>
        /// <param name="newTip">Block hash and height tip.</param>
        /// <returns><c>true</c> when a <see cref="ProvenBlockHeader"/> is saved to disk, otherwise <c>false</c>.</returns>
        Task PutAsync(SortedDictionary<int, ProvenBlockHeader> provenBlockHeaders, HashHeightPair newTip);
    }

    public static class BlockHeaderRepositoryConstants
    {
        /// <summary> Database key under which the block hash and height of a <see cref="ProvenBlockHeader"/> tip is stored.</summary>
        public static readonly byte[] BlockHashHeightKey = new byte[] { 1 };
        public static readonly byte ProvenBlockHeaderTable = 1;
        public static readonly byte BlockHashHeightTable = 2;
    }
}