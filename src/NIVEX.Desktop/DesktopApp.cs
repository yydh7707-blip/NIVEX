using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text.Json;
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
    public string LoginEyebrow { get; private set; } = "";
    public string Workspace { get; private set; } = "";
    public string WorkspaceLead { get; private set; } = "";
    public string Authentication { get; private set; } = "";
    public string Entitlement { get; private set; } = "";
    public string Account { get; private set; } = "";
    public string LocalReadings { get; private set; } = "";
    public string ReadingsPrivacy { get; private set; } = "";
    public string Preferences { get; private set; } = "";
    public string PreferencesLead { get; private set; } = "";
    public string DeviceId { get; private set; } = "";
    public string Activation { get; private set; } = "";
    public string Remember { get; private set; } = "";
    public string Hide { get; private set; } = "";
    public string Login { get; private set; } = "";
    public string Working { get; private set; } = "";
    public string Brand { get; private set; } = "";
    public string IdHint { get; private set; } = "";
    public string CodeHint { get; private set; } = "";
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
    public string Saved { get; private set; } = "";
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
    public string AiTitle { get; private set; } = "";
    public string AiProvider { get; private set; } = "";
    public string AiCurrent { get; private set; } = "";
    public string AiAvailable { get; private set; } = "";
    public string AiModelIdLabel { get; private set; } = "";
    public string AiDescription { get; private set; } = "";
    public string AiRefresh { get; private set; } = "";
    public string AiTest { get; private set; } = "";
    public string AiDefault { get; private set; } = "";
    public string AiSave { get; private set; } = "";

    public void Use(string language)
    {
        var ko = language != "en";
        Welcome = ko ? "NIVEX에 오신 것을\n환영합니다" : "Welcome to\nNIVEX";
        Lead = ko ? "등록된 장치 정보로 로그인하세요." : "Sign in with the device registered to this PC.";
        LoginEyebrow = ko ? "NIVEX  /  장치 인증" : "NIVEX  /  DEVICE AUTHENTICATION";
        Workspace = ko ? "내 작업 공간" : "YOUR WORKSPACE";
        WorkspaceLead = ko ? "이 PC의 인증 상태와 이용권, 최근 활동을 확인하세요." : "Your device status, entitlement, and recent activity.";
        Authentication = ko ? "인증 상태" : "AUTHENTICATION";
        Entitlement = ko ? "이용 권한" : "ENTITLEMENT";
        Account = ko ? "계정" : "ACCOUNT";
        LocalReadings = ko ? "로컬 시스템 정보" : "LOCAL READINGS";
        ReadingsPrivacy = ko ? "시스템 정보는 이 PC에서만 수집하며 NIVEX로 전송하지 않습니다." : "Readings are collected on this PC and are not sent to NIVEX.";
        Preferences = ko ? "환경 설정" : "PREFERENCES";
        PreferencesLead = ko ? "이 PC에서 NIVEX의 모양과 연결 방식을 설정합니다." : "Personalize how NIVEX looks and connects on this PC.";
        DeviceId = ko ? "장치 ID" : "Device ID";
        Activation = ko ? "활성화 코드" : "Activation code";
        Remember = ko ? "이 기기에서 로그인 유지" : "Keep me signed in on this PC";
        Hide = ko ? "입력 숨기기" : "Hide input";
        Login = ko ? "로그인" : "Sign in";
        Working = ko ? "로그인 중" : "Signing in";
        Brand = ko ? "장치 ID로 이 PC를 인증합니다." : "Authenticate this PC with a Device ID.";
        IdHint = "NIV-XXXX-XXXX-XXXX-XXXX";
        CodeHint = ko ? "웹에서 발급한 코드" : "Code from the website";
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
        Saved = ko ? "설정을 저장했습니다." : "Settings saved.";
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
        AiTitle = ko ? "NIVEX Intelligence" : "NIVEX Intelligence";
        AiProvider = ko ? "Google Gemini · 서버 보안 연결" : "Google Gemini · server-side secure connection";
        AiCurrent = ko ? "현재 저장된 모델" : "Currently saved model";
        AiAvailable = ko ? "사용 가능한 모델" : "Available models";
        AiModelIdLabel = ko ? "API 모델 ID" : "API model ID";
        AiDescription = ko
            ? "Gemini API에서 실제 제공하는 모델만 표시합니다. 저장 전에 연결 및 JSON 응답 테스트가 필요합니다. API 키는 서버에만 보관됩니다."
            : "Only models returned by the Gemini API are listed. Test the connection and JSON output before saving. The API key remains on the server.";
        AiRefresh = ko ? "모델 새로고침" : "Refresh models";
        AiTest = ko ? "연결 테스트" : "Test connection";
        AiDefault = ko ? "모델 선택 초기화" : "Clear selection";
        AiSave = ko ? "모델 저장" : "Save model";
        RefreshAll();
    }
}

