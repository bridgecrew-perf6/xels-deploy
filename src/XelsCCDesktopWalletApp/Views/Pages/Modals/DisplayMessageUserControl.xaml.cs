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
    /// Interaction logic for DisplayMessageUserControl.xaml
    /// </summary>
    public partial class DisplayMessageUserControl : UserControl
    {
        public DisplayMessageUserControl(string message)
        {
            InitializeComponent();

            this.Message_Modal.Text = message; 
        }

        private void Rectangle_MouseDown(object sender, MouseButtonEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }
        private void Button_Close_Modal(object sender, RoutedEventArgs e)
        {
            this.Visibility = Visibility.Collapsed;
        }
    }
}
