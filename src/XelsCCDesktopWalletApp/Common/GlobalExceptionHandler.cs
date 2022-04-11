using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using XelsCCDesktopWalletApp.Models.CommonModels;

namespace XelsCCDesktopWalletApp.Common
{
    public static class GlobalExceptionHandler
    {
        private static String ErrorlineNo, Errormsg, extype, exurl, hostIp, ErrorLocation, HostAdd;

        public static void SendErrorToText(Exception ex)
        {
            var line = Environment.NewLine + Environment.NewLine;

            ErrorlineNo = ex.StackTrace.Substring(ex.StackTrace.Length - 7, 7);
            Errormsg = ex.Message.ToString();
            extype = ex.GetType().ToString();
            ErrorLocation = ex.StackTrace.ToString();
            string AppDataPath;
            try
            {
                // string walletCurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (URLConfiguration.Chain == "-mainchain")
                {
                    AppDataPath = @"%AppData%\XelsNode\xlc\XlcMain\";
                }
                else
                {
                    AppDataPath = @"%AppData%\XelsNode\CC\CCMain\";
                }
                //string filepath = AppDataPath + @"\Exceptions\";

                if (!Directory.Exists(AppDataPath))
                {
                    Directory.CreateDirectory(AppDataPath);
                }
                AppDataPath = Environment.ExpandEnvironmentVariables(AppDataPath);
                AppDataPath = AppDataPath + "Exception" + DateTime.Today.ToString("dd-MM-yy") + ".txt";   //Text File Name
                if (!File.Exists(AppDataPath))
                {
                    File.Create(AppDataPath).Dispose();
                }
                using (StreamWriter sw = File.AppendText(AppDataPath))
                {
                    string error = "Log Written Date:" + " " + DateTime.Now.ToString() + line + "Error Line No :" + " " + ErrorlineNo + line + "Error Message:" + " " + Errormsg + line + "Exception Type:" + " " + extype + line + "Error Location :" + " " + ErrorLocation + line;
                    sw.WriteLine("-----------Exception Details on " + " " + DateTime.Now.ToString() + "-----------------");
                    sw.WriteLine("-------------------------------------------------------------------------------------");
                    sw.WriteLine(error);
                    sw.WriteLine("--------------------------------*End*------------------------------------------");
                    sw.WriteLine(line);
                    sw.Flush();
                    sw.Close();

                }
            }
            catch (Exception e)
            {
                e.ToString();

            }
        }
    }
}
