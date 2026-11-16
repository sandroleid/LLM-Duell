# Automatischer Test der fertigen App gegen ein simuliertes Ollama (mock_ollama.py).
#
#   .\Windows\build.ps1 -Package
#   .\Windows\Tests\smoke.ps1
#
# Startet "dist\LLM Duell.exe" in mehreren Szenarien, prüft die Messwerte und legt Bilder
# des Fensters unter build\shots ab. Überschreibt dabei die gespeicherten Einstellungen der App.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$exe = Join-Path $root 'dist\LLM Duell.exe'
$shots = Join-Path $root 'build\shots'
$settings = Join-Path $env:APPDATA 'LLM Duell\settings.json'
$failures = @()

Remove-Item $shots -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $shots, (Split-Path $settings) | Out-Null

# Die Runner von GitHub starten mit 1024x768; das Fenster soll in voller Größe erscheinen.
try { Set-DisplayResolution -Width 1920 -Height 1080 -Force | Out-Null } catch { Write-Host "Auflösung unverändert: $_" }

function Save-Screen($path) {
    try {
        Add-Type -AssemblyName System.Windows.Forms, System.Drawing
        $bounds = [System.Windows.Forms.SystemInformation]::VirtualScreen
        $bitmap = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
        $bitmap.Save($path)
        $graphics.Dispose(); $bitmap.Dispose()
    } catch { Write-Host "Kein Bildschirmfoto: $_" }
}

# Startet die App mit den angegebenen Einstellungen und liefert den Inhalt von result.txt.
function Invoke-Scenario($name, $config, $variables, [int]$ScreenAfter = 0) {
    Write-Host "`n=== $name ==="
    $dir = Join-Path $shots $name
    New-Item -ItemType Directory -Force $dir | Out-Null
    $config | ConvertTo-Json | Set-Content $settings -Encoding utf8

    $all = @{ LLMDUELL_SNAPSHOT_DIR = $dir; LLMDUELL_QUIT = '1' } + $variables
    foreach ($key in $all.Keys) { Set-Item "env:$key" $all[$key] }
    try {
        $process = Start-Process $exe -PassThru
        if ($ScreenAfter -gt 0) { Start-Sleep $ScreenAfter; Save-Screen (Join-Path $dir 'screen.png') }
        if (-not $process.WaitForExit(120000)) {
            Save-Screen (Join-Path $dir 'timeout.png')
            $process.Kill()
            throw "$name`: App hat sich nicht beendet"
        }
        if ($process.ExitCode -ne 0) {
            $detail = Get-Content (Join-Path $dir 'error.txt') -Raw -ErrorAction SilentlyContinue
            throw "$name`: Exit-Code $($process.ExitCode)`n$detail"
        }
    } finally {
        foreach ($key in $all.Keys) { Remove-Item "env:$key" }
    }
    $result = Get-Content (Join-Path $dir 'result.txt') -Raw
    Write-Host $result
    return $result
}

function Assert-Match($name, $text, $pattern) {
    if ($text -notmatch $pattern) {
        $script:failures += "$name`: erwartet /$pattern/"
        Write-Host "FEHLER: $name erwartet /$pattern/" -ForegroundColor Red
    }
}

$base = @{ host = 'http://localhost:11434'; temperature = 0.7; useSeed = $true; seed = 42; systemPrompt = '' }
$prompt = 'Was ist ein Token? Erkläre es kurz und mit einem Beispiel.'

