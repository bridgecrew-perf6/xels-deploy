using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Models.SmartContractModels
{
    public static class GlobalPropertyModel
    {
        public static string WalletName { get; set; }
        public static string Address { get; set; }
        public static string AddressBalance { get; set; }

        public static string CoinUnit { get; set; }
        public static bool MiningStart{ get; set; }
        public static bool StakingStart { get; set; }
        public static bool HasBalance { get; set; }

        public static double SpendableBalance { get; set; }

        public static string selectAddressFromAddressBook { get; set; }

        public static string ChainCheckMessage { get; set; }

        public static int enterCount { get; set; }= 0;
    }
}
