namespace NIVEX.Domain;

public enum SessionPhase { SignedOut, Verified, OfflineUnverified, Revoked, Expired }

public static class SessionPolicy
{
    public static SessionPhase Evaluate(bool hasStoredSession, bool serverAccepted, bool offline, bool revoked, bool expired)
    {
        if (revoked) return SessionPhase.Revoked;
        if (expired) return SessionPhase.Expired;
        if (offline && hasStoredSession) return SessionPhase.OfflineUnverified;
        if (serverAccepted) return SessionPhase.Verified;
        return SessionPhase.SignedOut;
    }
}
