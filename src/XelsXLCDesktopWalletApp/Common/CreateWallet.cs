using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Nethereum.Web3.Accounts;
using Newtonsoft.Json;
using Nethereum.Web3;
using Nethereum.Contracts.ContractHandlers;
using Xels.Bitcoin.Features.Interop.ETHClient;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using NBitcoin;
using System.Windows;
using Newtonsoft.Json.Linq;

namespace XelsXLCDesktopWalletApp.Common
{

    public class Wallet
    {
        public string Address { get; set; }
        public string PrivateKey { get; set; }
    }

    public class StoredWallet
    {
        public string Address { get; set; }
        public string PrivateKey { get; set; }
        public string Walletname { get; set; }
        public string Coin { get; set; }
        public string Wallethash { get; set; }
    }

    public class CreateWallet
    {
        Token token = new Token();

        public Wallet WalletCreation(string mnemonics)
        {
            try
            {
                string keyPath = "m/44'/60'/0'/0";
                var mnemonic = new Mnemonic(mnemonics);
                var keyPathToDerive = NBitcoin.KeyPath.Parse("");
                var pk = new ExtKey(mnemonic.DeriveSeed("")).Derive(keyPathToDerive);
                ExtKey keyNew = pk.Derive(0);
                var pkeyBytes = keyNew.PrivateKey.PubKey.ToHex();

                var account = new Account("0x" + pkeyBytes);
                Wallet Wallet = new Wallet() { Address = account.Address, PrivateKey = account.PrivateKey };
                return Wallet;
            }
            catch (Exception s)
            {
                GlobalExceptionHandler.SendErrorToText(s);
            }

            return null;

        }

        public Wallet WalletCreationFromPk(string pKey)
        {
            try
            {
                Wallet wallet = new Wallet();
                var account = new Account(pKey);

                wallet.PrivateKey = pKey;
                wallet.Address = account.Address;

                return wallet;
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
                return null;
            }
        }

