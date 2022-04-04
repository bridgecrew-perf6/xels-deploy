using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Numerics;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MaterialDesignThemes.Wpf;
using Nethereum.RPC.Eth.DTOs;

using Newtonsoft.Json;

using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;
using XelsXLCDesktopWalletApp.Views.Dialogs.DialogsModel;
using XelsXLCDesktopWalletApp.Views.Pages.Modals;

namespace XelsXLCDesktopWalletApp.Views.Pages
{
    /// <summary>
    /// Interaction logic for ExchangePage.xaml
    /// </summary>
    public partial class ExchangePage : Page
    {


        #region Base
        private string baseURL = URLConfiguration.BaseURL;
        private string baseURLExchange = URLConfiguration.BaseURLExchange;
        #endregion
        #region Wallet Info
        private readonly WalletInfo walletInfo = new WalletInfo();

        private string walletName;
        public string WalletName
        {
            get
            {
                return this.walletName;
            }
            set
            {
                this.walletName = value;
            }
        }
        #endregion

        string addr;
        bool updatesuccess = false;
        private readonly StoredWallet mywallet = new StoredWallet();
        private CreateWallet createWallet = new CreateWallet();
        private TransactionWallet transactionWalletObj = new TransactionWallet();

        #region Exchanges
        public List<ExchangeData> exchangelist = new List<ExchangeData>();

        public List<ExchangeData> Exchangelist
        {
            get
            {
                return this.exchangelist;
            }
            set
            {
                this.exchangelist = value;
            }
        }
        #endregion
        #region Coin Select

        private ExchangeCoin selectedcoin = new ExchangeCoin();
        public ExchangeCoin SelectedCoin
        {
            get
            {
                return this.selectedcoin;
            }
            set
            {
                this.selectedcoin = value;
            }
        }

        private List<ExchangeCoin> coins = new List<ExchangeCoin>() {
                new ExchangeCoin(){ Name="SELS"},
                new ExchangeCoin(){ Name="BELS"}
            };

        public List<ExchangeCoin> Coins
        {
            get
            {
                return this.coins;
            }
            set
            {
                this.coins = value;
            }
        }

