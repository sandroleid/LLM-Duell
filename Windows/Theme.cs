using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace LLMDuell
{
    /// <summary>Heller und dunkler Modus; folgt der Einstellung „App-Modus“ von Windows.</summary>
    static class Theme
    {
        static readonly Dictionary<string, string[]> Palette = new Dictionary<string, string[]>
        {
            //                        hell          dunkel
            { "WindowBg",    new[] { "#F4F5F9",   "#17181C" } },
            { "CardBg",      new[] { "#D9FFFFFF", "#CC26282F" } },
            { "CardBorder",  new[] { "#14000000", "#1AFFFFFF" } },
            { "Text",        new[] { "#1A1B1F",   "#F1F2F4" } },
            { "Text2",       new[] { "#666A76",   "#A4A8B4" } },
            { "Text3",       new[] { "#9A9EAA",   "#6F7380" } },
            { "ChipBg",      new[] { "#0F000000", "#17FFFFFF" } },
            { "PopupBg",     new[] { "#FFFFFF",   "#2A2C34" } },
            { "HoverBg",     new[] { "#12000000", "#1FFFFFFF" } },
            { "InputBg",     new[] { "#0A000000", "#14FFFFFF" } },
            { "GridLine",    new[] { "#12000000", "#14FFFFFF" } },
            { "ScrollThumb", new[] { "#40000000", "#40FFFFFF" } },
            { "SegmentSel",  new[] { "#FFFFFF",   "#33FFFFFF" } },
        };

        public static bool IsDark { get; private set; }

        /// <summary>Erhöht sich bei jedem Wechsel, damit selbst gezeichnete Elemente neu zeichnen.</summary>
        public static int Version { get; private set; }

        public static void Initialize()
        {
            Apply(DetectDark());
            SystemEvents.UserPreferenceChanged += (sender, e) =>
            {
                // Das Ereignis kommt auf einem eigenen Thread.
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    bool dark = DetectDark();
                    if (dark != IsDark) Apply(dark);
                }));
            };
        }

        static bool DetectDark()
        {
            string forced = Environment.GetEnvironmentVariable("LLMDUELL_APPEARANCE");
            if (forced == "dark") return true;
            if (forced == "light") return false;
            try
            {
                object value = Registry.GetValue(
                    @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                    "AppsUseLightTheme", 1);
                return value is int light && light == 0;
            }
            catch (Exception)
            {
                return false;
            }
        }

        static void Apply(bool dark)
        {
            IsDark = dark;
            Version++;
            foreach (KeyValuePair<string, string[]> entry in Palette)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(entry.Value[dark ? 1 : 0]));
                brush.Freeze();
                Application.Current.Resources[entry.Key] = brush;
            }
            foreach (Window window in Application.Current.Windows) ApplyTitleBar(window);
        }

        public static Brush Get(string key) => (Brush)Application.Current.Resources[key];

        public static Color ColorOf(string key) => ((SolidColorBrush)Application.Current.Resources[key]).Color;

        /// <summary>Färbt die Titelleiste passend zum Modus. Wirkt erst, wenn das Fenster ein Handle hat.</summary>
        public static void ApplyTitleBar(Window window)
        {
            IntPtr handle = new WindowInteropHelper(window).Handle;
            if (handle == IntPtr.Zero) return;
            int value = IsDark ? 1 : 0;
            try
            {
                // 20 = DWMWA_USE_IMMERSIVE_DARK_MODE; ältere Builds von Windows 10 verwenden 19.
                if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
                    DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
            }
            catch (Exception)
            {
                // Rein kosmetisch.
            }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
