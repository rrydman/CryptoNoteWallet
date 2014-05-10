﻿using CryptoNoteWallet.Core;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Xceed.Wpf.Toolkit;

namespace CryptoNoteWallet
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private delegate void SetTextDelegate(string text);

        private DaemonWrapper Daemon { get; set; }
        private WalletWrapper Wallet { get; set; }

        private System.Timers.Timer Timer { get; set; }

        private List<string> DaemonLogLines { get; set; }
        private List<string> WalletLogLines { get; set; }

        public MainWindow(string path, bool isNew)
        {
            InitializeComponent();

            Title = string.Format(
                "{0} v{1}.{2}",
                Title,
                Assembly.GetEntryAssembly().GetName().Version.Major, 
                Assembly.GetEntryAssembly().GetName().Version.Minor);

            int interval = int.Parse(ConfigurationManager.AppSettings["RefreshInterval"]);
            Timer = new System.Timers.Timer(interval);
            Timer.Elapsed += (s, e) => Wallet.Refresh();

            string daemonClientExe = ConfigurationManager.AppSettings["DaemonFileName"];
            string walletClientExe = ConfigurationManager.AppSettings["WalletClientFileName"];

            // Initialize and start daemon.
            Daemon = new DaemonWrapper(path, daemonClientExe);
            DaemonLogLines = new List<string>();
            Daemon.OutputReceived += new EventHandler<WrapperEvent<string>>((s, e) => Dispatcher.Invoke(() => AddDaemonLogText(e.Data)));
            Daemon.Start();

            // Initialize and start wallet client.
            Wallet = new WalletWrapper(path, walletClientExe, isNew);
            WalletLogLines = new List<string>();
            Wallet.ReadyToLogin += new EventHandler<EventArgs>((s, e) => Dispatcher.Invoke(() => ShowLogin()));
            Wallet.StatusChanged += new EventHandler<WrapperEvent<string>>((s, e) => Dispatcher.Invoke(() => SetStatus(e.Data)));
            Wallet.AddressReceived += new EventHandler<WrapperEvent<string>>((s, e) => Dispatcher.Invoke(() => SetAddress(e.Data)));
            Wallet.OutputReceived += new EventHandler<WrapperEvent<string>>((s, e) => Dispatcher.Invoke(() => AddWalletLogText(e.Data)));
            Wallet.BalanceUpdated += new EventHandler<WrapperBalanceEvent>((s, e) => Dispatcher.Invoke(() => SetBalance(e.Total, e.Unlocked)));
            Wallet.Error += new EventHandler<WrapperErrorEvent>((s, e) => Dispatcher.Invoke(() => ShowError(e.Message, e.ShouldExit)));
            Wallet.Information += new EventHandler<WrapperEvent<string>>((s, e) => Dispatcher.Invoke(() => ShowInformation(e.Data)));
            Wallet.Start();

            Timer.Enabled = true;
        }

        /// <summary>
        /// Show general error message.
        /// </summary>
        /// <param name="message"></param>
        private void ShowError(string message, bool shouldExit)
        {
            System.Windows.MessageBox.Show(this, message, "Error", MessageBoxButton.OK, MessageBoxImage.Warning);

            if (shouldExit)
            {
                Close();   
            }
        }

        /// <summary>
        /// Show general information message.
        /// </summary>
        /// <param name="message"></param>
        private void ShowInformation(string message)
        {
            System.Windows.MessageBox.Show(this, message, "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        /// <summary>
        /// Transfer coins.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SendClick(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(tbAddress.Text)
                || !tbSendAmount.Value.HasValue
                || !tbSendMixin.Value.HasValue)
            {
                System.Windows.MessageBox.Show("You need to enter an address, an amount and a mixin count.", "Input error", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                string address = tbSendAddress.Text;
                decimal amount = tbSendAmount.Value.Value;
                int mixin = tbSendMixin.Value.Value;

                Wallet.Transfer(address, amount, mixin);
            }
        }

        /// <summary>
        /// Show login window. Login to wallet when login is entered. If users cancels, close application.
        /// </summary>
        private void ShowLogin()
        {
            var loginPrompt = new LoginPrompt(this);
            if (loginPrompt.ShowDialog() == true)
            {
                Wallet.Login(loginPrompt.Password);
            }
            else
            {
                Close();
            }
        }

        /// <summary>
        /// Sets the balance.
        /// </summary>
        /// <param name="total"></param>
        /// <param name="unlocked"></param>
        private void SetBalance(decimal total, decimal unlocked)
        {
            lblBalance.Content = string.Format("{0} MRO", unlocked);
            lblUnconfirmed.Content = string.Format("{0} MRO", total);
        }

        /// <summary>
        /// Set status in status bar.
        /// </summary>
        /// <param name="status"></param>
        private void SetStatus(string status)
        {
            tbStatus.Text = status;

            btnSend.IsEnabled = status.Equals("Ready");
        }

        /// <summary>
        /// Update current wallet address.
        /// </summary>
        /// <param name="address"></param>
        private void SetAddress(string address)
        {
            tbAddress.Text = address;
            btnCopyAddress.IsEnabled = true;
        }

        /// <summary>
        /// Update wallet log.
        /// </summary>
        /// <param name="text"></param>
        private void AddWalletLogText(string text)
        {
            if (WalletLogLines.Count == 50)
            {
                WalletLogLines.RemoveAt(0);
            }
            WalletLogLines.Add(text);

            tbWalletClientOutput.Text = WalletLogLines.Aggregate((l1, l2) => l1 + Environment.NewLine + l2);
            tbWalletClientOutput.ScrollToEnd();
        }

        /// <summary>
        /// Update daemon log.
        /// </summary>
        /// <param name="text"></param>
        private void AddDaemonLogText(string text)
        {
            if (DaemonLogLines.Count == 50)
            {
                DaemonLogLines.RemoveAt(0);
            }
            DaemonLogLines.Add(text);

            tbDaemonOutput.Text = DaemonLogLines.Aggregate((l1, l2) => l1 + Environment.NewLine + l2);
            tbDaemonOutput.ScrollToEnd();
        }

        /// <summary>
        /// When the form closes, make sure the wallet stops refreshing and all work is saved.
        /// </summary>
        /// <param name="e"></param>
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Cursor = Cursors.Wait;
            SetStatus("Waiting for daemon to exit...");

            Timer.Stop();
            Wallet.Exit();
            Daemon.Exit();

            base.OnClosing(e);
        }

        private void btnCopyAddressClick(object sender, RoutedEventArgs e)
        {
            Clipboard.SetText(tbAddress.Text);
        }
    }
}