﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestPlatform.CommunicationUtilities;
using NBitcoin;
using Newtonsoft.Json.Linq;
using Xels.Bitcoin.Connection;
using Xels.Bitcoin.Controllers.Models;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.Miner.Controllers;
using Xels.Bitcoin.Features.Miner.Interfaces;
using Xels.Bitcoin.Features.Miner.Models;
using Xels.Bitcoin.Features.RPC.Models;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Controllers;
using Xels.Bitcoin.Features.Wallet.Models;
using Xels.Bitcoin.IntegrationTests.Common;
using Xels.Bitcoin.IntegrationTests.Common.EnvironmentMockUpHelpers;
using Xels.Bitcoin.IntegrationTests.Common.TestNetworks;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Tests.Common;
using Xels.Bitcoin.Tests.Common.TestFramework;
using Xunit.Abstractions;

namespace Xels.Bitcoin.IntegrationTests.API
{
    public partial class ApiSpecification : BddSpecification
    {
        private const string JsonContentType = "application/json";
        private const string WalletName = "mywallet";
        private const string WalletAccountName = "account 0";
        private const string WalletPassword = "password";
        private const string WalletPassphrase = "wallet_passphrase";

        // BlockStore
        private const string BlockUri = "api/blockstore/block";
        private const string GetBlockCountUri = "api/blockstore/getblockcount";

        // ConnectionManager
        private const string AddnodeUri = "api/connectionmanager/addnode";
        private const string GetPeerInfoUri = "api/connectionmanager/getpeerinfo";

        // Consensus
        private const string GetBestBlockHashUri = "api/consensus/getbestblockhash";
        private const string GetBlockHashUri = "api/consensus/getblockhash";

        // Mempool
        private const string GetRawMempoolUri = "api/mempool/getrawmempool";

        // Mining
        private const string GenerateUri = "api/mining/generate";

        // Node
        private const string GetBlockHeaderUri = "api/node/getblockheader";
        private const string GetRawTransactionUri = "api/node/getrawtransaction";
        private const string GetTxOutUri = "api/node/gettxout";
        private const string StatusUri = "api/node/status";
        private const string ValidateAddressUri = "api/node/validateaddress";

        // RPC
        private const string RPCCallByNameUri = "api/rpc/callbyname";
        private const string RPCListmethodsUri = "api/rpc/listmethods";

        // Staking
        private const string StartStakingUri = "api/staking/startstaking";
        private const string GetStakingInfoUri = "api/staking/getstakinginfo";

        // Wallet
        private const string AccountUri = "api/wallet/account";
        private const string GeneralInfoUri = "api/wallet/general-info";
        private const string BalanceUri = "api/wallet/balance";
        private const string RecoverViaExtPubKeyUri = "api/wallet/recover-via-extpubkey";

        private CoreNode xelsPosApiNode;
        private CoreNode firstXelsPowApiNode;
        private CoreNode secondXelsPowApiNode;

        private HttpResponseMessage response;
        private string responseText;

        private int maturity = 1;
        private HdAddress receiverAddress;
        private readonly Money transferAmount = Money.COIN * 1;
        private NodeBuilder powNodeBuilder;
        private NodeBuilder posNodeBuilder;

        private Transaction transaction;
        private uint256 block;
        private Uri apiUri;
        private HttpClient httpClient;
        private HttpClientHandler httpHandler;
        private Network powNetwork;
        private Network posNetwork;

        public ApiSpecification(ITestOutputHelper output) : base(output)
        {
        }

        protected override void BeforeTest()
        {
            this.httpHandler = new HttpClientHandler() { ServerCertificateCustomValidationCallback = (request, cert, chain, errors) => true };
            this.httpClient = new HttpClient(this.httpHandler);
            this.httpClient.DefaultRequestHeaders.Accept.Clear();
            this.httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(JsonContentType));

            this.powNodeBuilder = NodeBuilder.Create(Path.Combine(this.GetType().Name, this.CurrentTest.DisplayName));
            this.posNodeBuilder = NodeBuilder.Create(Path.Combine(this.GetType().Name, this.CurrentTest.DisplayName));

            this.powNetwork = new BitcoinRegTestOverrideCoinbaseMaturity(1);
            this.posNetwork = new StraxRegTest();
        }

        protected override void AfterTest()
        {
            if (this.httpClient != null)
            {
                this.httpClient.Dispose();
                this.httpClient = null;
            }

            if (this.httpHandler != null)
            {
                this.httpHandler.Dispose();
                this.httpHandler = null;
            }

            this.powNodeBuilder.Dispose();
            this.posNodeBuilder.Dispose();
        }

