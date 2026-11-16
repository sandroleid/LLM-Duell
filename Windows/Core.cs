using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace LLMDuell
{
    /// <summary>Monotone Uhr in Sekunden; unabhängig von Korrekturen der Systemzeit.</summary>
    static class Clock
    {
        static readonly Stopwatch Watch = Stopwatch.StartNew();

        public static double Now => Watch.Elapsed.TotalSeconds;
    }

    static class Format
    {
        public static string Tps(double value) => value.ToString("F1", CultureInfo.CurrentCulture);

        public static string Seconds(double? value)
        {
            if (value == null) return "–";
            double v = value.Value;
            if (v < 1) return (int)Math.Round(v * 1000) + " ms";
            return v.ToString(v < 10 ? "F2" : "F1", CultureInfo.CurrentCulture) + " s";
        }

        public static string Tokens(int? value) =>
            value == null ? "–" : value.Value.ToString("N0", CultureInfo.CurrentCulture);

        public static string Bytes(long bytes)
        {
            if (bytes >= 1_000_000_000) return (bytes / 1e9).ToString("0.#", CultureInfo.CurrentCulture) + " GB";
            if (bytes >= 1_000_000) return (bytes / 1e6).ToString("0", CultureInfo.CurrentCulture) + " MB";
            return (bytes / 1e3).ToString("0", CultureInfo.CurrentCulture) + " KB";
        }
    }

    enum RunMode
    {
        Sequential,
        Parallel,
    }

    /// <summary>Einstellungen, gespeichert unter <c>%APPDATA%\LLM Duell\settings.json</c>.</summary>
    sealed class Settings
    {
        public const string DefaultHost = "http://localhost:11434";

        string host = DefaultHost;
        RunMode mode = RunMode.Sequential;
        double temperature = 0.7;
        bool useSeed = true;
        int seed = 42;
        string systemPrompt = "";
        string modelLeft = "";
        string modelRight = "";

        bool dirty;

        public string Host { get => host; set => Set(ref host, value ?? ""); }
        public RunMode Mode { get => mode; set => Set(ref mode, value); }
        public double Temperature { get => temperature; set => Set(ref temperature, value); }
        public bool UseSeed { get => useSeed; set => Set(ref useSeed, value); }
        public int Seed { get => seed; set => Set(ref seed, value); }
        public string SystemPrompt { get => systemPrompt; set => Set(ref systemPrompt, value ?? ""); }
        public string ModelLeft { get => modelLeft; set => Set(ref modelLeft, value ?? ""); }
        public string ModelRight { get => modelRight; set => Set(ref modelRight, value ?? ""); }

        void Set<T>(ref T field, T value)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            dirty = true;
        }

        static string FilePath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "LLM Duell", "settings.json");

        public static Settings Load()
        {
            var settings = new Settings();
            try
            {
                if (File.Exists(FilePath) && Json.Parse(File.ReadAllText(FilePath)) is Dictionary<string, object> json)
                {
                    settings.host = Json.String(json, "host") ?? settings.host;
                    settings.mode = Json.String(json, "mode") == "parallel" ? RunMode.Parallel : RunMode.Sequential;
                    if (json.TryGetValue("temperature", out object t) && t != null)
                        settings.temperature = Math.Max(0, Math.Min(2, Convert.ToDouble(t, CultureInfo.InvariantCulture)));
                    if (json.TryGetValue("useSeed", out object u) && u is bool flag) settings.useSeed = flag;
                    settings.seed = (int)(Json.Long(json, "seed") ?? settings.seed);
                    settings.systemPrompt = Json.String(json, "systemPrompt") ?? "";
                    settings.modelLeft = Json.String(json, "modelLeft") ?? "";
                    settings.modelRight = Json.String(json, "modelRight") ?? "";
                }
            }
            catch (Exception)
            {
                // Eine beschädigte Datei soll den Start nicht verhindern – dann gelten die Standardwerte.
            }
            return settings;
        }

        public void SaveIfChanged()
        {
            if (!dirty) return;
            dirty = false;
            try
            {
                var json = new Dictionary<string, object>
                {
                    { "host", host },
                    { "mode", mode == RunMode.Parallel ? "parallel" : "sequential" },
                    { "temperature", temperature },
                    { "useSeed", useSeed },
                    { "seed", seed },
                    { "systemPrompt", systemPrompt },
                    { "modelLeft", modelLeft },
                    { "modelRight", modelRight },
                };
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, Json.Serialize(json));
            }
            catch (Exception)
            {
                // Nicht speicherbare Einstellungen sind kein Grund, die App zu unterbrechen.
            }
        }
    }
}
