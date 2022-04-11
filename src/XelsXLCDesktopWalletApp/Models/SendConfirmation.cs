using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using Xels.Bitcoin.Features.Wallet;

namespace XelsXLCDesktopWalletApp.Models
{
    public class SendConfirmation
    {
        public TransactionBuilding Transaction { get; set; }
        public double TransactionFee { get; set; }
        public string Cointype { get; set; }
    }

    public class SendConfirmationSC
    {
        public TransactionBuildingSidechain Transaction { get; set; }
        public double TransactionFee { get; set; }
        //public bool sidechainEnabled { get; set; }
        public double OpReturnAmount { get; set; }
        //public bool hasOpReturn { get; set; }
        public string cointype { get; set; }
        public string FedarationAddress { get; set; }
    }
}
