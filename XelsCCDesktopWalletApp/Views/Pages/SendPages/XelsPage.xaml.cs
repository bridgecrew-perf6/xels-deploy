using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

using NBitcoin;

using Newtonsoft.Json;

using XelsCCDesktopWalletApp.Models;
using XelsCCDesktopWalletApp.Models.CommonModels;
using XelsCCDesktopWalletApp.Models.SmartContractModels;

namespace XelsCCDesktopWalletApp.Views.Pages.SendPages
{
    /// <summary>
    /// Interaction logic for XelsPage.xaml
    /// </summary>
    public partial class XelsPage : Page
    { 
        private string walletName = GlobalPropertyModel.WalletName;

        public XelsPage()
        { 
            InitializeComponent();
            this.DataContext = this;
        }


        private void Mainchain_Button_Click(object sender, RoutedEventArgs e)
        {
            this.xelsPageContent.Content = new MainchainPage();
        }

        private void Sidechain_Button_Click(object sender, RoutedEventArgs e)
        {
            this.xelsPageContent.Content = new SidechainPage();
        }

        private void Window_Initialized(object sender, System.EventArgs e)
        {
            this.xelsPageContent.Content = new MainchainPage();
        }

    }
}