        private void a_proof_of_stake_node_with_api_enabled()
        {
            this.xelsPosApiNode = this.posNodeBuilder.CreateXelsPosNode(this.posNetwork).Start();

            this.xelsPosApiNode.FullNode.NodeService<IPosMinting>(true).Should().NotBeNull();
            this.apiUri = this.xelsPosApiNode.FullNode.NodeService<ApiSettings>().ApiUri;
        }

        private void the_proof_of_stake_node_has_passed_LastPOWBlock()
        {
            typeof(ChainedHeader).GetProperty("Height").SetValue(this.xelsPosApiNode.FullNode.ConsensusManager().Tip,
                this.xelsPosApiNode.FullNode.Network.Consensus.LastPOWBlock + 1);
        }

        private void two_connected_proof_of_work_nodes_with_api_enabled()
        {
            a_proof_of_work_node_with_api_enabled();
            a_second_proof_of_work_node_with_api_enabled();
            calling_addnode_connects_two_nodes();

            this.receiverAddress = this.secondXelsPowApiNode.FullNode.WalletManager()
                .GetUnusedAddress(new WalletAccountReference(WalletName, WalletAccountName));
        }

        private void a_proof_of_work_node_with_api_enabled()
        {
            this.firstXelsPowApiNode = this.powNodeBuilder.CreateXelsPowNode(this.powNetwork).WithWallet().Start();
            this.firstXelsPowApiNode.Mnemonic = this.firstXelsPowApiNode.Mnemonic;

            this.firstXelsPowApiNode.FullNode.Network.Consensus.CoinbaseMaturity = this.maturity;
            this.apiUri = this.firstXelsPowApiNode.FullNode.NodeService<ApiSettings>().ApiUri;
        }

        private void a_second_proof_of_work_node_with_api_enabled()
        {
            this.secondXelsPowApiNode = this.powNodeBuilder.CreateXelsPowNode(this.powNetwork).WithWallet().Start();
            this.secondXelsPowApiNode.Mnemonic = this.secondXelsPowApiNode.Mnemonic;
        }

        protected void a_block_is_mined_creating_spendable_coins()
        {
            TestHelper.MineBlocks(this.firstXelsPowApiNode, 1);
        }

        private void more_blocks_mined_past_maturity_of_original_block()
        {
            TestHelper.MineBlocks(this.firstXelsPowApiNode, this.maturity);
        }

        private async Task a_real_transaction()
        {
            await this.SendTransaction(await this.BuildTransaction());
        }

        private void the_block_with_the_transaction_is_mined()
        {
            this.block = TestHelper.MineBlocks(this.firstXelsPowApiNode, 2).BlockHashes[0];
        }

        private void calling_startstaking()
        {
            var stakingRequest = new StartStakingRequest() { Name = WalletName, Password = WalletPassword };

            // With these tests we still need to create the wallets outside of the builder
            (_, this.xelsPosApiNode.Mnemonic) = this.xelsPosApiNode.FullNode.WalletManager().CreateWallet(WalletPassword, WalletName, WalletPassphrase);

            var httpRequestContent = new StringContent(stakingRequest.ToString(), Encoding.UTF8, JsonContentType);
            this.response = this.httpClient.PostAsync($"{this.apiUri}{StartStakingUri}", httpRequestContent).GetAwaiter().GetResult();

            this.response.StatusCode.Should().Be(HttpStatusCode.OK);
            this.responseText = this.response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            this.responseText.Should().BeEmpty();
        }

        private void calling_rpc_getblockhash_via_callbyname()
        {
            this.send_api_post_request(RPCCallByNameUri, new { methodName = "getblockhash", height = 0 });
        }

        private void calling_rpc_listmethods()
        {
            this.send_api_get_request($"{RPCListmethodsUri}");
        }

        private void calling_recover_via_extpubkey_for_account_0()
        {
            this.RecoverViaExtPubKey(WalletName, "xpub6CCo1eBTzCPDuV7MDAV3SmRPNJyygTVc9FLwWey8qYQSnKFyv3iGsYpX9P5opDj1DXhbTxSgyy5jnKZPoCWqCtpsZdcGJWqrWri5LnQbPex", 0);
        }

        private void attempting_to_add_an_account()
        {
            var request = new GetUnusedAccountModel()
            {
                WalletName = WalletName,
                Password = WalletPassword
            };

            this.response = this.httpClient.PostAsJsonAsync($"{this.apiUri}{AccountUri}", request)
                .GetAwaiter().GetResult();
        }