        #endregion
        public ExchangePage()
        {
            InitializeComponent();
        }
        public ExchangePage(string walletname)
        {
            InitializeComponent();
            this.DataContext = this;

            this.walletName = GlobalPropertyModel.WalletName.ToString();
            this.walletInfo.WalletName = GlobalPropertyModel.WalletName.ToString();
            try
            {
                this.mywallet = this.createWallet.GetLocalWalletDetails(GlobalPropertyModel.WalletName.ToString());
                Task.Run(async () => await LoadCreateAsync());
                Task.Run(async () => await UpdateExchangeListAsync());
                this.updatesuccess = false;
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }

        public bool isValid()
        {
            if (this.Combobox.SelectedItem == null)
            {
                //MessageBox.Show("Deposit From is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Exchange.Children.Add(new DisplayErrorMessageUserControl("Deposit From is required!"));
                this.Combobox.Focus();
                return false;
            }

            if (this.AmountTxt.Text == string.Empty)
            {
                //MessageBox.Show("Deposit Amount is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Exchange.Children.Add(new DisplayErrorMessageUserControl("Deposit Amount is required!"));
                this.AmountTxt.Focus();
                return false;
            }

            return true;
        }


        #region Api Requests
        public async Task<List<ExchangeResponse>> GetOrdersAsync(string hash)
        {
            try
            {
                string postUrl = this.baseURLExchange + "/api/getOrders";
                var content = "";
                //this for Ssl Connection error.
                HttpClientHandler clientHandler = new HttpClientHandler();
                clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => { return true; };
                HttpClient client = new HttpClient(clientHandler);
                //End
               // HttpClient client = new HttpClient();

                List<ExchangeResponse> exchangedata = new List<ExchangeResponse>();
                PostHash code = new PostHash();
                code.user_code = hash;

                client.DefaultRequestHeaders.Add("Authorization", "1234567890");


                HttpResponseMessage response = await client.PostAsJsonAsync(postUrl, code).ConfigureAwait(false);
                content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    exchangedata = JsonConvert.DeserializeObject<List<ExchangeResponse>>(content);
                }
                return exchangedata;

            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
            return null;
        }
        //this is for new oreder submit button function
        public async Task<Tuple<TransactionReceipt, string>> NewOrderAsync(ExchangeOrder order)
        {
            var returnMessage = new Tuple<TransactionReceipt, string>(null, null);
            double balance = await this.createWallet.GetBalanceMainAsync(this.mywallet.Address, this.mywallet.Coin);
            BigInteger bgBalance = (BigInteger)balance;
            if (bgBalance > 0 && bgBalance != 0)
            {
                try
                {
                    string postUrl = URLConfiguration.BaseURLExchange + "/api/new-order";

                    var content = "";
                    ExchangeResponse exchangedata = new ExchangeResponse();
                    HttpClient client = URLConfiguration.Client;
                    client.DefaultRequestHeaders.Add("Authorization", "1234567890");

                    HttpResponseMessage response = await client.PostAsJsonAsync(postUrl, order);
                    content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        exchangedata = JsonConvert.DeserializeObject<ExchangeResponse>(content);

                        returnMessage = await DepositAsync(order.deposit_symbol, order.deposit_amount, exchangedata);
                        return returnMessage;
                    }
                    else
                    {
                        string errmes = "Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase;
                        returnMessage = new Tuple<TransactionReceipt, string>(null, errmes);
                        return returnMessage;
                    }
                
                 }
            catch (Exception e)
            {
                    GlobalExceptionHandler.SendErrorToText(e);
                    returnMessage = new Tuple<TransactionReceipt, string>(null, e.Message.ToString());
                    return returnMessage;
            }
            }
            else
            {
                string msg = "You have Insufficient Balance for this address.";
                returnMessage = new Tuple<TransactionReceipt, string>(null, msg);
                return returnMessage;
            }

        }
        //end
        #endregion

        public async Task UpdateExchangeListAsync()
        {
            List<ExchangeResponse> exchangedatalist = new List<ExchangeResponse>();
            try
            {
                this.exchangelist.Clear();
                if (this.mywallet != null)
                {
                    if (this.mywallet.Wallethash != "")
                    {
                        try
                        {
                            exchangedatalist = await GetOrdersAsync(this.mywallet.Wallethash);
                            this.Dispatcher.Invoke(() =>
                            {

                                if (exchangedatalist != null && exchangedatalist.Count > 0)
                                {
                                    this.ExchangesList.ItemsSource = null;
                                    this.NoData.Visibility = Visibility.Hidden;
                                    this.ListData.Visibility = Visibility.Visible;
                                    // Processed data

                                    foreach (ExchangeResponse data in exchangedatalist)
                                    {

                                        ExchangeData edata = new ExchangeData();
                                        edata.excid = data.id;
                                        edata.deposit_address_amount_symbol = $"{data.deposit_address} {data.deposit_amount} {data.deposit_symbol}";
                                        edata.xels_address_amount = $"{data.xels_address} {data.xels_amount} XELS";

                                        if (data.status == 0)
                                        {
                                            edata.showstatus = "Wating for deposit";
                                            edata.IsDepositEnableBtn = true;
                                        }
                                        else if (data.status == 1)
                                        {
                                            edata.showstatus = "Pending Exchange";
                                            edata.IsDepositEnableBtn = false;
                                        }
                                        else if (data.status == 2)
                                        {
                                            edata.showstatus = "Complete";
                                            edata.IsDepositEnableBtn = false;
                                        }

                                        this.exchangelist.Add(edata);
                                    }

                                    this.ExchangesList.ItemsSource = this.exchangelist;
                                }
                                else
                                {
                                    this.ListData.Visibility = Visibility.Hidden;
                                    this.NoData.Visibility = Visibility.Visible;
                                }
                                this.updatesuccess = true;
                            });
                        }
                        catch (Exception e)
                        {
                            GlobalExceptionHandler.SendErrorToText(e);
                        }

                    }

                }
                else
                {
                    //MessageBox.Show("Wallet Data not Found.");

                    var dialogMessage = ErrorDialogMessage.GetInstance();
                    dialogMessage.Message = "Wallet Data not Found.";
                    await DialogHost.Show(dialogMessage, "ExchangePage");
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
                
        }

        public async Task LoadCreateAsync()
        {
            this.addr = await GetUnusedReceiveAddressesAsync(this.baseURL);
            this.addr = this.addr.TrimStart('"').TrimEnd('"');

        }

      
        private async Task<string> GetUnusedReceiveAddressesAsync(string path)
        {
            var content = "";
            try
            {
                string getUrl = path + $"/wallet/unusedaddress?WalletName={this.walletInfo.WalletName}&AccountName=account 0";


                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);

                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStringAsync();
                }
                
                return content;
            }
            catch (Exception ee)
            {

                GlobalExceptionHandler.SendErrorToText(ee);
                return content;
            }
           
        }

