using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Windows.Threading;

namespace XelsCCDesktopWalletApp.Models.CommonModels
{
    public class URLConfiguration
    {
        public static string BaseURLMain = "http://localhost:37221/api";  //main chain

        public static string BaseURLSideChain = "http://localhost:37223/api";  //side chain

        public static string BaseURL;//value assagin from program.cs

        public static HttpClient Client = new HttpClient();

        public static string Chain;

        public static string BaseURLExchange = "https://exchange.xels.io";  //exchange

        //public static string Wb3URLExchangeMain = "https://kovan.infura.io/v3/15851454d7644cff846b1b8701403647";//test token er janno
        public static string Wb3URLExchangeMain = "https://mainnet.infura.io/v3/15851454d7644cff846b1b8701403647";

        public static string SELSContractAddress = "0x0E74264EAd02B3a9768Dc4F1A509fA7F49952df6";
        public static string BELSContractAddress = "0x6fcf304f636d24ca102ab6e4e4e089115c04ebae";
        public static string TSTContractAddress = "0xfcb525e2c7351900a204d09bd507a522cebac783";
        public static bool Pagenavigation;//timer stop korar jonno 

        public static string SideChainSavePath = @"%AppData%\XelsNode\CC\CCMain";// sidechain Appdata folder file save location

        public static string MainChainSavePath = @"%AppData%\XelsNode\xlc\XlcMain";// mainchain Appdata folder file save location

    }
}
