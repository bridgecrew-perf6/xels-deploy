using System;
using System.ComponentModel;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Newtonsoft.Json;
using XelsCCDesktopWalletApp.Common;
using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Views.Pages.Modals;

namespace XelsCCDesktopWalletApp.Views
{
    /// <summary>
    /// Interaction logic for CreateConfirmMnemonic.xaml
    /// </summary>
    public partial class CreateConfirmMnemonic : Page
    {

        string baseURL = URLConfiguration.BaseURL;//"http://localhost:37221/api/wallet";

        WalletCreation Walletcreateconfirm = new WalletCreation();

        private bool canPassMnemonic = false;

        string[] words;

        int[] randomidx = new int[3];

        public CreateConfirmMnemonic()
        {
            InitializeComponent();
        }

        public CreateConfirmMnemonic(WalletCreation walletcreation)
        {
            InitializeComponent();
            InitializeWalletCreationModel(walletcreation);
            RandomSelect();
        }

        private void InitializeWalletCreationModel(WalletCreation CreateWallet)
        {
            this.Walletcreateconfirm.Name = CreateWallet.Name;
            this.Walletcreateconfirm.Passphrase = CreateWallet.Passphrase;
            this.Walletcreateconfirm.Password = CreateWallet.Password;
            this.Walletcreateconfirm.Mnemonic = CreateWallet.Mnemonic;
        }

        #region field property 
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(String property)
        {
            if (PropertyChanged != null)
            {
                PropertyChanged(this, new PropertyChangedEventArgs(property));
            }
        }

        private string valueone;
        public string Valueone
        {
            get { return this.valueone; }
            set { this.valueone = value; OnPropertyChanged("Valueone"); }
        }

        private string valuetwo;
        public string Valuetwo
        {
            get { return this.valuetwo; }
            set { this.valuetwo = value; OnPropertyChanged("Valuetwo"); }
        }

        private string valuethree;


        public string Valuethree
        {
            get { CreateConfirmMnemonic createConfirmMnemonic = this; return createConfirmMnemonic.valuethree; }
            set { this.valuethree = value; OnPropertyChanged("Valuethree"); }
        }
        #endregion


        private void RandomSelect()
        {

            this.words =  this.Walletcreateconfirm.Mnemonic.Split(' ');

            //// Random number select
            for (int i = 0; i < 3; i++)
            {
                var idx = new Random().Next(this.words.Length);

                if (!this.randomidx.Contains(idx))
                {
                    this.randomidx[i] = idx;
                }
            }
            Array.Sort(this.randomidx);
            int fInd = this.randomidx[0] + 1;
            int sInd = this.randomidx[1] + 1;
            int tInd = this.randomidx[2] + 1;
            this.valueone = "Word number " + fInd;
            this.valuetwo = "Word number " + sInd;
            this.valuethree = "Word number " + tInd;

            this.wordone.Content = this.valueone;
            this.wordtwo.Content = this.valuetwo;
            this.wordthree.Content = this.valuethree;

        }

        public void CheckMnemonic()
        {
            string firstword = this.words[this.randomidx[0]];
            string secondword = this.words[this.randomidx[1]];
            string thirdword = this.words[this.randomidx[2]];

            // Check for validation
            if (this.Walletcreateconfirm.Mnemonic != "" && this.word1.Text.Trim() == firstword &&
                this.word2.Text.Trim() == secondword && this.word3.Text.Trim() == thirdword)
            {
                this.canPassMnemonic = true;
            }
            else
            {
                this.Confirm_Account_Creation.Children.Add(new DisplayErrorMessageUserControl("Secret words do not match!"));
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CreateOrRestore parentWindow = (CreateOrRestore)Window.GetWindow(this);
            parentWindow.Content = new CreateShowMnemonic(this.Walletcreateconfirm);
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.IsEnabled = false;
                CheckMnemonic();
                if (this.canPassMnemonic == true)
                {
                    string postUrl = this.baseURL + "/wallet/create";

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsync(postUrl, new StringContent(JsonConvert.SerializeObject(this.Walletcreateconfirm), Encoding.UTF8, "application/json"));

                    var content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        //MessageBox.Show($"Successfully created wallet with Name: {this.Walletcreateconfirm.Name}");

                        this.Confirm_Account_Creation.Children.Add(new AccountCreatedUserControl($"Successfully created wallet with Name: {this.Walletcreateconfirm.Name}"));

                        //CreateOrRestore parentWindow = (CreateOrRestore)Window.GetWindow(this);
                        //parentWindow.Visibility = Visibility.Collapsed;
                        //MainWindow mw = new MainWindow();
                        //mw.Show();
                    }
                }
            }
            catch (Exception ee)
            {
                this.IsEnabled = true;
                GlobalExceptionHandler.SendErrorToText(ee);
            }
            this.IsEnabled = true;
        }

    }
}
