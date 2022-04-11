using System;
using System.Collections.Generic;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using XelsCCDesktopWalletApp.Models.SmartContractModels;
using XelsCCDesktopWalletApp.Views.layout;

namespace XelsCCDesktopWalletApp.Views.Pages.Modals
{
    /// <summary>
    /// Interaction logic for LogoutConfirmUserControl.xaml
    /// </summary>
    public partial class LogoutConfirmUserControl : UserControl
    {
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
        public LogoutConfirmUserControl()
        {
            InitializeComponent();
        }
        public LogoutConfirmUserControl(string walletname)
        {
            InitializeComponent();
            this.walletName = walletname;
        }
        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            MainLayout ds = new MainLayout(this.walletName);
            ds.Show();
        }

        private void logoutButton_Click(object sender, RoutedEventArgs e)
        {
            GlobalPropertyModel.enterCount = 0;
            MainLayout parentWindow = (MainLayout)Window.GetWindow(this);
            parentWindow.Visibility = Visibility.Collapsed;
            MainWindow mw = new MainWindow();
            mw.Show();
        }
        private void Rectangle_MouseDown(object sender, RoutedEventArgs e)
        {
            MainLayout ds = new MainLayout(this.walletName);
            ds.Show();
        }

        private void closeBtn_Click(object sender, RoutedEventArgs e)
        {
            MainLayout ds = new MainLayout(this.walletName);
            ds.Show();
        }
    }
}
