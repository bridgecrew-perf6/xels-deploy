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

namespace XelsCCDesktopWalletApp.Views.Pages.Cross_chain_Transfer
{
    /// <summary>
    /// Interaction logic for CrosschainUserControl.xaml
    /// </summary>
    public partial class CrosschainUserControl : UserControl
    {
        public CrosschainUserControl()
        {
            InitializeComponent();
            this.CrossChainFrame.Content = new CrossChainTransferPage();
        }

        private void Rectangle_MouseDown(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }


        private void HidePopup_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }
    }
}
