using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IWshRuntimeLibrary;
using NBitcoin.Protocol;
using Xels.Bitcoin;
using Xels.Bitcoin.Builder;
using Xels.Bitcoin.Configuration;
using Xels.Bitcoin.Consensus;
using Xels.Bitcoin.Features.Api;
using Xels.Bitcoin.Features.BlockStore;
using Xels.Bitcoin.Features.ColdStaking;
using Xels.Bitcoin.Features.Consensus;
using Xels.Bitcoin.Features.ExternalApi;
using Xels.Bitcoin.Features.MemoryPool;
using Xels.Bitcoin.Features.Miner;
using Xels.Bitcoin.Features.Notifications;
using Xels.Bitcoin.Features.RPC;
using Xels.Bitcoin.Features.SignalR;
using Xels.Bitcoin.Features.Wallet;
using Xels.Bitcoin.Networks;
using Xels.Bitcoin.Utilities;
using Xels.Features.Diagnostic;
using Xels.Features.SQLiteWalletRepository;
using Xels.Features.Unity3dApi;
using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models.CommonModels;






namespace XelsXLCDesktopWalletApp
{
    public class Program
    {
        private const string MainchainArgument = "-mainchain";

        [STAThread]
        public static void Main(string[] args)
        {
            //args = new string[] { MainchainArgument };

            App app = new App();
            //CreateShortCut();

            MainAsync(args).Wait(5);

            app.InitializeComponent();
            app.Run();
        }
        public static async Task MainAsync(string[] args)
        {
            try
            {
                IFullNode fullNode = BuildXlcNode(args);

                if (fullNode != null)
                    await fullNode.RunAsync();
            }
            catch (Exception ex)
            {
                GlobalExceptionHandler.SendErrorToText(ex);
                //Console.WriteLine("There was a problem initializing the node. Details: '{0}'", ex.Message);
            }
        }

        private static IFullNode BuildXlcNode(string[] args)
        {

            //URLConfiguration.Chain = args[0];
            //// TODO: Hardcode -addressindex for better user experience
            //URLConfiguration.BaseURL = "http://localhost:37221/api";//Main Chain Url


            //// TODO: Hardcode -addressindex for better user experience

            //var nodeSettings = new NodeSettings(networksSelector: Networks.Xlc, protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, args: args)
            //{
            //    MinProtocolVersion = ProtocolVersion.ALT_PROTOCOL_VERSION
            //};

            //DbType dbType = nodeSettings.GetDbType();

            //IFullNode node = new FullNodeBuilder()
            //    .UseNodeSettings(nodeSettings, dbType)
            //    .UseBlockStore(dbType)
            //    .UseTransactionNotification()
            //    .UseBlockNotification()
            //    .UseApi()
            //    .UseMempool()
            //    .AddRPC()
            //    .UsePosConsensus(dbType)
            //    .UseWallet()
            //    .AddSQLiteWalletRepository()
            //    .AddPowPosMining(true)
            //    .Build();


            URLConfiguration.BaseURL = "http://localhost:17103/api";//Main Chain Url
            var nodeSettings = new NodeSettings(networksSelector: Networks.Strax, protocolVersion: ProtocolVersion.PROVEN_HEADER_VERSION, args: args)
            {
                MinProtocolVersion = ProtocolVersion.PROVEN_HEADER_VERSION
            };

            // Set the console window title to identify this as a Strax full node (for clarity when running Strax and Cirrus on the same machine).
            //Console.Title = $"Strax Full Node {nodeSettings.Network.NetworkType}";

            DbType dbType = nodeSettings.GetDbType();

            IFullNodeBuilder nodeBuilder = new FullNodeBuilder()
                .UseNodeSettings(nodeSettings, dbType)
                .UseBlockStore(dbType)
                .UsePosConsensus(dbType)
                .UseMempool()
                .UseColdStakingWallet()
                .AddSQLiteWalletRepository()
                .AddPowPosMining(true)
                .UseApi()
                .UseUnity3dApi()
                .AddRPC()
                .AddSignalR(options =>
                {
                    DaemonConfiguration.ConfigureSignalRForStrax(options);
                })
                .UseDiagnosticFeature()
                .AddExternalApi();

            IFullNode node = nodeBuilder.Build();

            return node;
        }


        //public static void CreateShortCut()
        //{

        //    string[] argumentList = { MainchainArgument };

        //    string destinationPath = Directory.GetCurrentDirectory();

        //    //Console.WriteLine(distinationPath);
        //    //Console.ReadLine();
        //    foreach (var arg in argumentList)
        //    {

        //        object shDesktop = (object)"Desktop";
        //        WshShell shell = new WshShell();

        //        string shortcutAddress = (string)shell.SpecialFolders.Item(ref shDesktop) + @"\xels-app" + arg + ".lnk";
        //        if (!System.IO.File.Exists(shortcutAddress))
        //        {
        //            IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(shortcutAddress);

        //            shortcut.Arguments = arg;
        //            shortcut.TargetPath = destinationPath + @"\XelsXLCDesktopWalletApp.exe";
        //            shortcut.Save();
        //        }
        //    }
        //}
    }
}
