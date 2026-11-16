using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace LLMDuell
{
    /// <summary>Kennzahlen, Diagramm und Ausgabe eines Modells.</summary>
    public partial class ModelPanel : UserControl
    {
        AppModel app;
        ModelRun run;
        Brush sideBrush;
        Brush sideGradient;

        bool selecting;
        int shownModelsVersion = -1;
        string shownBadge;
        bool pulsing;
        bool thinkingExpanded;

        // Stand des Diagramms
        int chartSamples = -1;
        ChartScale chartScale;
        double? chartAverage;
        int chartTheme = -1;
        int chartGeneration = -1;

        // Stand der Ausgabe: Nur die letzte Zeile ändert sich beim Streamen, alle davor bleiben stehen.
        readonly FlowDocument document = new FlowDocument { PagePadding = new Thickness(0) };
        int shownGeneration = -1;
        int shownLength;
        int lastLineStart;
        bool lastLineInsideCode;

        public ModelPanel()
        {
            InitializeComponent();
            document.FontFamily = FontFamily;
            document.FontSize = 13.5;
            document.LineHeight = 20;
            document.SetResourceReference(FlowDocument.ForegroundProperty, "Text");
            OutputBox.Document = document;
        }

        internal void Bind(AppModel appModel, ModelRun modelRun)
        {
            app = appModel;
            run = modelRun;
            bool left = run.Side == Side.Left;
            sideBrush = (Brush)FindResource(left ? "LeftBrush" : "RightBrush");
            sideGradient = (Brush)FindResource(left ? "LeftGradient" : "RightGradient");
            var gradient = (LinearGradientBrush)sideGradient;

            LetterTile.Background = sideGradient;
            LetterText.Text = run.Letter;
            SpeedValue.Foreground = sideGradient;
            ChartAverage.Foreground = sideBrush;
            SessionAverage.Foreground = sideBrush;
            PlaceholderIcon.Foreground = sideBrush;
            Chart.SetColors(gradient.GradientStops[0].Color, gradient.GradientStops[1].Color);
        }

        /// <summary>Überträgt den aktuellen Zustand in die Oberfläche. Wird mehrmals pro Sekunde aufgerufen.</summary>
        internal void Refresh(double now, ChartScale scale)
        {
            RefreshHeader();

            GenerationStats stats = run.Stats;
            SpeedValue.Text = Format.Tps(run.DisplayTps);
            SpeedCaption.Text = stats != null ? "Gemessen von Ollama (ohne Ladezeit)"
                : run.IsActive ? "Live-Schätzung" : "Noch keine Messung";
            TokenValue.Text = Format.Tokens(run.DisplayOutputTokens);
            TokenPrompt.Text = Format.Tokens(stats?.PromptTokens);
            TokenTotal.Text = Format.Tokens(stats?.TotalTokens);

            FirstTokenValue.Text = Format.Seconds(run.TimeToFirstToken);
            LoadValue.Text = Format.Seconds(stats?.LoadSeconds);
            double? promptSpeed = stats?.PromptTokensPerSecond;
            PromptSpeedValue.Text = promptSpeed != null ? Format.Tps(promptSpeed.Value) + " tok/s" : "–";
            DurationValue.Text = Format.Seconds(run.Elapsed(now));

            RefreshChart(scale);
            RefreshOutput();

            SessionSummary.Text = run.SessionRuns + (run.SessionRuns == 1 ? " Lauf" : " Läufe")
                + " · " + Format.Tokens(run.SessionTotalTokens) + " Tokens gesamt";
            SessionDetail.Text = "(" + Format.Tokens(run.SessionPromptTokens) + " in / " + Format.Tokens(run.SessionOutputTokens) + " out)";
            SessionAverage.Text = run.SessionRuns > 0 ? "Ø " + Format.Tps(run.SessionAverageTps) + " tok/s" : "";
        }

        // MARK: Kopfzeile

        void RefreshHeader()
        {
            if (shownModelsVersion != app.ModelsVersion)
            {
                shownModelsVersion = app.ModelsVersion;
                selecting = true;
                ModelBox.ItemsSource = app.Models.Select(m => m.Name).ToList();
                selecting = false;
            }

            string name = run.ModelName;
            ModelBox.Tag = name.Length > 0 ? name : "Modell wählen";
            if (!Equals(ModelBox.SelectedItem, name))
            {
                selecting = true;
                ModelBox.SelectedItem = app.Model(name) != null ? name : null;
                selecting = false;
            }
            ModelBox.IsEnabled = !app.IsRunning && app.Models.Count > 0;
            ModelBox.Cursor = ModelBox.IsEnabled ? Cursors.Hand : null;

            OllamaModel model = app.Model(name);
            SetChip(ChipParams, ChipParamsText, model?.ParameterSize);
            SetChip(ChipQuant, ChipQuantText, model?.Quantization);
            SetChip(ChipSize, ChipSizeText, model?.SizeLabel);

            RefreshBadge();
        }

        static void SetChip(Border chip, TextBlock label, string text)
        {
            chip.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
            label.Text = text ?? "";
        }

        void OnModelSelected(object sender, SelectionChangedEventArgs e)
        {
            if (selecting || run == null) return;
            if (ModelBox.SelectedItem is string name) run.ModelName = name;
        }

        void RefreshBadge()
        {
            string key = run.Phase + "|" + Theme.Version;
            if (key != shownBadge)
            {
                shownBadge = key;
                string text, icon;
                Color color;
                switch (run.Phase)
                {
                    case Phase.Waiting: text = "Wartet"; icon = ""; color = Theme.ColorOf("Text2"); break;
                    case Phase.Loading: text = "Lädt Modell"; icon = ""; color = Rgb(0xF0, 0x8C, 0x1A); break;
                    case Phase.Thinking: text = "Denkt nach"; icon = ""; color = Rgb(0xA8, 0x55, 0xF7); break;
                    case Phase.Streaming: text = "Generiert"; icon = ""; color = ((SolidColorBrush)sideBrush).Color; break;
                    case Phase.Done: text = "Fertig"; icon = ""; color = Rgb(0x1F, 0xAD, 0x56); break;
                    case Phase.Cancelled: text = "Abgebrochen"; icon = ""; color = Theme.ColorOf("Text2"); break;
                    case Phase.Failed: text = "Fehler"; icon = ""; color = Rgb(0xE5, 0x48, 0x4D); break;
                    default: text = "Bereit"; icon = ""; color = Theme.ColorOf("Text2"); break;
                }
                var brush = new SolidColorBrush(color);
                PhaseText.Text = text;
                PhaseIcon.Text = icon;
                PhaseText.Foreground = brush;
                PhaseIcon.Foreground = brush;
                PhaseBadge.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(0x1F, color.R, color.G, color.B));
            }

            if (run.IsActive != pulsing)
            {
                pulsing = run.IsActive;
                PhaseIcon.BeginAnimation(OpacityProperty, pulsing
                    ? new DoubleAnimation(1, 0.35, TimeSpan.FromSeconds(0.7)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever }
                    : null);
            }
        }

        static Color Rgb(byte r, byte g, byte b) => System.Windows.Media.Color.FromRgb(r, g, b);

        // MARK: Diagramm

        void RefreshChart(ChartScale scale)
        {
            double? average = run.Stats?.TokensPerSecond;
            ChartAverage.Text = average != null ? "Ø " + Format.Tps(average.Value) + " tok/s" : "";

            if (chartSamples == run.Samples.Count && chartGeneration == run.Generation && chartTheme == Theme.Version
                && chartAverage == average && chartScale.MaxX == scale.MaxX && chartScale.MaxY == scale.MaxY) return;

            chartSamples = run.Samples.Count;
            chartGeneration = run.Generation;
            chartTheme = Theme.Version;
            chartAverage = average;
            chartScale = scale;
            Chart.Update(run.Samples, scale, average);
        }

        // MARK: Ausgabe

        void RefreshOutput()
        {
            bool atBottom = Scroll.VerticalOffset >= Scroll.ScrollableHeight - 40;
            bool changed = false;

            // Gedankengang
            string thinking = run.Thinking;
            bool hasThinking = thinking.Length > 0;
            bool thinkingNow = run.Phase == Phase.Thinking;
            ThinkingBox.Visibility = hasThinking ? Visibility.Visible : Visibility.Collapsed;
            if (hasThinking)
            {
                bool open = thinkingExpanded || thinkingNow;
                ThinkingTitle.Text = thinkingNow ? "Denkt nach …" : "Gedankengang";
                ThinkingCount.Text = thinking.Length.ToString("N0") + " Zeichen";
                ThinkingChevron.Text = open ? "" : "";
                ThinkingText.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
                if (open && ThinkingText.Text.Length != thinking.Length)
                {
                    ThinkingText.Text = thinking;
                    changed = true;
                }
            }
            else
            {
                thinkingExpanded = false;
                if (ThinkingText.Text.Length > 0) ThinkingText.Text = "";
            }

            // Antwort
            string output = run.Output;
            if (shownGeneration != run.Generation || output.Length < shownLength)
            {
                shownGeneration = run.Generation;
                document.Blocks.Clear();
                shownLength = 0;
                lastLineStart = 0;
                lastLineInsideCode = false;
            }
            if (output.Length != shownLength)
            {
                AppendOutput(output);
                changed = true;
            }
            bool hasOutput = output.Length > 0;
            OutputBox.Visibility = hasOutput ? Visibility.Visible : Visibility.Collapsed;

            // Platzhalter und Fehler
            bool failed = run.Phase == Phase.Failed;
            Placeholder.Visibility = hasOutput || failed ? Visibility.Collapsed : Visibility.Visible;
            if (Placeholder.Visibility == Visibility.Visible)
            {
                switch (run.Phase)
                {
                    case Phase.Loading: SetPlaceholder("", "Warte auf das erste Token …"); break;
                    case Phase.Waiting: SetPlaceholder("", "Startet, sobald Modell A fertig ist"); break;
                    case Phase.Cancelled: SetPlaceholder("", "Abgebrochen"); break;
                    default: SetPlaceholder("", "Die Antwort erscheint hier"); break;
                }
            }
            ErrorBox.Visibility = failed ? Visibility.Visible : Visibility.Collapsed;
            ErrorBox.Margin = new Thickness(0, hasOutput ? 12 : 0, 0, 0);
            if (failed && ErrorText.Text != run.ErrorMessage) ErrorText.Text = run.ErrorMessage;

            // Rahmen leuchtet, solange das Modell arbeitet
            if (run.IsActive) OutputBorder.BorderBrush = sideGradient;
            else OutputBorder.SetResourceReference(Border.BorderBrushProperty, "CardBorder");

            // Mitlaufen, außer man hat selbst nach oben gescrollt
            if (changed && run.IsActive && atBottom) Scroll.ScrollToEnd();
        }

        void AppendOutput(string output)
        {
            if (shownLength > 0 && document.Blocks.LastBlock != null) document.Blocks.Remove(document.Blocks.LastBlock);

            int position = lastLineStart;
            bool insideCode = lastLineInsideCode;
            while (true)
            {
                int newline = output.IndexOf('\n', position);
                string line = newline < 0 ? output.Substring(position) : output.Substring(position, newline - position);
                lastLineStart = position;
                lastLineInsideCode = insideCode;
                document.Blocks.Add(Markdown.Line(line, ref insideCode));
                if (newline < 0) break;
                position = newline + 1;
            }
            shownLength = output.Length;
        }

        void SetPlaceholder(string icon, string text)
        {
            PlaceholderIcon.Text = icon;
            PlaceholderText.Text = text;
        }

        internal void OpenModelList() => ModelBox.IsDropDownOpen = true;

        void OnToggleThinking(object sender, RoutedEventArgs e) => thinkingExpanded = !thinkingExpanded;

        /// <summary>Die Textfelder in der Ausgabe würden das Mausrad sonst verschlucken.</summary>
        void OnScrollWheel(object sender, MouseWheelEventArgs e)
        {
            Scroll.ScrollToVerticalOffset(Scroll.VerticalOffset - e.Delta * 0.6);
            e.Handled = true;
        }
    }
}
