using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Newtonsoft.Json;
using XelsCCDesktopWalletApp.Common;
using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Models.SmartContractModels;
using XelsCCDesktopWalletApp.Views.layout;
using XelsCCDesktopWalletApp.Views.Pages.Modals;

namespace XelsCCDesktopWalletApp.Views.Pages.SendPages
{
    /// <summary>
    /// Interaction logic for SidechainPage.xaml
    /// </summary>
    public partial class SidechainPage : Page
    {
        private readonly string baseURL = URLConfiguration.BaseURL; // "http://localhost:37221/api";
         
        private TransactionSending TransactionSending = new TransactionSending();
        private TransactionBuildingSidechain TransactionBuilding = new TransactionBuildingSidechain();

        private BuildTransaction BuildTransaction = new BuildTransaction();
        private WalletBalance WalletBalance = new WalletBalance();
 
        private string cointype; 

        private double estimatedSidechainFee = 0;
        private bool isSending = false;
        private double opReturnAmount = 1;

        private string walletName = GlobalPropertyModel.WalletName;
         

        public SidechainPage()
        {
            InitializeComponent();
            this.DataContext = this;
 
            LoadCreateAsync();
            this.coin.Text = GlobalPropertyModel.CoinUnit;
        }

        public bool isAddrAmtValid()
        {

            if (this.SidechainDestinationAddressText.Text == string.Empty)
            {
                //MessageBox.Show("An address is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("An address is required."));
                this.SidechainDestinationAddressText.Focus();
                return false;
            }

            if (this.SidechainDestinationAddressText.Text.Length < 26)
            {
                //MessageBox.Show("An address is at least 26 characters long.");
                this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("An address is at least 26 characters long."));
                this.SidechainDestinationAddressText.Focus();
                return false;
            }

            return true;
        }

        public bool isValid()
        {
            if (isAddrAmtValid())
            {

                if (this.TransactionFeeText.Text == "")
                {
                    //MessageBox.Show("Transaction Fee is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Transaction Fee is required."));
                    this.TransactionFeeText.Focus();
                    return false;
                }

                if (this.password.Password == "")
                {
                    //MessageBox.Show("Your password is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Your password is required."));
                    this.password.Focus();
                    return false;
                }
            }

            return true;
        }

        public async void LoadCreateAsync()
        {
            await GetWalletBalanceAsync(this.baseURL);

        }

