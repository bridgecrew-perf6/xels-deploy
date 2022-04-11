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

namespace XelsCCDesktopWalletApp.Views.Pages.Modals
{
    /// <summary>
    /// Interaction logic for AccountCreatedUserControl.xaml
    /// </summary>
    public partial class AccountCreatedUserControl : UserControl
    {
        public AccountCreatedUserControl(string message)
        {
            InitializeComponent();
            this.Message_Modal.Text = message;
        }
        private void Button_Close_Modal(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
            CreateOrRestore parentWindow = (CreateOrRestore)Window.GetWindow(this);
            parentWindow.Visibility = Visibility.Collapsed;
            MainWindow mw = new MainWindow();
            mw.Show();
        }
    }
}
