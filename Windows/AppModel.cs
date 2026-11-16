using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace LLMDuell
{
    enum ConnectionState
    {
        Checking,
        Connected,
        Failed,
    }

    /// <summary>Einstellungen, Modellliste und Ablauf des Vergleichs. Wird nur auf dem UI-Thread verwendet.</summary>
    sealed class AppModel
    {
        CancellationTokenSource running;
        int refreshTicket;

        public Settings Settings { get; } = Settings.Load();
        public string Prompt { get; set; } = "";
        public List<OllamaModel> Models { get; private set; } = new List<OllamaModel>();

        /// <summary>Erhöht sich, sobald sich die Modellliste ändert.</summary>
        public int ModelsVersion { get; private set; }

        public ConnectionState Connection { get; private set; } = ConnectionState.Checking;

        /// <summary>Bei bestehender Verbindung die Ollama-Version, sonst die Fehlermeldung.</summary>
        public string ConnectionDetail { get; private set; } = "";

        public ModelRun Left { get; }
        public ModelRun Right { get; }

        public AppModel()
        {
            Left = new ModelRun(Side.Left, Settings);
            Right = new ModelRun(Side.Right, Settings);
        }

        public bool IsRunning => running != null;

        public OllamaClient Client => OllamaClient.Create(Settings.Host);

        public bool CanStart =>
            !IsRunning && Client != null
            && Prompt.Trim().Length > 0
            && Left.ModelName.Length > 0 && Right.ModelName.Length > 0;

        public OllamaModel Model(string name) => Models.FirstOrDefault(m => m.Name == name);

        public async Task RefreshModelsAsync()
        {
            int ticket = ++refreshTicket;
            Connection = ConnectionState.Checking;
            ConnectionDetail = "";

            OllamaClient client = Client;
            if (client == null)
            {
                Models = new List<OllamaModel>();
                ModelsVersion++;
                Connection = ConnectionState.Failed;
                ConnectionDetail = "Ungültige Host-Adresse";
                return;
            }

            try
            {
                Task<string> versionTask = client.VersionAsync();
                List<OllamaModel> models = await client.ListModelsAsync();
                string version = "";
                try
                {
                    version = await versionTask;
                }
                catch (Exception)
                {
                    // Die Version ist nur Zierde; die Modellliste reicht für eine Verbindung.
                }
                if (ticket != refreshTicket) return;

                Models = models;
                ModelsVersion++;
                Connection = ConnectionState.Connected;
                ConnectionDetail = version;
                if (Model(Left.ModelName) == null) Left.ModelName = models.FirstOrDefault()?.Name ?? "";
                if (Model(Right.ModelName) == null)
                    Right.ModelName = models.FirstOrDefault(m => m.Name != Left.ModelName)?.Name ?? Left.ModelName;
            }
            catch (Exception error)
            {
                if (ticket != refreshTicket) return;
                Models = new List<OllamaModel>();
                ModelsVersion++;
                Connection = ConnectionState.Failed;
                ConnectionDetail = client.Describe(error);
            }
        }

        public async void Start()
        {
            OllamaClient client = Client;
            if (!CanStart || client == null) return;

            string prompt = Prompt.Trim();
            var options = new GenerationOptions
            {
                Temperature = Settings.Temperature,
                Seed = Settings.UseSeed ? Settings.Seed : (int?)null,
                SystemPrompt = Settings.SystemPrompt.Trim(),
            };
            RunMode mode = Settings.Mode;

            var source = new CancellationTokenSource();
            running = source;
            Left.Prepare(false);
            Right.Prepare(mode == RunMode.Sequential);
            try
            {
                if (mode == RunMode.Sequential)
                {
                    await Left.RunAsync(client, prompt, options, source.Token);
                    if (source.IsCancellationRequested) Right.MarkCancelled();
                    else await Right.RunAsync(client, prompt, options, source.Token);
                }
                else
                {
                    await Task.WhenAll(
                        Left.RunAsync(client, prompt, options, source.Token),
                        Right.RunAsync(client, prompt, options, source.Token));
                }
            }
            finally
            {
                running = null;
                source.Dispose();
            }
        }

        public void Stop() => running?.Cancel();

        public void SwapModels()
        {
            if (IsRunning) return;
            string left = Left.ModelName;
            Left.ModelName = Right.ModelName;
            Right.ModelName = left;
        }

        public void ResetSession()
        {
            Left.ResetSession();
            Right.ResetSession();
        }
    }
}