        public bool StoreLocally(Wallet wallet, string walletname, string symbol, string wallethash)
        {
            string JSONresult="";
            try
            {
                string AppDataPath;
                if (walletname != null)
                {
                    List<StoredWallet> storedWallets = new List<StoredWallet>();
                    List<StoredWallet> returnedWallets = new List<StoredWallet>();

                    returnedWallets = RetrieveWallets();

                    if (returnedWallets != null && returnedWallets.Count > 0)
                    {
                        storedWallets = returnedWallets;
                    }
                    else
                    {
                        StoredWallet storedWallet = new StoredWallet();
                        storedWallet.Address = wallet.Address;
                        storedWallet.PrivateKey = wallet.PrivateKey;
                        storedWallet.Walletname = walletname;
                        storedWallet.Coin = symbol;
                        storedWallet.Wallethash = wallethash;
                        storedWallets.Add(storedWallet);
                    }
                    if (storedWallets.Count > 0)
                    {
                        foreach (var a in storedWallets.ToList())
                        {
                            int i = storedWallets.FindIndex(w => w.Coin == symbol && w.Walletname == walletname);

                            StoredWallet storedWallet = new StoredWallet();
                            storedWallet.Address = wallet.Address;
                            storedWallet.PrivateKey = wallet.PrivateKey;
                            storedWallet.Walletname = walletname;
                            storedWallet.Coin = symbol;
                            storedWallet.Wallethash = wallethash;

                            if (i != -1)
                            {
                                storedWallets[i] = storedWallet;
                            }
                            else
                            {
                                storedWallets.Add(storedWallet);
                            }
                        }

                        JSONresult = JsonConvert.SerializeObject(storedWallets.ToArray(), Formatting.Indented);
                    }


                    //string walletCurrentDirectory = Directory.GetCurrentDirectory(); // AppDomain.CurrentDomain.BaseDirectory;

                    if (URLConfiguration.Chain == "-mainchain")
                    {
                        AppDataPath = URLConfiguration.MainChainSavePath;
                    }
                    else
                    {
                        AppDataPath = URLConfiguration.SideChainSavePath;
                    }
                    AppDataPath = Environment.ExpandEnvironmentVariables(AppDataPath);
                    string path = AppDataPath + @"\SelsBelsAddress.json";

                    //string path = Path.GetFullPath(walletFile);

                    //File.SetAttributes(path, FileAttributes.Hidden);


                    if (File.Exists(path))
                    {
                        File.Delete(path);

                        using (var sw = new StreamWriter(path, true))
                        {
                            sw.WriteLine(JSONresult.ToString());
                            sw.Close();
                        }
                    }
                    else if (!File.Exists(path))
                    {
                        using (var sw = new StreamWriter(path, true))
                        {
                            sw.WriteLine(JSONresult.ToString());
                            sw.Close();
                        }
                    }
                    return true;
                }
                else
                {
                    return false;
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
            return false;
        }

        public List<StoredWallet> RetrieveWallets()
        {
            try
            {
                string AppDataPath;
                List<StoredWallet> wallets = new List<StoredWallet>();

                //string walletCurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

                if (URLConfiguration.Chain == "-mainchain")
                {
                    AppDataPath = URLConfiguration.MainChainSavePath;
                }
                else
                {
                    AppDataPath = URLConfiguration.SideChainSavePath;
                }
                AppDataPath = Environment.ExpandEnvironmentVariables(AppDataPath);
                string walletFile = AppDataPath + @"\SelsBelsAddress.json";
                //string walletFile = Path.Combine(walletCurrentDirectory, @"..\..\..\File\Wallets.json");
                //string path = Path.GetFullPath(walletFile);

                if (File.Exists(walletFile))
                {
                    using (StreamReader r = new StreamReader(walletFile))
                    {
                        string json = r.ReadToEnd();

                        if (json != "{ }" || json != "")
                        {
                            wallets = JsonConvert.DeserializeObject<List<StoredWallet>>(json);
                        }

                        return wallets;
                    }
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
            return null;
        }

        public StoredWallet GetLocalWallet(string walletname, string symbol)
        {
            StoredWallet wallet = new StoredWallet();
            List<StoredWallet> wallets = new List<StoredWallet>();
            string AppDataPath;
            try
            {
                if (URLConfiguration.Chain == "-mainchain")
                {
                    AppDataPath = URLConfiguration.MainChainSavePath;
                }
                else
                {
                    AppDataPath = URLConfiguration.SideChainSavePath;
                }
                AppDataPath = Environment.ExpandEnvironmentVariables(AppDataPath);
                //string walletCurrentDirectory = AppDomain.CurrentDomain.BaseDirectory; //Directory.GetCurrentDirectory();// 
                //string walletFile = Path.Combine(walletCurrentDirectory, @"..\..\..\File\Wallets.json");
                //string path = Path.GetFullPath(walletFile);

                string path = AppDataPath + @"\SelsBelsAddress.json";

                using (StreamReader r = new StreamReader(path))
                {
                    string json = r.ReadToEnd();


                    if (json != "{ }" || json != "")
                    {
                        wallets = JsonConvert.DeserializeObject<List<StoredWallet>>(json);
                        wallet = wallets.Where(c => c.Walletname == walletname && c.Coin == symbol).FirstOrDefault();
                    }

                    return wallet;
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
            return wallet;
        }

        //public void Initialize(string cointype)
        //{
        //    using (StreamReader r = new StreamReader(@"D:\All_Projects\xels-fullnode-wpf-v4\src\XelsXLCDesktopWalletApp\Config\Token.json"))
        //    {
        //        string json = r.ReadToEnd();
        //        List<Token> tokens = new List<Token>();

        //        if (json != null)
        //        {
        //            tokens = JsonConvert.DeserializeObject<List<Token>>(json);
        //            this.token = tokens.Where(c => c.name == cointype).FirstOrDefault();
        //        }

        //    }
        //}

        public async Task<double> GetBalanceMainAsync(string address, string cointype)
        {
            try
            {
                var url = URLConfiguration.Wb3URLExchangeMain;
                Web3 web3 = new Web3(url);
                string contractAddress = "";

                if (cointype == "SELS")
                {
                    contractAddress = URLConfiguration.SELSContractAddress;
                }
                else if (cointype == "BELS")
                {
                    contractAddress = URLConfiguration.BELSContractAddress;
                }
                else if (cointype == "TST")//test er jonno
                {
                    contractAddress = URLConfiguration.TSTContractAddress;
                }

                var balanceOfFunctionMessage = new BalanceOfFunction()
                {
                    Owner = address
                };

                IContractQueryHandler<BalanceOfFunction> balanceHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
                BigInteger balance = await balanceHandler.QueryAsync<BigInteger>(contractAddress, balanceOfFunctionMessage);

                double balanceconvert = (double)balance * 0.00000001;
                return balanceconvert;
            }

            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
                return 0;
            }

        }

        public async Task<string> GetBalanceAsync(string address, string cointype)
        {
            try
            {
                var url = URLConfiguration.Wb3URLExchangeMain;
                Web3 web3 = new Web3(url);
                string contractAddress = "";

                if (cointype == "SELS")
                {
                    contractAddress = address;
                }
                else if (cointype == "BELS")
                {
                    contractAddress = address;
                }
                else if (cointype == "TST")//test er jonno
                {
                    contractAddress = URLConfiguration.TSTContractAddress;
                }

                var balanceOfFunctionMessage = new BalanceOfFunction()
                {
                    Owner = address
                };

                IContractQueryHandler<BalanceOfFunction> balanceHandler = web3.Eth.GetContractQueryHandler<BalanceOfFunction>();
                BigInteger balance = await balanceHandler.QueryAsync<BigInteger>(contractAddress, balanceOfFunctionMessage);

                double balanceconvert = (double)balance * 0.00000001;
                string balancemain = balanceconvert.ToString();
                return balancemain;
            }

            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
                return "";
            }

        }

        public StoredWallet GetLocalWalletDetails(string walletname)
        {
            StoredWallet wallet = new StoredWallet();
            string AppDataPath;
            try
            {
                //string walletCurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;
                if (URLConfiguration.Chain == "-mainchain")
                {
                    AppDataPath = URLConfiguration.MainChainSavePath;
                }
                else
                {
                    AppDataPath = URLConfiguration.SideChainSavePath;
                }
                AppDataPath = Environment.ExpandEnvironmentVariables(AppDataPath);
                string path = AppDataPath + @"\SelsBelsAddress.json";

                using (StreamReader r = new StreamReader(path))
                {
                    string json = r.ReadToEnd();
                    List<StoredWallet> wallets = new List<StoredWallet>();

                    if (json != "")
                    {
                        wallets = JsonConvert.DeserializeObject<List<StoredWallet>>(json);
                        wallet = wallets.Where(c => c.Walletname == walletname).FirstOrDefault();
                    }

                    return wallet;
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
                return wallet;
            }
            
        }

        public StoredWallet GetLocalWalletDetailsByWalletAndCoin(string walletname, string coin)
        {
            StoredWallet wallet = new StoredWallet();
            string AppDataPath;
            try
            {
                //string walletCurrentDirectory = AppDomain.CurrentDomain.BaseDirectory;

                if (URLConfiguration.Chain == "-mainchain")
                {
                    AppDataPath = URLConfiguration.MainChainSavePath;
                }
                else
                {
                    AppDataPath = URLConfiguration.SideChainSavePath;
                }
                AppDataPath = Environment.ExpandEnvironmentVariables(AppDataPath);
                string path = AppDataPath + @"\SelsBelsAddress.json";

                using (StreamReader r = new StreamReader(path))
                {
                    string json = r.ReadToEnd();
                    List<StoredWallet> wallets = new List<StoredWallet>();

                    if (json != "")
                    {
                        wallets = JsonConvert.DeserializeObject<List<StoredWallet>>(json);
                        wallet = wallets.Where(c => c.Walletname == walletname && c.Coin == coin).FirstOrDefault();
                    }

                    return wallet;
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
                return wallet;
            }            
        }
    }
}
