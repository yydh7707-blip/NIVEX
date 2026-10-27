using System.Text.RegularExpressions;

namespace NIVEX.Domain;

public static partial class DeviceId
{
    public const string ExampleShape = "NIV-XXXX-XXXX-XXXX-XXXX";

    [GeneratedRegex("^NIV-[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}(?:-[ABCDEFGHJKLMNPQRSTUVWXYZ23456789]{4}){3}$")]
    private static partial Regex Pattern();

    public static bool IsValid(string? value) => !string.IsNullOrWhiteSpace(value) && Pattern().IsMatch(value.Trim().ToUpperInvariant());

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
