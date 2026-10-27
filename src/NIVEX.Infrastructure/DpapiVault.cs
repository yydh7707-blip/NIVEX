using System.Security.Cryptography;
using System.Text.Json;
using NIVEX.Application;

namespace NIVEX.Infrastructure;

public sealed class DpapiVault : IVault
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = false };

    public VaultState Load()
    {
        Directory.CreateDirectory(AppPaths.Root);
        if (!File.Exists(AppPaths.Vault)) return NewState();
        try
        {
            var protectedBytes = File.ReadAllBytes(AppPaths.Vault);
            var plain = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            var state = JsonSerializer.Deserialize<VaultState>(plain, Json) ?? NewState();
            if (string.IsNullOrWhiteSpace(state.InstallationId)) state.InstallationId = Guid.NewGuid().ToString();
            return state;
        }
        catch (Exception exception) when (exception is CryptographicException or JsonException or IOException)
        {
            File.Move(AppPaths.Vault, AppPaths.Vault + ".bak", overwrite: true);
            var reset = NewState();
            reset.WasReset = true;
            Save(reset);
            return reset;
        }
    }

    public void Save(VaultState state)
    {
        Directory.CreateDirectory(AppPaths.Root);
        var copy = new VaultState
        {
            InstallationId = state.InstallationId,
            PrivateKeysPkcs8Base64 = state.PrivateKeysPkcs8Base64,
            Session = state.Session,
        };
        var plain = JsonSerializer.SerializeToUtf8Bytes(copy, Json);
        var protectedBytes = ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);
        var temporary = AppPaths.Vault + ".tmp";
        File.WriteAllBytes(temporary, protectedBytes);
        File.Move(temporary, AppPaths.Vault, overwrite: true);
    }

    private static VaultState NewState() => new() { InstallationId = Guid.NewGuid().ToString() };
}

public static class AppPaths
{
    public static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NIVEX");
    public static string Vault => Path.Combine(Root, "vault.bin");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static string LogFile => Path.Combine(Root, "logs", "nivex.log");
}
