namespace NIVEX.Application;

public sealed class ChallengeResponse
{
    public string Status { get; set; } = "";
    public string? Nonce { get; set; }
}

public sealed class TokenResponse
{
    public string AccessToken { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string ExpiresAt { get; set; } = "";
    public string DeviceId { get; set; } = "";
}

public sealed class ActivityItem
{
    public string Type { get; set; } = "";
    public string Result { get; set; } = "";
    public string At { get; set; } = "";
}

public sealed class DeviceProfile
{
    public string DeviceId { get; set; } = "";
    public string Status { get; set; } = "";
    public string? CreatedAt { get; set; }
    public string? ClaimedAt { get; set; }
    public string? LastSeenAt { get; set; }
    public string Plan { get; set; } = "";
    public List<ActivityItem> RecentEvents { get; set; } = [];
}

public sealed class ApiErrorBody
{
    public string Code { get; set; } = "";
    public string Message { get; set; } = "";
}

public sealed class ApiEnvelope
{
    public ApiErrorBody? Error { get; set; }
}

public sealed record ApiResult<T>(bool Ok, int Status, string Code, string Message, T? Value);

public enum LoginKind { Success, NeedsActivation, NeedsRecovery, InvalidId, BadCode, Revoked, Unavailable, TimedOut, Rejected }

public sealed record LoginResult(LoginKind Kind, string ServerMessage);

public sealed class PersistedSession
{
    public string DeviceId { get; set; } = "";
    public string RefreshToken { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public DateTimeOffset AccessExpiresAt { get; set; }
    public bool Remember { get; set; }
}

public sealed class VaultState
{
    public string InstallationId { get; set; } = "";
    public Dictionary<string, string> PrivateKeysPkcs8Base64 { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public PersistedSession? Session { get; set; }
    public bool WasReset { get; set; }
}

public sealed class UserSettings
{
    public string Language { get; set; } = "en";
    public bool ReduceMotion { get; set; }
    public bool StartWithWindows { get; set; }
    public string ApiBaseUrl { get; set; } = ProductEndpoints.Api;
    public int MetricsIntervalSeconds { get; set; } = 2;
    public string GeminiModelId { get; set; } = "";
}

public sealed class GeminiModelInfo
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public int? InputTokenLimit { get; set; }
    public int? OutputTokenLimit { get; set; }
    public bool SupportsGenerateContent { get; set; }
}

public sealed class GeminiModelTestResponse
{
    public bool Success { get; set; }
    public string ModelId { get; set; } = "";
    public bool StructuredOutput { get; set; }
}

public sealed record SystemSnapshot(
    double? CpuPercent,
    bool CpuPending,
    ulong? MemoryUsedBytes,
    ulong? MemoryTotalBytes,
    string? GpuName,
    double? GpuPercent,
    bool GpuAvailable,
    ulong? DiskUsedBytes,
    ulong? DiskTotalBytes,
    bool? NetworkConnected);

public enum StartupKind { SignedOut, Verified, Offline }
public sealed record StartupResult(StartupKind Kind, string? Notice);