public sealed class EqualsVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EqualsBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        string.Equals(value?.ToString(), parameter?.ToString(), StringComparison.Ordinal);
    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is true ? parameter?.ToString() ?? "" : Binding.DoNothing;
}

public sealed class AppModel : Observable
{
    private readonly SessionService _sessions;
    private readonly IApiClient _api;
    private readonly ISettingsStore _settings;
    private readonly IStartupRegistration _startup;
    private readonly IAppLog _log;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource _lifetime = new();

    public AppModel(SessionService sessions, IApiClient api, ISettingsStore settings, ISystemMetrics metrics, IStartupRegistration startup, IAppLog log, Dispatcher dispatcher)
    {
        _sessions = sessions;
        _api = api;
        _settings = settings;
        _startup = startup;
        _log = log;
        _dispatcher = dispatcher;
        var stored = settings.Load();
        Text.Use(stored.Language);
        ToolText.Instance.Use(stored.Language);
        _language = stored.Language == "ko" ? "ko" : "en";
        _selectedGeminiModelId = stored.GeminiModelId;
        _savedGeminiModelId = stored.GeminiModelId;
        ReduceMotion = stored.ReduceMotion;
        StartWithWindows = startup.IsEnabled();
        Version = typeof(AppModel).Assembly.GetName().Version?.ToString(3) ?? "0.1.0";
        Tools = new ToolkitModel(metrics, dispatcher, section => Section = section, () => settings.Load().MetricsIntervalSeconds, Version);
        NavItems =
        [
            new("Home", "M2,8 L9,2 16,8 16,16 11,16 11,11 7,11 7,16 2,16 Z", () => Text.Home),
            new("Fix", "M3,15 L10,8 M10,8 A3.5,3.5 0 1 1 14,4 L11.5,6.5 12.5,7.5 15,5 A3.5,3.5 0 0 1 10,8", () => ToolText.Get("navFix")),
            new("Monitor", "M2,15 L6,10 9,12 14,5 17,7", () => ToolText.Get("navMonitor")),
            new("Network", "M9,2 A7,7 0 1 0 9,16 A7,7 0 1 0 9,2 M2,9 L16,9 M9,2 C6,5 6,13 9,16 M9,2 C12,5 12,13 9,16", () => ToolText.Get("navNetwork")),
            new("Flow", "M2,4 L7,4 7,9 12,9 12,14 16,14 M14,12 L16,14 14,16", () => ToolText.Get("navFlow")),
            new("Profiles", "M3,3 L15,3 15,15 3,15 Z M3,7 L15,7 M7,7 L7,15", () => ToolText.Get("navProfiles")),
            new("Insight", "M4,1 L14,1 17,4 17,17 4,17 Z M14,1 L14,4 17,4 M7,8 L14,8 M7,11 L14,11 M7,14 L12,14", () => ToolText.Get("navInsight")),
            new("Device", "M2,4 L16,4 16,12 2,12 Z M6,15 L12,15 M9,12 L9,15", () => Text.Device),
            new("Settings", "M9,1 L11,1 12,4 14,5 17,4 18,6 16,9 16,11 18,14 17,16 14,15 12,16 11,19 9,19 8,16 6,15 3,16 2,14 4,11 4,9 2,6 3,4 6,5 8,4 Z M10,7 A3,3 0 1 0 10,13 A3,3 0 1 0 10,7", () => Text.Settings),
        ];
        NavItems[0].IsSelected = true;
        LoginCommand = new RelayCommand(LoginAsync);
        LogoutCommand = new RelayCommand(LogoutAsync);
        SaveCommand = new RelayCommand(SaveAsync);
        LoadGeminiModelsCommand = new RelayCommand(LoadGeminiModelsAsync);
        TestGeminiModelCommand = new RelayCommand(TestGeminiModelAsync);
        SaveGeminiModelCommand = new RelayCommand(SaveGeminiModelAsync);
        DefaultGeminiModelCommand = new RelayCommand(SelectDefaultGeminiModelAsync);
        RefreshCommand = new RelayCommand(async () => await ReadMetricsAsync());
        CopyCommand = new RelayCommand(CopySummaryAsync);
        OpenCommand = new RelayCommand(OpenSiteAsync);
        CopyIdCommand = new RelayCommand(CopyIdAsync);
    }

