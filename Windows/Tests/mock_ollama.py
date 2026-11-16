#!/usr/bin/env python3
"""Simuliert die Ollama-API, damit die App ohne echtes Modell getestet werden kann.

    python mock_ollama.py [port]

Die Modelle unterscheiden sich nur in Tempo und Ladezeit; „mock-denker“ schickt
zusätzlich einen Gedankengang, „mock-defekt“ bricht mit einem Fehler ab.
"""
import json
import re
import sys
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer

# Name: (Tokens pro Sekunde, Ladezeit in Sekunden, Größe in Bytes, Parameter, Quantisierung)
MODELS = {
    "mock-flink:4b": (70, 0.4, 3_300_000_000, "4.3B", "Q4_K_M"),
    "mock-gross:12b": (28, 1.2, 8_100_000_000, "12.2B", "Q4_K_M"),
    "mock-denker:8b": (45, 0.6, 5_200_000_000, "8.2B", "Q4_K_M"),
    "mock-defekt:1b": (10, 0.1, 800_000_000, "1B", "Q8_0"),
}

THINKING = (
    "Der Nutzer möchte eine kurze Erklärung. Ich sollte zuerst den Begriff einordnen, "
    "dann zwei oder drei Kernpunkte nennen und mit einem kleinen Beispiel schließen. "
    "Wichtig ist, dass die Antwort knapp bleibt und trotzdem ein Codebeispiel enthält."
)

ANSWER = """# Was ist ein Token?

Ein **Token** ist die kleinste Einheit, in der ein Sprachmodell Text verarbeitet – oft ein Wortteil, manchmal ein ganzes Wort oder nur ein Satzzeichen.

## Warum das wichtig ist

- Die *Geschwindigkeit* eines Modells wird in **Tokens pro Sekunde** gemessen.
- Kontextfenster und Kosten werden ebenfalls in Tokens angegeben.
- Deutsche Wörter zerfallen häufiger in mehrere Tokens als englische, z. B. `Donau|dampf|schiff`.

## Ein kleines Beispiel

```python
text = "Zwei Modelle, ein Prompt."
tokens = tokenizer.encode(text)
print(len(tokens))  # 7
```

Als Faustregel gilt: Ein Token entspricht ungefähr **vier Zeichen** oder einem dreiviertel Wort. Für einen fairen Vergleich sollten beide Modelle denselben Prompt und denselben Seed erhalten, denn die Länge der Antwort beeinflusst die gemessene Gesamtdauer deutlich stärker als die reine Generierungsgeschwindigkeit.
"""


def pieces(text):
    """Zerlegt Text in tokenähnliche Stücke und behält alle Leerzeichen und Umbrüche."""
    return re.findall(r"\s*\S{1,5}|\s+$", text)


class Handler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.0"

    def log_message(self, *args):
        pass

    def send_json(self, value, status=200):
        body = json.dumps(value).encode()
        self.send_response(status)
        self.send_header("Content-Type", "application/json")
        self.send_header("Content-Length", str(len(body)))
        self.end_headers()
        self.wfile.write(body)

    def do_GET(self):
        if self.path == "/api/version":
            self.send_json({"version": "0.0.0-mock"})
        elif self.path == "/api/tags":
            self.send_json({"models": [
                {"name": name, "model": name, "size": size,
                 "details": {"family": "mock", "parameter_size": params, "quantization_level": quant}}
                for name, (_, _, size, params, quant) in MODELS.items()
            ]})
        else:
            self.send_json({"error": "not found"}, 404)

    def do_POST(self):
        request = json.loads(self.rfile.read(int(self.headers.get("Content-Length", 0))))
        if self.path != "/api/chat":
            return self.send_json({"error": "not found"}, 404)
        name = request.get("model")
        if name not in MODELS:
            return self.send_json({"error": f"model '{name}' not found"}, 404)
        if "defekt" in name:
            return self.send_json({"error": "llama runner process has terminated: simulated crash"}, 500)

        tps, load, _, _, _ = MODELS[name]
        self.send_response(200)
        self.send_header("Content-Type", "application/x-ndjson")
        self.end_headers()

        def send(message, **extra):
            line = {"model": name, "created_at": "2026-01-01T00:00:00Z", "message": message, "done": False, **extra}
            self.wfile.write((json.dumps(line, ensure_ascii=False) + "\n").encode())
            self.wfile.flush()

        started = time.monotonic()
        time.sleep(load)
        generation_started = time.monotonic()
        count = 0
        try:
            if "denker" in name:
                for piece in pieces(THINKING):
                    send({"role": "assistant", "content": "", "thinking": piece})
                    count += 1
                    time.sleep(1 / tps)
            for piece in pieces(ANSWER):
                send({"role": "assistant", "content": piece})
                count += 1
                time.sleep(1 / tps)
            now = time.monotonic()
            prompt_tokens = 12 + len(json.dumps(request.get("messages", ""))) // 4
            send({"role": "assistant", "content": ""}, done=True, done_reason="stop",
                 total_duration=int((now - started) * 1e9),
                 load_duration=int(load * 1e9),
                 prompt_eval_count=prompt_tokens,
                 prompt_eval_duration=int(prompt_tokens / 900 * 1e9),
                 eval_count=count,
                 eval_duration=int((now - generation_started) * 1e9))
        except (BrokenPipeError, ConnectionError):
            pass  # Die App hat abgebrochen.


if __name__ == "__main__":
    port = int(sys.argv[1]) if len(sys.argv) > 1 else 11434
    print(f"Simuliertes Ollama auf http://127.0.0.1:{port}", flush=True)
    ThreadingHTTPServer(("127.0.0.1", port), Handler).serve_forever()
