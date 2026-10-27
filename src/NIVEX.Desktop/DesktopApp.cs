using System.ComponentModel;
using System.Diagnostics;
using System.Net.Http;
using System.Runtime.CompilerServices;
using NIVEX.Application;
using NIVEX.Domain;

namespace NIVEX.Desktop;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return;
        field = value;
        Notify(property);
    }
    protected void Notify(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
    protected void RefreshAll() => Notify(null);
}

public sealed class RelayCommand(Func<Task> action) : ICommand
{
    private bool _busy;
    public bool CanExecute(object? parameter) => !_busy;
    public async void Execute(object? parameter)
    {
        if (_busy) return;
        _busy = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await action(); }
        catch (Exception) { /* the model records the failure */ }
        finally
        {
            _busy = false;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }
    public event EventHandler? CanExecuteChanged;
}

public sealed class UiText : Observable
{
    public string Welcome { get; private set; } = "";
    public string Lead { get; private set; } = "";
    public string DeviceId { get; private set; } = "";
    public string Activation { get; private set; } = "";
    public string Remember { get; private set; } = "";
    public string Hide { get; private set; } = "";
    public string Login { get; private set; } = "";
    public string CreateLink { get; private set; } = "";
    public string Home { get; private set; } = "";
    public string Device { get; private set; } = "";
    public string Overview { get; private set; } = "";
    public string Settings { get; private set; } = "";
    public string Activity { get; private set; } = "";
    public string Copy { get; private set; } = "";
    public string Logout { get; private set; } = "";
    public string Refresh { get; private set; } = "";
    public string Summary { get; private set; } = "";
    public string OpenSite { get; private set; } = "";
    public string Motion { get; private set; } = "";
    public string Startup { get; private set; } = "";
    public string Language { get; private set; } = "";
    public string Api { get; private set; } = "";
    public string Interval { get; private set; } = "";
    public string Save { get; private set; } = "";
    public string Unavailable { get; private set; } = "";
    public string Measuring { get; private set; } = "";
    public string Connected { get; private set; } = "";
    public string Disconnected { get; private set; } = "";
    public string Offline { get; private set; } = "";
    public string Verified { get; private set; } = "";
    public string Revoked { get; private set; } = "";
    public string BadId { get; private set; } = "";
    public string NeedCode { get; private set; } = "";
    public string NeedRecovery { get; private set; } = "";
    public string Rejected { get; private set; } = "";
    public string Privacy { get; private set; } = "";
    public string Guidance { get; private set; } = "";
    public string VaultReset { get; private set; } = "";
    public string Mask { get; private set; } = "";
    public string Reveal { get; private set; } = "";

    public void Use(string language)
    {
        var ko = language != "en";
        Welcome = ko ? "이 PC의 NIVEX" : "NIVEX on this PC";
        Lead = ko ? "웹에서 만든 장치 ID로 이 설치를 활성화합니다." : "Activate this installation with the Device ID from the website.";
        DeviceId = ko ? "장치 ID" : "Device ID";
        Activation = ko ? "활성화 코드" : "Activation code";
        Remember = ko ? "이 기기에서 로그인 유지" : "Keep me signed in on this PC";
        Hide = ko ? "입력 숨기기" : "Hide input";
        Login = ko ? "로그인" : "Sign in";
        CreateLink = ko ? "장치 ID 만들기" : "Create a Device ID";
        Home = ko ? "홈" : "Home";
        Device = ko ? "장치" : "Device";
        Overview = ko ? "PC" : "PC";
        Settings = ko ? "설정" : "Settings";
        Activity = ko ? "최근 활동" : "Recent activity";
        Copy = ko ? "복사" : "Copy";
        Logout = ko ? "로그아웃" : "Sign out";
        Refresh = ko ? "측정 새로고침" : "Refresh readings";
        Summary = ko ? "PC 요약 복사" : "Copy PC summary";
        OpenSite = ko ? "장치 페이지 열기" : "Open device page";
        Motion = ko ? "애니메이션 줄이기" : "Reduce animation";
        Startup = ko ? "Windows 시작 시 실행" : "Start with Windows";
        Language = ko ? "언어" : "Language";
        Api = ko ? "API 주소" : "API address";
        Interval = ko ? "측정 간격(초)" : "Reading interval (seconds)";
        Save = ko ? "설정 저장" : "Save settings";
        Unavailable = ko ? "이용 불가" : "Unavailable";
        Measuring = ko ? "측정 중" : "Measuring";
        Connected = ko ? "연결됨" : "Connected";
        Disconnected = ko ? "연결 안 됨" : "Disconnected";
        Offline = ko ? "오프라인 · 서버에 확인하지 못함" : "Offline · not verified with the server";
        Verified = ko ? "서버 확인됨" : "Verified with the server";
        Revoked = ko ? "이 장치 ID는 폐기되었습니다." : "This Device ID has been revoked.";
        BadId = ko ? "장치 ID를 확인하지 못했습니다." : "The Device ID was not accepted.";
        NeedCode = ko ? "처음 활성화에는 활성화 코드가 필요합니다." : "First activation needs the activation code.";
        NeedRecovery = ko ? "이 장치 ID는 다른 설치에 연결되어 있습니다. 복구 코드로 연결을 해제한 뒤 다시 활성화하세요." : "This Device ID is bound to another installation. Detach it with the recovery code, then activate again.";
        Rejected = ko ? "요청이 거절되었습니다." : "The request was rejected.";
        Privacy = ko ? "NIVEX는 하드웨어 일련번호와 원문 IP를 보관하지 않습니다. 이 화면에는 비밀 키가 표시되지 않습니다." : "NIVEX does not store hardware serial numbers or raw IP addresses. Secret keys are not shown here.";
        Guidance = ko ? "로그아웃은 이 PC의 세션만 지웁니다. 다른 PC로 옮기려면 웹사이트의 복구 코드를 사용하세요." : "Sign-out clears the session on this PC. Use the recovery code on the website to move to another PC.";
        VaultReset = ko ? "이 PC에 저장된 자격 증명을 읽지 못했습니다. 다시 로그인해야 합니다." : "The saved credential on this PC could not be read. Sign in again.";
        Mask = ko ? "ID 가리기" : "Mask ID";
        Reveal = ko ? "ID 표시" : "Show ID";
        RefreshAll();
    }
}

