using System;
using System.IO;
using System.Windows;
using System.Windows.Threading;

namespace LLMDuell
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnUnhandledException;
            Theme.Initialize();
            base.OnStartup(e);
        }

        void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            string directory = Environment.GetEnvironmentVariable("LLMDUELL_SNAPSHOT_DIR");
            if (!string.IsNullOrEmpty(directory))
            {
                // Im automatischen Test gibt es niemanden, der einen Dialog wegklickt.
                try
                {
                    File.WriteAllText(Path.Combine(directory, "error.txt"), e.Exception.ToString());
                }
                catch (Exception)
                {
                    // Dann bleibt nur der Exit-Code.
                }
                Environment.Exit(1);
            }
            MessageBox.Show(
                "Es ist ein unerwarteter Fehler aufgetreten:\n\n" + e.Exception.Message,
                "LLM Duell", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
