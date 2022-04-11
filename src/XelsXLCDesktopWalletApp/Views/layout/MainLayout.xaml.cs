using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;
using XelsXLCDesktopWalletApp.Views.Pages;
using XelsXLCDesktopWalletApp.Views.Pages.Modals;
using XelsXLCDesktopWalletApp.Views.ViewPage;


namespace XelsXLCDesktopWalletApp.Views.layout
{
    /// <summary>
    /// Interaction logic for MainLayout.xaml
    /// </summary>
    public partial class MainLayout : Window
    {
        #region Common
        private string baseURL = URLConfiguration.BaseURL;
        private readonly WalletInfo walletInfo = new WalletInfo();
        private string walletName;

        private WalletGeneralInfoModel walletGeneralInfoModel = new WalletGeneralInfoModel();
        private string processedText;
        private double percentSyncedNumber = 0;

        public bool sidechainEnabled = false;
        public bool stakingEnabled = false;
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

        public MainLayout()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        public MainLayout(string walletname)
        {
            this.walletName = walletname;
            this.walletInfo.WalletName = this.walletName;

            InitializeComponent();
            this.DataContext = this;
            this.labWalletName.Content = this.walletName;
            this.labCheckChainMessage.Content = GlobalPropertyModel.ChainCheckMessage;
            _ = GetGeneralWalletInfoAsync();

            if (GlobalPropertyModel.StakingStart == true)
            {
                this.StakingInfo.Text = "Staking";
                this.thumbsup.Visibility = Visibility.Visible;
                this.thumbDown.Visibility = Visibility.Collapsed;
            }

            if (URLConfiguration.Chain == "-sidechain")// (!this.sidechainEnabled)
            {
                this.thumbDown.Visibility = Visibility.Collapsed;
                this.thumbsup.Visibility = Visibility.Collapsed;
            }
            //GetGeneralInfoAsync();
            //LoadLoginAsync();
            //GetHistoryAsync();

            //if (URLConfiguration.Chain != "-sidechain")// (!this.sidechainEnabled)
            //{
            //    _ = GetStakingInfoAsync(this.baseURL);
            //}

            if (URLConfiguration.Chain != "-sidechain")// (!this.sidechainEnabled)
            {
                this.btn_SmartContract.Visibility = Visibility.Hidden;
            }
            //PopulateTxt();

            //DispatcherTimer timer = new DispatcherTimer();
            //timer.Interval = TimeSpan.FromSeconds(10);
            //timer.Tick += Window_Initialized;
            //timer.Start();
        }

        private void ButtonFechar_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void GridBarraTitulo_MouseDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
            
        private void btnDashboard_Click(object sender, RoutedEventArgs e)
        {
            this.PageContent.Content = new DashboardPage(this.walletName);
 
            this.btnDashboard.Focus();
        }

        private void btn_SmartContract_Click(object sender, RoutedEventArgs e)
        {
            this.PageContent.Content = null;
            this.PageContent.Content = new SmtAddressSelection(this.walletName);
        }

        private void btnHistory_Click(object sender, RoutedEventArgs e)
        {
            this.PageContent.Content = null;
            this.PageContent.Content = new HistoryPage();
        }

        private void btn_Exchange_Click(object sender, RoutedEventArgs e)
        {
            this.PageContent.Content = null;
            this.PageContent.Content = new ExchangePage(this.walletName);
        }

        private void LogOut_Button(object sender, RoutedEventArgs e)
        {
            //LogoutConfirm lc = new LogoutConfirm(this.walletName);
            //lc.Show();
            //this.Close();
            this.PageContent.Content= new LogoutConfirmUserControl(this.walletName);
        }

        private void AddressBookButton_Click(object sender, RoutedEventArgs e)
        {

            this.PageContent.Content = new AddressBookPage(this.walletName);

        }

        private void AdvancedButton_Click(object sender, RoutedEventArgs e)
        {
            this.PageContent.Content = new AdvancedPage(this.walletName);

        }

        private void Window_Initialized(object sender, System.EventArgs e)
        {            
            this.PageContent.Content = new DashboardPage(this.walletName);

           
        }

        private void windowMin_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }


        //private void ButtonOpenMenu_Click(object sender, RoutedEventArgs e)
        //{
        //    this.ButtonCloseMenu.Visibility = Visibility.Visible;
        //    this.ButtonOpenMenu.Visibility = Visibility.Collapsed;
        //    this.GridMain.Width = 880;
        //}

