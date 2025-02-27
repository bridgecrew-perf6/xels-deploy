﻿using System;
using System.Text;
using NBitcoin;
using Stratis.SmartContracts;
using Xels.SmartContracts.CLR.ContractLogging;
using Xels.SmartContracts.CLR.Serialization;
using Xels.SmartContracts.Networks;
using Xunit;

namespace Xels.SmartContracts.CLR.Tests.Logs
{
    public class RawLogTests
    {
        public struct Example
        {
            [Index]
            public string Name;

            [Index]
            public uint Amount;

            public Example(string name, uint amount)
            {
                this.Name = name;
                this.Amount = amount;
            }
        }

        [Fact]
        public void RawLog_With_Null_Value_Serializes()
        {
            var serializer = new ContractPrimitiveSerializerV2(new SmartContractsPoARegTest());
            var exampleLog = new Example(null, 0);

            var rawLog = new RawLog(uint160.One, exampleLog);
            var log = rawLog.ToLog(serializer);

            Assert.Equal(3, log.Topics.Count);
            Assert.Equal((string)nameof(Example), (string)Encoding.UTF8.GetString(log.Topics[0]));

            // Check that null has been serialized correctly
            Assert.Equal(new byte[0], log.Topics[1]);
            Assert.Equal(exampleLog.Amount, BitConverter.ToUInt32(log.Topics[2]));
        }
    }
}