        private void backButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            // this.Close();
        }
         
        private void sendButton_Click(object sender, RoutedEventArgs e)
        {
            this.isSending = true;
            BuildTransactionSideChainAsync();
        }

        private async Task GetWalletBalanceAsync(string path)
        {
            try
            {
                string getUrl = path + $"/wallet/balance?WalletName={this.walletName}&AccountName=account 0";
                var content = "";

                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);

                content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var balances = JsonConvert.DeserializeObject<WalletBalanceArray>(content);

                    foreach (var balance in balances.Balances)
                    {
                        this.WalletBalance = balance;
                        this.AvailableBalanceText.Content = $"{(balance.AmountConfirmed / 100000000).ToString("0.##############")} {GlobalPropertyModel.CoinUnit}";
                    }

                }
            }
            catch (Exception ess)
            {

                GlobalExceptionHandler.SendErrorToText(ess);
            }   
        }
 
        private List<RecipientSidechain> GetRecipient()
        {

            List<RecipientSidechain> list = new List<RecipientSidechain>() {

               new RecipientSidechain{ DestinationAddress = this.MainchainFederationAddressText.Text.Trim(),
               Amount = this.SendAmountText.Text}
            };
            return list;
        }


        private async void EstimateFeeSideChainAsync()
        {
            var content = "";
            try
            {
                if (isAddrAmtValid())
                {
                    //this.TransactionFeeTypeLabel.Content = "medium";
                    string feeType = "medium";
                    string postUrl = this.baseURL + $"/wallet/estimate-txfee";

                    // problem while send for sidechain, sidechain destination address is returned invalid from api
                    // found dissimilarity with angular app, declare on model fedartion address as recipent list, but send destination address as recipient list, 

                    FeeEstimationSideChain feeEstimation = new FeeEstimationSideChain();
                    feeEstimation.WalletName = this.walletName;
                    feeEstimation.AccountName = "account 0";
                    feeEstimation.Recipients = GetRecipient();
                    feeEstimation.FeeType = feeType;
                    feeEstimation.AllowUnconfirmed = true;

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(feeEstimation), Encoding.UTF8, "application/json"));

                    content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        this.estimatedSidechainFee = Convert.ToDouble(content) / 100000000;
                        this.TransactionFeeText.Text = this.estimatedSidechainFee.ToString();
                        this.WarningLabelSidechain.Visibility = Visibility.Hidden;
                    }
                    else
                    {
                        //MessageBox.Show("Invalid Address.");
                        this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Invalid Address."));
                    }
                }
            }
            catch (Exception ed)
            {
                GlobalExceptionHandler.SendErrorToText(ed);
            }

        }

        private bool validationCheck()
        {
            
            string amt = this.SendAmountText.Text.Trim();
            if (amt != "")
            {
                var rex = Regex.IsMatch(amt, "[^0-9]+");
                if (rex)
                {
                    //MessageBox.Show("Data is not valid");
                    this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Data is not valid"));
                    this.SendAmountText.Focus();
                    return false;
                }
            }
            if (this.SendAmountText.Text.Trim() == string.Empty)
            {
                //MessageBox.Show("Amount is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Amount is required!"));
                this.SendAmountText.Focus();
                return false;
            }
            if (this.SidechainDestinationAddressText.Text.ToString().Trim() == "")
            {
                //MessageBox.Show("SideChain Address is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("SideChain Address is required!"));
                this.SidechainDestinationAddressText.Focus();
                return false;
            }
            if (this.MainchainFederationAddressText.Text.ToString().Trim() == "")
            {
                //MessageBox.Show("MainChain Federation Address is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("MainChain Federation Address is required!"));
                this.MainchainFederationAddressText.Focus();
                return false;
            }
            if (this.password.Password.ToString().Trim() == "")
            {
                //MessageBox.Show("Password is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Password is required!"));
                this.password.Focus();
                return false;
            }
            return true;
        }
        private async void BuildTransactionSideChainAsync()
        {
            try
            {
                if (validationCheck())
                {
                    double t = this.opReturnAmount / 100000000;
                    string postUrl = this.baseURL + $"/wallet/build-transaction";
                    var content = "";

                    this.TransactionBuilding.WalletName = this.walletName;
                    this.TransactionBuilding.AccountName = "account 0";
                    this.TransactionBuilding.Password = this.password.Password;
                    this.TransactionBuilding.Recipients = GetRecipient();
                    this.TransactionBuilding.FeeAmount = this.estimatedSidechainFee;
                    this.TransactionBuilding.AllowUnconfirmed = true;
                    this.TransactionBuilding.ShuffleOutputs = false;
                    this.TransactionBuilding.OpReturnData = this.SidechainDestinationAddressText.Text.Trim();
                    this.TransactionBuilding.OpReturnAmount = (t).ToString("0." + new string('#', 10));

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(this.TransactionBuilding), Encoding.UTF8, "application/json"));

                    content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {

                        this.BuildTransaction = JsonConvert.DeserializeObject<BuildTransaction>(content);

                        this.estimatedSidechainFee = this.BuildTransaction.Fee;
                        this.TransactionSending.Hex = this.BuildTransaction.Hex;

                        if (this.isSending)
                        {
                            _ = SendTransactionAsync(this.TransactionSending);
                        }
                    }
                    else
                    {
                        this.isSending = false;
                        var errors = JsonConvert.DeserializeObject<ErrorModel>(content);

                        foreach (var error in errors.Errors)
                        {
                            MessageBox.Show(error.Message);
                            break;
                        }
                    }
                }
            }
            catch (Exception d)
            {
                GlobalExceptionHandler.SendErrorToText(d);
            }

            
        }

        private async Task SendTransactionAsync(TransactionSending tranSending)
        {
            try
            {
                if (isValid())
                {
                    string postUrl = this.baseURL + $"/wallet/send-transaction";

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(tranSending), Encoding.UTF8, "application/json"));

                    var content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {

                        SendConfirmationSC sendConfirmationSc = new SendConfirmationSC();
                        sendConfirmationSc.Transaction = this.TransactionBuilding;
                        sendConfirmationSc.TransactionFee = this.estimatedSidechainFee;
                        sendConfirmationSc.OpReturnAmount = this.opReturnAmount;
                        sendConfirmationSc.cointype = this.cointype;
                        sendConfirmationSc.FedarationAddress = this.TransactionBuilding.OpReturnData;

                        this.NavigationService.Navigate(new SendConfirmationSideChain(sendConfirmationSc, this.walletName));

                    }
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }

        }

        private void CheckSendAmount_OnChange(object sender, RoutedEventArgs e)
        {
            string sendingAmount = this.SendAmountText.Text.Trim();
            //if (this.SendAmountText.Text == string.Empty)
            //{
            //    MessageBox.Show("An amount is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);

            //}
            if (!Regex.IsMatch(this.SendAmountText.Text.Trim(), @"^([0-9]+)?(\.[0-9]{0,8})?$") || this.SendAmountText.Text.Trim() == string.Empty)
            {
                //MessageBox.Show("Enter a valid transaction amount. Only positive numbers and no more than 8 doubles are allowed.");
                this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("Enter a valid transaction amount. Only positive numbers and no more than 8 doubles are allowed."));
            }
            else
            {
               
                if (Convert.ToDouble(sendingAmount) < 0.00001)
                {
                    //MessageBox.Show("The amount has to be more or equal to 1.");
                    this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("The amount has to be more or equal to 1."));
                }

                if (Convert.ToDouble(sendingAmount) > ((this.WalletBalance.AmountConfirmed - this.estimatedSidechainFee) / 100000000))
                {
                    //MessageBox.Show("The total transaction amount exceeds your spendable balance.");
                    this.Sidechain_Send_Page.Children.Add(new DisplayErrorMessageUserControl("The total transaction amount exceeds your spendable balance."));
                }

               
                if (this.SidechainDestinationAddressText.Text != "" && this.SendAmountText.Text != "")
                {
                    EstimateFeeSideChainAsync();
                    this.SidechainDestinationAddressText.Focus();
                }

                //this.SendAmountText.Text = "";
                this.SidechainDestinationAddressText.Text = "";
                this.SendAmountText.Focus();
            }
        }

        private void CalculateTransactionFee_OnChange(object sender, RoutedEventArgs e)
        {
            if (this.SidechainDestinationAddressText.Text != "" && this.SendAmountText.Text != "")
            {
                EstimateFeeSideChainAsync();
                this.SidechainDestinationAddressText.Focus();
            }
        }
    }
}
