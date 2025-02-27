﻿using System;
using System.Collections.Generic;
using System.Net;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Xels.Bitcoin.Networks.Deployments;
using Xels.Bitcoin.Networks.Policies;

namespace Xels.Bitcoin.Networks
{
    public class XelsTest : XelsMain
    {
        public XelsTest()
        {
            // The message start string is designed to be unlikely to occur in normal data.
            // The characters are rarely used upper ASCII, not valid as UTF-8, and produce
            // a large 4-byte int at any alignment.
            var messageStart = new byte[4];
            messageStart[0] = 0x71;
            messageStart[1] = 0x31;
            messageStart[2] = 0x21;
            messageStart[3] = 0x11;
            uint magic = BitConverter.ToUInt32(messageStart, 0); // 0x11213171;

            this.Name = "XelsTest";
            this.NetworkType = NetworkType.Testnet;
            this.Magic = magic;
            this.DefaultPort = 26178;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 109;
            this.DefaultRPCPort = 26174;
            this.DefaultAPIPort = 38221;
            this.DefaultSignalRPort = 39824;
            this.CoinTicker = "TSTRAT";
            this.DefaultBanTimeSeconds = 16000; // 500 (MaxReorg) * 64 (TargetSpacing) / 2 = 4 hours, 26 minutes and 40 seconds

            var powLimit = new Target(new uint256("0000ffff00000000000000000000000000000000000000000000000000000000"));

            var consensusFactory = new PosConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = 1470467000;
            this.GenesisNonce = 1831645;
            this.GenesisBits = 0x1e0fffff;
            this.GenesisVersion = 1;
            this.GenesisReward = Money.Zero;

            Block genesisBlock = CreateXelsGenesisBlock(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward);

            genesisBlock.Header.Time = 1493909211;
            genesisBlock.Header.Nonce = 2433759;
            genesisBlock.Header.Bits = powLimit;

            this.Genesis = genesisBlock;

            // Taken from XelsX.
            var consensusOptions = new PosConsensusOptions(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 100_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 5,
                witnessScaleFactor: 4
            );

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 0,
                [BuriedDeployments.BIP65] = 0,
                [BuriedDeployments.BIP66] = 0
            };

            var bip9Deployments = new XelsBIP9Deployments()
            {
                [XelsBIP9Deployments.TestDummy] = new BIP9DeploymentsParameters("TestDummy", 28,
                    new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    BIP9DeploymentsParameters.DefaultTestnetThreshold),

                [XelsBIP9Deployments.CSV] = new BIP9DeploymentsParameters("CSV", 0,
                    new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    BIP9DeploymentsParameters.DefaultTestnetThreshold),

                [XelsBIP9Deployments.Segwit] = new BIP9DeploymentsParameters("Segwit", 1,
                    new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2020, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    BIP9DeploymentsParameters.DefaultTestnetThreshold),
                
                [XelsBIP9Deployments.ColdStaking] = new BIP9DeploymentsParameters("ColdStaking", 2,
                    new DateTime(2018, 11, 1, 0, 0, 0, DateTimeKind.Utc),
                    new DateTime(2019, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                    BIP9DeploymentsParameters.DefaultTestnetThreshold)
            };

            this.Consensus = new NBitcoin.Consensus(
                consensusFactory: consensusFactory,
                consensusOptions: consensusOptions,
                coinType: 105,
                hashGenesisBlock: genesisBlock.GetHash(),
                subsidyHalvingInterval: 210000,
                majorityEnforceBlockUpgrade: 750,
                majorityRejectBlockOutdated: 950,
                majorityWindow: 1000,
                buriedDeployments: buriedDeployments,
                bip9Deployments: bip9Deployments,
                bip34Hash: new uint256("0x000000000000024b89b42a942fe0d9fea3bb44ab7bd1b19115dd6a759c0808b8"),
                minerConfirmationWindow: 2016, // nPowTargetTimespan / nPowTargetSpacing
                maxReorgLength: 500,
                defaultAssumeValid: new uint256("0x690e7e30ae3fa6c10855db0f8bc10110a54f5c73019f5581ee038186154397d0"), // 1100000
                maxMoney: long.MaxValue,
                coinbaseMaturity: 10,
                premineHeight: 2,
                premineReward: Money.Coins(98000000),
                proofOfWorkReward: Money.Coins(4),
                powTargetTimespan: TimeSpan.FromSeconds(14 * 24 * 60 * 60), // two weeks
                targetSpacing: TimeSpan.FromSeconds(64),
                powAllowMinDifficultyBlocks: false,
                posNoRetargeting: false,
                powNoRetargeting: false,
                powLimit: powLimit,
                minimumChainWork: null,
                isProofOfStake: true,
                lastPowBlock: 12500,
                proofOfStakeLimit: new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeLimitV2: new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeReward: Money.COIN
            );

            this.Consensus.PosEmptyCoinbase = true;

            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { (65) };
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { (196) };
            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (65 + 128) };

            this.Bech32Encoders = new Bech32Encoder[2];
            var encoder = new Bech32Encoder("tstrat");
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

