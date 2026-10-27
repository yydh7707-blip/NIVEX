using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using NIVEX.Application;

namespace NIVEX.Infrastructure;

public sealed class ApiClient(ISettingsStore settings) : IApiClient
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public Task<ApiResult<ChallengeResponse>> ChallengeAsync(string deviceId, CancellationToken cancellationToken) =>
        Post<ChallengeResponse>("api/v1/auth/challenge", new { deviceId }, null, cancellationToken);

    public Task<ApiResult<TokenResponse>> ActivateAsync(string deviceId, string activationCode, string publicKeySpki, string installationId, CancellationToken cancellationToken) =>
        Post<TokenResponse>("api/v1/auth/activate", new { deviceId, activationCode, publicKeySpki, installationId }, null, cancellationToken);

    public Task<ApiResult<TokenResponse>> ProveAsync(string deviceId, string nonce, string signature, CancellationToken cancellationToken) =>
        Post<TokenResponse>("api/v1/auth/prove", new { deviceId, nonce, signature }, null, cancellationToken);

    public Task<ApiResult<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken) =>
        Post<TokenResponse>("api/v1/auth/refresh", new { refreshToken }, null, cancellationToken);

    public async Task<ApiResult<bool>> LogoutAsync(string accessToken, string refreshToken, CancellationToken cancellationToken)
    {
        var result = await Post<JsonElement>("api/v1/auth/logout", new { refreshToken }, accessToken, cancellationToken);
        return new ApiResult<bool>(result.Ok, result.Status, result.Code, result.Message, result.Ok);
    }

    public Task<ApiResult<DeviceProfile>> MeAsync(string accessToken, CancellationToken cancellationToken) =>
        Send<DeviceProfile>(HttpMethod.Get, "api/v1/devices/me", null, accessToken, cancellationToken);

    private Task<ApiResult<T>> Post<T>(string path, object body, string? accessToken, CancellationToken cancellationToken) =>
        Send<T>(HttpMethod.Post, path, body, accessToken, cancellationToken);

    private async Task<ApiResult<T>> Send<T>(HttpMethod method, string path, object? body, string? accessToken, CancellationToken cancellationToken)
    {
        var baseUrl = settings.Load().ApiBaseUrl.Trim();
        if (!Uri.TryCreate(baseUrl.TrimEnd('/') + "/", UriKind.Absolute, out var root))
            return new ApiResult<T>(false, 0, "configuration_error", "", default);
        using var request = new HttpRequestMessage(method, new Uri(root, path));
        if (!string.IsNullOrWhiteSpace(accessToken)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        if (body is not null) request.Content = JsonContent.Create(body, options: Json);
        using var response = await _http.SendAsync(request, cancellationToken);
        var text = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var error = TryRead(text);
            return new ApiResult<T>(false, (int)response.StatusCode, error?.Code ?? "http_error", error?.Message ?? "", default);
        }
        var value = string.IsNullOrWhiteSpace(text) ? default : JsonSerializer.Deserialize<T>(text, Json);
        return new ApiResult<T>(true, (int)response.StatusCode, "", "", value);
    }

    private static ApiErrorBody? TryRead(string text)
    {
        try
        {
            return JsonSerializer.Deserialize<ApiEnvelope>(text, Json)?.Error;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