        private void an_extpubkey_only_wallet_with_account_0()
        {
            this.RecoverViaExtPubKey(WalletName, "xpub6CCo1eBTzCPDuV7MDAV3SmRPNJyygTVc9FLwWey8qYQSnKFyv3iGsYpX9P5opDj1DXhbTxSgyy5jnKZPoCWqCtpsZdcGJWqrWri5LnQbPex", 0);
        }

        private void RecoverViaExtPubKey(string walletName, string extPubKey, int accountIndex)
        {
            var request = new WalletExtPubRecoveryRequest
            {
                ExtPubKey = extPubKey,
                AccountIndex = accountIndex,
                Name = walletName,
                CreationDate = DateTime.UtcNow
            };

            this.send_api_post_request(RecoverViaExtPubKeyUri, request);
            this.response.StatusCode.Should().Be(StatusCodes.Status200OK);
        }

        private void send_api_post_request<T>(string url, T request)
        {
            this.response = this.httpClient.PostAsJsonAsync($"{this.apiUri}{url}", request)
                .GetAwaiter().GetResult();
        }

        private void a_wallet_is_created_without_private_key_for_account_0()
        {
            this.CheckAccountExists(WalletName, 0);
        }

        private void a_wallet_is_created_without_private_key_for_account_1()
        {
            this.CheckAccountExists("Secondary_Wallet", 1);
        }

        private void CheckAccountExists(string walletName, int accountIndex)
        {
            this.send_api_get_request($"{BalanceUri}?walletname={walletName}&AccountName=account {accountIndex}");

            this.responseText.Should().StartWith("{\"balances\":[{\"accountName\":\"account " + accountIndex + $"\",\"accountHdPath\":\"m/44'/{this.posNetwork.Consensus.CoinType}'/" + accountIndex + $"'\",\"coinType\":{this.posNetwork.Consensus.CoinType},\"amountConfirmed\":0,\"amountUnconfirmed\":0,\"spendableAmount\":0,\"addresses\":");
        }

        private void calling_general_info()
        {
            // With these tests we still need to create the wallets outside of the builder
            this.xelsPosApiNode.FullNode.WalletManager().CreateWallet(WalletPassword, WalletName, WalletPassphrase);
            this.xelsPosApiNode.FullNode.WalletManager().SaveWallet(WalletName);

            this.send_api_get_request($"{GeneralInfoUri}?name={WalletName}");
        }

        private void calling_addnode_connects_two_nodes()
        {
            this.send_api_get_request($"{AddnodeUri}?endpoint={this.secondXelsPowApiNode.Endpoint.ToString()}&command=onetry");
            this.responseText.Should().Be("true");

            TestBase.WaitLoop(() => TestHelper.AreNodesSynced(this.firstXelsPowApiNode, this.secondXelsPowApiNode));
        }

        private void calling_block()
        {
            this.send_api_get_request($"{BlockUri}?Hash={this.block}&OutputJson=true");
        }

        private void calling_getblockcount()
        {
            this.send_api_get_request(GetBlockCountUri);
        }

        private void calling_getbestblockhash()
        {
            this.send_api_get_request(GetBestBlockHashUri);
        }

        private void calling_getpeerinfo()
        {
            this.send_api_get_request(GetPeerInfoUri);
        }

        private void calling_getblockhash()
        {
            this.send_api_get_request($"{GetBlockHashUri}?height=0");
        }

        private void calling_getblockheader()
        {
            this.send_api_get_request($"{GetBlockHeaderUri}?hash={KnownNetworks.RegTest.Consensus.HashGenesisBlock.ToString()}");
        }

        private void calling_status()
        {
            this.send_api_get_request(StatusUri);
        }

        private void calling_validateaddress()
        {
            string address = this.firstXelsPowApiNode.FullNode.WalletManager()
                .GetUnusedAddress()
                .ScriptPubKey.GetDestinationAddress(this.firstXelsPowApiNode.FullNode.Network).ToString();
            this.send_api_get_request($"{ValidateAddressUri}?address={address}");
        }

        private void calling_getrawmempool()
        {
            this.send_api_get_request(GetRawMempoolUri);
        }

        private void calling_gettxout_notmempool()
        {
            this.send_api_get_request($"{GetTxOutUri}?trxid={this.transaction.GetHash().ToString()}&vout=1&includeMemPool=false");
        }

