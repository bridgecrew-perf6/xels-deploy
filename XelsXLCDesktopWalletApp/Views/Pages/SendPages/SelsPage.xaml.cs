using System.Windows.Controls;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

using Newtonsoft.Json;

using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Views.layout;
using System.Text.RegularExpressions;
using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;
using System;
using Nethereum.RPC.Eth.DTOs;
using XelsXLCDesktopWalletApp.Views.Pages.Modals;

namespace XelsXLCDesktopWalletApp.Views.Pages.SendPages
{
    /// <summary>
    /// Interaction logic for SelsPage.xaml
    /// </summary>
    public partial class SelsPage : Page
    { 
        private readonly string baseURL = URLConfiguration.BaseURL; // "http://localhost:37221/api";

        private readonly WalletInfo walletInfo = new WalletInfo();
        private TransactionSending transactionSending = new TransactionSending();
        private CreateWallet createWallet = new CreateWallet();
        private TransactionWallet transactionWallet = new TransactionWallet();

        public SelsPage()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        public bool isValid()
        {
            if (this.textToAddress.Text.ToString().Trim() =="")
            {
                //MessageBox.Show("Address To is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.SelsPage_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Address To is required!"));
                this.textToAddress.Focus();
                return false;
            }
            string amt = this.textAmount.Text.Trim();
            if (amt != "")
            {
                var rex = Regex.IsMatch(amt, "[^0-9]+");
                if (rex)
                {
                    //MessageBox.Show("Data is not valid");
                    this.SelsPage_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Data is not valid"));
                    this.textAmount.Focus();
                    return false;
                }
            }
            if (this.textAmount.Text.Trim() == string.Empty)
            {
                //MessageBox.Show("Amount is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.SelsPage_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Amount is required!"));
                this.textAmount.Focus();
                return false;
            }

            return true;
        }
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            _ = SendTransactionAsync();
        }

        private async Task SendTransactionAsync()
        {
            //wallet Password dibe ki na..pore jane nite hobe???
            if (isValid())
            {
                var sendResult = new Tuple<TransactionReceipt, string>(null, null);
                string coinType = this.selsHidden.Text.ToString();
                StoredWallet localWallateData = this.createWallet.GetLocalWalletDetailsByWalletAndCoin(GlobalPropertyModel.WalletName.ToString(), coinType);
                if (localWallateData.Address != null)
                {
                    StoredWallet mWallet = new StoredWallet();

                    string toAddress = this.textToAddress.Text.ToString().Trim();
                    double amount =Convert.ToDouble(this.textAmount.Text);

                    mWallet.Address = localWallateData.Address;
                    mWallet.Walletname = localWallateData.Walletname;
                    mWallet.Coin = localWallateData.Coin;
                    mWallet.Wallethash = localWallateData.Wallethash;
                    mWallet.PrivateKey = Encryption.DecryptPrivateKey(localWallateData.PrivateKey);
                    sendResult = await this.transactionWallet.TransferAsync(mWallet, toAddress, amount);
                    if (sendResult.Item2 == "SUCCESS")
                    {
                        string tranID = sendResult.Item1.TransactionHash.ToString();
                        string message = this.textAmount.Text + " Token successfully send to " + toAddress + "and Transaction Id: "+ tranID;
                        //MessageBox.Show(message, "SUCCESS", MessageBoxButton.OK);
                        this.SelsPage_Send_Page.Children.Add(new DisplayMessageUserControl(message));
                    }
                    else
                    {
                        //MessageBox.Show(sendResult.Item2, "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                        this.SelsPage_Send_Page.Children.Add(new DisplayErrorMessageUserControl(sendResult.Item2));
                    }
                   

                }
                else
                {
                    //MessageBox.Show("You have not imported yet!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.SelsPage_Send_Page.Children.Add(new DisplayErrorMessageUserControl("You have not imported yet!"));
                }
            }
            
        }

    }
}
