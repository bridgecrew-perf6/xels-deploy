﻿#pragma checksum "..\..\..\..\Views\CreateShowMnemonic.xaml" "{ff1816ec-aa5e-4d10-87f7-6f4963833460}" "672B5166A0314A742BC855EBA994AF7B88DCBF25"
//------------------------------------------------------------------------------
// <auto-generated>
//     This code was generated by a tool.
//     Runtime Version:4.0.30319.42000
//
//     Changes to this file may cause incorrect behavior and will be lost if
//     the code is regenerated.
// </auto-generated>
//------------------------------------------------------------------------------

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
using XelsCCDesktopWalletApp.Views;


namespace XelsCCDesktopWalletApp.Views {
    
    
    /// <summary>
    /// CreateShowMnemonic
    /// </summary>
    public partial class CreateShowMnemonic : System.Windows.Controls.Page, System.Windows.Markup.IComponentConnector {
        
        
        #line 11 "..\..\..\..\Views\CreateShowMnemonic.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Grid Mnemonic_Copy;
        
        #line default
        #line hidden
        
        
        #line 52 "..\..\..\..\Views\CreateShowMnemonic.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.TextBlock textBoxTextToMnemonic;
        
        #line default
        #line hidden
        
        
        #line 57 "..\..\..\..\Views\CreateShowMnemonic.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Button copyClipboardButton;
        
        #line default
        #line hidden
        
        
        #line 75 "..\..\..\..\Views\CreateShowMnemonic.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Button cancelButton;
        
        #line default
        #line hidden
        
        
        #line 83 "..\..\..\..\Views\CreateShowMnemonic.xaml"
        [System.Diagnostics.CodeAnalysis.SuppressMessageAttribute("Microsoft.Performance", "CA1823:AvoidUnusedPrivateFields")]
        internal System.Windows.Controls.Button continueButton;
        
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
            System.Uri resourceLocater = new System.Uri("/XelsCCDesktopWalletApp;component/views/createshowmnemonic.xaml", System.UriKind.Relative);
            
            #line 1 "..\..\..\..\Views\CreateShowMnemonic.xaml"
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
            this.Mnemonic_Copy = ((System.Windows.Controls.Grid)(target));
            return;
            case 2:
            this.textBoxTextToMnemonic = ((System.Windows.Controls.TextBlock)(target));
            return;
            case 3:
            this.copyClipboardButton = ((System.Windows.Controls.Button)(target));
            
            #line 57 "..\..\..\..\Views\CreateShowMnemonic.xaml"
            this.copyClipboardButton.Click += new System.Windows.RoutedEventHandler(this.copyClipboardButton_Click);
            
            #line default
            #line hidden
            return;
            case 4:
            this.cancelButton = ((System.Windows.Controls.Button)(target));
            
            #line 75 "..\..\..\..\Views\CreateShowMnemonic.xaml"
            this.cancelButton.Click += new System.Windows.RoutedEventHandler(this.cancelButton_Click);
            
            #line default
            #line hidden
            return;
            case 5:
            this.continueButton = ((System.Windows.Controls.Button)(target));
            
            #line 83 "..\..\..\..\Views\CreateShowMnemonic.xaml"
            this.continueButton.Click += new System.Windows.RoutedEventHandler(this.continueButton_Click);
            
            #line default
            #line hidden
            return;
            }
            this._contentLoaded = true;
        }
    }
}