        private void calling_getrawtransaction_nonverbose()
        {
            this.send_api_get_request($"{GetRawTransactionUri}?trxid={this.transaction.GetHash().ToString()}&verbose=false");
        }

        private void calling_getrawtransaction_verbose()
        {
            this.send_api_get_request($"{GetRawTransactionUri}?trxid={this.transaction.GetHash().ToString()}&verbose=true");
        }

        private void calling_getstakinginfo()
        {
            this.send_api_get_request(GetStakingInfoUri);
        }

        private void calling_generate()
        {
            var request = new MiningRequest() { BlockCount = 1 };
            this.send_api_post_request(GenerateUri, request);
        }

        private void a_valid_address_is_validated()
        {
            JObject jObjectResponse = JObject.Parse(this.responseText);
            jObjectResponse["isvalid"].Value<bool>().Should().BeTrue();
        }

        private void the_consensus_tip_blockhash_is_returned()
        {
            this.responseText.Should().Be("\"" + this.firstXelsPowApiNode.FullNode.ConsensusManager().Tip.HashBlock.ToString() + "\"");
        }

        private void the_blockcount_should_match_consensus_tip_height()
        {
            this.responseText.Should().Be(this.firstXelsPowApiNode.FullNode.ConsensusManager().Tip.Height.ToString());
        }

        private void the_real_block_should_be_retrieved()
        {
            var blockResponse = JsonDataSerializer.Instance.Deserialize<BlockModel>(this.responseText);
            blockResponse.Hash.Should().Be(this.block.ToString());
        }

        private void the_block_should_contain_the_transaction()
        {
            var blockResponse = JsonDataSerializer.Instance.Deserialize<BlockModel>(this.responseText);
            blockResponse.Transactions[1].Should().Be(this.transaction.GetHash().ToString());
        }

        private void it_is_rejected_as_forbidden()
        {
            this.response.StatusCode.Should().Be(StatusCodes.Status403Forbidden);
        }

        private void the_blockhash_is_returned()
        {
            this.responseText.Should().Be("\"" + KnownNetworks.RegTest.Consensus.HashGenesisBlock.ToString() + "\"");
        }

        private void the_blockhash_is_returned_from_post()
        {
            var responseContent = this.response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            responseContent.Should().Be("\"" + KnownNetworks.RegTest.Consensus.HashGenesisBlock.ToString() + "\"");
        }

        private void a_full_list_of_available_commands_is_returned()
        {
            var commands = JsonDataSerializer.Instance.Deserialize<List<RpcCommandModel>>(this.responseText);

            commands.Count.Should().Be(37);
        }

        private void status_information_is_returned()
        {
            var statusNode = this.firstXelsPowApiNode.FullNode;
            var statusResponse = JsonDataSerializer.Instance.Deserialize<StatusModel>(this.responseText);
            statusResponse.Agent.Should().Contain(statusNode.Settings.Agent);
            statusResponse.Version.Should().Be(statusNode.Version.ToString());
            statusResponse.Network.Should().Be(statusNode.Network.Name);
            statusResponse.ConsensusHeight.Should().Be(0);
            statusResponse.BlockStoreHeight.Should().Be(0);
            statusResponse.ProtocolVersion.Should().Be((uint)(statusNode.Settings.ProtocolVersion));
            statusResponse.RelayFee.Should().Be(statusNode.Settings.MinRelayTxFeeRate.FeePerK.ToUnit(MoneyUnit.BTC));
            statusResponse.DataDirectoryPath.Should().Be(statusNode.Settings.DataDir);

            List<string> featuresNamespaces = statusResponse.FeaturesData.Select(f => f.Namespace).ToList();
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Base.BaseFeature");
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Features.Api.ApiFeature");
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Features.BlockStore.BlockStoreFeature");
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Features.Consensus.PowConsensusFeature");
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Features.MemoryPool.MempoolFeature");
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Features.Miner.MiningFeature");
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Features.RPC.RPCFeature");
            featuresNamespaces.Should().Contain("Xels.Bitcoin.Features.Wallet.WalletFeature");

            statusResponse.FeaturesData.All(f => f.State == "Initialized").Should().BeTrue();
        }

        private void general_information_about_the_wallet_and_node_is_returned()
        {
            var generalInfoResponse = JsonDataSerializer.Instance.Deserialize<WalletGeneralInfoModel>(this.responseText);
            generalInfoResponse.WalletName.Should().Be(WalletName);
            generalInfoResponse.Network.Should().Be("StraxRegTest");
            generalInfoResponse.ChainTip.Should().Be(0);
            generalInfoResponse.IsChainSynced.Should().BeFalse();
            generalInfoResponse.ConnectedNodes.Should().Be(0);
            generalInfoResponse.IsDecrypted.Should().BeTrue();
        }

