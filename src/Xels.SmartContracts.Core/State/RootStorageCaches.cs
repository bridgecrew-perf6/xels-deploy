﻿using System.Collections.Generic;
using Xels.Patricia;

namespace Xels.SmartContracts.Core.State
{
    /// <summary>
    /// The place that all StorageCaches are spawned.
    /// </summary>
    public class RootStorageCaches : IStorageCaches
    {
        protected readonly Dictionary<byte[], StorageCache> cache;

        private readonly StateRepositoryRoot repo;

        /// <summary>
        /// Synchronizes access to the cache dictionary.
        /// </summary>
        private readonly object cacheLock = new object();

        public RootStorageCaches(StateRepositoryRoot repo)
        {
            this.repo = repo;
            this.cache = new Dictionary<byte[], StorageCache>(new ByteArrayComparer());
        }

        /// <inheritdoc />
        public ISource<byte[], byte[]> Get(byte[] key)
        {
            lock (this.cacheLock)
            {
                if (this.cache.ContainsKey(key))
                    return this.cache[key];

                AccountState accountState = this.repo.accountStateCache.Get(key);
                IPatriciaTrie storageTrie = this.repo.GetTrieWithSameCache(accountState?.StateRoot);
                var newCache = new StorageCache(storageTrie);
                this.cache[key] = newCache;
                return newCache;
            }
        }

        /// <inheritdoc />
        public bool Flush()
        {
            // Theoretically the cache dictionary could be updated while a flush is attempted,
            // causing enumeration of the loop to fail.
            lock (this.cacheLock)
            {
                bool ret = false;
                foreach (KeyValuePair<byte[], StorageCache> kvp in this.cache)
                {
                    StorageCache childCache = kvp.Value;
                    ret |= childCache.Flush();
                    AccountState storageOwnerAcct = this.repo.accountStateCache.Get(kvp.Key);
                    // need to update account storage root
                    if (storageOwnerAcct != null)
                    {
                        childCache.trie.Flush();
                        byte[] rootHash = childCache.trie.GetRootHash();
                        storageOwnerAcct.StateRoot = rootHash;
                        this.repo.accountStateCache.Put(kvp.Key, storageOwnerAcct);
                    }
                }

                this.cache.Clear();

                return ret;
            }
        }
    }
}
