﻿#pragma checksum "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml" "{ff1816ec-aa5e-4d10-87f7-6f4963833460}" "57AF493ABC790D2C0B0A7A115C0F8EC2FC6DC85C"
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

using MaterialDesignThemes.Wpf;
using MaterialDesignThemes.Wpf.Converters;
using MaterialDesignThemes.Wpf.Transitions;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Controls.Ribbon;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Media.TextFormatting;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Shell;
using XelsCCDesktopWalletApp.Views.Pages;


namespace XelsCCDesktopWalletApp.Views.Pages {
    
    
    /// <summary>
    /// CreateWalletPage
    /// </summary>
    public partial class CreateWalletPage : System.Windows.Controls.Page, System.Windows.Markup.IComponentConnector {
        
        
        #line 30 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBox name;
        
        #line default
        #line hidden
        
        
        #line 31 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Label walletName_ErrorMessage;
        
        #line default
        #line hidden
        
        
        #line 48 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.PasswordBox password;
        
        #line default
        #line hidden
        
        
        #line 51 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Label password_ErrorMessage;
        
        #line default
        #line hidden
        
        
        #line 68 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.PasswordBox repassword;
        
        #line default
        #line hidden
        
        
        #line 74 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Label repassword_ErrorMessage;
        
        #line default
        #line hidden
        
        
        #line 90 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.PasswordBox passphrase;
        
        #line default
        #line hidden
        
        
        #line 110 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Button cancelButton;
        
        #line default
        #line hidden
        
        
        #line 118 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Button createButton;
        
        #line default
        #line hidden
        
        private bool _contentLoaded;
        
        /// <summary>
        /// InitializeComponent
        /// </summary>
        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "5.0.13.0")]
        public void InitializeComponent() {
            if (_contentLoaded) {
                return;
            }
            _contentLoaded = true;
            System.Uri resourceLocater = new System.Uri("/XelsCCDesktopWalletApp;component/views/pages/createwalletpage.xaml", System.UriKind.Relative);
            
            #line 1 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
            System.Windows.Application.LoadComponent(this, resourceLocater);
            
            #line default
            #line hidden
        }
        
        [System.Diagnostics.DebuggerNonUserCodeAttribute()]
        [System.CodeDom.Compiler.GeneratedCodeAttribute("PresentationBuildTasks", "5.0.13.0")]
        [System.ComponentModel.EditorBrowsableAttribute(System.ComponentModel.EditorBrowsableState.Never)]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Design", "CA1033:InterfaceMethodsShouldBeCallableByChildTypes")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Maintainability", "CA1502:AvoidExcessiveComplexity")]
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1800:DoNotCastUnnecessarily")]
        void System.Windows.Markup.IComponentConnector.Connect(int connectionId, object target) {
            switch (connectionId)
            {
            case 1:
            this.name = ((System.Windows.Controls.TextBox)(target));
            
            #line 30 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
            this.name.TextChanged += new System.Windows.Controls.TextChangedEventHandler(this.Textbox_Null_check_OnKeyPress);
            
            #line default
            #line hidden
            return;
            case 2:
            this.walletName_ErrorMessage = ((System.Windows.Controls.Label)(target));
            return;
            case 3:
            this.password = ((System.Windows.Controls.PasswordBox)(target));
            
            #line 49 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
            this.password.PasswordChanged += new System.Windows.RoutedEventHandler(this.Textbox_Null_check_OnKeyPress);
            
            #line default
            #line hidden
            return;
            case 4:
            this.password_ErrorMessage = ((System.Windows.Controls.Label)(target));
            return;
            case 5:
            this.repassword = ((System.Windows.Controls.PasswordBox)(target));
            
            #line 73 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
            this.repassword.PasswordChanged += new System.Windows.RoutedEventHandler(this.Textbox_Null_check_OnKeyPress);
            
            #line default
            #line hidden
            return;
            case 6:
            this.repassword_ErrorMessage = ((System.Windows.Controls.Label)(target));
            return;
            case 7:
            this.passphrase = ((System.Windows.Controls.PasswordBox)(target));
            return;
            case 8:
            this.cancelButton = ((System.Windows.Controls.Button)(target));
            
            #line 110 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
            this.cancelButton.Click += new System.Windows.RoutedEventHandler(this.CancelButton_Click);
            
            #line default
            #line hidden
            return;
            case 9:
            this.createButton = ((System.Windows.Controls.Button)(target));
            
            #line 118 "..\..\..\..\..\Views\Pages\CreateWalletPage.xaml"
            this.createButton.Click += new System.Windows.RoutedEventHandler(this.CreateButton_Click);
            
            #line default
            #line hidden
            return;
            }
            this._contentLoaded = true;
        }
    }
}

