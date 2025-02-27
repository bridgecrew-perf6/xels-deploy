﻿using System;
using System.Collections.Generic;

namespace Xels.SmartContracts.CLR.Validation
{
    public static class Primitives
    {
        public static IEnumerable<Type> Types { get; } = new []
        {
            typeof(bool),
            typeof(byte),
            typeof(sbyte),
            typeof(char),
            typeof(int),
            typeof(uint),
            typeof(long),
            typeof(ulong),
            typeof(Stratis.SmartContracts.UInt128),
            typeof(Stratis.SmartContracts.UInt256),
            typeof(string)
        };
    }
}