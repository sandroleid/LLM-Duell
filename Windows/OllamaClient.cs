using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LLMDuell
{
    sealed class OllamaModel
    {
        public string Name = "";
        public long Size;
        public string ParameterSize;
        public string Quantization;

        public string SizeLabel => Format.Bytes(Size);
    }

    /// <summary>Eine Zeile aus dem NDJSON-Stream von <c>/api/chat</c>, mit dem Empfangszeitpunkt.</summary>
    sealed class ChatChunk
    {
        public string Content = "";
        public string Thinking = "";
        public bool Done;
        public long? TotalDuration;
        public long? LoadDuration;
        public int? PromptEvalCount;
        public long? PromptEvalDuration;
        public int? EvalCount;
        public long? EvalDuration;

        /// <summary>Gemessen abseits des UI-Threads, direkt nach dem Lesen der Zeile.</summary>
        public double ReceivedAt;
    }

    sealed class GenerationOptions
    {
        public double Temperature;
        public int? Seed;
        public string SystemPrompt = "";
    }

    sealed class OllamaException : Exception
    {
        public OllamaException(string message) : base(message) { }
    }

    sealed class OllamaClient
    {
        static readonly HttpClient Http = CreateHttpClient();

        readonly string baseUrl;

        public string DisplayHost { get; }

        OllamaClient(string baseUrl, string displayHost)
        {
            this.baseUrl = baseUrl;
            DisplayHost = displayHost;
        }

        static HttpClient CreateHttpClient()
        {
            // Standard sind zwei Verbindungen pro Host – zu wenig für zwei parallele Streams plus Abfragen.
            ServicePointManager.DefaultConnectionLimit = 16;
            // Beides würde das Absenden kleiner Anfragen verzögern und die Messung verfälschen.
            ServicePointManager.Expect100Continue = false;
            ServicePointManager.UseNagleAlgorithm = false;

            // Ohne Proxy: Die automatische Proxy-Erkennung kann die erste Anfrage um Sekunden bremsen.
            var handler = new HttpClientHandler { UseProxy = false };
            // Großzügig, weil das erste Laden eines großen Modells dauern kann.
            return new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(15) };
        }

        /// <summary>Erzeugt einen Client aus der Eingabe im Host-Feld oder <c>null</c>, wenn sie keine gültige Adresse ist.</summary>
        public static OllamaClient Create(string host)
        {
            string value = (host ?? "").Trim();
            if (value.Length == 0) return null;
            if (!value.Contains("://")) value = "http://" + value;
            value = value.TrimEnd('/');
            if (!Uri.TryCreate(value, UriKind.Absolute, out Uri uri)) return null;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

            string target = value;
            if (string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
            {
                // Windows löst „localhost“ zuerst nach IPv6 auf. Ollama lauscht standardmäßig nur auf
                // 127.0.0.1, und der gescheiterte IPv6-Versuch kostet pro Verbindung bis zu zwei Sekunden.
                target = new UriBuilder(uri) { Host = "127.0.0.1" }.Uri.ToString().TrimEnd('/');
            }
            return new OllamaClient(target, value);
        }

        Uri Url(string path) => new Uri(baseUrl + "/" + path);

        public async Task<string> VersionAsync()
        {
            var json = await GetJsonAsync("api/version").ConfigureAwait(false);
            return json != null && json.TryGetValue("version", out object v) ? v as string ?? "" : "";
        }

        public async Task<List<OllamaModel>> ListModelsAsync()
        {
            var json = await GetJsonAsync("api/tags").ConfigureAwait(false);
            var result = new List<OllamaModel>();
            if (json != null && json.TryGetValue("models", out object list) && list is IEnumerable items)
            {
                foreach (object item in items)
                {
                    if (!(item is Dictionary<string, object> entry)) continue;
                    var model = new OllamaModel
                    {
                        Name = Json.String(entry, "name") ?? "",
                        Size = Json.Long(entry, "size") ?? 0,
                    };
                    if (entry.TryGetValue("details", out object d) && d is Dictionary<string, object> details)
                    {
                        model.ParameterSize = Json.String(details, "parameter_size");
                        model.Quantization = Json.String(details, "quantization_level");
                    }
                    if (model.Name.Length > 0) result.Add(model);
                }
            }
            result.Sort((a, b) => StrCmpLogicalW(a.Name, b.Name));
            return result;
        }

        async Task<Dictionary<string, object>> GetJsonAsync(string path)
        {
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            using (var response = await Http.GetAsync(Url(path), timeout.Token).ConfigureAwait(false))
            {
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) throw ErrorFrom(text, response.StatusCode);
                return Json.Parse(text) as Dictionary<string, object>;
            }
        }

        /// <summary>
        /// Streamt eine Antwort. <paramref name="onChunk"/> wird auf einem Hintergrund-Thread aufgerufen,
        /// damit der Empfangszeitpunkt nicht von der Auslastung der Oberfläche abhängt.
        /// </summary>
        public async Task ChatAsync(string model, string prompt, GenerationOptions options,
                                    Action<ChatChunk> onChunk, CancellationToken cancel)
        {
            var messages = new List<object>();
            if (!string.IsNullOrEmpty(options.SystemPrompt))
                messages.Add(new Dictionary<string, object> { { "role", "system" }, { "content", options.SystemPrompt } });
            messages.Add(new Dictionary<string, object> { { "role", "user" }, { "content", prompt } });

            var opts = new Dictionary<string, object> { { "temperature", options.Temperature } };
            if (options.Seed != null) opts["seed"] = options.Seed.Value;

            var body = new Dictionary<string, object>
            {
                { "model", model },
                { "messages", messages },
                { "stream", true },
                { "options", opts },
            };

            var request = new HttpRequestMessage(HttpMethod.Post, Url("api/chat"))
            {
                Content = new StringContent(Json.Serialize(body), Encoding.UTF8, "application/json"),
            };

            using (var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancel).ConfigureAwait(false))
            // Das Lesen der Zeilen kennt kein Abbruch-Token; das Schließen der Antwort beendet es sofort.
            using (cancel.Register(() => response.Dispose()))
            {
                if (!response.IsSuccessStatusCode)
                {
                    string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    throw ErrorFrom(text, response.StatusCode);
                }

                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                {
                    string line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        double receivedAt = Clock.Now;
                        if (line.Length == 0) continue;
                        ChatChunk chunk = ParseChunk(line, receivedAt);
                        onChunk(chunk);
                        if (chunk.Done) break;
                    }
                }
            }
        }

        static ChatChunk ParseChunk(string line, double receivedAt)
        {
            if (!(Json.Parse(line) is Dictionary<string, object> json))
                throw new OllamaException("Unerwartete Antwort von Ollama.");
            if (Json.String(json, "error") is string error) throw new OllamaException(error);

            var chunk = new ChatChunk { ReceivedAt = receivedAt };
            if (json.TryGetValue("message", out object m) && m is Dictionary<string, object> message)
            {
                chunk.Content = Json.String(message, "content") ?? "";
                chunk.Thinking = Json.String(message, "thinking") ?? "";
            }
            chunk.Done = json.TryGetValue("done", out object done) && done is bool flag && flag;
            chunk.TotalDuration = Json.Long(json, "total_duration");
            chunk.LoadDuration = Json.Long(json, "load_duration");
            chunk.PromptEvalCount = (int?)Json.Long(json, "prompt_eval_count");
            chunk.PromptEvalDuration = Json.Long(json, "prompt_eval_duration");
            chunk.EvalCount = (int?)Json.Long(json, "eval_count");
            chunk.EvalDuration = Json.Long(json, "eval_duration");
            return chunk;
        }

        static Exception ErrorFrom(string body, HttpStatusCode status)
        {
            try
            {
                if (Json.Parse(body) is Dictionary<string, object> json && Json.String(json, "error") is string error)
                    return new OllamaException(error);
            }
            catch (Exception)
            {
                // Kein JSON – dann zählt nur der Statuscode.
            }
            return new OllamaException("Ollama antwortete mit HTTP " + (int)status);
        }

        /// <summary>Verständliche Fehlermeldung; die Texte von .NET sind je nach System englisch und wenig hilfreich.</summary>
        public string Describe(Exception error)
        {
            if (error is OllamaException) return error.Message;
            if (error is OperationCanceledException) return "Zeitüberschreitung – " + DisplayHost + " antwortet nicht.";

            for (Exception e = error; e != null; e = e.InnerException)
            {
                if (e is SocketException || e is WebException)
                    return "Keine Verbindung zu " + DisplayHost + ". Läuft Ollama?";
            }

            Exception inner = error;
            while (inner.InnerException != null) inner = inner.InnerException;
            return inner.Message;
        }

        /// <summary>Sortiert wie der Explorer: „llama3.2“ vor „llama3.10“.</summary>
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
        static extern int StrCmpLogicalW(string a, string b);
    }

    static class Json
    {
        static JavaScriptSerializer Serializer => new JavaScriptSerializer { MaxJsonLength = int.MaxValue };

        public static object Parse(string text) => Serializer.DeserializeObject(text);

        public static string Serialize(object value) => Serializer.Serialize(value);

        public static string String(Dictionary<string, object> json, string key) =>
            json.TryGetValue(key, out object value) ? value as string : null;

        public static long? Long(Dictionary<string, object> json, string key)
        {
            if (!json.TryGetValue(key, out object value) || value == null) return null;
            try
            {
                return Convert.ToInt64(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
