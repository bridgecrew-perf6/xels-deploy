using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;

namespace XelsXLCDesktopWalletApp.Models
{
    public class ExchangeResponse
    {
        public string id { get; set; }
        public string xels_address { get; set; }
        public string deposit_address { get; set; }
        public string deposit_symbol { get; set; }
        public double xels_amount { get; set; }
        public double deposit_amount { get; set; }
        public string transaction_id { get; set; }
        public int status { get; set; }
    }

    // customized
    public class ExchangeData
    {
        public string excid { get; set; }
        public string deposit_address_amount_symbol { get; set; }
        public string xels_address_amount { get; set; }
        public string showstatus { get; set; }
        public bool IsDepositEnableBtn { get; set; }
    }


}