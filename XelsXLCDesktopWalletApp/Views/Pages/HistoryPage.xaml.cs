using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using Newtonsoft.Json;

using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;

namespace XelsXLCDesktopWalletApp.Views.Pages
{
    /// <summary>
    /// Interaction logic for HistoryPage.xaml
    /// </summary>
    public partial class HistoryPage : Page
    {
        private DataGridPagination _cview;
        string baseURL = URLConfiguration.BaseURLMain;

        private string walletName = GlobalPropertyModel.WalletName;

        // Hisotory detail data
        private int? lastBlockSyncedHeight = 0;
        private int? confirmations = 0;
        public HistoryPage()
        {
            InitializeComponent();
            this.DataContext = this;

            _ = GetGeneralWalletInfoAsync();
            _ = GetWalletHistoryTimerAsync(this.baseURL);

        }

        #region api call

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
                    var wallet = JsonConvert.DeserializeObject<WalletGeneralInfoModel>(content);
                    this.lastBlockSyncedHeight = wallet.LastBlockSyncedHeight; // for history detail data
                }
            }
            catch (Exception ee)
            {

                GlobalExceptionHandler.SendErrorToText(ee);
            }
           
        }

        private async Task GetWalletHistoryTimerAsync(string path)
        {
            try
            {
                var content = "";
                List<TransactionItemModel> HistoryListForTimer = new List<TransactionItemModel>();

                ObservableCollection<TransactionItemModel> observableList = new ObservableCollection<TransactionItemModel>();

                string getUrl = path + $"/wallet/history?WalletName={this.walletName}&AccountName=account 0";

                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);

                content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var history = JsonConvert.DeserializeObject<HistoryModelArray>(content);

                        foreach (var h in history.History)
                        {
                            HistoryListForTimer.AddRange(h.TransactionsHistory);
                        }

                        foreach (var hh in HistoryListForTimer)
                        {
                            if (hh.Type == "mined")
                            {
                                hh.TransactionType = "POW REWARD";
                            }
                            if (hh.Type == "staked")
                            {
                                hh.TransactionType = "HYBRID REWARD";
                            }
                            if (hh.Type == "send")
                            {
                                hh.TransactionType = "SENT";
                            }
                            if (hh.Type == "received")
                            {
                                hh.TransactionType = "RECEIVED";
                            }

                            //confirmed check
                            if (hh.ConfirmedInBlock != null && hh.ConfirmedInBlock > 0)
                            {
                                hh.TransactionIcon = "/Assets/Images/greenCircle.png";
                            }
                            else
                            {
                                hh.TransactionIcon = "/Assets/Images/redCircle.png";
                            }
                            //End
                        }

                        observableList = new ObservableCollection<TransactionItemModel>(HistoryListForTimer);
                        //this.HistoryListBinding.ItemsSource = observableList;

                        if (observableList.Count != 0)
                        {
                            this._cview = new DataGridPagination(observableList, 12);
                            this.HistoryListBinding.ItemsSource = this._cview;
                            PageCountWiseButton();
                        }
                        else
                        {
                            this.NoDataGrid.Visibility = Visibility.Visible;
                            this.HistoryDataGrid.Visibility = Visibility.Hidden;
                            this.Pagination.Visibility = Visibility.Hidden;
                        }
                    }
                    catch (Exception e)
                    {
                        GlobalExceptionHandler.SendErrorToText(e);
                    }

                }
                
            }
            catch (Exception ss)
            {
                GlobalExceptionHandler.SendErrorToText(ss);
            }

        }

        #endregion

        #region button events 
        private void DetailsButton_Click(object sender, RoutedEventArgs e)
        {
            TransactionItemModel item = (TransactionItemModel)((sender as Button))?.DataContext;
            this.IsEnabled = false;
            this.DetailsPopup.IsOpen = true;

            this.TypeTxt.Text = item.Type;
            this.TotalAmountTxt.Text = item.Amount.ToString();
            this.AmountSentTxt.Text = item.Amount.ToString();
            this.FeeTxt.Text = item.Fee.ToString();
            this.DateTxt.Text = item.Timestamp.ToString();
            this.BlockTxt.Text = item.ConfirmedInBlock.ToString();

            if (item.ConfirmedInBlock != 0 || item.ConfirmedInBlock != null)
            {
                this.confirmations = this.lastBlockSyncedHeight - item.ConfirmedInBlock + 1;
            }
            else
            {
                this.confirmations = 0;
            }
            this.ConfirmationsTxt.Text = this.confirmations.ToString();

            this.TransactionIDTxt.Text = item.Id.ToString();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(this.TransactionIDTxt.Text);
            this.CopyMessage.Visibility = Visibility.Visible;
            
        }

        private void HidePopup_Click(object sender, RoutedEventArgs e)
        {
            this.IsEnabled = true;
            this.DetailsPopup.IsOpen = false;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            this.DetailsPopup.IsOpen = false;
        }

        private void OnNextClicked(object sender, RoutedEventArgs e)
        {
            this._cview.MoveToNextPage();
        }

        private void OnPreviousClicked(object sender, RoutedEventArgs e)
        {
            this._cview.Refresh();
            this._cview.MoveToPreviousPage();
        }

        #endregion

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            //DispatcherTimer dispatcherTimer = new DispatcherTimer();
            //dispatcherTimer.Tick += new EventHandler(dispatcherTimer_Tick);
            //dispatcherTimer.Interval = new TimeSpan(0, 0, 10);
            //dispatcherTimer.Start();
        }

        private void dispatcherTimer_Tick(object sender, EventArgs e)
        {
            _ = GetWalletHistoryTimerAsync(this.baseURL);
        }

        private void PageCountWiseButton()
        {
            int pageCount = this._cview.PageCount;
            List<Button> Buttons = new List<Button>();
            for (int i = 0; i < pageCount; i++)
            {
                // if(i < 3)
                {
                    Button button = new Button()
                    {
                        Content = string.Format($"{i + 1 }"),
                        Tag = i
                    };
                    Buttons.Add(button);
                    // button.Click += new RoutedEventHandler(button_Click);
                    //this.buttons.Children.Add(button);
                    this.buttons.ItemsSource = Buttons;

                    button.Click += new RoutedEventHandler(button_Click);
                    button.Background = new SolidColorBrush(Colors.Green);
                }

            }
        }




        protected void button_Click(object sender, EventArgs e)
        {
            Button button = sender as Button;
            int i = Convert.ToInt32(button.Content);
            // identify which button was clicked and perform necessary actions
            //MessageBox.Show($"Clicked + { i }");
            if (i > this._cview.CurrentPage)
            {
                this._cview.MoveToNextPageNumber(i);
            }
            if (i < this._cview.CurrentPage)
            {
                this._cview.Refresh();
                this._cview.MoveToPreviousPageNumber(i);
            }

        }

    }
}
