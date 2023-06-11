namespace AuthenticatedOtelLogger
{
    public enum AuthorizationEnvironmentOptions
    {
        NoAuth,
        ServicePrincipal,
        SystemAssignedIdentity,
        SystemAssignedIdentityWithCertificate,
        UserAssignedIdentity
    }
}