        private void the_blockheader_is_returned()
        {
            var blockheaderResponse = JsonDataSerializer.Instance.Deserialize<BlockHeaderModel>(this.responseText);
            blockheaderResponse.PreviousBlockHash.Should()
                .Be("0000000000000000000000000000000000000000000000000000000000000000");
        }

        private void the_transaction_is_found_in_mempool()
        {
            List<string> transactionList = JArray.Parse(this.responseText).ToObject<List<string>>();
            transactionList[0].Should().Be(this.transaction.GetHash().ToString());
        }

        private void staking_is_enabled_but_nothing_is_staked()
        {
            var miningRpcController = this.xelsPosApiNode.FullNode.NodeController<StakingRpcController>();
            GetStakingInfoModel stakingInfo = miningRpcController.GetStakingInfo();
            stakingInfo.Should().NotBeNull();
            stakingInfo.Enabled.Should().BeTrue();
            stakingInfo.Staking.Should().BeFalse();
        }

        private void the_transaction_hash_is_returned()
        {
            this.responseText.Should().Be("\"" + this.transaction.ToHex() + "\"");
        }

        private void a_verbose_raw_transaction_is_returned()
        {
            var verboseRawTransactionResponse = JsonDataSerializer.Instance.Deserialize<TransactionVerboseModel>(this.responseText);
            verboseRawTransactionResponse.Hex.Should().Be(this.transaction.ToHex());
            verboseRawTransactionResponse.TxId.Should().Be(this.transaction.GetHash().ToString());
        }

        private void a_single_connected_peer_is_returned()
        {
            List<PeerNodeModel> getPeerInfoResponseList = JArray.Parse(this.responseText).ToObject<List<PeerNodeModel>>();
            getPeerInfoResponseList.Count.Should().Be(1);
            getPeerInfoResponseList[0].Id.Should().Be(0);
            getPeerInfoResponseList[0].Address.Should().Contain("[::ffff:127.0.0.1]");
        }

        private void the_txout_is_returned()
        {
            var txOutResponse = JsonDataSerializer.Instance.Deserialize<GetTxOutModel>(this.responseText);
            txOutResponse.Value.Should().Be(this.transferAmount);
        }

        private void staking_information_is_returned()
        {
            var stakingInfoModel = JsonDataSerializer.Instance.Deserialize<GetStakingInfoModel>(this.responseText);
            stakingInfoModel.Enabled.Should().Be(false);
            stakingInfoModel.Staking.Should().Be(false);
        }

        private void a_method_not_allowed_error_is_returned()
        {
            this.response.StatusCode.Should().Be(StatusCodes.Status405MethodNotAllowed);
        }

        private void send_api_get_request(string apiendpoint)
        {
            this.response = this.httpClient.GetAsync($"{this.apiUri}{apiendpoint}").GetAwaiter().GetResult();
            if (this.response.IsSuccessStatusCode)
            {
                this.responseText = this.response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            }
        }

        private async Task SendTransaction(IActionResult transactionResult)
        {
            var walletTransactionModel = (WalletBuildTransactionModel)(transactionResult as JsonResult)?.Value;
            this.transaction = this.firstXelsPowApiNode.FullNode.Network.CreateTransaction(walletTransactionModel.Hex);
            await this.firstXelsPowApiNode.FullNode.NodeController<WalletController>()
                .SendTransactionAsync(new SendTransactionRequest(walletTransactionModel.Hex));
        }

        private async Task<IActionResult> BuildTransaction()
        {
            IActionResult transactionResult = await this.firstXelsPowApiNode.FullNode
                .NodeController<WalletController>()
                .BuildTransactionAsync(new BuildTransactionRequest
                {
                    AccountName = WalletAccountName,
                    AllowUnconfirmed = true,
                    ShuffleOutputs = false,
                    Recipients = new List<RecipientModel>
                    {
                        new RecipientModel
                            {DestinationAddress = this.receiverAddress.Address, Amount = this.transferAmount.ToString()}
                    },
                    FeeType = FeeType.Medium.ToString("D"),
                    Password = WalletPassword,
                    WalletName = WalletName,
                    FeeAmount = Money.Satoshis(82275).ToString() // Minimum fee
                });

            return transactionResult;
        }
    }
}
