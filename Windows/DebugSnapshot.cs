using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace LLMDuell
{
    /// <summary>
    /// Für automatische Tests: startet per Umgebungsvariablen einen Lauf, speichert Bilder des Fensters
    /// und schreibt die Messwerte in eine Datei. Ohne <c>LLMDUELL_SNAPSHOT_DIR</c> passiert nichts.
    ///
    ///   LLMDUELL_SNAPSHOT_DIR   Zielordner für shot0.png … und result.txt
    ///   LLMDUELL_PROMPT         Prompt, mit dem der Vergleich gestartet wird
    ///   LLMDUELL_SHOTS          Anzahl der Bilder (Standard 5)
    ///   LLMDUELL_INTERVAL       Sekunden zwischen den Bildern (Standard 3)
    ///   LLMDUELL_WAIT           Nach dem letzten Bild warten, bis der Lauf fertig ist (höchstens so viele Sekunden)
    ///   LLMDUELL_OPEN           „options“ oder „models“ klappt das jeweilige Popup auf
    ///   LLMDUELL_APPEARANCE     „dark“ oder „light“ (wird in Theme.cs ausgewertet)
    ///   LLMDUELL_QUIT           „1“ beendet die App am Ende
    /// </summary>
    static class DebugSnapshot
    {
        public static async void StartIfRequested(MainWindow window, AppModel app)
        {
            string directory = Environment.GetEnvironmentVariable("LLMDUELL_SNAPSHOT_DIR");
            if (string.IsNullOrEmpty(directory)) return;
            Directory.CreateDirectory(directory);

            int shots = (int)Number("LLMDUELL_SHOTS", 5);
            double interval = Number("LLMDUELL_INTERVAL", 3);
            double wait = Number("LLMDUELL_WAIT", 0);

            await Task.Delay(1500);
            string prompt = Environment.GetEnvironmentVariable("LLMDUELL_PROMPT");
            if (!string.IsNullOrEmpty(prompt))
            {
                window.SetPrompt(prompt);
                app.Start();
            }
            string open = Environment.GetEnvironmentVariable("LLMDUELL_OPEN");
            if (open == "options") window.ShowOptions();
            if (open == "models") window.ShowModelList();

            for (int index = 0; index < shots; index++)
            {
                await Task.Delay(TimeSpan.FromSeconds(index == 0 ? 0.5 : interval));
                Save(window, Path.Combine(directory, "shot" + index + ".png"));
            }

            double deadline = Clock.Now + wait;
            while (app.IsRunning && Clock.Now < deadline) await Task.Delay(200);
            if (wait > 0)
            {
                await Task.Delay(300);
                Save(window, Path.Combine(directory, "final.png"));
            }

            File.WriteAllText(Path.Combine(directory, "result.txt"), Describe(app), Encoding.UTF8);
            if (Environment.GetEnvironmentVariable("LLMDUELL_QUIT") == "1") Application.Current.Shutdown();
        }

        static double Number(string variable, double fallback) =>
            double.TryParse(Environment.GetEnvironmentVariable(variable), NumberStyles.Float, CultureInfo.InvariantCulture, out double value)
                ? value : fallback;

        static void Save(Window window, string path)
        {
            var root = (FrameworkElement)window.Content;
            const double scale = 2;
            var bitmap = new RenderTargetBitmap(
                (int)(root.ActualWidth * scale), (int)(root.ActualHeight * scale), 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(root);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (FileStream stream = File.Create(path)) encoder.Save(stream);
        }

        static string Describe(AppModel app)
        {
            var text = new StringBuilder();
            text.AppendLine("connection: " + app.Connection + " " + app.ConnectionDetail);
            text.AppendLine("models: " + app.Models.Count);
            foreach (ModelRun run in new[] { app.Left, app.Right })
            {
                GenerationStats stats = run.Stats;
                text.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "{0}: model={1} phase={2} output_tokens={3} prompt_tokens={4} tps={5:F2} ttft={6:F3} samples={7} chars={8} thinking_chars={9} error={10}",
                    run.Side.ToString().ToLowerInvariant(), run.ModelName, run.Phase,
                    stats?.OutputTokens ?? -1, stats?.PromptTokens ?? -1, stats?.TokensPerSecond ?? 0,
                    run.TimeToFirstToken ?? -1, run.Samples.Count, run.Output.Length, run.Thinking.Length, run.ErrorMessage));
            }
            return text.ToString();
        }
    }
}
