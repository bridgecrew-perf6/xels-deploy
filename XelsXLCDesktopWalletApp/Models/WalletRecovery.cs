using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Models
{
    public class WalletRecovery
    {
        public string Name { get; set; }
        public string Mnemonic { get; set; }

        // public DateTime creationDate { get; set; }

        public string CreationDate { get; set; }
        public string Password { get; set; }
        public string Passphrase { get; set; }
    }
}
