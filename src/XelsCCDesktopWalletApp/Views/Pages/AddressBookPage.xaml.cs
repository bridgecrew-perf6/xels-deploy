using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

using System.Windows.Shapes;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using XelsCCDesktopWalletApp.Common;
using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Models.SmartContractModels;
using XelsCCDesktopWalletApp.Views.Pages.Modals;
using XelsCCDesktopWalletApp.Views.Pages.SendPages;
using XelsCCDesktopWalletApp.Views.ViewPage;

namespace XelsCCDesktopWalletApp.Views.Pages
{
    /// <summary>
    /// Interaction logic for AddressBookPage.xaml
    /// </summary>
    public partial class AddressBookPage : Page
    {
        #region Base
        string baseURL = URLConfiguration.BaseURL; 
        #endregion
        #region Local Info
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
        private List<AddressLabel> addresses = new List<AddressLabel>();
        private AddressLabelArray addressLabelArray = new AddressLabelArray();

        public AddressBookPage()
        {
            InitializeComponent();

            this.DataContext = this;
        }
        public AddressBookPage(string walletname)
        {
            InitializeComponent();

            this.DataContext = this;

            this.walletName = walletname;
            LoadAddresses();
        }
        #endregion

        #region Validations
        public bool isValid()
        {
            if (this.LabelTxt.Text == string.Empty)
            {
                MessageBox.Show("Please enter a label for your address.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.LabelTxt.Focus();
                return false;
            }

            if (this.LabelTxt.Text.Length < 2)
            {
                MessageBox.Show("A label needs to be at least 2 characters long.");
                this.LabelTxt.Focus();
                return false;
            }

            if (this.LabelTxt.Text.Length > 40)
            {
                MessageBox.Show("A label can't be more than 40 characters long.");
                this.LabelTxt.Focus();
                return false;
            }

            if (this.AddressTxt.Text == string.Empty)
            {
                MessageBox.Show("Please add a valid address.", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.AddressTxt.Focus();
                return false;
            }

            return true;
        }
        #endregion

        #region Get Addresses
        public async void LoadAddresses()
        {
            await GetAddressBookAddressesAsync(this.baseURL);

            if (this.addresses.Count > 0)
            {
                this.NoData.Visibility = Visibility.Hidden;
                this.ListData.Visibility = Visibility.Visible;
                this.AddressList.ItemsSource = this.addresses;
            }
            else
            {
                this.ListData.Visibility = Visibility.Hidden;
                this.NoData.Visibility = Visibility.Visible;
            }
        }
        private async Task GetAddressBookAddressesAsync(string path)
        {
            string getUrl = path + "/AddressBook";
            var content = "";
            try
            {
                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);
                content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        this.addressLabelArray = JsonConvert.DeserializeObject<AddressLabelArray>(content);

                        this.addresses.Clear();
                        foreach (var addr in this.addressLabelArray.Addresses)
                        {
                            AddressLabel addressLabel = new AddressLabel();
                            addressLabel.label = addr.label;
                            addressLabel.address = addr.address;
                            this.addresses.Add(addressLabel);
                        }
                    }
                    catch (Exception ex)
                    {
                        GlobalExceptionHandler.SendErrorToText(ex);
                    }
                }
            }
            catch (Exception ee)
            {
                GlobalExceptionHandler.SendErrorToText(ee);
            }
           
        }
        #endregion

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            DataGrid dataGrid = this.AddressList;
            DataGridRow Row = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromIndex(dataGrid.SelectedIndex);
            DataGridCell RowAndColumn = (DataGridCell)dataGrid.Columns[1].GetCellContent(Row).Parent;
            string CellValue = ((TextBlock)RowAndColumn.Content).Text;

            Clipboard.SetText(CellValue);
        }
        private void SendButton_Click(object sender, RoutedEventArgs e)
        {
            DataGrid dataGrid = this.AddressList;
            DataGridRow Row = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromIndex(dataGrid.SelectedIndex);
            DataGridCell RowAndColumn = (DataGridCell)dataGrid.Columns[1].GetCellContent(Row).Parent;
            string CellValue = ((TextBlock)RowAndColumn.Content).Text;

            GlobalPropertyModel.selectAddressFromAddressBook = CellValue;
            this.AddressBookContent.Children.Add(new SendUserControl());
        }

        #region Delete Address
        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            DataGrid dataGrid = this.AddressList;
            DataGridRow Row = (DataGridRow)dataGrid.ItemContainerGenerator.ContainerFromIndex(dataGrid.SelectedIndex);
            DataGridCell RowAndColumn = (DataGridCell)dataGrid.Columns[0].GetCellContent(Row).Parent;
            string item = ((TextBlock)RowAndColumn.Content).Text;

            _ = DeleteAddressAsync(item);
        }

        private async Task DeleteAddressAsync(string label)
        {
            try
            {
                string postUrl = this.baseURL + "/AddressBook/address?label=" + label;
                HttpResponseMessage response = await URLConfiguration.Client.DeleteAsync(postUrl);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    MessageBox.Show("Successfully deleted address with label: " + label);

                    NavigationService navService = NavigationService.GetNavigationService(this);
                    AddressBookPage page2Obj = new AddressBookPage(this.walletName);
                    navService.Navigate(page2Obj);
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }
        #endregion

        #region Add New Address
        private void AddAddress_Click(object sender, RoutedEventArgs e)
        {
            this.NewAddressPopup.IsOpen = true;
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            AddressLabel address = new AddressLabel();
            address.label = this.LabelTxt.Text;
            address.address = this.AddressTxt.Text;
            _ = AddNewAddress(address);
        }

        public async Task AddNewAddress(AddressLabel newaddress)
        {
            try
            {
                if (isValid())
                {
                    string postUrl = this.baseURL + "/AddressBook/address";

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(newaddress), Encoding.UTF8, "application/json"));

                    if (response.IsSuccessStatusCode)
                    {
                        this.NewAddressPopup.IsOpen = false;
                        MessageBox.Show("Successfully created with label: " + newaddress.label);

                        NavigationService navService = NavigationService.GetNavigationService(this);
                        AddressBookPage page2Obj = new AddressBookPage(this.walletName);
                        navService.Navigate(page2Obj);
                    }
                }
                else
                {
                    this.NewAddressPopup.IsOpen = false;
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }

        private void HidePopup_Click(object sender, RoutedEventArgs e)
        {
            this.NewAddressPopup.IsOpen = false;
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.NewAddressPopup.IsOpen = false;
        }
        #endregion

        
    }
}
