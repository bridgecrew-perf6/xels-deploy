﻿using NBitcoin;
using Xels.SmartContracts.CLR.ContractSigning;
using Xels.SmartContracts.Core.ContractSigning;
using Xunit;

namespace Xels.SmartContracts.CLR.Tests.ContractSigning
{
    public class CSharpContractSignerTests
    {
        private readonly IContractSigner contractSigner;
        private readonly CSharpContractSigner csharpContractSigner;
        private readonly Key privKey;

        public CSharpContractSignerTests()
        {
            this.contractSigner = new ContractSigner();
            this.csharpContractSigner = new CSharpContractSigner(this.contractSigner);
            this.privKey = new Key();
        }

        [Fact]
        public void Demo_CSharpSigning()
        {
            (byte[] contractCode, byte[] signature) signatureResult = this.csharpContractSigner.SignCSharpFile(this.privKey, "SmartContracts/Auction.cs");

            Assert.True(this.contractSigner.Verify(this.privKey.PubKey, signatureResult.contractCode, signatureResult.signature));
        }
    }
}