public sealed class EqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class AppModel : Observable
{
    private readonly SessionService _sessions;
    private readonly IApiClient _api;
    private readonly ISettingsStore _settings;
    private readonly ISystemMetrics _metrics;
    private readonly IStartupRegistration _startup;
    private readonly IAppLog _log;
    private readonly Dispatcher _dispatcher;
    private int _watch;
    private CancellationTokenSource _lifetime = new();

    public AppModel(SessionService sessions, IApiClient api, ISettingsStore settings, ISystemMetrics metrics, IStartupRegistration startup, IAppLog log, Dispatcher dispatcher)
    {
        _sessions = sessions;
        _api = api;
        _settings = settings;
        _metrics = metrics;
        _startup = startup;
        _log = log;
        _dispatcher = dispatcher;
        var stored = settings.Load();
        Text.Use(stored.Language);
        Language = stored.Language;
        ReduceMotion = stored.ReduceMotion;
        StartWithWindows = startup.IsEnabled();
        ApiBaseUrl = stored.ApiBaseUrl;
        Interval = stored.MetricsIntervalSeconds.ToString();
        Version = typeof(AppModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        LoginCommand = new RelayCommand(LoginAsync);
        LogoutCommand = new RelayCommand(LogoutAsync);
        SaveCommand = new RelayCommand(SaveAsync);
        RefreshCommand = new RelayCommand(async () => await ReadMetricsAsync());
        CopyCommand = new RelayCommand(CopySummaryAsync);
        OpenCommand = new RelayCommand(OpenSiteAsync);
        CopyIdCommand = new RelayCommand(CopyIdAsync);
    }

    public UiText Text { get; } = new();
    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand OpenCommand { get; }
    public ICommand CopyIdCommand { get; }

    private bool _showShell;
    private string _section = "Home";
    private bool _busy;
    private string _loginDeviceId = "";
    private string _loginCode = "";
    private bool _remember = true;
    private bool _hideInput;
    private string _loginError = "";
    private string _notice = "";
    private string _auth = "";
    private string _deviceStatus = "";
    private string _plan = "FREE";
    private string _activity = "";
    private string _deviceId = "";
    private bool _mask = true;
    private string _lastVerified = "";
    private string _cpu = "";
    private string _memory = "";
    private string _gpu = "";
    private string _disk = "";
    private string _network = "";
    private string _language = "ko";
    private bool _reduceMotion;
    private bool _startWithWindows;
    private string _apiBaseUrl = "";
    private string _interval = "2";
    private string _settingsMessage = "";

    public bool ShowShell { get => _showShell; private set => Set(ref _showShell, value); }
    public string Section { get => _section; set { if (SetAndReturn(ref _section, value)) WatchIfNeeded(); } }
    public bool Busy { get => _busy; private set => Set(ref _busy, value); }
    public string LoginDeviceId { get => _loginDeviceId; set => Set(ref _loginDeviceId, value); }
    public string LoginCode { get => _loginCode; set => Set(ref _loginCode, value); }
    public bool Remember { get => _remember; set => Set(ref _remember, value); }
    public bool HideInput { get => _hideInput; set => Set(ref _hideInput, value); }
    public string LoginError { get => _loginError; private set => Set(ref _loginError, value); }
    public string Notice { get => _notice; private set => Set(ref _notice, value); }
    public string Auth { get => _auth; private set => Set(ref _auth, value); }
    public string DeviceStatus { get => _deviceStatus; private set => Set(ref _deviceStatus, value); }
    public string Plan { get => _plan; private set => Set(ref _plan, value); }
    public string Activity { get => _activity; private set => Set(ref _activity, value); }
    public string ShownId => _mask && _deviceId.Length > 9 ? _deviceId[..4] + "-••••-••••-••••-" + _deviceId[^4..] : _deviceId;
    public string MaskLabel => _mask ? Text.Reveal : Text.Mask;
    public string LastVerified { get => _lastVerified; private set => Set(ref _lastVerified, value); }
    public string Cpu { get => _cpu; private set => Set(ref _cpu, value); }
    public string Memory { get => _memory; private set => Set(ref _memory, value); }
    public string Gpu { get => _gpu; private set => Set(ref _gpu, value); }
    public string Disk { get => _disk; private set => Set(ref _disk, value); }
    public string Network { get => _network; private set => Set(ref _network, value); }
    public string Version { get; }
    public string Language { get => _language; set => Set(ref _language, value); }
    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public string ApiBaseUrl { get => _apiBaseUrl; set => Set(ref _apiBaseUrl, value); }
    public string Interval { get => _interval; set => Set(ref _interval, value); }
    public string SettingsMessage { get => _settingsMessage; private set => Set(ref _settingsMessage, value); }
    public bool ShowSpinner => Busy && !ReduceMotion;

    public async Task InitializeAsync()
    {
        try
        {
            var result = await _sessions.RestoreAsync(_lifetime.Token);
            if (result.Notice == "reset") Notice = Text.VaultReset;
            if (result.Kind == StartupKind.SignedOut)
            {
                ShowShell = false;
                return;
            }
            ShowShell = true;
            if (result.Kind == StartupKind.Offline)
            {
                Auth = Text.Offline;
                DeviceStatus = Text.Offline;
                var session = _sessions.ActiveSession();
                _deviceId = session?.DeviceId ?? "";
                RaiseShownId();
                return;
            }
            Auth = Text.Verified;
            await LoadProfileAsync();
        }
        catch (Exception exception)
        {
            _log.Error("startup", exception);
            ShowShell = false;
            Notice = Text.VaultReset;
        }
    }

    public void ToggleMask()
    {
        _mask = !_mask;
        RaiseShownId();
        Raise(nameof(MaskLabel));
    }

    public void Shutdown()
    {
        _lifetime.Cancel();
        if (_metrics is IDisposable disposable) disposable.Dispose();
    }

    private async Task LoginAsync()
    {
        Busy = true;
        Raise(nameof(ShowSpinner));
        LoginError = "";
        try
        {
            var result = await _sessions.LoginAsync(LoginDeviceId, LoginCode, Remember, _lifetime.Token);
            if (result.Kind != LoginKind.Success)
            {
                LoginError = result.Kind switch
                {
                    LoginKind.NeedsActivation => Text.NeedCode,
                    LoginKind.NeedsRecovery => Text.NeedRecovery,
                    LoginKind.InvalidId => Text.BadId,
                    LoginKind.Revoked => Text.Revoked,
                    LoginKind.Rejected => Text.Rejected,
                    _ => Text.Offline,
                };
                return;
            }
            ShowShell = true;
            Auth = Text.Verified;
            Section = "Home";
            await LoadProfileAsync();
        }
        finally
        {
            Busy = false;
            Raise(nameof(ShowSpinner));
        }
    }

    private async Task LogoutAsync()
    {
        await _sessions.LogoutAsync(_lifetime.Token);
        ShowShell = false;
        _deviceId = "";
        Auth = "";
        Activity = "";
        RaiseShownId();
    }

    private async Task LoadProfileAsync()
    {
        var session = _sessions.ActiveSession();
        if (session is null) return;
        _deviceId = session.DeviceId;
        RaiseShownId();
        try
        {
            var me = await _api.MeAsync(session.AccessToken, _lifetime.Token);
            if (!me.Ok || me.Value is null)
            {
                if (me.Code == "device_revoked")
                {
                    ShowShell = false;
                    LoginError = Text.Revoked;
                    return;
                }
                Auth = Text.Offline;
                return;
            }
            Auth = Text.Verified;
            DeviceStatus = me.Value.Status;
            Plan = me.Value.Plan;
            LastVerified = me.Value.LastSeenAt ?? "";
            Activity = string.Join(Environment.NewLine, me.Value.RecentEvents.Select(item => $"{item.At}  {item.Type}  {item.Result}"));
            if (string.IsNullOrWhiteSpace(Activity)) Activity = "—";
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            Auth = Text.Offline;
        }
    }

    private async Task ReadMetricsAsync()
    {
        var snapshot = await Task.Run(_metrics.Read);
        await _dispatcher.InvokeAsync(() =>
        {
            Cpu = snapshot.CpuPending ? Text.Measuring : snapshot.CpuPercent is null ? Text.Unavailable : $"{snapshot.CpuPercent.Value:0}%";
            Memory = snapshot.MemoryTotalBytes is null ? Text.Unavailable : $"{Format(snapshot.MemoryUsedBytes)} / {Format(snapshot.MemoryTotalBytes)}";
            Gpu = snapshot.GpuName is null && !snapshot.GpuAvailable
                ? Text.Unavailable
                : $"{snapshot.GpuName ?? Text.Unavailable} · {(snapshot.GpuPercent is null ? (snapshot.GpuAvailable ? Text.Measuring : Text.Unavailable) : $"{snapshot.GpuPercent.Value:0}%")}";
            Disk = snapshot.DiskTotalBytes is null ? Text.Unavailable : $"{Format(snapshot.DiskUsedBytes)} / {Format(snapshot.DiskTotalBytes)}";
            Network = snapshot.NetworkConnected switch
            {
                true => Text.Connected,
                false => Text.Disconnected,
                null => Text.Unavailable,
            };
        });
    }

    private async Task CopySummaryAsync()
    {
        await ReadMetricsAsync();
        Clipboard.SetText($"NIVEX {Version}{Environment.NewLine}CPU {Cpu}{Environment.NewLine}Memory {Memory}{Environment.NewLine}GPU {Gpu}{Environment.NewLine}Disk {Disk}{Environment.NewLine}Network {Network}");
    }

    private Task OpenSiteAsync()
    {
        var baseUrl = _settings.Load().ApiBaseUrl.TrimEnd('/');
        Process.Start(new ProcessStartInfo(baseUrl + "/device") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private Task CopyIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_deviceId)) Clipboard.SetText(_deviceId);
        return Task.CompletedTask;
    }

