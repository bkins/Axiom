using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform;
using Avalonia.Threading;
using Axiom.Core;
using Axiom.Infrastructure;

namespace Axiom.App;

internal sealed class GitTerminalView : UserControl, IAsyncDisposable
{
    private readonly NativeWebView _view = new();
    private readonly NativeHtmlContent _html;
    private readonly string _token = Guid.NewGuid().ToString("N");
    private readonly TerminalInputState _state;
    private readonly Func<bool> _contextValid;
    private readonly TextBlock _status = new() { TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly List<Button> _recipes = [];
    private readonly StringBuilder _output = new();
    private readonly StringBuilder _history = new();
    private readonly object _outputGate = new();
    private readonly SemaphoreSlim _bridgeGate = new(1, 1);
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private WindowsTerminalSession? _session;
    private bool _ready, _disposed, _polling;
    private bool _loadingPage;
    private string _pageToken = "";
    private int _queuedInput;
    private int _columns, _rows;
    private bool _inputRejected;
    private readonly Button _restart = new() { Content = "Restart shell", IsEnabled = false };
    internal NativeWebView BrowserForTesting => _view;
    internal Action<string>? InputForTesting { get; set; }
    internal bool CanPrepareForTesting => _ready && !_disposed && !_inputRejected && _session?.IsRunning == true && _contextValid() && _state.CanPrepare;
    internal int? ProcessIdForTesting => _session?.ProcessId;
    internal string StatusForTesting => _status.Text ?? "";
    internal string DiagnosticForTesting => $"ready={_ready}, disposed={_disposed}, prompt={_state.AtPrompt}, root={_state.AtRepositoryRoot}, canPrepare={_state.CanPrepare}, context={_contextValid()}, status={_status.Text}";

    internal GitTerminalView(string root, Func<bool> contextValid)
    {
        _contextValid = contextValid; _state = new(_token);
        _html = new(Path.Combine(Path.GetTempPath(), "AxiomTerminal", _token));
        _view.EnvironmentRequested += (_, args) => { if (args is WindowsWebView2EnvironmentRequestedEventArgs windows) windows.UserDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Axiom", "TerminalWebViewCache"); };
        _view.NavigationStarted += (_, args) => { if (args.Request is { } uri && !(_loadingPage && (_html.Allows(uri) || uri.Scheme is "about" or "data"))) args.Cancel = true; };
        _view.NewWindowRequested += (_, args) => args.Handled = true;
        _view.WebMessageReceived += async (_, args) =>
        {
            if (_disposed || args.Body is not { } body || body.Length > 131072) return;
            try
            {
                using var outer = JsonDocument.Parse(body);
                var text = outer.RootElement.ValueKind == JsonValueKind.String ? outer.RootElement.GetString()! : body;
                using var data = JsonDocument.Parse(text);
                var packet = data.RootElement;
                if (packet.GetProperty("token").GetString() != _token) return;
                var input = packet.GetProperty("input").GetString() ?? "";
                var response = packet.GetProperty("response").GetBoolean();
                var session = _session;
                if (packet.TryGetProperty("rejected", out var rejected) && rejected.GetBoolean() || input.Length > 16384 || _queuedInput + input.Length > 16384)
                { _inputRejected = true; UpdateState(); return; }
                if (_inputRejected && !response) { if (input != "\u0003") return; _inputRejected = false; }
                _queuedInput += input.Length;
                try
                {
                    await _bridgeGate.WaitAsync();
                    try
                    {
                        if (_disposed || session is null || !ReferenceEquals(session, _session)) return;
                        if (!response) { InputForTesting?.Invoke(input); lock (_outputGate) _state.ObserveInput(input); }
                        await session.WriteAsync(input);
                    }
                    finally { _bridgeGate.Release(); }
                }
                finally { _queuedInput -= input.Length; }
            }
            catch (Exception error) { if (!_disposed) _status.Text = "Terminal input failed: " + error.Message; }
        };
        _view.AdapterDestroyed += (_, _) => { _ready = false; _timer.Stop(); };
        _view.AdapterCreated += (_, _) =>
        {
            if (_disposed) return;
            try
            {
                lock (_outputGate) { _output.Clear(); _output.Append(_history); }
                _ready = false; _pageToken = Guid.NewGuid().ToString("N"); _loadingPage = true; _html.Load(_view, BuildHtml());
                _session ??= new(root, _token, RecordOutput);
                _timer.Start();
            }
            catch (Exception error) { _status.Text = "Embedded terminal unavailable: " + error.Message + ". Use Changes or Open terminal here."; }
        };
        _view.NavigationCompleted += async (_, args) =>
        {
            if (_disposed || !args.IsSuccess) return;
            try
            {
                var page = (await _view.InvokeScript("window.axiomPageToken"))?.Trim().Trim('"');
                if (page == _pageToken) { _ready = true; _loadingPage = false; }
            }
            catch (Exception error) { if (!_disposed) _status.Text = "Terminal page not ready: " + error.Message; }
        };
        _timer.Tick += async (_, _) =>
        {
            if (_polling || !_ready || _disposed) return;
            _polling = true;
            try { await PumpAsync(); }
            catch (Exception error) { _status.Text = "Terminal stopped: " + error.Message; _timer.Stop(); foreach (var button in _recipes) button.IsEnabled = false; }
            finally { _polling = false; }
        };
        var commands = new WrapPanel();
        _restart.Click += async (_, _) =>
        {
            if (_disposed || !_ready || !_contextValid()) return;
            if (_session?.IsRunning == true && TopLevel.GetTopLevel(this) is Window owner &&
                await ChoiceDialog.ShowAsync(owner, "Restart terminal", "Restart stops this shell and its running programs, and discards its pending command. Editor buffers remain intact.", "Restart", "Cancel") != "Restart") return;
            if (_session is not null) await _session.DisposeAsync();
            await _bridgeGate.WaitAsync();
            try
            {
                lock (_outputGate) { _output.Clear(); _history.Clear(); _state.Reset(); } _inputRejected = false;
                await _view.InvokeScript("window.axiomReset()");
                _session = new(root, _token, RecordOutput);
                _timer.Start();
            }
            catch (Exception error) { _status.Text = "Could not restart shell: " + error.Message; }
            finally { _bridgeGate.Release(); }
        };
        foreach (var (label, command) in new[] { ("Status", "git status"), ("Stage all changes here", "git add ."), ("Stage tracked changes", "git add -u"), ("Saved diff", "git diff"), ("Staged diff", "git diff --cached"), ("Commit…", "git commit"), ("Push", "git push") })
        {
            var button = new Button { Content = label, Margin = new Thickness(0, 0, 5, 5), IsEnabled = false };
            AutomationProperties.SetName(button, "Prepare " + label + " in terminal");
            ToolTip.SetTip(button, "Insert " + command + " without Enter. Review/edit it, then press Enter in the terminal.");
            button.Click += async (_, _) => await PrepareAsync(command);
            _recipes.Add(button); commands.Children.Add(button);
        }
        var header = new TextBlock { Text = root, TextTrimming = Avalonia.Media.TextTrimming.CharacterEllipsis };
        commands.Children.Add(_restart);
        ToolTip.SetTip(header, "Repository root: " + root);
        _status.Text = "Starting PowerShell…";
        var guidance = new TextBlock { Text = "Buttons prepare commands; Enter runs them. Git sees saved files, including non-Markdown files. Unsaved editor buffers are separate. Switching workspaces or closing Axiom stops this shell.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, FontSize = 12 };
        AutomationProperties.SetName(_view, "Embedded repository PowerShell terminal");
        var footer = new StackPanel { Spacing = 4, Children = { commands, _status, guidance } };
        var grid = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), RowSpacing = 6, Children = { header, _view, footer } };
        Grid.SetRow(_view, 1); Grid.SetRow(footer, 2); Content = grid;
    }
    private void RecordOutput(string output)
    {
        lock (_outputGate)
        {
            _state.ObserveOutput(output); _output.Append(output); _history.Append(output);
            if (_output.Length > 262144) _output.Remove(0, _output.Length - 262144);
            if (_history.Length > 262144) _history.Remove(0, _history.Length - 262144);
        }
    }
    private async Task PumpAsync()
    {
        await _bridgeGate.WaitAsync();
        try
        {
            if (_disposed || _session is null) return;
            await ReadInputAsync();
            string output;
            lock (_outputGate) { output = _output.ToString(); _output.Clear(); }
            if (output.Length > 0)
            {
                await _view.InvokeScript("window.axiomTerminal.write(" + JsonSerializer.Serialize(output) + ")");
            }
            UpdateState();
        }
        finally { _bridgeGate.Release(); }
    }
    private async Task ReadInputAsync()
    {
        var raw = await _view.InvokeScript("JSON.stringify(window.axiomDrain())");
        if (raw is null) return;
        // WebView adapters may return either the script string or its JSON-encoded representation.
        using var outer = JsonDocument.Parse(raw);
        var text = outer.RootElement.ValueKind == JsonValueKind.String ? outer.RootElement.GetString()! : raw;
        using var data = JsonDocument.Parse(text);
        var packet = data.RootElement;
        if (packet.GetProperty("token").GetString() != _token) throw new InvalidOperationException("Terminal session changed.");
        var columns = packet.GetProperty("cols").GetInt32(); var rows = packet.GetProperty("rows").GetInt32();
        if (_columns != columns || _rows != rows) { _session!.Resize(columns, rows); _columns = columns; _rows = rows; }
    }
    internal async Task<bool> PrepareAsync(string command)
    {
        if (!_ready || _disposed || _session is null) return false;
        await _bridgeGate.WaitAsync();
        try
        {
            await ReadInputAsync();
            bool prepared;
            lock (_outputGate) prepared = !_inputRejected && _session.IsRunning && _contextValid() && _state.TryPrepare(command);
            if (!prepared) { UpdateState(); return false; }
            await _session.WriteAsync(command);
            _view.Focus();
            await _view.InvokeScript("window.axiomTerminal.focus()");
            UpdateState(); return true;
        }
        catch (Exception error) { _status.Text = "Could not prepare command: " + error.Message; return false; }
        finally { _bridgeGate.Release(); }
    }
    private void UpdateState()
    {
        var enabled = !_inputRejected && _state.CanPrepare && _session?.IsRunning == true && _contextValid() && !_disposed;
        _restart.IsEnabled = _ready && !_disposed && _contextValid();
        foreach (var button in _recipes) button.IsEnabled = enabled;
        _status.Text = _inputRejected ? "Input limit reached; some text was rejected. Press Ctrl+C in the terminal to clear the command before continuing." : _session?.IsRunning != true ? "Shell exited. Choose Restart shell to start again at this repository root." : enabled ? "Empty prompt — choose a command or type directly."
            : !_state.AtPrompt ? "Shell is running or waiting for input. Helpers resume at an empty prompt."
            : !_state.AtRepositoryRoot ? "Current directory differs from this repository root. Return there to use helpers."
            : "Review the current command, then press Enter. Use Ctrl+C to return to an empty prompt.";
    }
    private string BuildHtml()
    {
        static string Read(string file) { using var stream = AssetLoader.Open(new Uri("avares://Axiom.App/Assets/Terminal/" + file)); using var reader = new StreamReader(stream); return reader.ReadToEnd(); }
        return $$"""
            <!doctype html><html><head><meta charset="utf-8"><meta http-equiv="Content-Security-Policy" content="default-src 'none'; script-src 'nonce-{{_token}}'; style-src 'unsafe-inline'"><style>{{Read("xterm.css")}}html,body,#terminal{height:100%;margin:0;background:#111827}#terminal{box-sizing:border-box;padding:6px}</style></head><body><div id="terminal"></div><script nonce="{{_token}}">{{Read("xterm.js")}}</script><script nonce="{{_token}}">{{Read("addon-fit.js")}}</script><script nonce="{{_token}}">
            const terminal = window.axiomTerminal = new Terminal({fontFamily:'Cascadia Mono, Consolas, monospace',fontSize:13,scrollback:3000,allowProposedApi:false,theme:{background:'#111827',foreground:'#e5e7eb'},screenReaderMode:true});
            const fit = new FitAddon.FitAddon(); terminal.loadAddon(fit); terminal.open(document.getElementById('terminal'));
            terminal.onData(input=>{ if(input.length>16384){window.chrome.webview.postMessage(JSON.stringify({token:'{{_token}}',input:'',response:false,rejected:true}));return;} const response=/^\x1b\[\??[\d;]*[Rcn]$/.test(input)||/^\x1b\[[IO]$/.test(input); window.chrome.webview.postMessage(JSON.stringify({token:'{{_token}}',input,response})); });
            new ResizeObserver(()=>fit.fit()).observe(document.getElementById('terminal')); fit.fit();
            window.axiomDrain=()=>({token:'{{_token}}',cols:terminal.cols,rows:terminal.rows});
            window.axiomPageToken='{{_pageToken}}';
            window.axiomReset=()=>{terminal.reset();terminal.focus();};
            terminal.focus();
            </script></body></html>
            """;
    }
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true; _timer.Stop();
        if (_session is not null) await _session.DisposeAsync();
        await _bridgeGate.WaitAsync();
        try { _html.Dispose(); }
        finally { _bridgeGate.Release(); }
    }
}
