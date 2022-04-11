using System;
using System.Collections.Generic;
using System.Text;

namespace XelsCCDesktopWalletApp.Models
{
    public class AddressLabel
    {
        public string label { get; set; }
        public string address { get; set; }
    }
    public class AddressLabelArray
    {
        public AddressLabel[] Addresses { get; set; }
    }
}
