﻿using System;
using System.Collections.Generic;
using FluentAssertions;
using NBitcoin;
using Xels.Bitcoin.Features.Consensus.Rules.CommonRules;
using Xels.Bitcoin.Features.Consensus.Rules.ProvenHeaderRules;
using Xels.Bitcoin.Networks;
using Xunit;

namespace Xels.Bitcoin.Features.Consensus.Tests.Rules.CommonRules
{
    public class ConsensusRulesRegistrationTest
    {
        [Fact]
        public void BitcoinConsensusRulesRegistrationTest()
        {
            Network network = new BitcoinTest();
            //new FullNodeBuilderConsensusExtension.PowConsensusRulesRegistration().RegisterRules(network.Consensus);

            List<Type> headerValidationRules = network.Consensus.ConsensusRules.HeaderValidationRules;

            headerValidationRules.Count.Should().Be(4);

            headerValidationRules[0].FullName.Should().Be(typeof(HeaderTimeChecksRule).FullName);
            headerValidationRules[1].FullName.Should().Be(typeof(CheckDifficultyPowRule).FullName);
            headerValidationRules[2].FullName.Should().Be(typeof(BitcoinActivationRule).FullName);
            headerValidationRules[3].FullName.Should().Be(typeof(BitcoinHeaderVersionRule).FullName);

            List<Type> integrityValidationRules = network.Consensus.ConsensusRules.IntegrityValidationRules;

            integrityValidationRules.Count.Should().Be(1);
            integrityValidationRules[0].FullName.Should().Be(typeof(BlockMerkleRootRule).FullName);

            List<Type> partialValidationRules = network.Consensus.ConsensusRules.PartialValidationRules;

            partialValidationRules.Count.Should().Be(8);

            partialValidationRules[0].FullName.Should().Be(typeof(SetActivationDeploymentsPartialValidationRule).FullName);
            partialValidationRules[1].FullName.Should().Be(typeof(TransactionLocktimeActivationRule).FullName);
            partialValidationRules[2].FullName.Should().Be(typeof(CoinbaseHeightActivationRule).FullName);
            partialValidationRules[3].FullName.Should().Be(typeof(WitnessCommitmentsRule).FullName);
            partialValidationRules[4].FullName.Should().Be(typeof(BlockSizeRule).FullName);
            partialValidationRules[5].FullName.Should().Be(typeof(EnsureCoinbaseRule).FullName);
            partialValidationRules[6].FullName.Should().Be(typeof(CheckPowTransactionRule).FullName);
            partialValidationRules[7].FullName.Should().Be(typeof(CheckSigOpsRule).FullName);

            List<Type> fullValidationRules = network.Consensus.ConsensusRules.FullValidationRules;

            fullValidationRules.Count.Should().Be(6);

            fullValidationRules[0].FullName.Should().Be(typeof(SetActivationDeploymentsFullValidationRule).FullName);
            fullValidationRules[1].FullName.Should().Be(typeof(FetchCoinviewRule).FullName);
            fullValidationRules[2].FullName.Should().Be(typeof(TransactionDuplicationActivationRule).FullName);
            fullValidationRules[3].FullName.Should().Be(typeof(PowCoinviewRule).FullName);
            fullValidationRules[4].FullName.Should().Be(typeof(PushCoinviewRule).FullName);
            fullValidationRules[5].FullName.Should().Be(typeof(FlushCoinviewRule).FullName);

        }

        [Fact]
        public void StraxConsensusRulesRegistrationTest()
        {
            Network network = new StraxTest();
            //new FullNodeBuilderConsensusExtension.PosConsensusRulesRegistration().RegisterRules(network.Consensus);

            List<Type> headerValidationRules = network.Consensus.ConsensusRules.HeaderValidationRules;

            headerValidationRules.Count.Should().Be(7);
            headerValidationRules[0].FullName.Should().Be(typeof(HeaderTimeChecksRule).FullName);
            headerValidationRules[1].FullName.Should().Be(typeof(HeaderTimeChecksPosRule).FullName);
            headerValidationRules[2].FullName.Should().Be(typeof(PosFutureDriftRule).FullName);
            headerValidationRules[3].FullName.Should().Be(typeof(CheckDifficultyPosRule).FullName);
            headerValidationRules[4].FullName.Should().Be(typeof(XelsHeaderVersionRule).FullName);
            headerValidationRules[5].FullName.Should().Be(typeof(ProvenHeaderSizeRule).FullName);
            headerValidationRules[6].FullName.Should().Be(typeof(ProvenHeaderCoinstakeRule).FullName);

            List<Type> integrityValidationRules = network.Consensus.ConsensusRules.IntegrityValidationRules;

            integrityValidationRules.Count.Should().Be(3);
            integrityValidationRules[0].FullName.Should().Be(typeof(BlockMerkleRootRule).FullName);
            integrityValidationRules[1].FullName.Should().Be(typeof(PosBlockSignatureRepresentationRule).FullName);
            integrityValidationRules[2].FullName.Should().Be(typeof(PosBlockSignatureRule).FullName);

            List<Type> partialValidationRules = network.Consensus.ConsensusRules.PartialValidationRules;

            partialValidationRules.Count.Should().Be(11);

            partialValidationRules[0].FullName.Should().Be(typeof(SetActivationDeploymentsPartialValidationRule).FullName);
            partialValidationRules[1].FullName.Should().Be(typeof(PosTimeMaskRule).FullName);
            partialValidationRules[2].FullName.Should().Be(typeof(TransactionLocktimeActivationRule).FullName);
            partialValidationRules[3].FullName.Should().Be(typeof(CoinbaseHeightActivationRule).FullName);
            partialValidationRules[4].FullName.Should().Be(typeof(WitnessCommitmentsRule).FullName);
            partialValidationRules[5].FullName.Should().Be(typeof(BlockSizeRule).FullName);
            partialValidationRules[6].FullName.Should().Be(typeof(EnsureCoinbaseRule).FullName);
            partialValidationRules[7].FullName.Should().Be(typeof(CheckPowTransactionRule).FullName);
            partialValidationRules[8].FullName.Should().Be(typeof(CheckPosTransactionRule).FullName);
            partialValidationRules[9].FullName.Should().Be(typeof(CheckSigOpsRule).FullName);
            partialValidationRules[10].FullName.Should().Be(typeof(StraxCoinstakeRule).FullName);

            List<Type> fullValidationRules = network.Consensus.ConsensusRules.FullValidationRules;

            fullValidationRules.Count.Should().Be(7);

            fullValidationRules[0].FullName.Should().Be(typeof(SetActivationDeploymentsFullValidationRule).FullName);
            fullValidationRules[1].FullName.Should().Be(typeof(CheckDifficultyHybridRule).FullName);
            fullValidationRules[2].FullName.Should().Be(typeof(LoadCoinviewRule).FullName);
            fullValidationRules[3].FullName.Should().Be(typeof(TransactionDuplicationActivationRule).FullName);
            fullValidationRules[4].FullName.Should().Be(typeof(StraxCoinviewRule).FullName);
            fullValidationRules[5].FullName.Should().Be(typeof(StraxColdStakingRule).FullName);
            fullValidationRules[6].FullName.Should().Be(typeof(SaveCoinviewRule).FullName);
        }
    }
}
