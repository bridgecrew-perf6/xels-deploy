using System;
using System.Windows;
using System.Windows.Controls;
using MaterialDesignThemes.Wpf;
using XelsXLCDesktopWalletApp.Common;
using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Views.Dialogs.DialogsModel;
using XelsXLCDesktopWalletApp.Views.Pages;
using XelsXLCDesktopWalletApp.Views.Pages.Modals;

namespace XelsXLCDesktopWalletApp.Views
{
    /// <summary>
    /// Interaction logic for CreateShowMnemonic.xaml
    /// </summary>
    public partial class CreateShowMnemonic : Page
    {
        private string copyMnemonicText; 
        #region Show Mnemonic Property

        WalletCreation _walletcreate = new WalletCreation();

        #endregion

        public CreateShowMnemonic()
        {
            InitializeComponent();
        }

        
        public CreateShowMnemonic(WalletCreation model)
        {
            InitializeComponent();
            try
            {
                string words = model.Mnemonic.Replace("\"", "");
                string[] mn = words.Split(' ');
                string[] mnv = new string[12];
                string[] mnCopy = new string[12];

                for (int i = 0; i < mn.Length; i++)
                {
                    mnv[i] = $"{ i + 1 }.{" "}{ mn[i] }{" "}";
                    mnCopy[i] = mn[i];
                }

                this.textBoxTextToMnemonic.Text = String.Join(" ", mnv);
                this.copyMnemonicText = String.Join(" ", mnCopy);

                InitializeWalletCreationModel(model);
            }
            catch (Exception c)
            {
                //MessageBox.Show(c.Message.ToString());

                var dialogMessage = ErrorDialogMessage.GetInstance();
                dialogMessage.Message = c.Message.ToString();
                _=DialogHost.Show(dialogMessage, "CreateShowMnemonic");
            }
           
        }

        private void InitializeWalletCreationModel(WalletCreation CreateWallet)
        {
            this._walletcreate.Name = CreateWallet.Name;
            this._walletcreate.Passphrase = CreateWallet.Passphrase;
            this._walletcreate.Password = CreateWallet.Password;
            this._walletcreate.Mnemonic = CreateWallet.Mnemonic;
        }

        private void copyClipboardButton_Click(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(this.copyMnemonicText);

            //this.Mnemonic_Copy.Children.Add(new DisplayMessageUserControl("Copied successfully."));
            var dialogMessage = InfoDialogMessage.GetInstance();
            dialogMessage.Message = "Copied successfully.";
            _ = DialogHost.Show(dialogMessage, "CreateShowMnemonic");
            //MessageBox.Show("Copied successfully.");
        }

        private void continueButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.IsEnabled = false;
                CreateOrRestore parentWindow = (CreateOrRestore)Window.GetWindow(this);
                parentWindow.Content = new CreateConfirmMnemonic(this._walletcreate);
            }
            catch (Exception ee)
            {
                this.IsEnabled = true;
                GlobalExceptionHandler.SendErrorToText(ee);
            }

            this.IsEnabled = true;
        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            CreateOrRestore parentWindow = (CreateOrRestore)Window.GetWindow(this);
            parentWindow.Content = new CreateWalletPage();
          
        }
    }
}
