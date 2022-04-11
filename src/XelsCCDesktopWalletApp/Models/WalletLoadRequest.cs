using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.Text;

namespace XelsCCDesktopWalletApp.Models
{
    public class WalletLoadRequest
    {
        //public WalletLoadRequest()
        //{
        //    this.Name = "Empty Name";
        //    this.Password = "";
        //}

        [Required(ErrorMessage = "A password is required.")]
        public string Password { get; set; }

        [Required(ErrorMessage = "The name of the wallet is missing.")]
        public string Name { get; set; }

        public List<string> WalletNames { get; set; }

    }    
}