        public async Task<Tuple<TransactionReceipt, string>>  DepositAsync(string symbol, double amount, ExchangeResponse exchangeResponse)
        {
            var deporesult = new Tuple<TransactionReceipt, string>(null,null);
            string addre = mywallet.Address;
            string privtKey= mywallet.PrivateKey;
            string  walltName= mywallet.Walletname;
            string coin = mywallet.Coin;
            string wathash = mywallet.Wallethash;

            StoredWallet mWallet = new StoredWallet();
            try
            {
                if (symbol == coin)
                {
                    mWallet.Address = addre;
                    mWallet.Walletname = walltName;
                    mWallet.Coin = coin;
                    mWallet.Wallethash = wathash;
                    mWallet.PrivateKey= Encryption.DecryptPrivateKey(privtKey);
                }
                // Transfer
                 deporesult = await this.transactionWalletObj.TransferAsync(mWallet, exchangeResponse.deposit_address, amount);
                return deporesult;
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
                deporesult = new Tuple<TransactionReceipt, string>(null, e.Message.ToString());
                return deporesult;
            }
        }
     
        //This is for deposit now button function
        public async Task<Tuple<TransactionReceipt, string>> GetOrderAsync(string orderId)
        {
            var deporesult = new Tuple<TransactionReceipt, string>(null, null);
            ExchangeResponse exchangedata = new ExchangeResponse();
            try
            {
                string path = URLConfiguration.BaseURLExchange + "/api/getOrder/" + orderId;
                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(path);
                string content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    exchangedata = JsonConvert.DeserializeObject<ExchangeResponse>(content);

                    if (exchangedata.id == null)
                    {
                        //MessageBox.Show($"Your provided Order Id: {orderId} is not found!");
                        //this.Exchange.Children.Add(new DisplayErrorMessageUserControl($"Your provided Order Id: {orderId} is not found!"));

                        var dialogMessage = ErrorDialogMessage.GetInstance();
                        dialogMessage.Message = $"Your provided Order Id: {orderId} is not found!";
                        await DialogHost.Show(dialogMessage, "ExchangePage");
                    }
                    else
                    {
                        deporesult= await DepositAsync(exchangedata.deposit_symbol, exchangedata.deposit_amount, exchangedata);
                       
                    }

                }
                
                return deporesult;

            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
                deporesult = new Tuple<TransactionReceipt, string>(null, e.Message.ToString());
                return deporesult;
            }
        }
        //end

