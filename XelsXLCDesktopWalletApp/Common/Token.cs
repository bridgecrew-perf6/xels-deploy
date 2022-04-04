using System;
using System.Collections.Generic;
using System.Text;

namespace XelsXLCDesktopWalletApp.Common
{
    public class Token
    {
        public string name { get; set; }
        public string contract { get; set; }
        public List<Abi> abi { get; set; }
    }


    public class Abi
    {
        public bool constant { get; set; }
        public List<Inputs> inputs { get; set; }
        public string name { get; set; }
        public List<Outputs> outputs { get; set; }

        public bool payable { get; set; }
        public string stateMutability { get; set; }
        public string type { get; set; }
    }
    public class Inputs
    {
        public string name { get; set; }
        public string type { get; set; }
    }
    public class Outputs
    {
        public string name { get; set; }
        public string type { get; set; }
    }


}
