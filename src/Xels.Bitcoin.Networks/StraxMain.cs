using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using NBitcoin;
using NBitcoin.BouncyCastle.Math;
using NBitcoin.DataEncoders;
using NBitcoin.Protocol;
using Xels.Bitcoin.Networks.Deployments;
using Xels.Bitcoin.Networks.Policies;

namespace Xels.Bitcoin.Networks
{
    public class StraxMain : Network
    {
        public StraxMain()
        {
            this.Name = "XlcMain";
            this.NetworkType = NetworkType.Mainnet;
            this.Magic = BitConverter.ToUInt32(Encoding.ASCII.GetBytes("StrX"));
            this.DefaultPort = 17105;
            this.DefaultMaxOutboundConnections = 16;
            this.DefaultMaxInboundConnections = 109;
            this.DefaultRPCPort = 17104;
            this.DefaultAPIPort = 17103;
            this.DefaultSignalRPort = 17102;
            this.MaxTipAge = 2 * 60 * 60;
            this.MinTxFee = 10000;
            this.FallbackFee = 10000;
            this.MinRelayTxFee = 10000;
            this.RootFolderName = StraxNetwork.StraxRootFolderName;
            this.DefaultConfigFilename = StraxNetwork.StraxDefaultConfigFilename;
            this.MaxTimeOffsetSeconds = 25 * 60;
            this.CoinTicker = "XLC";
            this.DefaultBanTimeSeconds = 11250; // 500 (MaxReorg) * 45 (TargetSpacing) / 2 = 3 hours, 7 minutes and 30 seconds

            this.CirrusRewardDummyAddress = "CKe36GSqPx3EasYY9FevtLSnyx5nryojaN"; // Cirrus main address
            this.RewardClaimerBatchActivationHeight = 119_200; // Tuesday, 12 January 2021 9:00:00 AM (Estimated)
            this.RewardClaimerBlockInterval = 100;

            this.ConversionTransactionFeeDistributionDummyAddress = "CKe36GSqPx3EasYY9FevtLSnyx5nryojaN";

            // To successfully process the OP_FEDERATION opcode the federations should be known.
            this.Federations = new Federations();
            this.Federations.RegisterFederation(new Federation(new[]
            {
                new PubKey("02b5ae6ae2997c33ad728d5a65a91c212789c3bd61015db5675d487020ba5bfe0d"),
                new PubKey("021335d02d35b21527b1db28d1bd37cb4376700aa77709eb7106ebb56b221b9395"),
                new PubKey("037e7aa5e9dd41de242a842530e893c256bd76234708d44081e5baabd39d411cc7")}));

            var consensusFactory = new PosConsensusFactory();

            // Create the genesis block.
            this.GenesisTime = Utils.DateTimeToUnixTime(new DateTimeOffset(2021, 04, 14, 0, 0, 0, TimeSpan.Zero)); //1609459200;1604913812; // ~9 November 2020 - https://www.unixtimestamp.com/
            this.GenesisNonce = 2553536; // Set to 1 until correct value found
            this.GenesisBits = 0x1e0fffff; // The difficulty target
            this.GenesisVersion = 536870912; // 'Empty' BIP9 deployments as they are all activated from genesis already
            this.GenesisReward = Money.Zero;

            Block genesisBlock = StraxNetwork.CreateGenesisBlock(consensusFactory, this.GenesisTime, this.GenesisNonce, this.GenesisBits, this.GenesisVersion, this.GenesisReward, "There is hope yet! We all need to work together. WE GOT THIS!!!");

            this.Genesis = genesisBlock;

            // Taken from Xels.
            var consensusOptions = new PosConsensusOptions(
                maxBlockBaseSize: 1_000_000,
                maxStandardVersion: 2,
                maxStandardTxWeight: 150_000,
                maxBlockSigopsCost: 20_000,
                maxStandardTxSigopsCost: 20_000 / 2,
                witnessScaleFactor: 4
            );

            var buriedDeployments = new BuriedDeploymentsArray
            {
                [BuriedDeployments.BIP34] = 0,
                [BuriedDeployments.BIP65] = 0,
                [BuriedDeployments.BIP66] = 0
            };

            var bip9Deployments = new StraxBIP9Deployments()
            {
                // Always active.
                [StraxBIP9Deployments.CSV] = new BIP9DeploymentsParameters("CSV", 0, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultMainnetThreshold),
                [StraxBIP9Deployments.Segwit] = new BIP9DeploymentsParameters("Segwit", 1, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultMainnetThreshold),
                [StraxBIP9Deployments.ColdStaking] = new BIP9DeploymentsParameters("ColdStaking", 2, BIP9DeploymentsParameters.AlwaysActive, 999999999, BIP9DeploymentsParameters.DefaultMainnetThreshold)
            };

            this.Consensus = new NBitcoin.Consensus(
                consensusFactory: consensusFactory,
                consensusOptions: consensusOptions,
                coinType: 105105, // https://github.com/satoshilabs/slips/blob/master/slip-0044.md
                hashGenesisBlock: genesisBlock.GetHash(),
                subsidyHalvingInterval: 210000,
                majorityEnforceBlockUpgrade: 750,
                majorityRejectBlockOutdated: 950,
                majorityWindow: 1000,
                buriedDeployments: buriedDeployments,
                bip9Deployments: bip9Deployments,
                bip34Hash: null,
                minerConfirmationWindow: 2016,
                maxReorgLength: 500,
                defaultAssumeValid: null, // TODO: Set this once some checkpoint candidates have elapsed
                maxMoney: long.MaxValue,
                coinbaseMaturity: 5,
                premineHeight: 1,
                premineReward: Money.Coins(21000000),
                proofOfWorkReward: Money.Coins(50),
                powTargetTimespan: TimeSpan.FromSeconds(14 * 24 * 60 * 60),
                targetSpacing: TimeSpan.FromSeconds(150),
                powAllowMinDifficultyBlocks: false,
                posNoRetargeting: false,
                powNoRetargeting: false,
                powLimit: new Target(new uint256("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff")),
                minimumChainWork: null,
                isProofOfStake: true,
                lastPowBlock: int.MaxValue - 100,
                proofOfStakeLimit: new BigInteger(uint256.Parse("00000fffffffffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeLimitV2: new BigInteger(uint256.Parse("000000000000ffffffffffffffffffffffffffffffffffffffffffffffffffff").ToBytes(false)),
                proofOfStakeReward: Money.Coins(100)
            );

            this.Consensus.PosEmptyCoinbase = false;

            this.Base58Prefixes = new byte[12][];
            this.Base58Prefixes[(int)Base58Type.PUBKEY_ADDRESS] = new byte[] { 75 }; // X
            this.Base58Prefixes[(int)Base58Type.SCRIPT_ADDRESS] = new byte[] { 140 }; // y
            this.Base58Prefixes[(int)Base58Type.SECRET_KEY] = new byte[] { (75 + 128) };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_NO_EC] = new byte[] { 0x01, 0x42 };
            this.Base58Prefixes[(int)Base58Type.ENCRYPTED_SECRET_KEY_EC] = new byte[] { 0x01, 0x43 };
            this.Base58Prefixes[(int)Base58Type.EXT_PUBLIC_KEY] = new byte[] { (0x04), (0x88), (0xB2), (0x1E) };
            this.Base58Prefixes[(int)Base58Type.EXT_SECRET_KEY] = new byte[] { (0x04), (0x88), (0xAD), (0xE4) };
            this.Base58Prefixes[(int)Base58Type.PASSPHRASE_CODE] = new byte[] { 0x2C, 0xE9, 0xB3, 0xE1, 0xFF, 0x39, 0xE2 };
            this.Base58Prefixes[(int)Base58Type.CONFIRMATION_CODE] = new byte[] { 0x64, 0x3B, 0xF6, 0xA8, 0x9A };
            this.Base58Prefixes[(int)Base58Type.STEALTH_ADDRESS] = new byte[] { 0x2a };
            this.Base58Prefixes[(int)Base58Type.ASSET_ID] = new byte[] { 23 };
            this.Base58Prefixes[(int)Base58Type.COLORED_ADDRESS] = new byte[] { 0x13 };

            this.Checkpoints = new Dictionary<int, CheckpointInfo>
            {
                { 0, new CheckpointInfo(new uint256("0x000000367112f710a0400b4b5d1ff42c376b3dffe753abcf0f0363571ebb8a65"), new uint256("0x0000000000000000000000000000000000000000000000000000000000000000")) }
            };

            this.Bech32Encoders = new Bech32Encoder[2];
            var encoder = new Bech32Encoder("xlc");
            this.Bech32Encoders[(int)Bech32Type.WITNESS_PUBKEY_ADDRESS] = encoder;
            this.Bech32Encoders[(int)Bech32Type.WITNESS_SCRIPT_ADDRESS] = encoder;

            this.DNSSeeds = new List<DNSSeedData>
            {
                new DNSSeedData("api.xels.io","api.xels.io"),
                new DNSSeedData("mainnet.xels.io","mainnet.xels.io")
            };

            this.SeedNodes = new List<NetworkAddress>
            {
                //new NetworkAddress(IPAddress.Parse("52.68.239.4"), this.DefaultPort ), // public node with DNS Server Enabled
                //new NetworkAddress(IPAddress.Parse("54.64.43.45"), this.DefaultPort ), // public node with DNS Server Enabled
                //new NetworkAddress(IPAddress.Parse("54.238.248.117"), this.DefaultPort), // public node
                //new NetworkAddress(IPAddress.Parse("13.114.52.87"), this.DefaultPort), // public node
                //new NetworkAddress(IPAddress.Parse("52.192.229.45"), this.DefaultPort), // public node
                //new NetworkAddress(IPAddress.Parse("52.199.121.139"), this.DefaultPort ) // public node
            };

            this.StandardScriptsRegistry = new StraxStandardScriptsRegistry();

            Assert(this.DefaultBanTimeSeconds <= this.Consensus.MaxReorgLength * this.Consensus.TargetSpacing.TotalSeconds / 2);

            // TODO: Update these when the final block is mined
            Assert(this.Consensus.HashGenesisBlock == uint256.Parse("0xf08ed070b9a5d324e667793ea7310101c6106242df6c7946798d73043a31a218"));
            Assert(this.Genesis.Header.HashMerkleRoot == uint256.Parse("0x64637abf3dd01134a4c7038916396f5cabe26c64c14a41ddd633389a7f4c28c2"));

            StraxNetwork.RegisterRules(this.Consensus);
            StraxNetwork.RegisterMempoolRules(this.Consensus);
        }
    }
}
