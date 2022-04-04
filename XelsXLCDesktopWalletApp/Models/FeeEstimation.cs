using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Models
{
    public class FeeEstimation
    {
        public string WalletName { get; set; }
        public string AccountName { get; set; }

        public List<Recipient> Recipients { get; set; }
        public string FeeType { get; set; }
        public bool AllowUnconfirmed { get; set; }


    }
    public class FeeEstimationSideChain
    {
        public string WalletName { get; set; }
        public string AccountName { get; set; }

        //public List<RecipientSidechain> Recipients { get; set; }
        public List<RecipientSidechain> Recipients { get; set; }
        public string OpReturnData { get; set; } // destinationaddr

        public string FeeType { get; set; }
        public bool AllowUnconfirmed { get; set; }

    }
}
