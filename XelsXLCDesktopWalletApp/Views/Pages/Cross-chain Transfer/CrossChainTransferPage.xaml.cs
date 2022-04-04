using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using Newtonsoft.Json;

using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;
using XelsXLCDesktopWalletApp.Views.Pages.Modals;

namespace XelsXLCDesktopWalletApp.Views.Pages.Cross_chain_Transfer
{
    /// <summary>
    /// Interaction logic for CrossChainTransferPage.xaml
    /// </summary>
    public partial class CrossChainTransferPage : Page
    {
        public string walletName = GlobalPropertyModel.WalletName;
        public CrossChainTransferPage()
        {
            InitializeComponent();
            _ = GetMaxBalanceAsync();
        }

        private readonly string baseURL = URLConfiguration.BaseURL;

        private TransactionSending TransactionSending = new TransactionSending();
        private TransactionBuilding TransactionBuilding = new TransactionBuilding();
        //private PchTransactionBuilding TransactionBuilding = new PchTransactionBuilding();
        private WalletBalance WalletBalance = new WalletBalance();

        private BuildTransaction BuildTransaction = new BuildTransaction();
        private BuildTransaction BuildTransactionForPCH = new BuildTransaction();

        public bool IsAddressAndAmountValid()
        {
            if (this.DestinationAddressText.Text == string.Empty)
            {
                //MessageBox.Show("An address is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Cross_Chain_Trans.Children.Add(new DisplayErrorMessageUserControl("An address is required."));
                this.DestinationAddressText.Focus();
                return false;
            }

            if (this.DestinationAddressText.Text.Length < 26)
            {
                //MessageBox.Show("An address is at least 26 characters long.");
                this.Cross_Chain_Trans.Children.Add(new DisplayErrorMessageUserControl("An address is at least 26 characters long."));
                this.DestinationAddressText.Focus();
                return false;
            }

            if (this.SendAmountText.Text == string.Empty)
            {
                //MessageBox.Show("An amount is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Cross_Chain_Trans.Children.Add(new DisplayErrorMessageUserControl("An amount is required."));
                this.SendAmountText.Focus();
                return false;
            }

            if (this.SendAmountText.Text.Length < 0.00001)
            {
                //MessageBox.Show("The amount has to be more or equal to 1.");
                this.Cross_Chain_Trans.Children.Add(new DisplayErrorMessageUserControl("The amount has to be more or equal to 1."));
                this.SendAmountText.Focus();
                return false;
            }

            return true;
        }

        public bool isValid()
        {
            if (IsAddressAndAmountValid())
            {

                if (this.TransactionFeeText.Text == "")
                {
                    //MessageBox.Show("Transaction Fee is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    this.Cross_Chain_Trans.Children.Add(new DisplayErrorMessageUserControl("Transaction Fee is required."));
                    this.TransactionFeeText.Focus();
                    return false;
                }

                //if (this.password.Password == "")
                //{
                //    MessageBox.Show("Your password is required.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                //    this.password.Focus();
                //    return false;
                //}
            }

            return true;
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
                    this.Cross_Chain_Trans.Children.Add(new DisplayErrorMessageUserControl("Data is not valid."));
                    this.SendAmountText.Focus();
                    return false;
                }
            }
            if (this.SendAmountText.Text.Trim() == string.Empty)
            {
                //MessageBox.Show("Amount is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Cross_Chain_Trans.Children.Add(new DisplayErrorMessageUserControl("Amount is required!"));
                this.SendAmountText.Focus();
                return false;
            }
            if (this.DestinationAddressText.Text.ToString().Trim() == "")
            {
                //MessageBox.Show(" Address is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Cross_Chain_Trans.Children.Add(new DisplayMessageUserControl("Address is required!"));
                this.DestinationAddressText.Focus();
                return false;
            }

