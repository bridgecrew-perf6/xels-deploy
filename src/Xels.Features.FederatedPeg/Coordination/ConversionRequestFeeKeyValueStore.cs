using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Persistence;
using Xels.Bitcoin.Persistence.KeyValueStores;
using Xels.Bitcoin.Utilities;

namespace Xels.Features.FederatedPeg.Coordination
{
    /// <summary>
    /// This is implemented separately to <see cref="KeyValueRepository"/> so that the repository can live in its own folder on disk.
    /// </summary>
    public interface IConversionRequestFeeKeyValueStore : IKeyValueRepository
    {
    }

    public sealed class ConversionRequestFeeKeyValueStore : LevelDbKeyValueRepository, IConversionRequestFeeKeyValueStore
    {
        public ConversionRequestFeeKeyValueStore(DataFolder dataFolder, DBreezeSerializer dBreezeSerializer) : this(dataFolder.InteropFeeRepositoryPath, dBreezeSerializer)
        {
        }

        public ConversionRequestFeeKeyValueStore(string folder, DBreezeSerializer dBreezeSerializer) : base(folder, dBreezeSerializer)
        {
        }
    }
}
