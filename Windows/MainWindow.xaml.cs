using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace LLMDuell
{
    /// <summary>
    /// Hauptfenster. Die Oberfläche wird nicht über Datenbindung aktualisiert, sondern rund 30-mal pro
    /// Sekunde aus dem Modell gelesen: Das hält die Anzeige auch bei sehr schnellen Modellen flüssig.
    /// </summary>
    public partial class MainWindow : Window
    {
        static readonly Color Green = Color.FromRgb(0x1F, 0xAD, 0x56);
        static readonly Color Orange = Color.FromRgb(0xF0, 0x8C, 0x1A);
        static readonly Color Red = Color.FromRgb(0xE5, 0x48, 0x4D);

        readonly AppModel app = new AppModel();
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };

        bool syncing;
        string shownVerdict;
        string connectedHost;
        double lastSave;
        double optionsClosedAt = -1;

        public MainWindow()
        {
            InitializeComponent();

            // Auf kleinen Bildschirmen nicht über den Rand hinausragen.
            Rect area = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(Width, area.Width - 40));
            Height = Math.Max(MinHeight, Math.Min(Height, area.Height - 40));

            LeftPanel.Bind(app, app.Left);
            RightPanel.Bind(app, app.Right);
            LoadSettingsIntoControls();

            timer.Tick += (sender, e) => Tick();
            timer.Start();
            RefreshUi();

            SourceInitialized += (sender, e) => Theme.ApplyTitleBar(this);
            Loaded += OnLoaded;
            Closing += OnClosing;
        }

        async void OnLoaded(object sender, RoutedEventArgs e)
        {
            PromptBox.Focus();
            DebugSnapshot.StartIfRequested(this, app);
            await ConnectAsync();
        }

        void OnClosing(object sender, CancelEventArgs e)
        {
            app.Stop();
            app.Settings.SaveIfChanged();
        }

        void Tick()
        {
            app.Left.Pump();
            app.Right.Pump();
            RefreshUi();

            if (Clock.Now - lastSave > 1)
            {
                lastSave = Clock.Now;
                app.Settings.SaveIfChanged();
            }
        }

        async System.Threading.Tasks.Task ConnectAsync()
        {
            connectedHost = app.Settings.Host;
            await app.RefreshModelsAsync();
        }

        internal void SetPrompt(string text)
        {
            PromptBox.Text = text;
            PromptBox.CaretIndex = text.Length;
        }

        internal void ShowOptions() => OptionsPopup.IsOpen = true;

        internal void ShowModelList() => LeftPanel.OpenModelList();

        // MARK: Anzeige

        void RefreshUi()
        {
            bool running = app.IsRunning;

            RefreshConnection();
            SwapButton.IsEnabled = !running;
            StartButton.IsEnabled = app.CanStart;
            StartButton.Visibility = running ? Visibility.Collapsed : Visibility.Visible;
            StopButton.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
            ModeSequential.IsEnabled = ModeParallel.IsEnabled = !running;
            OptionsContent.IsEnabled = !running;
            PromptHint.Visibility = PromptBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            double now = Clock.Now;
            var scale = new ChartScale(app.Left, app.Right);
            LeftPanel.Refresh(now, scale);
            RightPanel.Refresh(now, scale);

            RefreshComparison();
        }

        void RefreshConnection()
        {
            Color color;
            string text, tip;
            switch (app.Connection)
            {
                case ConnectionState.Connected:
                    int count = app.Models.Count;
                    color = Green;
                    text = ("Ollama " + app.ConnectionDetail).Trim() + " · " + count + (count == 1 ? " Modell" : " Modelle");
                    tip = app.Settings.Host;
                    break;
                case ConnectionState.Failed:
                    color = Red;
                    text = "Ollama nicht erreichbar";
                    tip = app.ConnectionDetail;
                    break;
                default:
                    color = Orange;
                    text = "Verbinde …";
                    tip = app.Settings.Host;
                    break;
            }
            if (ConnectionText.Text == text && Equals(ConnectionPill.ToolTip, tip)) return;
            ConnectionText.Text = text;
            ConnectionPill.ToolTip = tip;
            ConnectionDot.Fill = new SolidColorBrush(color);
        }

        void RefreshComparison()
        {
            ModelRun a = app.Left, b = app.Right;
            GenerationStats sa = a.Stats, sb = b.Stats;

            CompareLeftSpeed.Text = Format.Tps(a.DisplayTps);
            CompareRightSpeed.Text = Format.Tps(b.DisplayTps);

            bool hasData = a.DisplayTps > 0 || b.DisplayTps > 0;
            DuelBar.Visibility = hasData ? Visibility.Visible : Visibility.Collapsed;
            if (hasData)
            {
                double share = a.DisplayTps / (a.DisplayTps + b.DisplayTps);
                DuelLeft.Width = new GridLength(Math.Max(share, 0.0001), GridUnitType.Star);
                DuelRight.Width = new GridLength(Math.Max(1 - share, 0.0001), GridUnitType.Star);
            }

            bool both = sa != null && sb != null;
            CompareRow.Visibility = both ? Visibility.Visible : Visibility.Collapsed;
            if (both)
            {
                CompareFirstA.Text = Format.Seconds(sa.TimeToFirstToken);
                CompareFirstB.Text = Format.Seconds(sb.TimeToFirstToken);
                CompareOutputA.Text = Format.Tokens(sa.OutputTokens);
                CompareOutputB.Text = Format.Tokens(sb.OutputTokens);
                CompareTotalA.Text = Format.Tokens(sa.TotalTokens);
                CompareTotalB.Text = Format.Tokens(sb.TotalTokens);
                CompareDurationA.Text = Format.Seconds(sa.TotalSeconds);
                CompareDurationB.Text = Format.Seconds(sb.TotalSeconds);
            }

            RefreshVerdict(a, b, sa, sb);
        }

        void RefreshVerdict(ModelRun a, ModelRun b, GenerationStats sa, GenerationStats sb)
        {
            // Erst den Inhalt bestimmen, dann nur bei einer Änderung neu aufbauen.
            string winnerName = null, message;
            Side winnerSide = Side.Left;
            bool emphasized = false;

            if (sa != null && sb != null && sa.TokensPerSecond > 0 && sb.TokensPerSecond > 0)
            {
                emphasized = true;
                double ratio = Math.Max(sa.TokensPerSecond, sb.TokensPerSecond) / Math.Min(sa.TokensPerSecond, sb.TokensPerSecond);
                if (ratio < 1.03)
                {
                    message = "Praktisch gleich schnell";
                }
                else
                {
                    ModelRun winner = sa.TokensPerSecond >= sb.TokensPerSecond ? a : b;
                    winnerSide = winner.Side;
                    winnerName = a.ModelName == b.ModelName ? "Modell " + winner.Letter : winner.ModelName;
                    message = "ist " + ratio.ToString("F2", CultureInfo.CurrentCulture) + "× schneller";
                }
            }
            else if (app.IsRunning)
            {
                message = "Vergleich läuft …";
            }
            else if (app.Connection == ConnectionState.Failed)
            {
                message = app.ConnectionDetail;
            }
            else if (app.Connection == ConnectionState.Connected && app.Models.Count == 0)
            {
                message = "Noch kein Modell installiert – z. B. mit „ollama pull llama3.2“ laden.";
            }
            else if (app.Connection == ConnectionState.Connected && app.Models.Count < 2)
            {
                message = "Nur ein Modell installiert – weitere z. B. mit „ollama pull llama3.2“ laden.";
            }
            else
            {
                message = "Wähle zwei Modelle und starte einen Vergleich.";
            }

            string key = winnerSide + "|" + winnerName + "|" + message + "|" + emphasized;
            if (key == shownVerdict) return;
            shownVerdict = key;

            Verdict.Inlines.Clear();
            Verdict.FontWeight = emphasized ? FontWeights.SemiBold : FontWeights.Normal;
            Verdict.FontSize = emphasized ? 15 : 13.5;
            Verdict.SetResourceReference(TextBlock.ForegroundProperty, emphasized ? "Text" : "Text2");
            if (winnerName != null)
            {
                var brush = (Brush)FindResource(winnerSide == Side.Left ? "LeftBrush" : "RightBrush");
                Verdict.Inlines.Add(new Run(" ") { FontFamily = (FontFamily)FindResource("IconFont"), FontSize = 13, Foreground = brush });
                Verdict.Inlines.Add(new Run(winnerName + " ") { Foreground = brush });
            }
            Verdict.Inlines.Add(new Run(message));
        }

        // MARK: Aktionen

        void OnStart(object sender, RoutedEventArgs e) => app.Start();

        void OnStop(object sender, RoutedEventArgs e) => app.Stop();

        void OnSwap(object sender, RoutedEventArgs e) => app.SwapModels();

        async void OnRefresh(object sender, RoutedEventArgs e) => await ConnectAsync();

        void OnPromptChanged(object sender, TextChangedEventArgs e) => app.Prompt = PromptBox.Text;

        void OnPromptFocusChanged(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (PromptBox.IsKeyboardFocused) PromptBorder.BorderBrush = (Brush)FindResource("DuelGradient");
            else PromptBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorder");
        }

        /// <summary>Eingabetaste startet den Vergleich, Umschalt+Eingabe fügt eine neue Zeile ein.</summary>
        void OnPromptKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter || Keyboard.Modifiers != ModifierKeys.None) return;
            e.Handled = true;
            app.Start();
        }

        async void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            ModifierKeys modifiers = Keyboard.Modifiers;
            if (e.Key == Key.Escape)
            {
                if (OptionsPopup.IsOpen) OptionsPopup.IsOpen = false;
                else if (app.IsRunning) app.Stop();
                else return;
                e.Handled = true;
            }
            else if (e.Key == Key.Enter && modifiers == ModifierKeys.Control)
            {
                e.Handled = true;
                app.Start();
            }
            else if (e.Key == Key.T && modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                e.Handled = true;
                app.SwapModels();
            }
            else if (e.Key == Key.F5 || (e.Key == Key.R && modifiers == ModifierKeys.Control))
            {
                e.Handled = true;
                await ConnectAsync();
            }
        }

        // MARK: Optionen

        void LoadSettingsIntoControls()
        {
            Settings settings = app.Settings;
            syncing = true;
            HostBox.Text = settings.Host;
            TemperatureSlider.Value = settings.Temperature;
            TemperatureValue.Text = settings.Temperature.ToString("F2", CultureInfo.CurrentCulture);
            SeedCheck.IsChecked = settings.UseSeed;
            SeedBox.Text = settings.Seed.ToString(CultureInfo.InvariantCulture);
            SeedBox.Visibility = settings.UseSeed ? Visibility.Visible : Visibility.Hidden;
            SystemPromptBox.Text = settings.SystemPrompt;
            SyncModeControls();
            syncing = false;
        }

        void SyncModeControls()
        {
            bool sequential = app.Settings.Mode == RunMode.Sequential;
            ModeSequential.IsChecked = OptionSequential.IsChecked = sequential;
            ModeParallel.IsChecked = OptionParallel.IsChecked = !sequential;
        }

        void OnToggleOptions(object sender, RoutedEventArgs e)
        {
            // Der Klick auf den Knopf schließt ein offenes Popup bereits von selbst – dann nicht sofort wieder öffnen.
            if (Clock.Now - optionsClosedAt < 0.25) return;
            OptionsPopup.IsOpen = !OptionsPopup.IsOpen;
        }

        async void OnOptionsClosed(object sender, EventArgs e)
        {
            optionsClosedAt = Clock.Now;
            if (ApplyHost()) await ConnectAsync();
        }

        /// <summary>Übernimmt die Adresse aus dem Feld; meldet, ob neu verbunden werden muss.</summary>
        bool ApplyHost()
        {
            string host = HostBox.Text.Trim();
            if (host.Length == 0)
            {
                host = Settings.DefaultHost;
                HostBox.Text = host;
            }
            app.Settings.Host = host;
            return host != connectedHost;
        }

        async void OnConnect(object sender, RoutedEventArgs e)
        {
            ApplyHost();
            await ConnectAsync();
        }

        async void OnHostKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter) return;
            e.Handled = true;
            ApplyHost();
            await ConnectAsync();
        }

        void OnModeChanged(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            bool parallel = sender == ModeParallel || sender == OptionParallel;
            app.Settings.Mode = parallel ? RunMode.Parallel : RunMode.Sequential;
            syncing = true;
            SyncModeControls();
            syncing = false;
        }

        void OnTemperatureChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (syncing) return;
            double value = Math.Round(e.NewValue, 2);
            app.Settings.Temperature = value;
            TemperatureValue.Text = value.ToString("F2", CultureInfo.CurrentCulture);
        }

        void OnSeedToggled(object sender, RoutedEventArgs e)
        {
            if (syncing) return;
            bool on = SeedCheck.IsChecked == true;
            app.Settings.UseSeed = on;
            SeedBox.Visibility = on ? Visibility.Visible : Visibility.Hidden;
        }

        void OnSeedChanged(object sender, TextChangedEventArgs e)
        {
            if (syncing) return;
            if (int.TryParse(SeedBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed))
                app.Settings.Seed = seed;
        }

        void OnSystemPromptChanged(object sender, TextChangedEventArgs e)
        {
            if (syncing) return;
            app.Settings.SystemPrompt = SystemPromptBox.Text;
        }

        void OnResetSession(object sender, RoutedEventArgs e) => app.ResetSession();
    }
}
