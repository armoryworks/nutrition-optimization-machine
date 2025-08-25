namespace Nom.Orch.UtilityServices
{
    public enum SecurityEventType
    {
        Login,
        FailedLogin,
        Logout,
        DataAccess,
        UnauthorizedAccess,
        BruteForceAttempt,
        AccountCompromise,
        DataBreachAttempt,
        DenialOfService,
        SuspiciousActivity,
        GeographicAnomaly,
        TimeBasedAnomaly,
        ResourceAnomaly
    }
}


