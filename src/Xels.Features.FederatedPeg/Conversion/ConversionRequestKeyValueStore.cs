﻿using System.Collections.Generic;
using System.IO;
using System.Text;
using LevelDB;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Persistence;
using Xels.Bitcoin.Utilities;
using Xels.Bitcoin.Utilities.JsonConverters;

namespace Xels.Features.FederatedPeg.Conversion
{
    public interface IConversionRequestKeyValueStore : IKeyValueRepository
    {
        List<ConversionRequest> GetAll();
        List<ConversionRequest> GetAll(ConversionRequestType type, bool onlyUnprocessed);

        void Delete(string key);
    }

    public class ConversionRequestKeyValueStore : IConversionRequestKeyValueStore
    {
        /// <summary>Access to database.</summary>
        private readonly DB leveldb;

        private readonly DBreezeSerializer dBreezeSerializer;

        public ConversionRequestKeyValueStore(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this(dataFolder.InteropRepositoryPath, dBreezeSerializer)
        {
        }

        public ConversionRequestKeyValueStore(string folder, DBreezeSerializer dBreezeSerializer)
        {
            Directory.CreateDirectory(folder);
            this.dBreezeSerializer = dBreezeSerializer;

            // Open a connection to a new DB and create if not found.
            var options = new Options { CreateIfMissing = true };
            this.leveldb = new DB(options, folder);
        }

        public List<ConversionRequest> GetAll()
        {
            var values = new List<ConversionRequest>();
            IEnumerator<KeyValuePair<byte[], byte[]>> enumerator = this.leveldb.GetEnumerator();

            while (enumerator.MoveNext())
            {
                (byte[] key, byte[] value) = enumerator.Current;

                if (value == null)
                    continue;

                ConversionRequest deserialized = this.dBreezeSerializer.Deserialize<ConversionRequest>(value);
                values.Add(deserialized);
            }

            return values;
        }

        public List<ConversionRequest> GetAll(ConversionRequestType type, bool onlyUnprocessed)
        {
            var values = new List<ConversionRequest>();
            IEnumerator<KeyValuePair<byte[], byte[]>> enumerator = this.leveldb.GetEnumerator();

            while (enumerator.MoveNext())
            {
                (byte[] key, byte[] value) = enumerator.Current;

                if (value == null)
                    continue;

                ConversionRequest deserialized = this.dBreezeSerializer.Deserialize<ConversionRequest>(value);

                if (deserialized.RequestType != type)
                    continue;

                if (deserialized.Processed && onlyUnprocessed)
                    continue;

                values.Add(deserialized);
            }

            return values;
        }

        public List<T> GetAllAsJson<T>()
        {
            throw new System.NotImplementedException();
        }

        public void SaveBytes(string key, byte[] bytes, bool overWrite = false)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            this.leveldb.Put(keyBytes, bytes);
        }

        public void SaveValue<T>(string key, T value, bool overWrite = false)
        {
            this.SaveBytes(key, this.dBreezeSerializer.Serialize(value));
        }

        public void SaveValueJson<T>(string key, T value, bool overWrite = false)
        {
            string json = Serializer.ToString(value);
            byte[] jsonBytes = Encoding.ASCII.GetBytes(json);

            this.SaveBytes(key, jsonBytes);
        }

        public byte[] LoadBytes(string key)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);

            byte[] row = this.leveldb.Get(keyBytes);

            if (row == null)
                return null;

            return row;
        }

        public T LoadValue<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default(T);

            T value = this.dBreezeSerializer.Deserialize<T>(bytes);
            return value;
        }

        public T LoadValueJson<T>(string key)
        {
            byte[] bytes = this.LoadBytes(key);

            if (bytes == null)
                return default(T);

            string json = Encoding.ASCII.GetString(bytes);

            T value = Serializer.ToObject<T>(json);

            return value;
        }

        public void Dispose()
        {
            this.leveldb.Dispose();
        }

        public void Delete(string key)
        {
            byte[] keyBytes = Encoding.ASCII.GetBytes(key);
            this.leveldb.Delete(keyBytes);
        }
    }
}
