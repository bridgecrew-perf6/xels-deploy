using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;

using Newtonsoft.Json;
using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;
using XelsXLCDesktopWalletApp.Views;
using XelsXLCDesktopWalletApp.Views.layout;
using XelsXLCDesktopWalletApp.Views.Pages;
using XelsXLCDesktopWalletApp.Views.Pages.Modals;

namespace XelsXLCDesktopWalletApp
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        string baseURL = URLConfiguration.BaseURL;
        
        private WalletLoadRequest UserWallet;
        List<WalletLoadRequest> walletList;
        NodeStatusModel NodeStatusModel;

        public MainWindow()
        {
            this.UserWallet = new WalletLoadRequest();
            this.walletList = new List<WalletLoadRequest>();
            InitializeComponent();

            this.NodeStatusModel = new NodeStatusModel();

            this.DataContext = this;
           // _=  GetNodeStatus();
            _ = LoadWalletList();
        } 

        private async Task LoadWalletList()
        {
            try
            {
                string getUrl = this.baseURL + "/wallet/list-wallets";
                var content = "";

                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);
                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStringAsync();
                    this.comboWallets.ItemsSource = await WalletNamesforDropdown(content);
                    GlobalPropertyModel.ChainCheckMessage = "";
                    if (URLConfiguration.Chain == "-mainchain")
                    {
                        this.labChainCheck.Text = "Xels Main chain wallet / XLC Wallet";
                        GlobalPropertyModel.ChainCheckMessage = "Xels Main chain wallet / XLC Wallet";
                    } else if(URLConfiguration.Chain == "-sidechain")
                    {
                        this.labChainCheck.Text = "Xels Side chain wallet / Carbon Credit Wallet";
                        GlobalPropertyModel.ChainCheckMessage = "Xels Side chain wallet / Carbon Credit Wallet";
                    }
                }
            }
            catch (Exception e)
            {
                GlobalExceptionHandler.SendErrorToText(e);
            }
        }


        private async Task GetNodeStatus()
        {
            try
            {
                this.IsEnabled = false;
                string getNodeStatusInterval = this.baseURL + $"/node/status";
                var content = "";

                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getNodeStatusInterval);

                if (response.IsSuccessStatusCode)
                {
                    content = await response.Content.ReadAsStringAsync();
                    this.NodeStatusModel = JsonConvert.DeserializeObject<NodeStatusModel>(content);
                    GlobalPropertyModel.CoinUnit = this.NodeStatusModel.CoinTicker;
                    //if (this.NodeStatusModel.FeaturesData.Count > 0)
                    //{
                    //    if (this.NodeStatusModel.FeaturesData[0].Namespace == "Xels.Bitcoin.Base.BaseFeature" && this.NodeStatusModel.FeaturesData[0].State== "Initialized")
                    //    {
                    //        _ = LoadWalletList();
                    //        this.IsEnabled = true;
                    //    }
                    //    else
                    //    {
                    //        MessageBox.Show("loading Failed and Failed to start wallet!.");
                    //        this.decryptButton.Visibility = Visibility.Collapsed;
                    //        this.laNodeStatusCheck.Content = "loading Failed and Failed to start wallet!.";
                    //        this.loginInfoGrid.Visibility = Visibility.Collapsed;
                    //        this.loginInforactaangle.Visibility = Visibility.Collapsed;
                    //        this.CreateOrReplaceBlock.Visibility = Visibility.Hidden;
                    //        this.IsEnabled = true;
                    //    }

                    //}
                    //else
                    //{
                    //    MessageBox.Show("loading Failed And Failed to start wallet!.");
                    //    this.decryptButton.Visibility = Visibility.Collapsed;
                    //    this.laNodeStatusCheck.Content = "loading Failed and Failed to start wallet!.";
                    //    this.loginInforactaangle.Visibility = Visibility.Collapsed;
                    //    this.loginInfoGrid.Visibility = Visibility.Collapsed;
                    //    this.CreateOrReplaceBlock.Visibility = Visibility.Hidden;
                    //    this.IsEnabled = true;
                    //}

                }
                else
                {
                    MessageBox.Show("Error Code" + response.StatusCode + " : Message - " + response.ReasonPhrase);
                    this.IsEnabled = true;
                }
            }
            catch (Exception r)
            {
                MessageBox.Show(r.Message.ToString());
                this.decryptButton.Visibility = Visibility.Collapsed;
                this.laNodeStatusCheck.Content = r.Message.ToString();
                this.loginInfoGrid.Visibility = Visibility.Collapsed;
                this.loginInforactaangle.Visibility = Visibility.Collapsed;
                this.CreateOrReplaceBlock.Visibility = Visibility.Hidden;
                this.IsEnabled = true;
                GlobalExceptionHandler.SendErrorToText(r);
            }
        }


        private async Task<List<WalletLoadRequest>> WalletNamesforDropdown(string data)
        {
            
            var walletNames = JsonConvert.DeserializeObject<WalletLoadRequest>(data);

            foreach (var d in walletNames.WalletNames)
            {
                WalletLoadRequest wlr = new WalletLoadRequest();
                wlr.Name = d;
                this.walletList.Add(wlr);
            }

            return this.walletList;
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            MainWindow parentWindow = (MainWindow)Window.GetWindow(this);
            parentWindow.Visibility = Visibility.Collapsed;
            CreateOrRestore cr = new CreateOrRestore();

            cr.ShowDialog();
            Close();
        }

        private async void DecryptButton_ClickAsync(object sender, RoutedEventArgs e)
        {
            this.IsEnabled = false;
            this.preLoader.Visibility = Visibility.Visible;
           await LoginFunctionAsync();
        }

        private void ButtonClose_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private async Task<string> LoginFunctionAsync()
        {
            string msg = "";
            try
            {

                this.UserWallet.Name = (string)this.comboWallets.SelectedValue;
                this.UserWallet.Password = this.password.Password.Trim();

                if (this.UserWallet.Name != null && this.UserWallet.Password != "")
                {
                    string postUrl = this.baseURL + "/wallet/load/";
                    var content = "";

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsJsonAsync(postUrl, this.UserWallet);
                    content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        await GetNodeStatus();
                        GlobalPropertyModel.WalletName = this.UserWallet.Name;
                        this.preLoader.Visibility = Visibility.Collapsed;
                        this.IsEnabled = true;
                        MainLayout mainLayout = new MainLayout(this.UserWallet.Name);
                        mainLayout.Show();
                        this.Close();
                    }
                    else if (content != "" || content != null)
                    {
                        LoginError loginError = new LoginError();
                        loginError = JsonConvert.DeserializeObject<LoginError>(content);

                        this.Log_in_Window.Children.Add(new DisplayErrorMessageUserControl($"{loginError.errors[0].message}"));

                       // MessageBox.Show($"{loginError.errors[0].message}");
                        this.preLoader.Visibility = Visibility.Collapsed;
                        this.IsEnabled = true;
                    }
                    else
                    {
                        MessageBox.Show($"Error Code{response.StatusCode} : Message - {response.ReasonPhrase}");
                        this.preLoader.Visibility = Visibility.Collapsed;
                        this.IsEnabled = true;
                    }
                }
                else
                {
                    MessageBox.Show($"Enter Valid Information.");
                    this.preLoader.Visibility = Visibility.Collapsed;
                    this.IsEnabled = true;
                }
                return msg;
            }
            catch (Exception e)
            {
                this.preLoader.Visibility = Visibility.Collapsed;
                this.IsEnabled = true;
                msg = e.Message.ToString();
                GlobalExceptionHandler.SendErrorToText(e);
                return msg;
            }



        }
        private async void Grid_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            
            if (e.Key == Key.Enter && GlobalPropertyModel.enterCount == 0)
            {
                GlobalPropertyModel.enterCount = GlobalPropertyModel.enterCount + 1;
                this.IsEnabled = false;
                this.preLoader.Visibility = Visibility.Visible;
                await  LoginFunctionAsync();
            }
        }

        private void comboWallets_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            string value = (string)this.comboWallets.SelectedValue;
            if (value != "" || value != null)
            {
                this.password.Focus();
            }
            else
            {
                MessageBox.Show("Select Wallet Name.");
            }
        }
    }
}