    public UiText Text { get; } = new();
    public ToolkitModel Tools { get; }
    public IReadOnlyList<NavEntry> NavItems { get; }
    public ICommand LoginCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand SaveCommand { get; }
    public ICommand LoadGeminiModelsCommand { get; }
    public ICommand TestGeminiModelCommand { get; }
    public ICommand SaveGeminiModelCommand { get; }
    public ICommand DefaultGeminiModelCommand { get; }
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
    private string _language = "en";
    private bool _reduceMotion;
    private bool _startWithWindows;
    private string _settingsMessage = "";
    private string _selectedGeminiModelId = "";
    private string _savedGeminiModelId = "";
    private string _testedGeminiModelId = "";
    private string _geminiStatus = "";
    private bool _loadingGeminiModels;

    public bool ShowShell { get => _showShell; private set => Set(ref _showShell, value); }
    public string Section
    {
        get => _section;
        set
        {
            if (!SetAndReturn(ref _section, value)) return;
            foreach (var entry in NavItems) entry.IsSelected = entry.Section == value;
            Tools.OnSection(value);
            if (value == "Settings" && ShowShell) LoadGeminiModelsCommand.Execute(null);
        }
    }
    public bool Busy
    {
        get => _busy;
        private set
        {
            if (EqualityComparer<bool>.Default.Equals(_busy, value)) return;
            _busy = value;
            Notify(nameof(Busy));
            Notify(nameof(ShowSpinner));
            Notify(nameof(LoginLabel));
        }
    }
    public string LoginLabel => Busy ? Text.Working : Text.Login;
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
    public string Language
    {
        get => _language;
        set
        {
            var next = value == "ko" ? "ko" : "en";
            if (_language == next) return;
            _language = next;
            Notify(nameof(Language));
            ApplyLanguage();
        }
    }
    public bool ReduceMotion { get => _reduceMotion; set => Set(ref _reduceMotion, value); }
    public bool StartWithWindows { get => _startWithWindows; set => Set(ref _startWithWindows, value); }
    public string SettingsMessage { get => _settingsMessage; private set => Set(ref _settingsMessage, value); }
    public ObservableCollection<GeminiModelInfo> GeminiModels { get; } = [];
    public string SelectedGeminiModelId
    {
        get => _selectedGeminiModelId;
        set
        {
            if (!SetAndReturn(ref _selectedGeminiModelId, value ?? "")) return;
            _testedGeminiModelId = "";
            Notify(nameof(IsGeminiModelTested));
            Notify(nameof(CurrentGeminiModel));
            GeminiStatus = "";
        }
    }
    public string CurrentGeminiModel => string.IsNullOrWhiteSpace(_savedGeminiModelId)
        ? TextForLanguage("선택된 모델 없음", "No model selected")
        : GeminiModels.FirstOrDefault(model => model.Id == _savedGeminiModelId)?.DisplayName ?? _savedGeminiModelId;
    public string GeminiStatus { get => _geminiStatus; private set => Set(ref _geminiStatus, value); }
    public bool IsLoadingGeminiModels { get => _loadingGeminiModels; private set => Set(ref _loadingGeminiModels, value); }
    public bool IsGeminiModelTested => !string.IsNullOrEmpty(SelectedGeminiModelId) && SelectedGeminiModelId == _testedGeminiModelId;
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
        Tools.StopAll();
        Tools.Dispose();
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
        Tools.StopAll();
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
        var sample = await Tools.Service.SampleOnceAsync() ?? Tools.Service.Latest;
        var snapshot = sample?.Raw;
        await _dispatcher.InvokeAsync(() =>
        {
            if (snapshot is null)
            {
                Cpu = Memory = Gpu = Disk = Network = Text.Unavailable;
                return;
            }
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
        Process.Start(new ProcessStartInfo(ProductEndpoints.Site + "/device") { UseShellExecute = true });
        return Task.CompletedTask;
    }

    private Task CopyIdAsync()
    {
        if (!string.IsNullOrWhiteSpace(_deviceId)) Clipboard.SetText(_deviceId);
        return Task.CompletedTask;
    }

    private Task SaveAsync()
    {
        var settings = _settings.Load();
        settings.Language = Language;
        settings.ReduceMotion = ReduceMotion;
        var savedStartup = _startup.TrySet(StartWithWindows, out _);
        if (!savedStartup)
        {
            StartWithWindows = _startup.IsEnabled();
            SettingsMessage = Text.Rejected;
        }
        settings.StartWithWindows = StartWithWindows;
        _settings.Save(settings);
        Text.Use(settings.Language);
        UseToolLanguage(settings.Language);
        SettingsMessage = savedStartup ? Text.Saved : Text.Rejected;
        Raise(nameof(ShowSpinner));
        Raise(nameof(LoginLabel));
        Raise(nameof(MaskLabel));
        return Task.CompletedTask;
    }

    private async Task LoadGeminiModelsAsync()
    {
        if (IsLoadingGeminiModels) return;
        var session = _sessions.ActiveSession();
        if (session is null)
        {
            GeminiStatus = TextForLanguage("모델 목록을 보려면 먼저 로그인하세요.", "Sign in to load available models.");
            return;
        }
        IsLoadingGeminiModels = true;
        GeminiStatus = TextForLanguage("Gemini 모델을 확인하는 중...", "Checking Gemini models...");
        try
        {
            var result = await _api.GeminiModelsAsync(session.AccessToken, _lifetime.Token);
            if (!result.Ok || result.Value is null)
            {
                GeminiStatus = AiErrorMessage(result.Code);
                return;
            }
            GeminiModels.Clear();
            foreach (var model in result.Value) GeminiModels.Add(model);
            Notify(nameof(CurrentGeminiModel));
            if (GeminiModels.Count == 0)
                GeminiStatus = TextForLanguage("generateContent를 지원하는 모델을 찾지 못했습니다.", "No generateContent-compatible models were returned.");
            else if (!string.IsNullOrEmpty(_savedGeminiModelId) && !GeminiModels.Any(model => model.Id == _savedGeminiModelId))
                GeminiStatus = TextForLanguage("저장된 모델을 사용할 수 없습니다. 기존 설정은 유지됩니다.", "The saved model is unavailable. Your saved setting has been preserved.");
            else
                GeminiStatus = TextForLanguage($"{GeminiModels.Count}개 모델을 확인했습니다. 구조화된 출력 호환성은 연결 테스트 후 확인됩니다.", $"{GeminiModels.Count} models found. Structured output compatibility is confirmed by testing.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (TaskCanceledException)
        {
            GeminiStatus = TextForLanguage("모델 목록 요청 시간이 초과되었습니다.", "The model-list request timed out.");
        }
        catch (HttpRequestException)
        {
            GeminiStatus = TextForLanguage("서버에 연결할 수 없습니다. 네트워크를 확인하고 다시 시도하세요.", "Could not connect to the server. Check your network and retry.");
        }
        catch (JsonException)
        {
            GeminiStatus = TextForLanguage("서버 응답을 읽을 수 없습니다. 다시 시도하세요.", "The server response could not be read. Please retry.");
        }
        finally
        {
            IsLoadingGeminiModels = false;
        }
    }

    private async Task TestGeminiModelAsync()
    {
        var session = _sessions.ActiveSession();
        if (session is null)
        {
            GeminiStatus = TextForLanguage("먼저 로그인하세요.", "Sign in before testing a model.");
            return;
        }
        if (string.IsNullOrWhiteSpace(SelectedGeminiModelId))
        {
            GeminiStatus = TextForLanguage("연결 테스트할 모델을 선택하세요.", "Select a model to test.");
            return;
        }
        var modelId = SelectedGeminiModelId;
        GeminiStatus = TextForLanguage("선택한 모델과 JSON 응답을 테스트하는 중...", "Testing the selected model and JSON output...");
        try
        {
            var result = await _api.TestGeminiModelAsync(modelId, session.AccessToken, _lifetime.Token);
            if (!result.Ok || result.Value is null || !result.Value.Success || !result.Value.StructuredOutput)
            {
                _testedGeminiModelId = "";
                Notify(nameof(IsGeminiModelTested));
                GeminiStatus = AiErrorMessage(result.Code);
                return;
            }
            if (SelectedGeminiModelId != modelId)
            {
                GeminiStatus = TextForLanguage("테스트 중 선택한 모델이 바뀌었습니다. 현재 선택을 다시 테스트하세요.", "The selection changed during testing. Test the currently selected model.");
                return;
            }
            _testedGeminiModelId = result.Value.ModelId;
            Notify(nameof(IsGeminiModelTested));
            GeminiStatus = TextForLanguage("연결 성공. 이 모델의 JSON 구조화 응답을 확인했습니다.", "Connection succeeded. Structured JSON output was verified for this model.");
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (TaskCanceledException)
        {
            GeminiStatus = TextForLanguage("모델 테스트 시간이 초과되었습니다.", "The model test timed out.");
        }
        catch (HttpRequestException)
        {
            GeminiStatus = TextForLanguage("서버에 연결할 수 없습니다. 네트워크를 확인하고 다시 시도하세요.", "Could not connect to the server. Check your network and retry.");
        }
        catch (JsonException)
        {
            GeminiStatus = TextForLanguage("서버 응답을 읽을 수 없습니다. 다시 시도하세요.", "The server response could not be read. Please retry.");
        }
    }

    private Task SaveGeminiModelAsync()
    {
        if (!string.IsNullOrEmpty(SelectedGeminiModelId) &&
            (!GeminiModels.Any(model => model.Id == SelectedGeminiModelId) || !IsGeminiModelTested))
        {
            GeminiStatus = TextForLanguage("저장 전에 사용 가능한 모델을 선택하고 연결 테스트를 완료하세요.", "Select an available model and complete its connection test before saving.");
            return Task.CompletedTask;
        }
        try
        {
            var settings = _settings.Load();
            settings.GeminiModelId = SelectedGeminiModelId;
            _settings.Save(settings);
            _savedGeminiModelId = SelectedGeminiModelId;
            Notify(nameof(CurrentGeminiModel));
            GeminiStatus = TextForLanguage("모델 설정을 저장했습니다.", "Model setting saved.");
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            GeminiStatus = TextForLanguage("설정을 저장하지 못했습니다. 권한과 디스크 상태를 확인하세요.", "Could not save settings. Check permissions and disk status.");
            _log.Error("gemini_model_save", exception);
        }
        return Task.CompletedTask;
    }

    private Task SelectDefaultGeminiModelAsync()
    {
        SelectedGeminiModelId = "";
        GeminiStatus = TextForLanguage("모델 선택을 초기화했습니다. 저장을 눌러 적용하세요.", "Model selection cleared. Save to apply it.");
        return Task.CompletedTask;
    }

    private string AiErrorMessage(string code) => code switch
    {
        "ai_not_configured" => TextForLanguage("Gemini가 서버에 아직 설정되지 않았습니다. 운영자 설정이 필요합니다.", "Gemini is not configured on the server yet. Operator setup is required."),
        "ai_provider_auth_error" => TextForLanguage("Gemini 서버 인증에 실패했습니다. 운영자에게 문의하세요.", "Gemini server authentication failed. Contact the operator."),
        "ai_model_unavailable" => TextForLanguage("선택한 모델을 사용할 수 없습니다. 목록을 새로고침하세요.", "The selected model is unavailable. Refresh the list."),
        "invalid_model" => TextForLanguage("유효한 모델을 선택하세요.", "Select a valid model."),
        "ai_rate_limited" or "rate_limited" => TextForLanguage("요청 한도에 도달했습니다. 나중에 다시 시도하세요.", "The request limit was reached. Try again later."),
        "ai_response_invalid" => TextForLanguage("모델이 필요한 JSON 형식으로 응답하지 않았습니다.", "The model did not return the required JSON format."),
        "ai_timeout" => TextForLanguage("Gemini 요청 시간이 초과되었습니다.", "The Gemini request timed out."),
        "ai_network_error" => TextForLanguage("Gemini 서비스에 연결할 수 없습니다.", "Could not connect to Gemini."),
        _ => TextForLanguage("Gemini 요청에 실패했습니다. 잠시 후 다시 시도하세요.", "The Gemini request failed. Please retry later."),
    };

    private string TextForLanguage(string korean, string english) => Language == "ko" ? korean : english;

    private void ApplyLanguage()
    {
        var settings = _settings.Load();
        settings.Language = _language;
        _settings.Save(settings);
        Text.Use(_language);
        UseToolLanguage(_language);
        SettingsMessage = Text.Saved;
        GeminiStatus = "";
        Raise(nameof(LoginLabel));
        Raise(nameof(MaskLabel));
        Raise(nameof(CurrentGeminiModel));
        if (Section == "Settings" && ShowShell) LoadGeminiModelsCommand.Execute(null);
    }

    private void UseToolLanguage(string language)
    {
        ToolText.Instance.Use(language);
        foreach (var entry in NavItems) entry.Refresh();
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
