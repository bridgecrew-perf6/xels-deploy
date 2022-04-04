using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;

using XelsCCDesktopWalletApp.Views.Pages;

namespace XelsCCDesktopWalletApp.Views
{
    /// <summary>
    /// Interaction logic for CreateOrRestore.xaml
    /// </summary>
    public partial class CreateOrRestore : Window
    {
        public CreateOrRestore()
        {
            InitializeComponent();
        }

        private void RestoreWalletButton_Click(object sender, RoutedEventArgs e)
        {
            this.Content = new RestoreWalletPage();
            //Restore restore = new Restore();
            //restore.Show();
            //this.Close();
        }

        private void createButton_Click(object sender, RoutedEventArgs e)
        {
            this.Content = new CreateWalletPage();
        }

        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            MainWindow mw = new MainWindow();
            mw.Show();
           this.Close();
        }
    }
}
