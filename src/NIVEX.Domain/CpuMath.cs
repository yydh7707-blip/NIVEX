namespace NIVEX.Domain;

public static class CpuMath
{
    public static double? Percent(ulong idle1, ulong kernel1, ulong user1, ulong idle2, ulong kernel2, ulong user2)
    {
        var idle = Subtract(idle2, idle1);
        var kernel = Subtract(kernel2, kernel1);
        var user = Subtract(user2, user1);
        if (idle is null || kernel is null || user is null) return null;
        var total = kernel.Value + user.Value;
        if (total == 0 || idle.Value > total) return null;
        var percent = (total - idle.Value) * 100d / total;
        return percent is >= 0 and <= 100 ? percent : null;
    }

    private static ulong? Subtract(ulong current, ulong previous) => current >= previous ? current - previous : null;
}
