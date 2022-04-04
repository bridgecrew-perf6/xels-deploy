using System;
using System.Windows;
using System.Windows.Controls;

using NBitcoin;

using XelsXLCDesktopWalletApp.Models;
using XelsXLCDesktopWalletApp.Models.SmartContractModels;

namespace XelsXLCDesktopWalletApp.Views
{
    /// <summary>
    /// Interaction logic for SendConfirmationSideChain.xaml
    /// </summary>
    public partial class SendConfirmationSideChain : UserControl
    {
        private string walletName;

        public SendConfirmationSideChain()
        {
            InitializeComponent();
        }

        public SendConfirmationSideChain(SendConfirmationSC sendConf, string walletname)
        {
            InitializeComponent();
            this.DataContext = this;

            this.walletName = walletname;
            bindData(sendConf);
        }

        private void bindData(SendConfirmationSC data)
        {     
            this.AmountSent.Content = data.Transaction.Recipients[0].Amount + " " + GlobalPropertyModel.CoinUnit; ;
            //this.AmountSentType.Content = data.cointype;

            this.Fee.Content = data.TransactionFee /100000000;
             
            this.OPreturn.Content = data.OpReturnAmount; 

            this.Total.Content = (Convert.ToDouble(data.Transaction.FeeAmount) + Convert.ToDouble(data.Transaction.Recipients[0].Amount)).ToString() + " " + GlobalPropertyModel.CoinUnit;

            this.DestinationFederation.Content = data.Transaction.Recipients[0].DestinationAddress ; 

            this.DestinationAddress.Content = data.Transaction.OpReturnData;

        }

        private void cancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            //SendSideChain sendSc = new SendSideChain(this.walletName);
            //sendSc.Show();
            //this.Close();
        }
        private void Rectangle_MouseDown(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }   
    }
}
