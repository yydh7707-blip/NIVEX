namespace NIVEX.Application;

public interface IApiClient
{
    Task<ApiResult<ChallengeResponse>> ChallengeAsync(string deviceId, CancellationToken cancellationToken);
    Task<ApiResult<TokenResponse>> ActivateAsync(string deviceId, string activationCode, string publicKeySpki, string installationId, CancellationToken cancellationToken);
    Task<ApiResult<TokenResponse>> ProveAsync(string deviceId, string nonce, string signature, CancellationToken cancellationToken);
    Task<ApiResult<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken);
    Task<ApiResult<bool>> LogoutAsync(string accessToken, string refreshToken, CancellationToken cancellationToken);
    Task<ApiResult<DeviceProfile>> MeAsync(string accessToken, CancellationToken cancellationToken);
    Task<ApiResult<List<GeminiModelInfo>>> GeminiModelsAsync(string accessToken, CancellationToken cancellationToken);
    Task<ApiResult<GeminiModelTestResponse>> TestGeminiModelAsync(string modelId, string accessToken, CancellationToken cancellationToken);
}

public interface IVault
{
    VaultState Load();
    void Save(VaultState state);
}

public interface ISettingsStore
{
    UserSettings Load();
    void Save(UserSettings settings);
}

public interface ISystemMetrics
{
    SystemSnapshot Read();
}

public interface IStartupRegistration
{
    bool IsEnabled();
    bool TrySet(bool enabled, out string error);
}

public interface IAppLog
{
    void Info(string message);
    void Error(string message, Exception exception);
}
