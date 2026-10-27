using System.Security.Cryptography;
using NIVEX.Application;
using NIVEX.Domain;
using Xunit;

namespace NIVEX.UnitTests;

public sealed class DomainTests
{
    [Fact]
    public void DeviceId_rejects_sequential_and_email_forms()
    {
        Assert.False(DeviceId.IsValid("1"));
        Assert.False(DeviceId.IsValid("user@example.com"));
        Assert.False(DeviceId.IsValid("NIV-0000-0000-0000-0000"));
        Assert.True(DeviceId.IsValid(" niv-abcd-efgh-jklm-npqr "));
    }

    [Fact]
    public void Cpu_percent_ignores_a_zero_baseline_and_backwards_counters()
    {
        Assert.Null(CpuMath.Percent(0, 0, 0, 0, 0, 0));
        Assert.Null(CpuMath.Percent(10, 10, 10, 0, 0, 0));
        var percent = CpuMath.Percent(0, 0, 0, 25, 50, 50);
        Assert.NotNull(percent);
        Assert.Equal(75, percent!.Value, 1);
    }

    [Fact]
    public void Entitlement_expires_back_to_free()
    {
        var now = DateTimeOffset.Parse("2026-04-01T00:00:00Z");
        var plan = EntitlementMath.Resolve(
        [
            new Entitlement("FREE", "active", null),
            new Entitlement("PRO", "active", DateTimeOffset.Parse("2026-03-01T00:00:00Z")),
        ], now);
        Assert.Equal(PlanKind.Free, plan);
    }

    [Fact]
    public void Offline_session_is_not_reported_as_verified()
    {
        Assert.Equal(SessionPhase.OfflineUnverified, SessionPolicy.Evaluate(true, false, true, false, false));
        Assert.Equal(SessionPhase.Verified, SessionPolicy.Evaluate(true, true, false, false, false));
        Assert.Equal(SessionPhase.Revoked, SessionPolicy.Evaluate(true, false, false, true, false));
    }

    [Fact]
    public void Device_keys_round_trip()
    {
        var created = DeviceKeys.Create();
        var again = DeviceKeys.PublicFromPrivate(created.PrivatePkcs8);
        Assert.Equal(created.PublicSpki, again);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(created.PublicSpki), out _);
        var signature = Convert.FromBase64String(DeviceKeys.Sign(created.PrivatePkcs8, "nonce"));
        Assert.True(ecdsa.VerifyData(System.Text.Encoding.UTF8.GetBytes("nonce"), signature, HashAlgorithmName.SHA256));
    }
}

public sealed class SessionServiceTests
{
    private const string Id = "NIV-ABCD-EFGH-JKLM-NPQR";

    [Fact]
    public async Task Unclaimed_device_requires_an_activation_code_before_any_request()
    {
        var api = new FakeApi { Status = "unclaimed" };
        var service = new SessionService(api, new MemoryVault());
        var result = await service.LoginAsync(Id, null, true, CancellationToken.None);
        Assert.Equal(LoginKind.NeedsActivation, result.Kind);
        Assert.Equal(0, api.Activations);
    }

    [Fact]
    public async Task Activation_stores_the_private_key_before_the_server_can_answer()
    {
        var api = new FakeApi { Status = "unclaimed" };
        var vault = new MemoryVault();
        var service = new SessionService(api, vault);
        var result = await service.LoginAsync(Id, "ABCD-EFGH-JKLM-NPQR-STUV-WXYZ-2345-6789", true, CancellationToken.None);
        Assert.Equal(LoginKind.Success, result.Kind);
        Assert.True(vault.State.PrivateKeysPkcs8Base64.ContainsKey(Id));
        Assert.Equal(Id, vault.State.Session?.DeviceId);
    }

    [Fact]
    public async Task Copied_device_id_cannot_sign_in_on_a_new_install()
    {
        var api = new FakeApi { Status = "active" };
        var result = await new SessionService(api, new MemoryVault()).LoginAsync(Id, null, true, CancellationToken.None);
        Assert.Equal(LoginKind.NeedsRecovery, result.Kind);
        Assert.Equal(0, api.Proves);
    }

