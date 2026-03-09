using System;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace PcUsageTimer.Network;

public class RemoteLockServer : IDisposable
{
    private readonly int _port;
    private readonly Func<TimerStatus> _getStatus;
    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private bool _disposed;
    private int _failedAttempts;
    private DateTime _lockoutUntil = DateTime.MinValue;

    public event Action? LockRequested;
    public event Action<int>? TimerStartRequested;
    public event Action? UnlockRequested;
    public event Action<int>? ExtendRequested;
    public string? ServerUrl { get; private set; }
    public string? LanIpAddress { get; private set; }
    public bool IsRunning { get; private set; }

    public RemoteLockServer(int port, Func<TimerStatus> getStatus)
    {
        _port = port;
        _getStatus = getStatus;
    }

    public void Start()
    {
        LanIpAddress = LanHelper.GetLanIPv4Address();
        ServerUrl = $"http://{LanIpAddress}:{_port}";

        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://+:{_port}/");
        _cts = new CancellationTokenSource();

        _listener.Start();
        IsRunning = true;

        Task.Run(() => ListenLoop(_cts.Token));
    }

    public void Stop()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        try { _listener?.Stop(); } catch { }
        try { _listener?.Close(); } catch { }
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var context = await _listener!.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => HandleRequest(context), ct);
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch
            {
                // Transient error, continue
            }
        }
    }

    private void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            // CORS headers for fetch from same page
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            var path = request.Url?.AbsolutePath ?? "/";

            switch (path)
            {
                case "/":
                    ServeHtmlPage(response);
                    break;
                case "/status":
                    ServeStatus(response);
                    break;
                case "/lock":
                    HandleLock(request, response);
                    break;
                case "/start-timer":
                    HandleStartTimer(request, response);
                    break;
                case "/unlock":
                    HandleUnlock(request, response);
                    break;
                case "/extend":
                    HandleExtend(request, response);
                    break;
                default:
                    response.StatusCode = 404;
                    WriteJson(response, new { error = "Not found" });
                    break;
            }
        }
        catch
        {
            try { context.Response.StatusCode = 500; context.Response.Close(); } catch { }
        }
    }

    private void ServeHtmlPage(HttpListenerResponse response)
    {
        var html = GetMobileHtml();
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private void ServeStatus(HttpListenerResponse response)
    {
        var status = _getStatus();
        WriteJson(response, new
        {
            pcName = Environment.MachineName,
            timerRunning = status.IsRunning,
            remaining = status.Remaining?.ToString(status.Remaining.Value.TotalHours >= 1 ? @"h\:mm\:ss" : @"mm\:ss"),
            locked = status.IsLocked
        });
    }

    private void HandleLock(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            WriteJson(response, new { success = false, error = "Method not allowed" });
            return;
        }

        var (pin, _) = ReadPinFromBody(request);
        if (!ValidatePin(pin, response)) return;

        LockRequested?.Invoke();
        WriteJson(response, new { success = true });
    }

    private void HandleStartTimer(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            WriteJson(response, new { success = false, error = "Method not allowed" });
            return;
        }

        var (pin, body) = ReadPinFromBody(request);
        if (!ValidatePin(pin, response)) return;

        int minutes = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            minutes = doc.RootElement.GetProperty("minutes").GetInt32();
        }
        catch { }

        if (minutes < 1 || minutes > 480)
        {
            response.StatusCode = 400;
            WriteJson(response, new { success = false, error = "Minutes must be 1–480" });
            return;
        }

        var status = _getStatus();
        if (status.IsRunning || status.IsLocked)
        {
            response.StatusCode = 409;
            WriteJson(response, new { success = false, error = "Timer already running or screen locked" });
            return;
        }

        TimerStartRequested?.Invoke(minutes);
        WriteJson(response, new { success = true });
    }

    private void HandleUnlock(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            WriteJson(response, new { success = false, error = "Method not allowed" });
            return;
        }

        var (pin, _) = ReadPinFromBody(request);
        if (!ValidatePin(pin, response)) return;

        var status = _getStatus();
        if (!status.IsLocked)
        {
            response.StatusCode = 409;
            WriteJson(response, new { success = false, error = "Screen is not locked" });
            return;
        }

        UnlockRequested?.Invoke();
        WriteJson(response, new { success = true });
    }

    private void HandleExtend(HttpListenerRequest request, HttpListenerResponse response)
    {
        if (request.HttpMethod != "POST")
        {
            response.StatusCode = 405;
            WriteJson(response, new { success = false, error = "Method not allowed" });
            return;
        }

        var (pin, body) = ReadPinFromBody(request);
        if (!ValidatePin(pin, response)) return;

        int minutes = 0;
        try
        {
            using var doc = JsonDocument.Parse(body);
            minutes = doc.RootElement.GetProperty("minutes").GetInt32();
        }
        catch { }

        if (minutes < 1 || minutes > 480)
        {
            response.StatusCode = 400;
            WriteJson(response, new { success = false, error = "Minutes must be 1–480" });
            return;
        }

        var status = _getStatus();
        if (!status.IsRunning)
        {
            response.StatusCode = 409;
            WriteJson(response, new { success = false, error = "No timer running" });
            return;
        }

        ExtendRequested?.Invoke(minutes);
        WriteJson(response, new { success = true });
    }

    private (string? pin, string body) ReadPinFromBody(HttpListenerRequest request)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            body = reader.ReadToEnd();

        string? pin = null;
        try
        {
            using var doc = JsonDocument.Parse(body);
            pin = doc.RootElement.GetProperty("pin").GetString();
        }
        catch { }

        return (pin, body);
    }

    private bool ValidatePin(string? pin, HttpListenerResponse response)
    {
        if (DateTime.UtcNow < _lockoutUntil)
        {
            var wait = (int)Math.Ceiling((_lockoutUntil - DateTime.UtcNow).TotalSeconds);
            response.StatusCode = 429;
            WriteJson(response, new { success = false, error = $"Too many attempts. Wait {wait}s." });
            return false;
        }

        if (string.IsNullOrEmpty(pin) || !PinManager.HasPin || !PinManager.Validate(pin))
        {
            _failedAttempts++;
            if (_failedAttempts >= 3)
            {
                var delay = _failedAttempts >= 9 ? 60 : _failedAttempts >= 6 ? 15 : 5;
                _lockoutUntil = DateTime.UtcNow.AddSeconds(delay);
            }
            response.StatusCode = 403;
            var error = !PinManager.HasPin ? "No PIN set. Start a timer on the PC first." : "Wrong PIN";
            WriteJson(response, new { success = false, error });
            return false;
        }

        _failedAttempts = 0;
        return true;
    }

    private static void WriteJson(HttpListenerResponse response, object data)
    {
        var json = JsonSerializer.Serialize(data);
        var bytes = Encoding.UTF8.GetBytes(json);
        response.ContentType = "application/json; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
        response.Close();
    }

    private string GetMobileHtml()
    {
        return """
<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0, user-scalable=no">
<title>PC Usage Timer</title>
<style>
  * { box-sizing: border-box; margin: 0; padding: 0; }
  body {
    font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
    background: #1e1e2e; color: #cdd6f4;
    min-height: 100vh; display: flex; flex-direction: column;
    align-items: center; padding: 20px;
  }
  h1 { font-size: 1.5rem; margin-bottom: 8px; color: #89b4fa; }
  .pc-name { color: #a6adc8; font-size: 0.9rem; margin-bottom: 24px; }
  .card {
    background: #313244; border-radius: 12px; padding: 20px;
    width: 100%; max-width: 360px; margin-bottom: 16px;
  }
  .status-label { color: #a6adc8; font-size: 0.85rem; margin-bottom: 4px; }
  .status-value { font-size: 1.8rem; font-weight: bold; color: #a6e3a1; }
  .status-value.locked { color: #f38ba8; }
  .status-value.idle { color: #a6adc8; }
  .pin-input {
    width: 100%; padding: 14px; font-size: 1.3rem; text-align: center;
    background: #45475a; color: #cdd6f4; border: 2px solid #585b70;
    border-radius: 8px; outline: none; letter-spacing: 8px;
    -webkit-text-security: disc;
  }
  .pin-input:focus { border-color: #89b4fa; }
  .pin-input::placeholder { letter-spacing: normal; }
  .btn {
    width: 100%; padding: 16px; font-size: 1.1rem; font-weight: bold;
    border: none; border-radius: 8px; cursor: pointer; margin-top: 12px;
    transition: opacity 0.2s;
  }
  .btn:active { opacity: 0.7; }
  .btn-lock { background: #f38ba8; color: #1e1e2e; }
  .btn-lock:disabled { background: #585b70; color: #a6adc8; cursor: default; }
  .feedback {
    text-align: center; margin-top: 12px; font-size: 0.9rem;
    min-height: 1.2em;
  }
  .feedback.error { color: #f38ba8; }
  .feedback.success { color: #a6e3a1; }
  .timer-display { font-size: 2.5rem; font-weight: bold; color: #89b4fa; text-align: center; }
  .duration-row { display: flex; gap: 8px; margin-bottom: 8px; }
  .duration-btn {
    flex: 1; padding: 10px; font-size: 0.95rem; font-weight: bold;
    background: #45475a; color: #cdd6f4; border: 2px solid #585b70;
    border-radius: 8px; cursor: pointer;
  }
  .duration-btn.selected { border-color: #89b4fa; background: #585b70; }
  .duration-btn:active { opacity: 0.7; }
  .btn-start { background: #a6e3a1; color: #1e1e2e; }
  .btn-start:disabled { background: #585b70; color: #a6adc8; cursor: default; }
  .btn-unlock { background: #a6e3a1; color: #1e1e2e; }
  .btn-unlock:disabled { background: #585b70; color: #a6adc8; cursor: default; }
  .btn-extend { background: #89b4fa; color: #1e1e2e; }
  .btn-extend:disabled { background: #585b70; color: #a6adc8; cursor: default; }
  .section { display: none; }
  .section.visible { display: block; }
  .section-title { color: #a6adc8; font-size: 0.85rem; margin-bottom: 8px; }
</style>
</head>
<body>
  <h1>PC Usage Timer</h1>
  <div class="pc-name" id="pcName">Connecting...</div>

  <div class="card">
    <div class="status-label">Status</div>
    <div class="status-value" id="statusText">...</div>
    <div class="timer-display" id="timerDisplay" style="display:none"></div>
  </div>

  <div class="card">
    <input type="password" inputmode="numeric" maxlength="4" pattern="\d{4}"
           class="pin-input" id="pinInput" placeholder="PIN">

    <!-- Lock button — always available -->
    <button class="btn btn-lock" id="lockBtn" onclick="doAction('lock')">
      LOCK SCREEN NOW
    </button>

    <!-- Start timer — only when idle -->
    <div id="startSection" class="section">
      <div class="section-title" style="margin-top:12px">Start a timer</div>
      <div class="duration-row">
        <button class="duration-btn" onclick="selectMin(10)">10m</button>
        <button class="duration-btn" onclick="selectMin(20)">20m</button>
        <button class="duration-btn" onclick="selectMin(30)">30m</button>
        <button class="duration-btn" onclick="selectMin(60)">1h</button>
      </div>
      <button class="btn btn-start" id="startBtn" onclick="doAction('start-timer')">
        START TIMER
      </button>
    </div>

    <!-- Extend — only when timer running -->
    <div id="extendSection" class="section">
      <div class="section-title" style="margin-top:12px">Extend time</div>
      <div class="duration-row">
        <button class="duration-btn" onclick="selectExtend(5)">+5m</button>
        <button class="duration-btn" onclick="selectExtend(10)">+10m</button>
        <button class="duration-btn" onclick="selectExtend(15)">+15m</button>
        <button class="duration-btn" onclick="selectExtend(30)">+30m</button>
      </div>
      <button class="btn btn-extend" id="extendBtn" onclick="doAction('extend')">
        EXTEND
      </button>
    </div>

    <!-- Unlock — only when locked -->
    <div id="unlockSection" class="section">
      <button class="btn btn-unlock" id="unlockBtn" onclick="doAction('unlock')" style="margin-top:12px">
        UNLOCK SCREEN
      </button>
    </div>

    <div class="feedback" id="feedback"></div>
  </div>

<script>
const statusText = document.getElementById('statusText');
const timerDisplay = document.getElementById('timerDisplay');
const pcName = document.getElementById('pcName');
const pinInput = document.getElementById('pinInput');
const feedback = document.getElementById('feedback');
const startSection = document.getElementById('startSection');
const extendSection = document.getElementById('extendSection');
const unlockSection = document.getElementById('unlockSection');

let selectedMin = 10;
let selectedExtend = 10;

function selectMin(m) {
  selectedMin = m;
  startSection.querySelectorAll('.duration-btn').forEach((b,i) => {
    b.classList.toggle('selected', [10,20,30,60][i] === m);
  });
}

function selectExtend(m) {
  selectedExtend = m;
  extendSection.querySelectorAll('.duration-btn').forEach((b,i) => {
    b.classList.toggle('selected', [5,10,15,30][i] === m);
  });
}

function updateStatus() {
  fetch('/status')
    .then(r => r.json())
    .then(data => {
      pcName.textContent = data.pcName;
      const isLocked = data.locked;
      const isRunning = data.timerRunning;
      if (isLocked) {
        statusText.textContent = 'LOCKED';
        statusText.className = 'status-value locked';
        timerDisplay.style.display = 'none';
      } else if (isRunning) {
        statusText.textContent = 'Timer running';
        statusText.className = 'status-value';
        timerDisplay.textContent = data.remaining;
        timerDisplay.style.display = 'block';
      } else {
        statusText.textContent = 'Unlocked';
        statusText.className = 'status-value idle';
        timerDisplay.style.display = 'none';
      }
      startSection.classList.toggle('visible', !isRunning && !isLocked);
      extendSection.classList.toggle('visible', isRunning && !isLocked);
      unlockSection.classList.toggle('visible', isLocked);
    })
    .catch(() => {
      statusText.textContent = 'Offline';
      statusText.className = 'status-value locked';
    });
}

function doAction(action) {
  const pin = pinInput.value;
  if (pin.length !== 4) {
    feedback.textContent = 'Enter 4-digit PIN';
    feedback.className = 'feedback error';
    return;
  }
  const body = { pin };
  if (action === 'start-timer') body.minutes = selectedMin;
  if (action === 'extend') body.minutes = selectedExtend;

  feedback.textContent = '';
  fetch('/' + action, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  })
  .then(r => r.json())
  .then(data => {
    if (data.success) {
      const msgs = {
        'lock': 'Screen locked!',
        'start-timer': 'Timer started!',
        'unlock': 'Screen unlocked!',
        'extend': 'Time extended!'
      };
      feedback.textContent = msgs[action] || 'Done!';
      feedback.className = 'feedback success';
      pinInput.value = '';
      updateStatus();
    } else {
      feedback.textContent = data.error || 'Failed';
      feedback.className = 'feedback error';
    }
  })
  .catch(() => {
    feedback.textContent = 'Connection failed';
    feedback.className = 'feedback error';
  });
}

pinInput.addEventListener('keydown', e => { if (e.key === 'Enter') doAction('lock'); });
selectMin(10);
selectExtend(10);
updateStatus();
setInterval(updateStatus, 2000);
</script>
</body>
</html>
""";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        _cts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

public record TimerStatus(bool IsRunning, TimeSpan? Remaining, bool IsLocked);