        //private void ButtonCloseMenu_Click(object sender, RoutedEventArgs e)
        //{
        //    this.ButtonCloseMenu.Visibility = Visibility.Collapsed;
        //    this.ButtonOpenMenu.Visibility = Visibility.Visible;
        //    this.GridMain.Width = 1010;

        //}

        //private void ListViewMenu_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    //UserControl usc = null;
        //    //GridMain.Children.Clear();

        //    //switch (((ListViewItem)((ListView)sender).SelectedItem).Name)
        //    //{
        //    //    case "ItemHome":
        //    //        usc = new UserControlHome();
        //    //        GridMain.Children.Add(usc);
        //    //        break;
        //    //    case "ItemCreate":
        //    //        usc = new UserControlCreate();
        //    //        GridMain.Children.Add(usc);
        //    //        break;
        //    //    case "Page1":
        //    //        GridMain.Children.Add(new Page1());
        //    //        break;
        //    //    default:
        //    //        break;
        //    //}

        //    Page usc = null;
        //    this.GridMain.Content = null;

        //    switch (((ListViewItem)((ListView)sender).SelectedItem).Name)
        //    {

        //        case "Page1":
        //            this.GridMain.Content = new Page1();
        //            break;
        //        case "Page2":
        //            this.GridMain.Content = new Page2();
        //            break;
        //        default:
        //            break;
        //    }
        //}
        private async Task GetGeneralWalletInfoAsync()
        {
            try
            {
                string getUrl = this.baseURL + $"/wallet/general-info?Name={this.walletName}";
                var content = "";

                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);
                content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {

                    this.walletGeneralInfoModel = JsonConvert.DeserializeObject<WalletGeneralInfoModel>(content);


                    //this.processedText = $"Processed { this.walletGeneralInfoModel.LastBlockSyncedHeight ?? 0} out of { this.walletGeneralInfoModel.ChainTip} blocks.";

                    //this.blockChainStatus = $"Synchronizing.  { this.processedText}";

                    //this.ConnectionStatusTxt.Text = $"{ this.walletGeneralInfoModel.ConnectedNodes} connection";
                    this.ConnectedCountTxt.Text = this.walletGeneralInfoModel.ConnectedNodes.ToString();

                    if (!this.walletGeneralInfoModel.IsChainSynced)
                    {
                        this.ConnectionPercentTxt.Text = "syncing".ToString();
                    }
                    else
                    {
                        this.percentSyncedNumber = ((this.walletGeneralInfoModel.LastBlockSyncedHeight / this.walletGeneralInfoModel.ChainTip) * 100) ?? 0;

                        if (Math.Round(this.percentSyncedNumber) == 100 && this.walletGeneralInfoModel.LastBlockSyncedHeight != this.walletGeneralInfoModel.ChainTip)
                        {
                            this.ConnectionPercentTxt.Text = "99 %";
                        }
                        else
                        {
                            this.ConnectionPercentTxt.Text = "100 %";
                            this.syncingIcon.Visibility = Visibility.Collapsed;
                            this.syncCompleteIcon.Visibility = Visibility.Visible;
                        }
                    }


                    if (!GlobalPropertyModel.StakingStart && !this.sidechainEnabled)
                    {
                        this.StakingInfo.Text = "Not Staking";
                        this.thumbsup.Visibility = Visibility.Collapsed;
                        this.thumbDown.Visibility = Visibility.Visible;
                    }
                    else if (this.stakingEnabled && !this.sidechainEnabled)
                    {
                        this.StakingInfo.Text = "Staking";
                    }
                }

            }
            catch (Exception q)
            {
                GlobalExceptionHandler.SendErrorToText(q);
            }

        }

        private void StackingBarLoaded(object sender, RoutedEventArgs e)
        {
            DispatcherTimer dispatcherTimer = new DispatcherTimer();
            URLConfiguration.Pagenavigation = false;
            dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            dispatcherTimer.Interval = new TimeSpan(0, 0, 10);
            dispatcherTimer.Start();
        }
        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                //this.DataContext = this;
                //this.walletInfo.WalletName = this.walletName;
                _ = GetGeneralWalletInfoAsync();

                if (GlobalPropertyModel.StakingStart == true)
                {
                    this.StakingInfo.Text = "Staking";
                    this.thumbsup.Visibility = Visibility.Visible;
                    this.thumbDown.Visibility = Visibility.Collapsed;
                }
            }
            catch (Exception a)
            {
                GlobalExceptionHandler.SendErrorToText(a);
            }
        }
    }
}
