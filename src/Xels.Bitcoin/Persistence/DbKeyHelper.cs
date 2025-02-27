﻿using System;
using System.Collections.Generic;
using LevelDB;
using RocksDbSharp;

namespace Xels.Bitcoin.Persistence
{
    public static class DBH
    {
        public static byte[] Get(this DB db, byte table, byte[] key)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            return db.Get(dbkey.ToArray());
        }

        public static byte[] Get(this RocksDb db, byte table, byte[] key)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            return db.Get(dbkey.ToArray());
        }

        public static void Put(this DB db, byte table, byte[] key, byte[] value)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            db.Put(dbkey.ToArray(), value);
        }

        public static void Put(this RocksDb db, byte table, byte[] key, byte[] value)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            db.Put(dbkey.ToArray(), value);
        }

        public static void Delete(this DB db, byte table, byte[] key)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            db.Delete(dbkey.ToArray());
        }

        public static void Remove(this RocksDb db, byte table, byte[] key)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            db.Remove(dbkey.ToArray());
        }

        public static void Put(this LevelDB.WriteBatch batch, byte table, byte[] key, byte[] value)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            batch.Put(dbkey.ToArray(), value);
        }

        public static void Put(this RocksDbSharp.WriteBatch batch, byte table, byte[] key, byte[] value)
        {
            Span<byte> dbkey = stackalloc byte[key.Length + 1];
            dbkey[0] = table;
            key.AsSpan().CopyTo(dbkey.Slice(1));

            batch.Put(dbkey.ToArray(), value);
        }

        public static Dictionary<byte[], byte[]> SelectDictionary(this DB db, byte table)
        {
            var dict = new Dictionary<byte[], byte[]>();

            var enumerator = db.GetEnumerator();
            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Key[0] == table)
                    dict.Add(enumerator.Current.Key.AsSpan().Slice(1).ToArray(), enumerator.Current.Value);
            }

            return dict;
        }

        public static Dictionary<byte[], byte[]> SelectDictionary(this RocksDb db, byte table)
        {
            var dict = new Dictionary<byte[], byte[]>();

            var enumerator = db.NewIterator();
            enumerator.SeekToFirst();
            while (enumerator.Valid())
            {
                if (enumerator.Key()[0] == table)
                    dict.Add(enumerator.Key().AsSpan().Slice(1).ToArray(), enumerator.Value());

                enumerator.Next();
            }

            return dict;
        }
    }
}