        private async void DepositButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                DataGrid dataGrid = this.ExchangesList;
                DataGridRow Row = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromIndex(dataGrid.SelectedIndex);
                ExchangeData item = (ExchangeData)Row.DataContext;
                if (item != null)
                {
                    this.IsEnabled = false;
                    var result = await GetOrderAsync(item.excid);
                    if (result.Item1 != null && result.Item2.ToString() == "SUCCESS")
                    {
                        //MessageBox.Show("Token has been sent Successfully.");
                        this.Exchange.Children.Add(new DisplayErrorMessageUserControl("Token has been sent Successfully."));
                        await UpdateExchangeListAsync();
                        this.IsEnabled = true;
                    }
                    else
                    {
                        //MessageBox.Show(result.Item2.ToString());
                        this.Exchange.Children.Add(new DisplayErrorMessageUserControl(result.Item2.ToString()));
                        await UpdateExchangeListAsync();
                        this.IsEnabled = true;
                    }

                }
                else
                {
                    //MessageBox.Show("Value Must be required!.");
                    this.Exchange.Children.Add(new DisplayErrorMessageUserControl("Value Must be required!."));
                }
            }
            catch (Exception df)
            {

                GlobalExceptionHandler.SendErrorToText(df);
            }
           

        }


        private async void ExchangeOrderSubmitButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (isValid())
                {

                    if (this.mywallet == null || this.mywallet.PrivateKey == null)
                    {
                        //MessageBox.Show($"Your ethereum address is not imported properly. Please import your address again");
                        this.Exchange.Children.Add(new DisplayErrorMessageUserControl($"Your ethereum address is not imported properly. Please import your address again"));
                    }
                    else
                    {
                        this.exchangeSubmit.IsEnabled = false;
                        ExchangeOrder exchangeOrder = new ExchangeOrder();
                        exchangeOrder.xels_address = this.addr;
                        exchangeOrder.deposit_amount = Convert.ToDouble(this.AmountTxt.Text);
                        exchangeOrder.deposit_symbol = this.SelectedCoin.Name;
                        exchangeOrder.user_code = this.mywallet.Wallethash;
                        var result = await NewOrderAsync(exchangeOrder);
                        if (result.Item1 != null && result.Item2.ToString() == "SUCCESS")
                        {
                            //MessageBox.Show("Token has been sent Successfully.");
                            this.Exchange.Children.Add(new DisplayErrorMessageUserControl("Token has been sent Successfully."));
                            await UpdateExchangeListAsync();
                            this.exchangeSubmit.IsEnabled = true;
                        }
                        else
                        {
                            //MessageBox.Show(result.Item2.ToString());
                            this.Exchange.Children.Add(new DisplayErrorMessageUserControl(result.Item2.ToString()));
                            await UpdateExchangeListAsync();
                            this.exchangeSubmit.IsEnabled = true;
                        }

                    }
                }
            }
            catch (Exception es)
            {

                GlobalExceptionHandler.SendErrorToText(es);
            }

        }

        private void AmountTxt_KeyUp(object sender, KeyEventArgs e)
        {
            try
            {
                this.MessageTxt.Text = "";
                string amt = this.AmountTxt.Text;
                if (amt != "")
                {
                    e.Handled = Regex.IsMatch(amt, "[^0-9]+");
                    if (!e.Handled)
                    {
                        double val = Convert.ToDouble(amt);
                        string res = string.Format("{0:0.00}", val * 0.1);
                        this.MessageTxt.Text = "You will get " + res + " XELS";
                    }
                    else
                    {
                        //MessageBox.Show("Data is not valid");
                        //this.Exchange.Children.Add(new DisplayErrorMessageUserControl("Data is not valid"));
                        this.AmountTxt.Text = "";
                        var dialogMessage = ErrorDialogMessage.GetInstance();
                        dialogMessage.Message = "Data is not valid";
                        _ = DialogHost.Show(dialogMessage, "ExchangePage");
                    }
                }
            }
            catch (Exception er)
            {
                GlobalExceptionHandler.SendErrorToText(er);
            }
        }
    }
}
