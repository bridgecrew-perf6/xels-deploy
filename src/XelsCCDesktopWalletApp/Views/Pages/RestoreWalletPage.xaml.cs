using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

using Newtonsoft.Json;

using XelsCCDesktopWalletApp.Common;
using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Views.Pages.Modals;

namespace XelsCCDesktopWalletApp.Views.Pages
{
    /// <summary>
    /// Interaction logic for RestoreWalletPage.xaml
    /// </summary>
    public partial class RestoreWalletPage : Page
    {
        public RestoreWalletPage()
        {
            InitializeComponent();
        }

        string baseURL = URLConfiguration.BaseURL;


        public bool isValid()
        {
            if (string.IsNullOrWhiteSpace(this.name.Text))
            {
                this.walletName_ErrorMessage.Visibility = Visibility.Visible;
                this.walletName_ErrorMessage.Content = "Name is required";
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.mnemonic.Text))
            {
                this.mnemonic_ErrorMessage.Visibility = Visibility.Visible;
                this.mnemonic_ErrorMessage.Content = "Field is required!";
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.creationDate.Text))
            {
                this.date_ErrorMessage.Visibility = Visibility.Visible;
                this.date_ErrorMessage.Content = "Date is required!";
                return false;
            }
            if (string.IsNullOrWhiteSpace(this.password.Password))
            {
                this.password_ErrorMessage.Visibility = Visibility.Visible;

                this.password_ErrorMessage.Content = "Password is required!";               
                    
                return false;
            }

            //if (this.password.Password.Length < 8)
            //{
            //    this.password_ErrorMessage.Visibility = Visibility.Visible;
            //    this.password_ErrorMessage.Content = "A Password must contain at least one uppercase letter, one losercase letter, one number and one special character. A Password must be at least 8 characters long.";
            //}


            //if (string.IsNullOrWhiteSpace(this.passphrase.Password))
            //{
            //    MessageBox.Show("Passphrase is required!", "Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            //    return false;
            //}
            return true;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            CreateOrRestore createOrRestore = new CreateOrRestore();

            createOrRestore.Visibility = Visibility.Visible;
            Window win = (Window)this.Parent;
            win.Close();
        }

        public void Textbox_Null_check_OnKeyPress(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(this.name.Text))
            {
                this.walletName_ErrorMessage.Visibility = Visibility.Hidden;
            }

            if (!string.IsNullOrWhiteSpace(this.creationDate.Text))
            {
                this.date_ErrorMessage.Visibility = Visibility.Hidden;
            }

            if (!string.IsNullOrWhiteSpace(this.mnemonic.Text))
            {
                this.mnemonic_ErrorMessage.Visibility = Visibility.Hidden;
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
                    this.restoreButton.IsEnabled = true;
                }
                
            }
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
               
                if (isValid())
                {
                    string postUrl = this.baseURL + "/Wallet/recover";

                    WalletRecovery recovery = new WalletRecovery();
                    recovery.Name = this.name.Text;
                    //recovery.creationDate = creationDate.SelectedDate.Value;
                    recovery.CreationDate = this.creationDate.Text;
                    recovery.Mnemonic = this.mnemonic.Text;
                    recovery.Passphrase = this.passphrase.Password;
                    recovery.Password = this.password.Password;

                    HttpResponseMessage response = await URLConfiguration.Client.PostAsJsonAsync(postUrl, recovery);

                    var content = await response.Content.ReadAsStringAsync();

                    if (response.IsSuccessStatusCode)
                    {
                        this.Restore_Account.Children.Add(new DisplayMessageUserControl($"Successfully saved data with Name:{ recovery.Name}"));
                        CreateOrRestore parentWindow = (CreateOrRestore)Window.GetWindow(this);
                        parentWindow.Visibility = Visibility.Collapsed;
                        MainWindow mw = new MainWindow();
                        mw.Show();
                    }
                    else
                    {
                        var errors = JsonConvert.DeserializeObject<ErrorModel>(content);

                        foreach (var error in errors.Errors)
                        {
                            this.Restore_Account.Children.Add(new DisplayErrorMessageUserControl(error.Message));
                            //MessageBox.Show(error.Message);
                        }

                    }
                }                 
            }
            catch (System.Exception es)
            {
                GlobalExceptionHandler.SendErrorToText(es);
            }

        }
   
    
    }
}
