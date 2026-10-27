using System.Security.Cryptography;

namespace NIVEX.Application;

public static class DeviceKeys
{
    public static (string PublicSpki, string PrivatePkcs8) Create()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return (Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo()), Convert.ToBase64String(ecdsa.ExportPkcs8PrivateKey()));
    }

    public static string PublicFromPrivate(string privatePkcs8)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privatePkcs8), out _);
        return Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
    }

    public static string Sign(string privatePkcs8, string payload)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(privatePkcs8), out _);
        return Convert.ToBase64String(ecdsa.SignData(System.Text.Encoding.UTF8.GetBytes(payload), HashAlgorithmName.SHA256));
    }
}
