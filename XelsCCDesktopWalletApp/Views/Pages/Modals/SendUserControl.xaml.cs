using System.Windows;
using System.Windows.Controls;

using NBitcoin;

using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Views.Pages.SendPages;


namespace XelsCCDesktopWalletApp.Views.Pages.Modals
{
    /// <summary>
    /// Interaction logic for SendUserControl.xaml
    /// </summary>
    public partial class SendUserControl : UserControl
    {

        public SendUserControl()
        {
            InitializeComponent();
            this.DataContext = this;
        }


        private void backButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }

        private void XELS_Button_Click(object sender, RoutedEventArgs e)
        {

            this.SendFrame.Content = new XelsPage();
        }

        private void SELS_Button_Click(object sender, RoutedEventArgs e)
        {
            this.SendFrame.Content = new SelsPage();
        }

        private void BELS_Button_Click(object sender, RoutedEventArgs e)
        {
            this.SendFrame.Content = new BelsPage();
        }

        private void UserControl_Initialized(object sender, System.EventArgs e)
        {
            this.SendFrame.Content = new XelsPage();
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
