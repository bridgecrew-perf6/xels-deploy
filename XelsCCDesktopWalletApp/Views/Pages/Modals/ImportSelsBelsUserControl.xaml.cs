using System;
using System.Net.Http;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using XelsCCDesktopWalletApp.Common;
using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Models.SmartContractModels;

namespace XelsCCDesktopWalletApp.Views.Pages.Modals
{
    /// <summary>
    /// Interaction logic for ImportSelsBelsUserControl.xaml
    /// </summary>
    public partial class ImportSelsBelsUserControl : UserControl
    {
        string baseURL = URLConfiguration.BaseURL;
        private readonly WalletInfo walletInfo = new WalletInfo();
        private string walletName = GlobalPropertyModel.WalletName;
        private Wallet wallet = new Wallet();
        private Wallet sWallet = new Wallet();
        private Wallet bWallet = new Wallet();
        private CreateWallet createWallet = new CreateWallet();

        private string[] walletHashArray;
        private string walletHash;
        private bool isCheckBoxChecked = false;

        public ImportSelsBelsUserControl()
        {
            InitializeComponent();
        }

        public ImportSelsBelsUserControl(string walletname)
        {
            InitializeComponent();
            this.DataContext = this;

            if (!(bool)this.CheckboxPkey.IsChecked)
            {
                this.CheckboxPkey.IsChecked = true;
                this.SelsBelsBorder.Visibility = Visibility.Hidden;
                this.SELSPrivateKeyTxt.IsEnabled = false;
                this.BELSPrivateKeyTxt.IsEnabled = false;
            }
            this.DataContext = this;
        }

        public bool isValid()
        {
            if (this.MnemonicTxt.Text == string.Empty)
            {
                this.Import_Sels_Bels.Children.Add(new DisplayErrorMessageUserControl("Mnemonic is required!"));
                this.MnemonicTxt.Focus();
                return false;
            }

            return true;
        }

        public bool isValidPKey()
        {
            if (this.SELSPrivateKeyTxt.Text == string.Empty)
            {
                MessageBox.Show("SELS Private Key is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.SELSPrivateKeyTxt.Focus();
                return false;
            }

            if (this.BELSPrivateKeyTxt.Text == string.Empty)
            {
                MessageBox.Show("BELS Private Key is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                this.BELSPrivateKeyTxt.Focus();
                return false;
            }

            return true;
        }

        private string MnemonicToHash(string mnemonic)
        {
            this.walletHashArray = new string[mnemonic.Length];
            if (mnemonic.Length != 0)
            {
                int ind = 0;
                foreach (char c in mnemonic)
                {
                    int unicode = c;
                    string code = Convert.ToString(unicode, 2);
                    this.walletHashArray[ind] = code;
                    ind++;
                }
            }
            string hashvalue = string.Join("", this.walletHashArray);

            return hashvalue;
        }

        private void ImportSelsBelsButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {            
                if (isValid())
                {
                    this.walletHash = MnemonicToHash(this.MnemonicTxt.Text);

                    if (this.SELSPrivateKeyTxt.IsEnabled == true && this.BELSPrivateKeyTxt.IsEnabled == true)
                    {
                        if (isValidPKey())
                        {
                            this.sWallet = this.createWallet.WalletCreationFromPk(this.SELSPrivateKeyTxt.Text);
                            this.bWallet = this.createWallet.WalletCreationFromPk(this.BELSPrivateKeyTxt.Text);
                            if (this.sWallet != null && this.bWallet != null)
                            {
                                this.sWallet.PrivateKey = Encryption.EncryptPrivateKey(this.sWallet.PrivateKey);
                                this.bWallet.PrivateKey = Encryption.EncryptPrivateKey(this.bWallet.PrivateKey);

                                this.createWallet.StoreLocally(this.sWallet, this.walletName, "SELS", this.walletHash);
                                this.createWallet.StoreLocally(this.bWallet, this.walletName, "BELS", this.walletHash);

                                this.Import_Sels_Bels.Children.Add(new DisplayMessageUserControl("Import Done!"));
                            }
                        }
                    }
                    else
                    {
                        this.wallet = this.createWallet.WalletCreation(this.MnemonicTxt.Text);
                        this.wallet.PrivateKey = Encryption.EncryptPrivateKey(this.wallet.PrivateKey);

                        this.createWallet.StoreLocally(this.wallet, this.walletName, "SELS", this.walletHash);
                        this.createWallet.StoreLocally(this.wallet, this.walletName, "BELS", this.walletHash);

                        this.Import_Sels_Bels.Children.Add(new DisplayMessageUserControl("Import Done!"));

                       // MessageBox.Show("Import Done");
                        //this.Visibility = Visibility.Collapsed;
                        
                    }
                     
                }
            }
            catch (Exception ex)
            {
                GlobalExceptionHandler.SendErrorToText(ex);
                this.IsEnabled = false;
            }

        }

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            this.CheckboxPkey.IsChecked = true;
            this.SelsBelsBorder.Visibility = Visibility.Hidden;
            this.SELSPrivateKeyTxt.IsEnabled = false;
            this.BELSPrivateKeyTxt.IsEnabled = false;
        }

        private void CheckBox_Unchecked(object sender, RoutedEventArgs e)
        {
            this.CheckboxPkey.IsChecked = false;
            this.SelsBelsBorder.Visibility = Visibility.Visible;
            this.SELSPrivateKeyTxt.IsEnabled = true;
            this.BELSPrivateKeyTxt.IsEnabled = true;

        }

        private void HidePopup_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }

        private void Rectangle_MouseDown(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }

        private void Mnemonic_null_check_onchange(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(this.MnemonicTxt.Text))
            {
                string[] mnemonic = this.MnemonicTxt.Text.Trim().Split(' ');

                if (mnemonic.Length != 12)
                {
                    this.mnemonic_error.Visibility = Visibility.Visible;
                    this.mnemonic_error.Content = "You entered wrong number of Mnemonics!";
                    this.ImportSelsBelsButton.IsEnabled = false;
                }
                else
                {
                    this.ImportSelsBelsButton.IsEnabled = true;
                    this.mnemonic_error.Visibility = Visibility.Hidden;
                    this.MnemonicTxt.Focus();
                }

            }
            else
            {
                this.ImportSelsBelsButton.IsEnabled = false;
            }
        }


    }
}
