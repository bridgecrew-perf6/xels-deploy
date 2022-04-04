using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using Newtonsoft.Json;

namespace XelsCCDesktopWalletApp.Models
{
    public class ReceiveWalletStatus
    {

        private bool _isUsed;
        private bool _isChange;

        [JsonProperty(PropertyName = "address")]
        public string Address { get; set; }

        [JsonProperty(PropertyName = "isUsed")]
        public bool IsUsed { get; set; }

        [JsonProperty(PropertyName = "isChange")]
        public bool IsChange { get; set; }

        public decimal AmountConfirmed { get; set; }
        public decimal AmountUnconfirmed { get; set; }

        public List<ReceiveWalletStatus> UsedAddresses { get; set; }
        public List<ReceiveWalletStatus> UnusedAddresses { get; set; }
        public List<ReceiveWalletStatus> ChangedAddresses { get; set; }
    }

    // After getting the full list of received wallet. Then put them into seperate arraylist 
    public class ReceiveWalletArray
    {
        public ReceiveWalletStatus[] Addresses { get; set; }
    }

}
