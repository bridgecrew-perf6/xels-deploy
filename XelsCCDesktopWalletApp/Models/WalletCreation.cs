using System;
using System.Collections.Generic;
using System.Text;

namespace XelsCCDesktopWalletApp.Models
{
    public class WalletCreation
    {
        public string Name { get; set; }
        public string Password { get; set; }
        public string Passphrase { get; set; }
        public string Mnemonic { get; set; }
    }
}
