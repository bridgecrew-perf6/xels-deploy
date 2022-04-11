using System;
using System.Collections.Generic;
using System.Text;
using NBitcoin;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Features.Wallet.Models;

namespace XelsXLCDesktopWalletApp.Models
{
    public class WalletBalance
    {
        public string AccountName { get; set; }
        public string AccountHdPath { get; set; }

        public string CoinType { get; set; }

        public double AmountConfirmed { get; set; }
        public double AmountUnconfirmed { get; set; }
        public double SpendableAmount { get; set; }
        public double MaxSpendableAmount { get; set; }

        public double Fee { get; set; }

        //public IEnumerable<AddressModel> Addresses { get; set; }

    }

    public class WalletBalanceArray
    {
        public WalletBalance[] Balances { get; set; }
    }
}
