using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace LLMDuell
{
    enum Side
    {
        Left,
        Right,
    }

    enum Phase
    {
        Idle,
        Waiting,
        Loading,
        Thinking,
        Streaming,
        Done,
        Cancelled,
        Failed,
    }

    sealed class GenerationStats
    {
        public int PromptTokens;
        public int OutputTokens;
        public double PromptEvalSeconds;
        public double EvalSeconds;
        public double LoadSeconds;
        public double TotalSeconds;
        public double? TimeToFirstToken;

        public int TotalTokens => PromptTokens + OutputTokens;
        public double TokensPerSecond => EvalSeconds > 0 ? OutputTokens / EvalSeconds : 0;
        public double? PromptTokensPerSecond => PromptEvalSeconds > 0 ? PromptTokens / PromptEvalSeconds : (double?)null;
    }

    struct SpeedSample
    {
        public double T;
        public double Tps;
    }

    /// <summary>
    /// Zustand und Messwerte einer Seite des Vergleichs.
    /// Alle Felder werden nur auf dem UI-Thread verändert; der Netzwerk-Thread legt
    /// empfangene Chunks lediglich in eine Warteschlange, die <see cref="Pump"/> abarbeitet.
    /// </summary>
    sealed class ModelRun
    {
        readonly Settings settings;
        readonly ConcurrentQueue<ChatChunk> incoming = new ConcurrentQueue<ChatChunk>();
        readonly StringBuilder output = new StringBuilder();
        readonly StringBuilder thinking = new StringBuilder();
        readonly Queue<double> recentTokens = new Queue<double>();
        string outputCache = "";
        string thinkingCache = "";
        double? lastSampleTime;

        public Side Side { get; }
        public string Letter => Side == Side.Left ? "A" : "B";

        public Phase Phase { get; private set; } = Phase.Idle;
        public string ErrorMessage { get; private set; } = "";
        public int LiveTokens { get; private set; }
        public double LiveTps { get; private set; }
        public List<SpeedSample> Samples { get; } = new List<SpeedSample>();
        public GenerationStats Stats { get; private set; }
        public double? StartTime { get; private set; }
        public double? FirstTokenTime { get; private set; }
        public double? EndTime { get; private set; }

        public int SessionRuns { get; private set; }
        public int SessionPromptTokens { get; private set; }
        public int SessionOutputTokens { get; private set; }
        public double SessionEvalSeconds { get; private set; }

        /// <summary>Erhöht sich bei jedem neuen Lauf, damit die Oberfläche ihre Ausgabe verwerfen kann.</summary>
        public int Generation { get; private set; }

        public ModelRun(Side side, Settings settings)
        {
            Side = side;
            this.settings = settings;
        }

        public string ModelName
        {
            get => Side == Side.Left ? settings.ModelLeft : settings.ModelRight;
            set
            {
                if (Side == Side.Left) settings.ModelLeft = value;
                else settings.ModelRight = value;
            }
        }

        public string Output => outputCache.Length == output.Length ? outputCache : (outputCache = output.ToString());
        public string Thinking => thinkingCache.Length == thinking.Length ? thinkingCache : (thinkingCache = thinking.ToString());

        public bool IsActive => Phase == Phase.Loading || Phase == Phase.Thinking || Phase == Phase.Streaming;
        public double DisplayTps => Stats?.TokensPerSecond ?? LiveTps;
        public int DisplayOutputTokens => Stats?.OutputTokens ?? LiveTokens;
        public double? TimeToFirstToken => StartTime != null && FirstTokenTime != null ? FirstTokenTime - StartTime : null;
        public int SessionTotalTokens => SessionPromptTokens + SessionOutputTokens;
        public double SessionAverageTps => SessionEvalSeconds > 0 ? SessionOutputTokens / SessionEvalSeconds : 0;

        public double? Elapsed(double now) => StartTime == null ? (double?)null : (EndTime ?? now) - StartTime.Value;

        public void Prepare(bool waiting)
        {
            while (incoming.TryDequeue(out _)) { }
            output.Clear();
            thinking.Clear();
            outputCache = "";
            thinkingCache = "";
            Stats = null;
            ErrorMessage = "";
            LiveTokens = 0;
            LiveTps = 0;
            Samples.Clear();
            StartTime = null;
            FirstTokenTime = null;
            EndTime = null;
            recentTokens.Clear();
            lastSampleTime = null;
            Generation++;
            Phase = waiting ? Phase.Waiting : Phase.Loading;
        }

        public void MarkCancelled()
        {
            if (Phase == Phase.Waiting) Phase = Phase.Cancelled;
        }

        public void ResetSession()
        {
            SessionRuns = 0;
            SessionPromptTokens = 0;
            SessionOutputTokens = 0;
            SessionEvalSeconds = 0;
        }

        /// <summary>Führt einen Lauf aus. Muss auf dem UI-Thread gestartet werden.</summary>
        public async Task RunAsync(OllamaClient client, string prompt, GenerationOptions options, CancellationToken cancel)
        {
            Prepare(false);
            StartTime = Clock.Now;
            try
            {
                await client.ChatAsync(ModelName, prompt, options, incoming.Enqueue, cancel);
                Pump();
                if (Stats == null)
                {
                    EndTime = Clock.Now;
                    if (cancel.IsCancellationRequested) Phase = Phase.Cancelled;
                    else Fail("Der Stream wurde unerwartet beendet.");
                }
            }
            catch (Exception error)
            {
                Pump();
                if (Stats != null) return;
                EndTime = Clock.Now;
                if (cancel.IsCancellationRequested) Phase = Phase.Cancelled;
                else Fail(client.Describe(error));
            }
        }

        void Fail(string message)
        {
            ErrorMessage = message;
            Phase = Phase.Failed;
        }

        /// <summary>Übernimmt alle seit dem letzten Aufruf empfangenen Chunks. Nur auf dem UI-Thread aufrufen.</summary>
        public void Pump()
        {
            while (incoming.TryDequeue(out ChatChunk chunk))
            {
                if (Stats != null) continue;
                double now = chunk.ReceivedAt;
                bool hasContent = chunk.Content.Length > 0;
                if (hasContent || chunk.Thinking.Length > 0)
                {
                    if (FirstTokenTime == null) FirstTokenTime = now;
                    LiveTokens++;
                    output.Append(chunk.Content);
                    thinking.Append(chunk.Thinking);
                    Phase = hasContent ? Phase.Streaming : Phase.Thinking;
                    RecordSample(now);
                }
                if (chunk.Done) Finish(chunk, now);
            }
        }

        void RecordSample(double now)
        {
            recentTokens.Enqueue(now);
            while (recentTokens.Count > 0 && now - recentTokens.Peek() > 1.0) recentTokens.Dequeue();

            double sinceFirst = now - FirstTokenTime.Value;
            if (sinceFirst > 0.05) LiveTps = (LiveTokens - 1) / sinceFirst;

            if (lastSampleTime == null || now - lastSampleTime.Value >= 0.2)
            {
                // Gleitendes Fenster von einer Sekunde für die Kurve; die erste halbe Sekunde ist zu unruhig.
                double window = Math.Min(1.0, sinceFirst);
                if (sinceFirst >= 0.5)
                {
                    Samples.Add(new SpeedSample { T = sinceFirst, Tps = recentTokens.Count / window });
                    lastSampleTime = now;
                }
            }
        }

        void Finish(ChatChunk chunk, double now)
        {
            double Seconds(long? nanoseconds) => (nanoseconds ?? 0) / 1e9;

            var result = new GenerationStats
            {
                PromptTokens = chunk.PromptEvalCount ?? 0,
                OutputTokens = chunk.EvalCount ?? LiveTokens,
                PromptEvalSeconds = Seconds(chunk.PromptEvalDuration),
                EvalSeconds = Seconds(chunk.EvalDuration),
                LoadSeconds = Seconds(chunk.LoadDuration),
                TotalSeconds = Seconds(chunk.TotalDuration),
                TimeToFirstToken = TimeToFirstToken,
            };
            Stats = result;
            EndTime = now;
            Phase = Phase.Done;
            SessionRuns++;
            SessionPromptTokens += result.PromptTokens;
            SessionOutputTokens += result.OutputTokens;
            SessionEvalSeconds += result.EvalSeconds;
        }
    }
}
