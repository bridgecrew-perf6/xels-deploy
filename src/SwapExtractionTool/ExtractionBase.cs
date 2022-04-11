using System.Threading.Tasks;
using Flurl;
using Flurl.Http;
using NBitcoin;
using Xels.Bitcoin.Features.BlockStore.Models;

namespace SwapExtractionTool
{
    public abstract class ExtractionBase
    {
        public readonly int XelsNetworkApiPort;
        public readonly Network StraxNetwork;

        public const int EndHeight = 2500000;

        protected ExtractionBase(int xelsNetworkApiPort, Network straxNetwork)
        {
            this.XelsNetworkApiPort = xelsNetworkApiPort;
            this.StraxNetwork = straxNetwork;
        }

        protected async Task<BlockTransactionDetailsModel> RetrieveBlockAtHeightAsync(int blockHeight)
        {
            var blockHash = await $"http://localhost:{this.XelsNetworkApiPort}/api"
                .AppendPathSegment("consensus/getblockhash")
                .SetQueryParams(new { height = blockHeight })
                .GetJsonAsync<string>();

            if (blockHash == null)
                return null;

            BlockTransactionDetailsModel blockModel = await $"http://localhost:{this.XelsNetworkApiPort}/api"
                .AppendPathSegment("blockstore/block")
                .SetQueryParams(new SearchByHashRequest() { Hash = blockHash, ShowTransactionDetails = true, OutputJson = true })
                .GetJsonAsync<BlockTransactionDetailsModel>();

            return blockModel;
        }
    }

    public enum ExtractionType
    {
        Swap,
        Vote
    }
}
