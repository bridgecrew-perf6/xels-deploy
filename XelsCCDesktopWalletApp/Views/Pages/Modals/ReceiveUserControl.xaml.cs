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

using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Models.SmartContractModels;
using XelsCCDesktopWalletApp.Views.Pages.ReceivePages;

namespace XelsCCDesktopWalletApp.Views.Pages.Modals
{
    /// <summary>
    /// Interaction logic for ReceiveUserControl.xaml
    /// </summary>
    public partial class ReceiveUserControl : UserControl
    {    

        public ReceiveUserControl()
        {
            InitializeComponent();
            this.DataContext = this;
        }

        private void restoreButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }

        private void XelsButton_Click(object sender, RoutedEventArgs e)
        {
            this.ReceiveContent.Content = new XelsPage();
            this.XelsButton.Focus();
        }

        private void selsButton_Click(object sender, RoutedEventArgs e)
        {
            this.ReceiveContent.Content = new SelsPage();
        }

        private void Rectangle_MouseDown(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }

        private void BelsButton_Click(object sender, RoutedEventArgs e)
        {
            this.ReceiveContent.Content = new BelsPage();
        }

        private void UserControl_Initialized(object sender, EventArgs e)
        {
            this.ReceiveContent.Content = new XelsPage();
                this.XelsButton.Focus();
        }

        private void XelsButton_Initialized(object sender, EventArgs e)
        {
            this.ReceiveContent.Content = new XelsPage();
            this.XelsButton.Focus();
        }

        private void HidePopup_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }
    }
}
