using System;
using System.Collections.Generic;
using System.Text;

namespace XelsCCDesktopWalletApp.Models
{
    public class ExchangeOrder
    {
        public string xels_address { get; set; }
        public double deposit_amount { get; set; }
        public string deposit_symbol { get; set; }
        public string user_code { get; set; }
    }
}
