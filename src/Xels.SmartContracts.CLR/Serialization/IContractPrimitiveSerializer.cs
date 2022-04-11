using System;

namespace Xels.SmartContracts.CLR.Serialization
{
    public interface IContractPrimitiveSerializer
    {
        byte[] Serialize(object obj); 
        T Deserialize<T>(byte[] stream);
        object Deserialize(Type type, byte[] stream);
        Stratis.SmartContracts.Address ToAddress(string address);
    }
}
