using NIVEX.Domain;

namespace NIVEX.Application;

public sealed class SessionService(IApiClient api, IVault vault)
{
    public async Task<LoginResult> LoginAsync(string deviceId, string? activationCode, bool remember, CancellationToken cancellationToken)
    {
        if (!DeviceId.IsValid(deviceId)) return new LoginResult(LoginKind.InvalidId, "");
        var id = DeviceId.Normalize(deviceId);
        ApiResult<ChallengeResponse> challenge;
        try
        {
            challenge = await api.ChallengeAsync(id, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return NetworkFailure(exception, cancellationToken);
        }
        if (!challenge.Ok) return MapFailure(challenge.Code);
        var status = challenge.Value?.Status ?? "";
        if (status is "unknown") return new LoginResult(LoginKind.InvalidId, "");
        if (status is "revoked") return new LoginResult(LoginKind.Revoked, "");
        if (status is "unclaimed")
        {
            if (string.IsNullOrWhiteSpace(activationCode)) return new LoginResult(LoginKind.NeedsActivation, "");
            var state = LoadState();
            var (publicKey, privateKey) = EnsureKey(state, id);
            ApiResult<TokenResponse> activated;
            try
            {
                activated = await api.ActivateAsync(id, activationCode.Trim().ToUpperInvariant(), publicKey, state.InstallationId, cancellationToken);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                return NetworkFailure(exception, cancellationToken);
            }
            if (activated.Ok && activated.Value is not null)
            {
                StoreSession(state, activated.Value, remember);
                return new LoginResult(LoginKind.Success, "");
            }
            if (activated.Code != "already_activated") return MapFailure(activated.Code);
            challenge = await api.ChallengeAsync(id, cancellationToken);
            if (!challenge.Ok) return MapFailure(challenge.Code);
        }
        var current = LoadState();
        if (!current.PrivateKeysPkcs8Base64.ContainsKey(id) || string.IsNullOrWhiteSpace(challenge.Value?.Nonce))
            return new LoginResult(LoginKind.NeedsRecovery, "");
        var signature = DeviceKeys.Sign(current.PrivateKeysPkcs8Base64[id], challenge.Value!.Nonce!);
        ApiResult<TokenResponse> proved;
        try
        {
            proved = await api.ProveAsync(id, challenge.Value.Nonce!, signature, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new LoginResult(LoginKind.Unavailable, "");
        }
        if (!proved.Ok || proved.Value is null) return MapFailure(proved.Code);
        StoreSession(current, proved.Value, remember);
        return new LoginResult(LoginKind.Success, "");
    }

    public async Task<StartupResult> RestoreAsync(CancellationToken cancellationToken)
    {
        var state = vault.Load();
        var notice = state.WasReset ? "reset" : null;
        var session = state.Session;
        if (session is null || !session.Remember || string.IsNullOrWhiteSpace(session.RefreshToken))
            return new StartupResult(StartupKind.SignedOut, notice);
        try
        {
            var refreshed = await api.RefreshAsync(session.RefreshToken, cancellationToken);
            if (refreshed.Ok && refreshed.Value is not null)
            {
                StoreSession(state, refreshed.Value, true);
                return new StartupResult(StartupKind.Verified, notice);
            }
            if (refreshed.Status is 401 or 403)
            {
                state.Session = null;
                vault.Save(state);
                return new StartupResult(StartupKind.SignedOut, notice);
            }
            return new StartupResult(StartupKind.Offline, notice);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            return new StartupResult(StartupKind.Offline, notice);
        }
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        var state = vault.Load();
        var session = state.Session;
        state.Session = null;
        vault.Save(state);
        if (session is null) return;
        try
        {
            await api.LogoutAsync(session.AccessToken, session.RefreshToken, cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
        }
    }

    public PersistedSession? CurrentSession() => vault.Load().Session;

    private VaultState LoadState()
    {
        var state = vault.Load();
        if (string.IsNullOrWhiteSpace(state.InstallationId))
        {
            state.InstallationId = Guid.NewGuid().ToString();
            vault.Save(state);
        }
        return state;
    }

    private (string PublicKey, string PrivateKey) EnsureKey(VaultState state, string deviceId)
    {
        if (!state.PrivateKeysPkcs8Base64.TryGetValue(deviceId, out var privateKey))
        {
            var created = DeviceKeys.Create();
            state.PrivateKeysPkcs8Base64[deviceId] = created.PrivatePkcs8;
            vault.Save(state);
            return (created.PublicSpki, created.PrivatePkcs8);
        }
        return (DeviceKeys.PublicFromPrivate(privateKey), privateKey);
    }

    private void StoreSession(VaultState state, TokenResponse tokens, bool remember)
    {
        var session = new PersistedSession
        {
            DeviceId = tokens.DeviceId,
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            AccessExpiresAt = DateTimeOffset.TryParse(tokens.ExpiresAt, out var expires) ? expires : DateTimeOffset.UtcNow.AddMinutes(10),
            Remember = remember,
        };
        state.Session = remember ? session : null;
        vault.Save(state);
        if (!remember) Held = session;
    }

    public PersistedSession? Held { get; private set; }

    public PersistedSession? ActiveSession()
    {
        var stored = vault.Load().Session;
        return stored ?? Held;
    }

    private static LoginResult MapFailure(string code) => code switch
    {
        "device_revoked" => new LoginResult(LoginKind.Revoked, code),
        "invalid_device_id" or "not_found" => new LoginResult(LoginKind.InvalidId, code),
        "activation_required" or "device_already_claimed" or "invalid_signature" => new LoginResult(LoginKind.NeedsRecovery, code),
        "activation_invalid" => new LoginResult(LoginKind.Rejected, code),
        "rate_limited" => new LoginResult(LoginKind.Rejected, code),
        _ => new LoginResult(LoginKind.Unavailable, code),
    };

    private static LoginResult NetworkFailure(Exception exception, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            throw new OperationCanceledException("The login request was canceled.", exception, cancellationToken);
        return new LoginResult(exception is TaskCanceledException ? LoginKind.TimedOut : LoginKind.Unavailable, "");
    }
}
