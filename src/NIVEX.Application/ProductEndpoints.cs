namespace NIVEX.Application;

public static class ProductEndpoints
{
    public const string Api = "https://nivex.yydh7707.workers.dev";
    public const string Site = "https://nivex.xyz.ci";

    public static string NormalizeApi(string? value)
    {
        var trimmed = (value ?? "").Trim().TrimEnd('/');
        if (trimmed.Length == 0
            || trimmed.Equals("http://127.0.0.1:8787", StringComparison.OrdinalIgnoreCase)
            || trimmed.Equals("http://localhost:8787", StringComparison.OrdinalIgnoreCase))
            return Api;
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return Api;
        return trimmed;
    }
}