    [Fact]
    public async Task Existing_key_proves_without_replacing_the_credential()
    {
        var api = new FakeApi { Status = "active" };
        var vault = new MemoryVault();
        var keys = DeviceKeys.Create();
        vault.State.PrivateKeysPkcs8Base64[Id] = keys.PrivatePkcs8;
        var service = new SessionService(api, vault);
        var result = await service.LoginAsync(Id, null, false, CancellationToken.None);
        Assert.Equal(LoginKind.Success, result.Kind);
        Assert.Null(vault.State.Session);
        Assert.NotNull(service.ActiveSession());
        Assert.True(api.Proves > 0);
    }

    [Fact]
    public async Task Revoked_and_offline_states_stay_distinct()
    {
        var revoked = new FakeApi { Status = "revoked" };
        Assert.Equal(LoginKind.Revoked, (await new SessionService(revoked, new MemoryVault()).LoginAsync(Id, null, true, CancellationToken.None)).Kind);
        var offline = new FakeApi { ThrowNetwork = true, Status = "active" };
        var vault = new MemoryVault { State = { Session = new PersistedSession { DeviceId = Id, RefreshToken = "nrt_x", Remember = true } } };
        var restored = await new SessionService(offline, vault).RestoreAsync(CancellationToken.None);
        Assert.Equal(StartupKind.Offline, restored.Kind);
        Assert.NotNull(vault.State.Session);
    }
}

file sealed class MemoryVault : IVault
{
    public VaultState State { get; set; } = new() { InstallationId = "install" };
    public VaultState Load() => State;
    public void Save(VaultState state) => State = state;
}

file sealed class FakeApi : IApiClient
{
    public string Status { get; set; } = "unknown";
    public bool ThrowNetwork { get; set; }
    public int Activations { get; private set; }
    public int Proves { get; private set; }
    private static TokenResponse Tokens(string id) => new()
    {
        AccessToken = "access",
        RefreshToken = "nrt_refresh",
        ExpiresAt = "2026-01-15T00:10:00Z",
        DeviceId = id,
    };

    public Task<ApiResult<ChallengeResponse>> ChallengeAsync(string deviceId, CancellationToken cancellationToken)
    {
        if (ThrowNetwork) throw new HttpRequestException("offline");
        return Task.FromResult(new ApiResult<ChallengeResponse>(true, 200, "", "", new ChallengeResponse { Status = Status, Nonce = Status == "active" ? "nonce-1" : null }));
    }

    public Task<ApiResult<TokenResponse>> ActivateAsync(string deviceId, string activationCode, string publicKeySpki, string installationId, CancellationToken cancellationToken)
    {
        Activations++;
        Status = "active";
        return Task.FromResult(new ApiResult<TokenResponse>(true, 201, "", "", Tokens(deviceId)));
    }

    public Task<ApiResult<TokenResponse>> ProveAsync(string deviceId, string nonce, string signature, CancellationToken cancellationToken)
    {
        Proves++;
        return Task.FromResult(new ApiResult<TokenResponse>(true, 200, "", "", Tokens(deviceId)));
    }

    public Task<ApiResult<TokenResponse>> RefreshAsync(string refreshToken, CancellationToken cancellationToken)
    {
        if (ThrowNetwork) throw new HttpRequestException("offline");
        return Task.FromResult(new ApiResult<TokenResponse>(false, 401, "session_expired", "", null));
    }

    public Task<ApiResult<bool>> LogoutAsync(string accessToken, string refreshToken, CancellationToken cancellationToken) =>
        Task.FromResult(new ApiResult<bool>(true, 200, "", "", true));

    public Task<ApiResult<DeviceProfile>> MeAsync(string accessToken, CancellationToken cancellationToken) =>
        Task.FromResult(new ApiResult<DeviceProfile>(true, 200, "", "", new DeviceProfile()));
}