            this.Checkpoints = new Dictionary<int, CheckpointInfo>
            {
                { 0, new CheckpointInfo(new uint256("0x00000e246d7b73b88c9ab55f2e5e94d9e22d471def3df5ea448f5576b1d156b9"), new uint256("0x0000000000000000000000000000000000000000000000000000000000000000")) },
                { 2, new CheckpointInfo(new uint256("0x56959b1c8498631fb0ca5fe7bd83319dccdc6ac003dccb3171f39f553ecfa2f2"), new uint256("0x13f4c27ca813aefe2d9018077f8efeb3766796b9144fcc4cd51803bf4376ab02")) },
                { 50000, new CheckpointInfo(new uint256("0xb42c18eacf8fb5ed94eac31943bd364451d88da0fd44cc49616ffea34d530ad4"), new uint256("0x824934ddc5f935e854ac59ae7f5ed25f2d29a7c3914cac851f3eddb4baf96d78")) },
                { 100000, new CheckpointInfo(new uint256("0xf9e2f7561ee4b92d3bde400d251363a0e8924204c326da7f4ad9ccc8863aad79"), new uint256("0xdef8d92d20becc71f662ee1c32252aca129f1bf4744026b116d45d9bfe67e9fb")) },
                { 150000, new CheckpointInfo(new uint256("0x08b7c20a450252ddf9ce41dbeb92ecf54932beac9090dc8250e933ad3a175381"), new uint256("0xf05dad15f733ae0acbd34adc449be9429099dbee5fa9ecd8e524cf28e9153adb")) },
                { 200000, new CheckpointInfo(new uint256("0x8609cc873222a0573615788dc32e377b88bfd6a0015791f627d969ee3a415115"), new uint256("0xfa28c1f20a8162d133607c6a1c8997833befac3efd9076567258a7683ac181fa")) },
                { 250000, new CheckpointInfo(new uint256("0xdd664e15ac679a6f3b96a7176303956661998174a697ad8231f154f1e32ff4a3"), new uint256("0x19fc0fa29418f8b19cbb6557c1c79dfd0eff6779c0eaaec5d245c5cdf3c96d78")) },
                { 300000, new CheckpointInfo(new uint256("0x2409eb5ae72c80d5b37c77903d75a8e742a33843ab633935ce6e5264db962e23"), new uint256("0xf5ec7af55516b8e264ed280e9a5dba0180a4a9d3713351bfea275b18f3f1514e")) },
                { 350000, new CheckpointInfo(new uint256("0x36811041e9060f4b4c26dc20e0850dca5efaabb60618e3456992e9c0b1b2120e"), new uint256("0xbfda55ef0756bcee8485e15527a2b8ca27ca877aa09c88e363ef8d3253cdfd1c")) },
                { 400000, new CheckpointInfo(new uint256("0xb6abcb933d3e3590345ca5d3abb697461093313f8886568ac8ae740d223e56f6"), new uint256("0xfaf5fcebee3ec0df5155393a99da43de18b12e620fef5edb111a791ecbfaa63a")) },
                { 650000, new CheckpointInfo(new uint256("0x7065de13f14749798ebf70993af4debeb5bb2a968f5862ca232a2436fbac2fd0"), new uint256("0x175eb3708ffd9a1ca5938b0df0bf1f55af39ec8e2e4c396ed97c1406c4a5d701")) },
                { 720000, new CheckpointInfo(new uint256("0x041fb27f49f96be3a10034db0148290e9e2551b1c6196823b46521c36c69fbe2"), new uint256("0xba96e9c84c4134a2204d07e41b7738a9ae6e56c4322f443dcfe11421f1643e6e")) }, // 14-01-2019
                { 900000, new CheckpointInfo(new uint256("0xd48702aabf727570d96bbcd8bad220427a35113efa90c3adc91ae94a4b09c6e5"), new uint256("0x31515a27d55f819131f2dc0a263f46fb63c56ec0ff129bcb0b1c13d5922a62c2")) }, // 14-01-2019
                { 1000000, new CheckpointInfo(new uint256("0xa8775bca139bb50c16c803a64e324f83eec70e3f9b5e762265e590cc773b9930"), new uint256("0x3664cc8571bfea578cc22a2b8478148da05704226624ce203f2ea646a7339a38")) },
                { 1150000, new CheckpointInfo(new uint256("0xca63e5cc3b023f98bfddbf7f8df8dcb3dc90f37bec79b6396823b3da77ab9a24"), new uint256("0xd0a2024250b92ba7dbc8348e6e5dd3a83a770154e6a2ca7f7280284c8a25ba18")) },
                { 1245000, new CheckpointInfo(new uint256("0x759dfad85feb187710b85f07d4709a745700c220fae56755c78bc051f447f289"), new uint256("0x3d0b0e29ab715c2bd003bf2677d3646d85d2ce8f7a831e3b79d6ff783140646a")) },
                { 1400000, new CheckpointInfo(new uint256("0xf9e99174917a68159e7218c39f100545001e2c076bdfa11c00807df0936dd59b"), new uint256("0x5c5e728b113fb39dc6a14cd92d1a7f6a821cea0ad42eaafb50bc7dec5a5efdd1")) }
             };

            this.DNSSeeds = new List<DNSSeedData>
            {
                new DNSSeedData("testnet1.xelsnetwork.com", "testnet1.xelsnetwork.com"),
                new DNSSeedData("testnet2.xelsnetwork.com", "testnet2.xelsnetwork.com")
            };

            this.SeedNodes = new List<NetworkAddress>
            {
                new NetworkAddress(IPAddress.Parse("51.140.231.125"), 26178), // danger cloud node
                new NetworkAddress(IPAddress.Parse("169.1.13.216"), 26178),
            };

            this.StandardScriptsRegistry = new XelsStandardScriptsRegistry();

            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);
            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("0x00000e246d7b73b88c9ab55f2e5e94d9e22d471def3df5ea448f5576b1d156b9"));

            this.RegisterRules(this.Consensus);
            this.RegisterMempoolRules(this.Consensus);
        }
    }
}
