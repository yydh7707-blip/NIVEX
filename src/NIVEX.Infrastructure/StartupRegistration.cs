using Microsoft.Win32;
using NIVEX.Application;

namespace NIVEX.Infrastructure;

public sealed class StartupRegistration : IStartupRegistration
{
    private const string ValueName = "NIVEX";
    private const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public bool IsEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
        return key?.GetValue(ValueName) is string;
    }

    public bool TrySet(bool enabled, out string error)
    {
        error = "";
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(KeyPath, writable: true) ?? throw new InvalidOperationException("run-key");
            if (!enabled)
            {
                key.DeleteValue(ValueName, throwOnMissingValue: false);
                return true;
            }
            var path = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(path))
            {
                error = "path";
                return false;
            }
            key.SetValue(ValueName, $"\"{path}\"");
            return true;
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
        {
            error = "denied";
            return false;
        }
    }
}

public sealed class FileLog : IAppLog
{
    public void Info(string message) => Write("INFO", message);
    public void Error(string message, Exception exception) => Write("ERROR", message + " " + exception.GetType().Name);

    private static void Write(string level, string message)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AppPaths.LogFile)!);
            File.AppendAllText(AppPaths.LogFile, $"{DateTimeOffset.UtcNow:O} {level} {message.Replace('\n', ' ').Replace('\r', ' ')}{Environment.NewLine}");
        }
        catch (IOException)
        {
        }
    }
}