$mock = Start-Process python -ArgumentList "`"$PSScriptRoot\mock_ollama.py`"" -PassThru -WindowStyle Hidden
try {
    Start-Sleep 2

    # 1. Nacheinander, heller Modus
    $r = Invoke-Scenario 'hell-nacheinander' ($base + @{ mode = 'sequential'; modelLeft = 'mock-flink:4b'; modelRight = 'mock-gross:12b' }) `
        @{ LLMDUELL_APPEARANCE = 'light'; LLMDUELL_PROMPT = $prompt; LLMDUELL_SHOTS = '4'; LLMDUELL_INTERVAL = '3'; LLMDUELL_WAIT = '60' }
    Assert-Match 'hell-nacheinander' $r 'connection: Connected 0\.0\.0-mock'
    Assert-Match 'hell-nacheinander' $r 'models: 4'
    Assert-Match 'hell-nacheinander' $r 'left: model=mock-flink:4b phase=Done output_tokens=207 '
    Assert-Match 'hell-nacheinander' $r 'right: model=mock-gross:12b phase=Done output_tokens=207 '
    Assert-Match 'hell-nacheinander' $r 'left: .* chars=893 '
    Assert-Match 'hell-nacheinander' $r 'right: .* samples=[1-9]\d* '

    # 2. Parallel, dunkler Modus, mit Gedankengang
    $r = Invoke-Scenario 'dunkel-parallel' ($base + @{ mode = 'parallel'; modelLeft = 'mock-denker:8b'; modelRight = 'mock-flink:4b' }) `
        @{ LLMDUELL_APPEARANCE = 'dark'; LLMDUELL_PROMPT = $prompt; LLMDUELL_SHOTS = '3'; LLMDUELL_INTERVAL = '1.5'; LLMDUELL_WAIT = '60'; LLMDUELL_HOLD = '8' } -ScreenAfter 14
    Assert-Match 'dunkel-parallel' $r 'left: model=mock-denker:8b phase=Done output_tokens=\d+ .* thinking_chars=[1-9]\d+ '
    Assert-Match 'dunkel-parallel' $r 'right: model=mock-flink:4b phase=Done output_tokens=207 '

    # 3. Ein Modell schlägt fehl
    $r = Invoke-Scenario 'fehler' ($base + @{ mode = 'sequential'; modelLeft = 'mock-defekt:1b'; modelRight = 'mock-flink:4b' }) `
        @{ LLMDUELL_APPEARANCE = 'light'; LLMDUELL_PROMPT = $prompt; LLMDUELL_SHOTS = '1'; LLMDUELL_WAIT = '60' }
    Assert-Match 'fehler' $r 'left: .* phase=Failed .* error=llama runner process has terminated: simulated crash'
    Assert-Match 'fehler' $r 'right: .* phase=Done output_tokens=207 '

    # 4. und 5. Popups (nur als Bildschirmfoto sichtbar)
    Invoke-Scenario 'optionen' ($base + @{ mode = 'sequential'; modelLeft = 'mock-flink:4b'; modelRight = 'mock-gross:12b' }) `
        @{ LLMDUELL_APPEARANCE = 'light'; LLMDUELL_OPEN = 'options'; LLMDUELL_SHOTS = '2'; LLMDUELL_INTERVAL = '5' } -ScreenAfter 4 | Out-Null
    Invoke-Scenario 'modellliste' ($base + @{ mode = 'sequential'; modelLeft = 'mock-flink:4b'; modelRight = 'mock-gross:12b' }) `
        @{ LLMDUELL_APPEARANCE = 'dark'; LLMDUELL_OPEN = 'models'; LLMDUELL_SHOTS = '2'; LLMDUELL_INTERVAL = '5' } -ScreenAfter 4 | Out-Null
} finally {
    Stop-Process $mock -Force -ErrorAction SilentlyContinue
}

# 6. Ollama läuft nicht
Start-Sleep 1
$r = Invoke-Scenario 'offline' ($base + @{ mode = 'sequential'; modelLeft = ''; modelRight = '' }) `
    @{ LLMDUELL_APPEARANCE = 'light'; LLMDUELL_SHOTS = '2'; LLMDUELL_INTERVAL = '5' }
Assert-Match 'offline' $r 'connection: Failed Keine Verbindung zu http://localhost:11434'

if ($failures.Count -gt 0) { throw "Fehlgeschlagen:`n" + ($failures -join "`n") }
Write-Host "`nAlle Szenarien bestanden." -ForegroundColor Green
