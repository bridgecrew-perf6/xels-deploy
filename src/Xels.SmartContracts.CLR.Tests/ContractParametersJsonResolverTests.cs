﻿using NBitcoin;
using Newtonsoft.Json;
using Stratis.SmartContracts;
using Xels.Bitcoin.Features.SmartContracts.ReflectionExecutor;
using Xels.SmartContracts.CLR.Local;
using Xels.SmartContracts.Core;
using Xels.SmartContracts.Networks;
using Xunit;

namespace Xels.SmartContracts.CLR.Tests
{
    public class ContractParametersJsonResolverTests
    {
        private readonly Network network;
        private readonly ContractParametersContractResolver resolver;

        public ContractParametersJsonResolverTests()
        {
            this.network = new SmartContractsPoARegTest();
            this.resolver = new ContractParametersContractResolver(this.network);
        }

        [Fact]
        public void Address_Json_Outputs_As_Base58String()
        {
            uint160 testUint160 = new uint160(123);
            Address testAddress = testUint160.ToAddress();
            string expectedString = testUint160.ToBase58Address(this.network);

            string jsonOutput = JsonConvert.SerializeObject(testAddress, new JsonSerializerSettings
            {
                ContractResolver = this.resolver
            });
            Assert.Equal(expectedString, jsonOutput.Replace("\"", ""));
        }

        [Fact]
        public void UInt256_Json_Outputs_As_String()
        {
            UInt256 testUInt256 = (UInt256)123;
            string expectedString = testUInt256.ToString();

            string jsonOutput = JsonConvert.SerializeObject(testUInt256, new JsonSerializerSettings
            {
                ContractResolver = this.resolver
            });
            Assert.Equal(expectedString, jsonOutput.Replace("\"", ""));
        }
        
        [Fact]
        public void UInt128_Json_Outputs_As_String()
        {
            UInt128 testUInt128 = (UInt128)123;
            string expectedString = testUInt128.ToString();

            string jsonOutput = JsonConvert.SerializeObject(testUInt128, new JsonSerializerSettings
            {
                ContractResolver = this.resolver
            });
            Assert.Equal(expectedString, jsonOutput.Replace("\"", ""));
        }

        [Fact]
        public void LocalExecutionResult_Outputs_With_Address()
        {
            uint160 testUint160 = new uint160(123);
            Address testAddress = testUint160.ToAddress();
            string expectedString = testUint160.ToBase58Address(this.network);

            var execResult = new LocalExecutionResult
            {
                ErrorMessage = new ContractErrorMessage("Error message"),
                GasConsumed = (RuntimeObserver.Gas) 69,
                Return = testAddress
            };

            string jsonOutput = JsonConvert.SerializeObject(execResult, new JsonSerializerSettings
            {
                ContractResolver = this.resolver
            });
            Assert.Contains($"\"return\":\"{expectedString}\"", jsonOutput);
        }
    }
}
