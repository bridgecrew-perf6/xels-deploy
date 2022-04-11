using System;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.CommonModels;
using XelsXLCDesktopWalletApp.Views.Dialogs.DialogsModel;

namespace XelsXLCDesktopWalletApp.Views.Pages
{
    /// <summary>
    /// Interaction logic for CreateWalletPage.xaml
    /// </summary>
    public partial class CreateWalletPage : Page
    {
        public CreateWalletPage()
        {
            InitializeComponent();
            LoadMnemonics();
        }

        string baseURL = URLConfiguration.BaseURL;
        string _mnemonic;
        bool canProceedPass = false;


        public async void LoadMnemonics()
        {
            this._mnemonic = await GetMnemonics(this.baseURL);
        }

        public async Task<bool> IsValid()
        {
            if (string.IsNullOrWhiteSpace(this.name.Text))
            {
                this.walletName_ErrorMessage.Visibility = Visibility.Visible;
                this.walletName_ErrorMessage.Content = "Name is required";
                return false;
            }

            if (this.name.Text.Length < 1 || this.name.Text.Length > 24)
            {
                this.walletName_ErrorMessage.Visibility = Visibility.Visible;
                this.walletName_ErrorMessage.Content = "Name should be 1 to 24 characters long";
                this.name.Focus();
                return false;
            }

            // Name: /^[a-zA-Z0-9]*$/
            if (!Regex.IsMatch(this.name.Text, @"^[a-zA-Z0-9]*$"))
            {
                this.walletName_ErrorMessage.Visibility = Visibility.Visible;
                this.walletName_ErrorMessage.Content = "Please enter a valid wallet name. [a-Z] and [0-9] are the only characters allowed.";
                this.name.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(this.password.Password))
            {
                this.password_ErrorMessage.Visibility = Visibility.Visible;
                this.password_ErrorMessage.Content = "Password field is required!";
                this.password.Focus();
                return false;
            }

            if (string.IsNullOrWhiteSpace(this.repassword.Password))
            {
                this.repassword_ErrorMessage.Visibility = Visibility.Visible;
                this.repassword_ErrorMessage.Content = "Confirm password field is required!";
                this.repassword.Focus();
                return false;
            }

            if (this.password.Password != this.repassword.Password)
            {
                this.repassword_ErrorMessage.Visibility = Visibility.Visible;
                this.repassword_ErrorMessage.Content = "Password not matched";
                this.repassword.Focus();
                return false;
            }

            // Password:  /^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!#$%&'()*+,-./:;<=>?@[\]^_`{|}~])[A-Za-z\d!#$%&'()*+,-./:;<=>?@[\]^_`{|}~]{8,}$/
            if (!Regex.IsMatch(this.password.Password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!#$%&'()*+,-./:;<=>?@[\]^_`{|}~])[A-Za-z\d!#$%&'()*+,-./:;<=>?@[\]^_`{|}~]{8,}$"))
            {
                this.password_ErrorMessage.Visibility = Visibility.Visible;
                this.password_ErrorMessage.Content = "A password must contain at least one uppercase letter, one lowercase letter, one number and one special character.";

                this.password.Focus();
                return false;
            }

            if (this.password.Password.Length < 8)
            {
                //MessageBox.Show("A password should be at least 8 characters long");
                this.password.Focus();

                var errorDialogMessage = ErrorDialogMessage.GetInstance();
                errorDialogMessage.Message = "A password should be at least 8 characters long";
                await DialogHost.Show(errorDialogMessage, "CreateWalletPage");
                return false;
            }

            return true;
        }

        public async void CheckPassInput()
        {
            if (this.password.Password == this.repassword.Password)
            {
                canProceedPass = true;
            }
        }

        private void Content_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!this.name.Text.Equals(String.Empty) && this.password.Password.Equals(this.repassword.Password))
                this.createButton.IsEnabled = true;
            else
                this.createButton.IsEnabled = false;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CreateOrRestore createOrRestore = new CreateOrRestore();

            createOrRestore.Visibility = Visibility.Visible;
            Window win = (Window)this.Parent;
            win.Close();
        }

        private async Task<string> GetMnemonics(string path)
        {
            var content = "";
            string mnemonic = "";
            try
            {
                string getUrl = path + "/wallet/mnemonic?language=English&wordCount=12";

                HttpResponseMessage response = await URLConfiguration.Client.GetAsync(getUrl);
                content = await response.Content.ReadAsStringAsync();
                if (response.IsSuccessStatusCode)
                {
                    mnemonic = content.Replace("\"", "");
                }
                return mnemonic;
            }
            catch (Exception ex)
            {
                GlobalExceptionHandler.SendErrorToText(ex);
            }

            return mnemonic;
        }

        private async void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            this.IsEnabled = false;
            try
            {
                if (await IsValid())
                {
                    CheckPassInput();
                    if (this.canProceedPass == true)
                    {
                        this.IsEnabled = true;
                        WalletCreation creation = new WalletCreation();
                        creation.Name = this.name.Text;
                        creation.Password = this.password.Password;
                        creation.Passphrase = this.passphrase.Password;
                        creation.Mnemonic = this._mnemonic;

                        CreateOrRestore parentWindow = (CreateOrRestore)Window.GetWindow(this);
                        parentWindow.Content = new CreateShowMnemonic(creation);
                    }
                }
                else
                {
                    this.IsEnabled = true;
                }
            }
            catch (Exception es)
            {
                GlobalExceptionHandler.SendErrorToText(es);
            }

        }

        public void Textbox_Null_check_OnKeyPress(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(this.name.Text))
            {
                this.walletName_ErrorMessage.Visibility = Visibility.Hidden;
            }

            if (!string.IsNullOrWhiteSpace(this.password.Password))
            {
                if (this.password.Password.Length < 8)
                {
                    this.password_ErrorMessage.Visibility = Visibility.Visible;
                    this.password_ErrorMessage.Content = "A Password must be at least 8 characters long.";
                }
                else if (!Regex.IsMatch(this.password.Password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!#$%&'()*+,-./:;<=>?@[\]^_`{|}~])[A-Za-z\d!#$%&'()*+,-./:;<=>?@[\]^_`{|}~]{8,}$"))
                {
                    this.password_ErrorMessage.Visibility = Visibility.Visible;
                    this.password_ErrorMessage.Content = "A Password must contain at least one uppercase letter, one losercase letter, one number and one special character.";
                }
                else
                {
                    this.password_ErrorMessage.Visibility = Visibility.Hidden;
                }

            }

            if (!string.IsNullOrWhiteSpace(this.repassword.Password))
            {
                if (this.repassword.Password.Length < 8)
                {
                    this.repassword_ErrorMessage.Visibility = Visibility.Visible;
                    this.repassword_ErrorMessage.Content = "A Password must be at least 8 characters long.";
                }
                else if (!Regex.IsMatch(this.repassword.Password, @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[!#$%&'()*+,-./:;<=>?@[\]^_`{|}~])[A-Za-z\d!#$%&'()*+,-./:;<=>?@[\]^_`{|}~]{8,}$"))
                {
                    this.repassword_ErrorMessage.Visibility = Visibility.Visible;
                    this.repassword_ErrorMessage.Content = "A Password must contain at least one uppercase letter, one losercase letter, one number and one special character.";
                }
                else
                {
                    this.repassword_ErrorMessage.Visibility = Visibility.Hidden;
                }

            }
        }
    }
}
