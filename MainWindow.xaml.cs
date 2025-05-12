using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls;

namespace Upgraded_Pinger
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private CancellationTokenSource _pingCancellationTokenSource;
        private bool _isPinging;
        private DispatcherTimer _timeoutUpdateTimer;
        private const int TimeoutWindowSeconds = 3;
        private int _timeoutThreshold = 6;
        private bool _hasPlayedSound = false;
        private System.Media.SoundPlayer _loopPlayer = null;
        private bool _isPingingStopped = false;
        private int _frozenTimeoutCount = 0;
        private TimerManager _timerManager;

        public MainWindow()
        {
            InitializeComponent();

            PingTargets = new List<PingTarget>
            {
                new PingTarget { Name = "Google:", IpAddress = "8.8.8.8", PingValue = "-" },
                new PingTarget { Name = "Steam:", IpAddress = "store.steampowered.com", PingValue = "-" },
                new PingTarget { Name = "xbox:", IpAddress = "20.236.44.162", PingValue = "-" },
                new PingTarget { Name = "Riot:", IpAddress = "104.16.1.5", PingValue = "-" },
                new PingTarget { Name = "Router:", IpAddress = "192.168.1.1", PingValue = "-" }
            };

            _timerManager = new TimerManager(TimerToBeBack);

            _timeoutUpdateTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _timeoutUpdateTimer.Tick += UpdateTimeoutProgress;

            DataContext = this;
        }

        public List<PingTarget> PingTargets { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        private void StartPinging_Click(object sender, RoutedEventArgs e)
        {
            StartPinging();
        }

        private void StartPinging()
        {
            _isPingingStopped = false;
            _hasPlayedSound = false;
            _frozenTimeoutCount = 0;
            _timeoutUpdateTimer.Start();

            if (_loopPlayer != null)
            {
                _loopPlayer.Stop();
                _loopPlayer = null;
            }

            if (_isPinging) return;

            _isPinging = true;
            _pingCancellationTokenSource = new CancellationTokenSource();

            try
            {
                var tasks = new List<Task>();
                for (int i = 0; i < PingTargets.Count; i++)
                {
                    int index = i;
                    int delay = i * 200;
                    tasks.Add(PingContinuouslyAsync(index, delay, _pingCancellationTokenSource.Token));
                }
            }
            catch (TaskCanceledException)
            {
                // Expected when stopping
            }
        }

        private async Task PingContinuouslyAsync(int targetIndex, int initialDelay, CancellationToken cancellationToken)
        {
            var target = PingTargets[targetIndex];
            var ping = new Ping();

            await Task.Delay(initialDelay, cancellationToken);

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var reply = await ping.SendPingAsync(target.IpAddress, 1000);

                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        if (reply.Status == IPStatus.Success)
                        {
                            target.PingValue = reply.RoundtripTime.ToString();
                            target.StatusColor = Brushes.Green;
                        }
                        else
                        {
                            target.PingValue = "Timeout";
                            target.StatusColor = Brushes.Red;
                            target.TimeoutTimestamps.Enqueue(DateTime.Now);
                        }
                        OnPropertyChanged(nameof(PingTargets));
                    });
                }
                catch (PingException)
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        target.PingValue = "Error";
                        target.StatusColor = Brushes.Red;
                        OnPropertyChanged(nameof(PingTargets));
                    });
                }
                catch (Exception ex) when (ex is OperationCanceledException || ex is TaskCanceledException)
                {
                    break;
                }

                await Task.Delay(1000 - (initialDelay % 1000), cancellationToken);
            }
        }

        private void StopPinging()
        {
            _pingCancellationTokenSource?.Cancel();
            _isPinging = false;
        }

        private void StopPinging_Click(object sender, RoutedEventArgs e)
        {
            StopPinging();
        }

        private void Window_Closed(object sender, EventArgs e)
        {
            StopPinging();
            _timeoutUpdateTimer.Stop();
            _timerManager?.StopTimer();
            _loopPlayer?.Stop();
        }

        private void UpdateTimeoutProgress(object sender, EventArgs e)
        {
            if (_isPingingStopped)
            {
                TimeoutProgressBar.Value = _frozenTimeoutCount;
                TimeoutLabel.Text = $"{_frozenTimeoutCount} timeouts (pinging stopped)";
                return;
            }

            // Update threshold
            if (int.TryParse(Tolerance_Input.Text, out int threshold))
            {
                _timeoutThreshold = threshold;
            }
            else
            {
                _timeoutThreshold = 6;
                Tolerance_Input.Text = "6";
            }

            // Count adapters
            adapters_Count.Text = $"Number of active Ethernet adapters: {GetNumberOfActiveEthernetAdapters()}";

            // Count timeouts
            int timeoutCount = 0;
            DateTime now = DateTime.Now;
            DateTime cutoffTime = now.AddSeconds(-TimeoutWindowSeconds);

            foreach (var target in PingTargets)
            {
                while (target.TimeoutTimestamps.TryPeek(out DateTime oldest) && oldest < cutoffTime)
                {
                    target.TimeoutTimestamps.TryDequeue(out _);
                }

                timeoutCount += target.TimeoutTimestamps.Count;
            }

            TimeoutProgressBar.Value = timeoutCount;
            TimeoutProgressBar.Maximum = PingTargets.Count * TimeoutWindowSeconds;
            TimeoutLabel.Text = $"{timeoutCount} timeouts in last {TimeoutWindowSeconds}s";

            if (timeoutCount > _timeoutThreshold)
            {
                ((MainWindow)Application.Current.MainWindow).ToggleEthernetAdapter("Ethernet", enable: false);
                if (!_hasPlayedSound)
                {
                    _hasPlayedSound = true;
                    _frozenTimeoutCount = timeoutCount;
                    StopPinging();
                    _isPingingStopped = true;
                    _timerManager.StartTimer();
                    PlayAlertThenStartLoop();
                    if (killChrome.IsChecked == true)
                    {
                        CloseChrome();
                    }

                }
            }
        }

        private async void PlayAlertThenStartLoop()
        {
            var alertPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "alert.wav");
            var loopPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "loop.wav");

            if (File.Exists(alertPath))
            {
                using (var alertPlayer = new System.Media.SoundPlayer(alertPath))
                {
                    await Task.Run(() => alertPlayer.PlaySync());
                }
            }

            if (File.Exists(loopPath))
            {
                _loopPlayer = new System.Media.SoundPlayer(loopPath);
                _loopPlayer.PlayLooping();
            }
        }

        private void EnableEthernet1_Click(object sender, RoutedEventArgs e)
        {
            ToggleEthernetAdapter("Ethernet", enable: true);
        }

        private void DisableEthernet1_Click(object sender, RoutedEventArgs e)
        {
            ToggleEthernetAdapter("Ethernet", enable: false);
        }

        private void ToggleEthernetAdapter(string adapterName, bool enable)
        {
            try
            {
                var query = new SelectQuery($"SELECT * FROM Win32_NetworkAdapter WHERE NetConnectionID = '{adapterName}'");
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject adapter in searcher.Get())
                    {
                        adapter.InvokeMethod(enable ? "Enable" : "Disable", null);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to {(enable ? "enable" : "disable")} adapter: {ex.Message}");
            }
        }

        public static int GetNumberOfActiveEthernetAdapters()
        {
            int activeEthernetCount = 0;
            NetworkInterface[] interfaces = NetworkInterface.GetAllNetworkInterfaces();

            foreach (NetworkInterface adapter in interfaces)
            {
                if (adapter.NetworkInterfaceType == NetworkInterfaceType.Ethernet &&
                    adapter.OperationalStatus == OperationalStatus.Up)
                {
                    activeEthernetCount++;
                }
            }

            return activeEthernetCount;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private class TimerManager
        {
            private readonly ProgressBar _progressBar;
            private readonly TextBlock _textBlock;
            private DispatcherTimer _timer;
            private int _secondsRemaining;
            private const int TotalSeconds = 120;

            public TimerManager(ProgressBar progressBar)
            {
                _progressBar = progressBar;
                InitializeTimer();
            }

            private void InitializeTimer()
            {
                _progressBar.Value = 0;
                _progressBar.Maximum = TotalSeconds;
                _secondsRemaining = TotalSeconds;
            }

            public void StartTimer()
            {
                if (_timer == null)
                {
                    _timer = new DispatcherTimer();
                    _timer.Interval = TimeSpan.FromSeconds(1);
                    _timer.Tick += Timer_Tick;
                }

                if (!_timer.IsEnabled)
                {
                    InitializeTimer();
                    _timer.Start();
                }
            }

            //We reached the 120 sec here
            public void StopTimer()
            {
                if (Application.Current?.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.StartPinging();
                }
                _timer?.Stop();
            }

            private void Timer_Tick(object sender, EventArgs e)
            {
                _secondsRemaining--;
                _progressBar.Value = TotalSeconds - _secondsRemaining;

                TimeSpan time = TimeSpan.FromSeconds(_secondsRemaining);

                //Enabling ethernet based on timer
                if (_secondsRemaining <= 3)
                {
                    ((MainWindow)Application.Current.MainWindow).ToggleEthernetAdapter("Ethernet", enable: true);
                }

                if (_secondsRemaining <= 0)
                {
                    StopTimer();
                }
            }
        }

        public static void CloseChrome()
        {
            try
            {
                Process[] chromeProcesses = Process.GetProcessesByName("chrome");

                if (chromeProcesses.Length == 0)
                {
                    Console.WriteLine("No Chrome process found.");
                    return;
                }

                foreach (Process process in chromeProcesses)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            process.Kill();
                            // Optional: wait for it to close with timeout
                            process.WaitForExit(5000); // Wait up to 5 seconds
                            Console.WriteLine("Chrome process terminated.");
                        }
                    }
                    catch (Win32Exception ex)
                    {
                        Console.WriteLine($"Could not terminate process (Win32): {ex.Message}");
                    }
                    catch (InvalidOperationException ex)
                    {
                        Console.WriteLine($"Process already exited: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error terminating process: {ex.Message}");
                    }
                    finally
                    {
                        process.Dispose();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unexpected error in CloseChrome: {ex.Message}");
            }
        }
    }

    public class PingTarget : INotifyPropertyChanged
    {
        public PingTarget()
        {
            TimeoutTimestamps = new ConcurrentQueue<DateTime>();
        }

        public ConcurrentQueue<DateTime> TimeoutTimestamps { get; }

        private string _name;
        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        private string _ipAddress;
        public string IpAddress
        {
            get => _ipAddress;
            set { _ipAddress = value; OnPropertyChanged(nameof(IpAddress)); }
        }

        private string _pingValue = "-";
        public string PingValue
        {
            get => _pingValue;
            set { _pingValue = value; OnPropertyChanged(nameof(PingValue)); }
        }

        private Brush _statusColor = Brushes.Gray;
        public Brush StatusColor
        {
            get => _statusColor;
            set { _statusColor = value; OnPropertyChanged(nameof(StatusColor)); }
        }

        private bool _isEnabled = true;
        public bool IsEnabled
        {
            get => _isEnabled;
            set { _isEnabled = value; OnPropertyChanged(nameof(IsEnabled)); }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}