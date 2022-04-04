using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Models
{
    public class Error
    {
        public int status { get; set; }
        public string message { get; set; }
        public string description { get; set; }
    }
    public class LoginError
    {
        public Error[] errors { get; set; }
    }
}
