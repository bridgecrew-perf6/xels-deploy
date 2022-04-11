using System;
using NBitcoin;
using Newtonsoft.Json;
using Xels.Bitcoin.Utilities.JsonConverters;

namespace XelsCCDesktopWalletApp.Models
{
    public class WalletGeneralInfoModel
    {
        public string WalletName { get; set; }
        public string Network { get; set; }
        public string CreationTime { get; set; }
        public bool IsDecrypted { get; set; }
        public int? LastBlockSyncedHeight { get; set; }
        public int? ChainTip { get; set; }
        public bool IsChainSynced { get; set; }
        public int ConnectedNodes { get; set; }
    }
}
