using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;

namespace XelsCCDesktopWalletApp.Models
{
    public class TransactionSending
    {
        public string Hex { get; set; }
    }

    public class Recipient
    {
        public string DestinationAddress { get; set; }
        public string Amount { get; set; }
    }

    public class RecipientSidechain
    {
        public string DestinationAddress { get; set; }         
        public string Amount { get; set; }
    }

    public class TransactionBuilding
    {
        public string WalletName { get; set; }
        public string AccountName { get; set; }
        public string Password { get; set; }

        public List<Recipient> Recipients { get; set; }

        //public string destinationAddress { get; set; }
        //public string amount { get; set; }
        
        public double FeeAmount { get; set; }
        public bool AllowUnconfirmed { get; set; }
        public bool ShuffleOutputs { get; set; }

        //public string opReturnData { get; set; }
        //public string opReturnAmount { get; set; }
    }

    public class TransactionBuildingSidechain
    {
        public string WalletName { get; set; }
        public string AccountName { get; set; }
        public string Password { get; set; }

        public List<RecipientSidechain> Recipients { get; set; }       

        //public string destinationAddress { get; set; }
        //public string amount { get; set; }

        public double FeeAmount { get; set; }
        public bool AllowUnconfirmed { get; set; }
        public bool ShuffleOutputs { get; set; }

        public string OpReturnData { get; set; }
        public string OpReturnAmount { get; set; }
    }

    public class MaximumBalance
    {
        public string WalletName { get; set; }
        public string AccountName { get; set; }
        public string FeeType { get; set; }
        public bool AllowUnconfirmed { get; set; }
    }

}