            //if (this.password.Password.ToString().Trim() == "")
            //{
            //    MessageBox.Show("Password is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            //    this.password.Focus();
            //    return false;
            //}
            return true;
        }

        private List<Recipient> GetRecipient()
        {
            //if (this.DestinationAddressText.Text.Trim().StartsWith("X"))
            //{
            //    List<Recipient> recipientList = new List<Recipient>() {
            //        new Recipient
            //        {
            //         DestinationAddress=this.DestinationAddressText.Text.Trim(),
            //         Amount = this.SendAmountText.Text
            //        }
            //    };

            //    return recipientList;
            //}
            List<Recipient> list = new List<Recipient>() {

               new Recipient{ DestinationAddress = "XFYeZjeneU4VamUEZ31hknMxsVDmRbg5Mh", //static address
               //this.DestinationAddressText.Text.Trim(),
               Amount = this.SendAmountText.Text}
            };

            return list;
        }

        private List<Recipient> GetRecipientForPch()
        {
            List<Recipient> list = new List<Recipient>() {

               new Recipient{ DestinationAddress = this.DestinationAddressText.Text.Trim(), //static address
               //this.DestinationAddressText.Text.Trim(),
               Amount = this.SendAmountText.Text}
            };

            return list;
        }

        private List<Recipient> GetRecipientbyTextboxData()
        {
            List<Recipient> list = new List<Recipient>() {

               new Recipient{ DestinationAddress = this.DestinationAddressText.Text.Trim(),
               Amount = this.SendAmountText.Text}
            };
            return list;
        }

        #region api call 

        double estimatedFee;
        private bool isSending = false;
        private string cointype;
        private bool successTransaction = false;

        private async void EstimateFeeAsync()
        {

            try
            {
                if (IsAddressAndAmountValid())
                {
                    //this.TransactionFeeTypeLabel.Content = "medium";               
                    string feeType = "medium";
                    string postUrl = this.baseURL + $"/wallet/estimate-txfee";
                    var content = "";

                    FeeEstimation feeEstimation = new FeeEstimation();
                    feeEstimation.WalletName = this.walletName;
                    feeEstimation.AccountName = "account 0";
                    feeEstimation.Recipients = GetRecipient();
                    feeEstimation.FeeType = feeType;
                    feeEstimation.AllowUnconfirmed = true;

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(feeEstimation), Encoding.UTF8, "application/json"));

                    content = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        this.estimatedFee = (Convert.ToDouble(content) / 100000000);
                        this.TransactionFeeText.Text = (Convert.ToDouble(content) / 100000000).ToString();
                        this.TransactionWarningLabel.Visibility = Visibility.Hidden;
                    }
                    else
                    {

                        MessageBox.Show("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase);
                        this.DestinationAddressText.Text = "";
                    }
                }
            }
            catch (Exception c)
            {
                //GlobalExceptionHandler.SendErrorToText(c);
            }
        }

        private async Task GetMaxBalanceAsync()
        {
            var content = "";
            try
            {
                MaximumBalance maximumBalance = new MaximumBalance();
                maximumBalance.WalletName = this.walletName;
                // maximumBalance.AccountName = "account 0";
                maximumBalance.FeeType = "medium";
                maximumBalance.AllowUnconfirmed = true;

                string postUrl = this.baseURL +
                    $"/wallet/maxbalance?WalletName={maximumBalance.WalletName}&FeeType={maximumBalance.FeeType}&AllowUnconfirmed={maximumBalance.AllowUnconfirmed} &AccountName=account 0";
                //&accountName={maximumBalance.AccountName}
                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(postUrl);

                content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    var balance = JsonConvert.DeserializeObject<WalletBalance>(content);

                    //foreach (var balance in balances.Balances)
                    //{
                    this.WalletBalance = balance;

                    this.textAvailableCoin.Content = $"{(balance.MaxSpendableAmount / 100000000).ToString("0.##############")} {GlobalPropertyModel.CoinUnit}";

                    //}
                }
            }
            catch (Exception f)
            {
                GlobalExceptionHandler.SendErrorToText(f);
            }
        }

        private async Task BuildTransactionAsync()
        {
            //Recipient[] recipients = GetRecipient();
            try
            {
                if (validationCheck())
                {
                    string postUrl = this.baseURL + $"/wallet/build-transaction";
                    var content = "";

                    this.TransactionBuilding.WalletName = this.walletName;
                    this.TransactionBuilding.AccountName = "account 0";
                    this.TransactionBuilding.Password = this.password.Password;
                    this.TransactionBuilding.Recipients = GetRecipient();
                    this.TransactionBuilding.FeeAmount = this.estimatedFee;
                    this.TransactionBuilding.AllowUnconfirmed = true;
                    this.TransactionBuilding.ShuffleOutputs = false;


                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(this.TransactionBuilding), Encoding.UTF8, "application/json"));

                    content = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        this.BuildTransaction = JsonConvert.DeserializeObject<BuildTransaction>(content);

                        this.estimatedFee = this.BuildTransaction.Fee;
                        this.TransactionSending.Hex = this.BuildTransaction.Hex;

                        if (this.isSending)
                        {
                            _ = SendTransactionAsync(this.TransactionSending);
                            this.successTransaction = true;
                        }
                    }
                    else
                    {
                        this.isSending = false;

                        try
                        {
                            var errors = JsonConvert.DeserializeObject<ErrorModel>(content);

                            foreach (var error in errors.Errors)
                            {
                                MessageBox.Show(error.Message);
                                break;
                                //MessageBox.Show("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase);
                            }
                        }
                        catch (Exception e)
                        {
                            GlobalExceptionHandler.SendErrorToText(e);
                        }
                    }
                }
            }
            
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }

        //private async Task CarbonCreditBuildTransactionAsync()
        //{
        //    //Recipient[] recipients = GetRecipient();
        //    try
        //    {
        //        if (validationCheck())
        //        {
        //            string postUrl = this.baseURL + $"/wallet/build-transaction"; // base url need to change 
        //            var content = "";

        //            this.TransactionBuilding.WalletName = this.walletName;
        //            this.TransactionBuilding.AccountName = "account 0";
        //            this.TransactionBuilding.Password = this.password.Password;
        //            this.TransactionBuilding.Recipients = GetRecipientbyTextboxData();
        //            this.TransactionBuilding.FeeAmount = this.estimatedFee;
        //            this.TransactionBuilding.AllowUnconfirmed = true;
        //            this.TransactionBuilding.ShuffleOutputs = false;

        //            HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(this.TransactionBuilding), Encoding.UTF8, "application/json"));

        //            content = await response.Content.ReadAsStringAsync();
        //            if (response.IsSuccessStatusCode)
        //            {
        //                this.BuildTransaction = JsonConvert.DeserializeObject<BuildTransaction>(content);

        //                this.estimatedFee = this.BuildTransaction.Fee;
        //                this.TransactionSending.Hex = this.BuildTransaction.Hex;

        //                if (this.isSending)
        //                {
        //                    _ = SendTransactionAsync(this.TransactionSending);
        //                }
        //            }
        //            else
        //            {
        //                this.isSending = false;

        //                try
        //                {
        //                    var errors = JsonConvert.DeserializeObject<ErrorModel>(content);

        //                    foreach (var error in errors.Errors)
        //                    {
        //                        //MessageBox.Show(error.Message);
        //                        this.Cross_Chain.Children.Add(new DisplayErrorMessageUserControl(error.Message));
        //                        break;
        //                        //MessageBox.Show("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase);
        //                    }
        //                }
        //                catch (Exception e)
        //                {
        //                    GlobalExceptionHandler.SendErrorToText(e);
        //                }
        //            }
        //        }
        //    }
        //    catch (Exception e)
        //    {
        //        GlobalExceptionHandler.SendErrorToText(e);
        //    }
        //}

        private async Task PchBuildTransactionAsync()
        {
            //Recipient[] recipients = GetRecipient();
            try
            {
                if (validationCheck())
                {
                    //string postUrl = "http://54.238.248.117:38221/api" + $"/wallet/build-transaction";
                    string postUrl = "https://api.xels.io:2332/PostAPIResponse/38221/";
                    var content = "";

                    this.TransactionBuilding.URL = "/api/wallet/build-transaction";
                    this.TransactionBuilding.WalletName = "pch";
                    this.TransactionBuilding.AccountName = "account 0";
                    this.TransactionBuilding.Password = "Asdf1234!";
                    this.TransactionBuilding.Recipients = GetRecipientForPch();
                    this.TransactionBuilding.FeeAmount = this.estimatedFee;
                    this.TransactionBuilding.AllowUnconfirmed = true;
                    this.TransactionBuilding.ShuffleOutputs = false;

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(this.TransactionBuilding), Encoding.UTF8, "application/json"));

                    content = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        this.BuildTransactionForPCH = JsonConvert.DeserializeObject<BuildTransaction>(content);

                        this.estimatedFee = this.BuildTransactionForPCH.Fee;
                        this.TransactionSending.Hex = this.BuildTransactionForPCH.Hex;

                        if (this.isSending)
                        {
                            _ = SendTransactionForPchAsync(this.TransactionSending);
                        }

                    }
                    else
                    {
                        this.isSending = false;

                        try
                        {
                            var errors = JsonConvert.DeserializeObject<ErrorModel>(content);

                            foreach (var error in errors.Errors)
                            {
                                MessageBox.Show(error.Message);
                                break;
                                //MessageBox.Show("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase);
                            }
                        }
                        catch (Exception e)
                        {
                            GlobalExceptionHandler.SendErrorToText(e);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }

        private async Task SendTransactionAsync(TransactionSending tranSending)
        {
            try
            {
                if (isValid())
                {
                    string postUrl = this.baseURL + $"/wallet/send-transaction";
                    var content = "";

                    tranSending.URL = "";
                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(tranSending), Encoding.UTF8, "application/json"));

                    content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        SendConfirmation sendConfirmation = new SendConfirmation();

                        sendConfirmation.Transaction = this.TransactionBuilding;
                        sendConfirmation.TransactionFee = this.estimatedFee;
                        sendConfirmation.Cointype = this.cointype;
                        this.successTransaction = true;

                        //this.NavigationService.Navigate(new SendConfirmationMainChain(sendConfirmation, this.walletName));
                        this.Cross_Chain_Trans.Children.Add(new SendConfirmationMainChain(sendConfirmation, this.walletName));
                    }
                    else
                    {
                        this.isSending = false;
                        var errors = JsonConvert.DeserializeObject<ErrorModel>(content);

                        foreach (var error in errors.Errors)
                        {
                            MessageBox.Show(error.Message);
                            break;
                            //MessageBox.Show("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }

        private async Task SendTransactionForPchAsync(TransactionSending tranSending)
        {
            try
            {
                if (isValid())
                {
                    //string postUrl = "http://54.238.248.117:38221/api" + $"/wallet/send-transaction";
                    string postUrl = "https://api.xels.io:2332/PostAPIResponse/38221/";
                    var content = "";

                    tranSending.URL = $"/api/wallet/send-transaction";
                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(tranSending), Encoding.UTF8, "application/json"));

                    content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        SendConfirmation sendConfirmation = new SendConfirmation();

                        sendConfirmation.Transaction = this.TransactionBuilding;
                        sendConfirmation.TransactionFee = this.estimatedFee;
                        sendConfirmation.Cointype = this.cointype;
                        this.successTransaction = true;

                        //this.NavigationService.Navigate(new SendConfirmationMainChain(sendConfirmation, this.walletName));                        
                    }
                    else
                    {
                        this.isSending = false;
                        var errors = JsonConvert.DeserializeObject<ErrorModel>(content);

                        foreach (var error in errors.Errors)
                        {
                            MessageBox.Show(error.Message);
                            break;
                            //MessageBox.Show("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }

        #endregion

        #region Events

        private void CrossChainTransferButton_Click(object sender, RoutedEventArgs eventArgs)
        {
            this.isSending = true;
            //if (this.DestinationAddressText.Text.Trim().StartsWith("X"))
            //{
            //    _ = PchBuildTransactionAsync();
            //    if (this.successTransaction)
            //    {
            //        _ = BuildTransactionAsync();
            //    }
            //}
            //else
            //{
            //BuildTransactionAsync();
            //if (this.successTransaction)
            //{
            //    _ = PchBuildTransactionAsync();
            //}
            BuildTransactionAsync();
            PchBuildTransactionAsync();
            //}

        }

        private void Cancel_CrossChainTransferButton_Click(object sender, RoutedEventArgs eventArgs)
        {

        }

        private void CheckSendAmount_OnChange(object sender, RoutedEventArgs e)
        {
            string sendingAmount = this.SendAmountText.Text.Trim();

            if (!string.IsNullOrWhiteSpace(sendingAmount))
            {
                this.Amount_Error_Message_Label.Visibility = Visibility.Visible;
                if (!Regex.IsMatch(this.SendAmountText.Text, @"^([0-9]+)?(\.[0-9]{0,8})?$"))
                {
                    //MessageBox.Show("Enter a valid transaction amount. Only positive numbers and no more than 8 decimals are allowed.");
                    this.Amount_Error_Message_Label.Content = "Enter a valid transaction amount. Only positive numbers and no more than 8 decimals are allowed.";
                }
                else
                {
                    if (Convert.ToDouble(sendingAmount) > ((this.WalletBalance.MaxSpendableAmount - this.WalletBalance.Fee) / 100000000))
                    {
                        //MessageBox.Show("The total transaction amount exceeds your spendable balance.");
                        this.Amount_Error_Message_Label.Content = "The total transaction amount exceeds your spendable balance.";
                    }
                    else
                    {
                        if (this.DestinationAddressText.Text != "" && this.SendAmountText.Text != "")
                        {
                            EstimateFeeAsync();
                            this.DestinationAddressText.Focus();

                        }
                        this.Amount_Error_Message_Label.Visibility = Visibility.Hidden;
                    }
                }
            }

            this.SendAmountText.Focus();
        }

        private void CalculateTransactionFee_OnChange(object sender, RoutedEventArgs e)
        {
            if (this.DestinationAddressText.Text != "" && this.SendAmountText.Text != "")
            {
                EstimateFeeAsync();
                this.DestinationAddressText.Focus();
            }
        }


        #endregion
    }
}
