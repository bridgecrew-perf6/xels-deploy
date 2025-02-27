﻿namespace Xels.SmartContracts.RuntimeObserver
{
    public interface IMemoryMeter
    {
        ulong MemoryAvailable { get; }

        ulong MemoryConsumed { get; }

        ulong MemoryLimit { get; }

        void Spend(ulong toSpend);
    }
}
