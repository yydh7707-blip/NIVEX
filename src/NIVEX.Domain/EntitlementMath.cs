namespace NIVEX.Domain;

public enum PlanKind { None, Free, Pro }

public readonly record struct Entitlement(string Plan, string Status, DateTimeOffset? ExpiresAt);

public static class EntitlementMath
{
    public static PlanKind Resolve(IEnumerable<Entitlement> records, DateTimeOffset now)
    {
        var usable = records.Where(record =>
            record.Status.Equals("active", StringComparison.OrdinalIgnoreCase)
            && (record.ExpiresAt is null || record.ExpiresAt > now)).ToArray();
        if (usable.Any(record => record.Plan.Equals("PRO", StringComparison.OrdinalIgnoreCase))) return PlanKind.Pro;
        if (usable.Any(record => record.Plan.Equals("FREE", StringComparison.OrdinalIgnoreCase))) return PlanKind.Free;
        return PlanKind.None;
    }
}
