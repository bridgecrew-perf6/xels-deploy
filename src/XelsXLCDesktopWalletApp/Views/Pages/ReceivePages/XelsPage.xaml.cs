using System;
using System.Collections.Generic;
using System.Data;
using System.Drawing;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using MaterialDesignThemes.Wpf;
using Newtonsoft.Json;

using QRCoder;

using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;
using XelsXLCDesktopWalletApp.Views.Dialogs.DialogsModel;
using XelsXLCDesktopWalletApp.Views.Pages.Modals;

namespace XelsXLCDesktopWalletApp.Views.Pages.ReceivePages
{
    /// <summary>
    /// Interaction logic for XelsPage.xaml
    /// </summary>
    public partial class XelsPage : Page
    {

        string baseURL = URLConfiguration.BaseURL; //  "http://localhost:37221/api";

        private readonly WalletInfo walletInfo = new WalletInfo();

        private ReceiveWalletStatus ReceiveWalletStatus;

        QRCodeConverter QRCode = new QRCodeConverter();

        private string walletName = GlobalPropertyModel.WalletName;

        public XelsPage()
        {
            InitializeComponent();
            this.ReceiveWalletStatus = new ReceiveWalletStatus();
            this.ReceiveWalletStatus.UsedAddresses = new List<ReceiveWalletStatus>();
            this.ReceiveWalletStatus.UnusedAddresses = new List<ReceiveWalletStatus>();
            this.ReceiveWalletStatus.ChangedAddresses = new List<ReceiveWalletStatus>();
            this.DataContext = this;
             
            LoadCreate();
            GenerateQRCode();
        }

        public async Task LoadCreate()
        {
            string addr = await GetUnusedReceivedAddrAPIAsync();
            addr = FreshAddress(addr);

            this.textBoxTextToQr.Text = addr;
        }
        private string FreshAddress(string adr)
        {
            adr = adr.Trim(new char[] { '"' });
            return adr;
        }

        private async Task<string> GetUnusedReceivedAddrAPIAsync()
        {
            string getUrl = this.baseURL + $"/wallet/unusedaddress?WalletName={this.walletName}&AccountName=account 0";
            var content = "";

            HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);


            if (response.IsSuccessStatusCode)
            {
                content = await response.Content.ReadAsStringAsync();
            }
            else
            {
                //this.Xels_Receive_Page.Children.Add(new DisplayErrorMessageUserControl("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase));

                var errorDialogMessage = ErrorDialogMessage.GetInstance();
                errorDialogMessage.Message = "Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase;
                await DialogHost.Show(errorDialogMessage, "ReceiveUserControl");
            }

            return content;
        }

        private async Task GetAllAddresses()
        {

            string getUrl = this.baseURL + $"/wallet/addresses?WalletName={this.walletName}&AccountName=account 0";
            var content = "";

            HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);
            content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {

                try
                {
                    var addresses = JsonConvert.DeserializeObject<ReceiveWalletArray>(content);

                    foreach (var address in addresses.Addresses)
                    {
                        if (address.IsUsed)
                        {
                            this.ReceiveWalletStatus.UsedAddresses.Add(address);
                        }
                        else if (address.IsChange)
                        {
                            this.ReceiveWalletStatus.ChangedAddresses.Add(address);
                        }
                        else
                        {
                            this.ReceiveWalletStatus.UnusedAddresses.Add(address);
                        }

                    }
                    this.UsedAddressList.ItemsSource = this.ReceiveWalletStatus.UsedAddresses;
                    this.ChangedAddressList.ItemsSource = this.ReceiveWalletStatus.ChangedAddresses;
                    this.UnusedAddressList.ItemsSource = this.ReceiveWalletStatus.UnusedAddresses;
                }
                catch (Exception e)
                {
                    GlobalExceptionHandler.SendErrorToText(e);
                }
            }
            
        }

        private  void ShowAllAddressButton_Click(object sender, RoutedEventArgs e)
        {
            GetAllAddresses();
            this.AllAddressList.Visibility = Visibility.Visible;
            this.BackSingleAddressButton.Visibility = Visibility.Visible;
            this.SingleAddress.Visibility = Visibility.Hidden;
            this.BackShowAllAddressButton.Visibility = Visibility.Hidden;
            this.showall.Visibility = Visibility.Collapsed;
        }


        private async void GenerateQRCode()
        {
            await LoadCreate();
            this.image.Source = this.QRCode.GenerateQRCode(this.textBoxTextToQr.Text);
        }

        private async void SingleAddress_Copy_Button_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(this.textBoxTextToQr.Text);
            //this.Xels_Receive_Page.Children.Add(new DisplayMessageUserControl("Address Copied Successfully : " + this.textBoxTextToQr.Text.ToString()));

            var infoDialogMessage = InfoDialogMessage.GetInstance();
            infoDialogMessage.Message = "Address Copied Successfully : " + this.textBoxTextToQr.Text.ToString();
            await DialogHost.Show(infoDialogMessage, "ReceiveUserControl");
        }

        private void Address_Copy_Button_Click(object sender, RoutedEventArgs e)
        {
            ReceiveWalletStatus item = (ReceiveWalletStatus)((sender as Button)?.Tag as ListViewItem)?.DataContext;
            Clipboard.SetText(item.Address.ToString());
            //this.Xels_Receive_Page.Children.Add(new DisplayMessageUserControl("Address Copied Successfully :- " + item.Address.ToString()));
  
        }

        //private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        //{
        //    ReceiveShowall rsa = new ReceiveShowall(this.walletName);
        //    rsa.Show();

        //}

        private void BackSingleAddressButton_Click(object sender, RoutedEventArgs e)
        {
            this.AllAddressList.Visibility = Visibility.Collapsed;
            this.SingleAddress.Visibility = Visibility.Visible;
            this.BackSingleAddressButton.Visibility = Visibility.Collapsed;
            this.BackShowAllAddressButton.Visibility = Visibility.Visible;
            this.showall.Visibility = Visibility.Visible;
        }

    }
}
