using Microsoft.VisualBasic;
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.IO;
using System.Media;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Timers;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace Upgraded_Pinger
{
    public partial class MainWindow : Window
    {
        private static System.Timers.Timer? _timer;
        private int _count = 0;
        public bool pinging = false;

        private string[] _ipAddresses = new string[5];
        private readonly Queue<bool> _last15Results = new Queue<bool>(15);
        private readonly Queue<bool> _last100Results = new Queue<bool>(100);
        private int totalSeconds = 90;


        public MainWindow()
        {
            InitializeComponent();

            _timer = new System.Timers.Timer(200);
            _timer.Elapsed += every200msEvents;
            _timer.AutoReset = true;
            _timer.Enabled = true;

            ProtectionToggle.IsChecked = true;
            EnableDisableEthernet.IsChecked = true;
            KillChromeToggle.IsChecked = true;
            ReconnectBTN.IsChecked = true;

            adressIP1.Text = "8.8.8.8";
            adressIP2.Text = "store.steampowered.com";
            adressIP3.Text = "208.67.222.222";
            adressIP4.Text = "104.16.1.5";
            adressIP5.Text = "192.168.1.1";

            // Copy IP texts safely on UI thread
            Dispatcher.Invoke(() =>
            {
                _ipAddresses[0] = adressIP1.Text;
                _ipAddresses[1] = adressIP2.Text;
                _ipAddresses[2] = adressIP3.Text;
                _ipAddresses[3] = adressIP4.Text;
                _ipAddresses[4] = adressIP5.Text;
            });

        }

        // Main cycle: called every 200 ms
        private void every200msEvents(object? source, ElapsedEventArgs e)
        {
            _count++;
            int digit = _count % 10;

            // Check Ethernet status every 2 seconds and play sound conditionally
            if (digit == 0 || digit == 5)
            {
                Dispatcher.Invoke(() =>
                {
                    if (digit == 0)
                    {
                        var status = CheckEthernetStatus();
                        EnableDisableEthernet.Content = status;
                    }
                    else if (digit == 5 || digit == 8)
                    {
                        if (ActiveAdaptersLabel.Content?.ToString() == "1" &&
                            EnableDisableEthernet.Content?.ToString() == "Disabled")
                        {
                            string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "loop.wav");

                            if (File.Exists(soundPath) && digit == 5)
                            {
                                PlaySound(soundPath);
                                if (totalSeconds <= 5)
                                {
                                    PlaySound(soundPath);
                                }
                            }
                        }
                    }
                });
            }


            //Activities active only when pinging
            if (pinging)
            {
                string? ip = null;
                TextBlock? statusIndicator = null;
                TextBlock? nextIndicator = null;

                switch (digit)
                {
                    case 0:
                    case 5:
                        ip = _ipAddresses[0];
                        nextIndicator = StatusIndicator1; //Next to ping
                        statusIndicator = StatusIndicator0;
                        break;
                    case 1:
                    case 6:
                        ip = _ipAddresses[1];
                        nextIndicator = StatusIndicator2; //Next to ping
                        statusIndicator = StatusIndicator1;
                        break;
                    case 2:
                    case 7:
                        ip = _ipAddresses[2];
                        nextIndicator = StatusIndicator3; //Next to ping
                        statusIndicator = StatusIndicator2;
                        break;
                    case 3:
                    case 8:
                        ip = _ipAddresses[3];
                        nextIndicator = StatusIndicator4; //Next to ping
                        statusIndicator = StatusIndicator3;
                        break;
                    case 4:
                    case 9:
                        nextIndicator = StatusIndicator0;
                        ip = _ipAddresses[4];
                        statusIndicator = StatusIndicator4;
                        break;
                }

                if (ip != null && statusIndicator != null && pinging)
                {
                    bool pingResult = PingAddress(ip);
                    AddPingResult(pingResult); //function that tracks loss for 15 and 100 last pings

                    Dispatcher.Invoke(() =>
                    {
                        //Update every result with correct color and BAD or OK depending of loss or success
                        nextIndicator.Foreground = new SolidColorBrush(Colors.Gray);
                        statusIndicator.Foreground = new SolidColorBrush(
                            pingResult ? Color.FromArgb(0xFF, 0x00, 0xFF, 0x00) : Color.FromArgb(0xFF, 0xFF, 0x00, 0x00));
                        statusIndicator.Text = pingResult ? "OK" : "BAD";
                        
                        //update prgress Bar
                        TimeoutProgressBar.Value = CountPingLosses(_last15Results);

                        //Check if progress Bar (ping loss) is more than slider
                        if (TimeoutProgressBar.Value >= Tolerance_Input.Value)
                        {
                            if (ProtectionToggle.Content == "ON")
                            {
                                whatHappensWhenPingBad();
                            }
                        }

                        //update timeouts 100
                        int totalPings = _last100Results.Count;
                        int failedPings = CountPingLosses(_last100Results);

                        double lossPercent = totalPings == 0 ? 0 : (double)failedPings / totalPings * 100;

                        timeout100.Content = lossPercent.ToString("F1") + "%";

                        //Based on timouts100, controll the status of pings
                        if (lossPercent < 10)
                        {
                            timeout100.Background = new SolidColorBrush(Colors.Green);
                        }
                        else
                        {
                            timeout100.Background = new SolidColorBrush(Colors.Red);
                        }

                    });
                }
            }
            else
            {
                Dispatcher.Invoke(() =>
                {
                    StatusIndicator0.Text = "Stand by";
                    StatusIndicator0.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x40, 0x40));
                    StatusIndicator1.Text = "Stand by";
                    StatusIndicator1.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x40, 0x40));
                    StatusIndicator2.Text = "Stand by";
                    StatusIndicator2.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x40, 0x40));
                    StatusIndicator3.Text = "Stand by";
                    StatusIndicator3.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x40, 0x40));
                    StatusIndicator4.Text = "Stand by";
                    StatusIndicator4.Foreground = new SolidColorBrush(Color.FromArgb(0xFF, 0x40, 0x40, 0x40));
                });
            }

            // Safely update other UI elements
            Dispatcher.Invoke(() => status_tools());
        }


        // Updates the button color based on "pinging"
        private void status_tools()
        {
            //Is it pinging?
            if (pinging)
            {
                btn_pinging.Background = new SolidColorBrush(Colors.Green);
                btn_pinging.Content = "ON";
            }
            else
            {
                btn_pinging.Background = new SolidColorBrush(Colors.Red);
                btn_pinging.Content = "OFF";
            }

            //How many ethernet
            if (GetEnabledEthernetAdapterCount() < 2)
            {
                ActiveAdaptersLabel.Background = new SolidColorBrush(Colors.Red);
                ActiveAdaptersLabel.Content = GetEnabledEthernetAdapterCount();
            }
            else
            {
                ActiveAdaptersLabel.Background = new SolidColorBrush(Colors.Green);
                ActiveAdaptersLabel.Content = GetEnabledEthernetAdapterCount();
            }

            //Killing chrome are we?
            if ((bool)KillChromeToggle.IsChecked)
            {
                status_killchrome.Background = new SolidColorBrush(Colors.Green);
                status_killchrome.Content = "ON";
            }
            else
            {
                status_killchrome.Background = new SolidColorBrush(Colors.Red);
                status_killchrome.Content = "OFF";
            }
        }


        //Here we check what happens and what to kill
        private void whatHappensWhenPingBad()
        {
            bool protectionEnabled = ProtectionToggle.Content.ToString() == "ON";
            bool shouldKillChrome = KillChromeToggle.IsChecked ?? false;
            string soundPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "alert.wav");

            if (protectionEnabled)
            {
                SetEthernetState(false);
                ProtectionToggle.IsChecked = false;
                ProtectionToggle.Content = "Paused";

                if (File.Exists(soundPath))
                {
                    PlaySound(soundPath);
                }

                if (shouldKillChrome)
                {
                    KillChrome();
                }
            }

            if (ReconnectBTN.Content != "OFF") {
                // Start a timer for 90 seconds (90,000 milliseconds)
                DispatcherTimer timer = new DispatcherTimer();
                timer.Interval = TimeSpan.FromSeconds(1);
                timer.Tick += (sender, args) =>
                {
                    totalSeconds--;

                    ReconnectBTN.Content = $"({totalSeconds}s)";

                    if (totalSeconds <= 0)
                    {
                        totalSeconds = 90;
                        timer.Stop();
                        ReconnectBTN.Content = "ON";
                        ReconnectionFunction();
                    }
                };
                timer.Start();
            }
        }

        //this function is called to re enable ethernet 1
        public void ReconnectionFunction()
        {
            //Internet is back
            SetEthernetState(true);

            //testing again
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromSeconds(8);
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                ProtectionToggle.Content = "ON";
            };
            timer.Start();
        }


        public void PlaySound(string filePath)
        {
            try
            {
                if (File.Exists(filePath))
                {
                    using (SoundPlayer player = new SoundPlayer(filePath))
                    {
                        player.Load();
                        player.PlaySync();
                    }
                }
            }
            catch { }
        }

        //Button click on start pinging or stop it
        private void Button_Click(object sender, RoutedEventArgs e)
        {
            if (pinging == true)
            {
                pinging = false;
                btn_start_pinging.Content = "Start Pinging";
            }
            else
            {
                pinging = true;
                btn_start_pinging.Content = "Stop Pinging";
            }
        }

        //Toggled buttons: Ethernet
        private void ProtectionToggle_Click(object sender, RoutedEventArgs e)
        {
            if (ProtectionToggle.Content == "OFF" || ProtectionToggle.Content == "Paused")
            {
                ProtectionToggle.Content = "ON";
            }
            else
            {
                ProtectionToggle.Content = "OFF";
            }
        }

        //Toggled buttons: Reconnect
        private void ReconnectBTN_Click(object sender, RoutedEventArgs e)
        {
            if (ReconnectBTN.Content == "OFF" || ReconnectBTN.Content == "Paused")
            {
                ReconnectBTN.Content = "ON";
            }
            else
            {
                ReconnectBTN.Content = "OFF";
            }
        }

        //Enable or disable the ethernet
        private async void EthernetToggle_Click(object sender, RoutedEventArgs e)
        {
            if (EnableDisableEthernet.Content == "Enabled")
            {
                EnableDisableEthernet.Content = "Working";
                SetEthernetState(false);
            }
            else if (EnableDisableEthernet.Content == "Disabled")
            {
                EnableDisableEthernet.Content = "Working";
                ProtectionToggle.Content = "Paused";
                pinging = false;
                _last15Results.Clear();
                SetEthernetState(true);

                await Task.Delay(8000);
                ProtectionToggle.Content = "ON";
                pinging = true;
            }
        }

        private void ChromeKill_Click(object sender, RoutedEventArgs e)
        {
            if ((bool)KillChromeToggle.IsChecked)
            {
                KillChromeToggle.Content = "ON";
            }
            else
            {
                KillChromeToggle.Content = "OFF";
            }
        }

        public static int GetEnabledEthernetAdapterCount()
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni =>
                    ni.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                    ni.OperationalStatus == OperationalStatus.Up)
                .Count();
        }

        public bool PingAddress(string address)
        {
            try
            {
                using (Ping ping = new Ping())
                {
                    PingReply reply = ping.Send(address, 100);
                    long pingValue = reply.RoundtripTime;

                    // Safe UI update
                    Dispatcher.Invoke(() =>
                    {
                        if (pingValue > 2)
                        {
                            PingUI.Text = $"{pingValue} ms";
                        }
                    });

                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }


        private void AddPingResult(bool result)
        {
            if (_last15Results.Count == 15) _last15Results.Dequeue();
            _last15Results.Enqueue(result);

            if (_last100Results.Count == 100) _last100Results.Dequeue();
            _last100Results.Enqueue(result);
        }

        private int CountPingLosses(Queue<bool> results)
        {
            return results.Count(r => !r);
        }

        public static string CheckEthernetStatus()
        {
            try
            {
                var adapters = NetworkInterface.GetAllNetworkInterfaces();
                var ethernetAdapter = adapters.FirstOrDefault(ni =>
                    ni.Name.Equals("Ethernet", StringComparison.OrdinalIgnoreCase) ||
                    ni.Description.Contains("Ethernet", StringComparison.OrdinalIgnoreCase));

                if (ethernetAdapter == null)
                    return "Disabled"; // Not found

                if (ethernetAdapter.OperationalStatus == OperationalStatus.Up)
                    return "Enabled";
                else
                    return "Disabled";
            }
            catch
            {
                return "Disabled";
            }
        }

        //Function to enable or disable
        public static void SetEthernetState(bool enable, string adapterName = "Ethernet")
        {
            string action = enable ? "enable" : "disable";

            ProcessStartInfo psi = new ProcessStartInfo
            {
                FileName = "netsh",
                Arguments = $"interface set interface \"{adapterName}\" {action}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas" // Runs with admin privileges
            };

            try
            {
                using (Process process = Process.Start(psi))
                {
                    process.WaitForExit();
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    if (process.ExitCode == 0)
                        Console.WriteLine($"Adapter '{adapterName}' {action}d successfully.");
                    else
                        Console.WriteLine($"Failed to {action} adapter '{adapterName}': {error}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }
    }
        public static void KillChrome()
        {
            try
            {
                Process[] chromeInstances = Process.GetProcessesByName("chrome");

                foreach (Process chrome in chromeInstances)
                {
                    chrome.Kill();
                    chrome.WaitForExit();
                }

                Console.WriteLine($"Killed {chromeInstances.Length} chrome.exe process(es).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error killing Chrome: " + ex.Message);
            }
        }

    }


}