    private Task SaveAsync()
    {
        if (!int.TryParse(Interval, out var seconds)) seconds = 2;
        if (!Uri.TryCreate(ApiBaseUrl.Trim(), UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            SettingsMessage = Text.Rejected;
            return Task.CompletedTask;
        }
        var settings = _settings.Load();
        settings.Language = Language == "en" ? "en" : "ko";
        settings.ReduceMotion = ReduceMotion;
        settings.ApiBaseUrl = uri.ToString().TrimEnd('/');
        settings.MetricsIntervalSeconds = Math.Clamp(seconds, 2, 30);
        if (!_startup.TrySet(StartWithWindows, out _))
        {
            StartWithWindows = _startup.IsEnabled();
            SettingsMessage = Text.Rejected;
        }
        else SettingsMessage = "";
        settings.StartWithWindows = StartWithWindows;
        _settings.Save(settings);
        Text.Use(settings.Language);
        Interval = settings.MetricsIntervalSeconds.ToString();
        ApiBaseUrl = settings.ApiBaseUrl;
        Raise(nameof(ShowSpinner));
        Raise(nameof(MaskLabel));
        return Task.CompletedTask;
    }

    private void WatchIfNeeded()
    {
        if (Section != "Overview")
        {
            _watch++;
            return;
        }
        var generation = ++_watch;
        _ = WatchAsync(generation);
    }

    private async Task WatchAsync(int generation)
    {
        try
        {
            await ReadMetricsAsync();
            var interval = Math.Clamp(_settings.Load().MetricsIntervalSeconds, 2, 30);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(interval));
            while (generation == _watch && await timer.WaitForNextTickAsync(_lifetime.Token))
                await ReadMetricsAsync();
        }
        catch (OperationCanceledException)
        {
        }
    }

    private bool SetAndReturn<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(property ?? "");
        return true;
    }

    private void Raise(string property) => Notify(property);
    private void RaiseShownId() => Raise(nameof(ShownId));
    private static string Format(ulong? bytes) => bytes is null ? "—" : $"{bytes.Value / 1073741824d:0.0} GB";
}
