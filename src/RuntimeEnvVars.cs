namespace AuthenticatedOtelLogger
{
    public static class RuntimeEnvVars
    {
        public const string ArcDataOpenTelemetryClientIdEnvVarName = "ARCDATA_OTEL_CLIENT_ID";
        public const string AuthorizationEnvironmentEnvVarName = "AUTHORIZATION_ENV";
        public const string ClientIdEnvVarName = "CLIENT_ID";
        public const string ClientSecretEnvVarName = "CLIENT_SECRET";
        public const string DemoFlavor = "DEMO_FLAVOR";
        public const string OtelFqdnEnvVarName = "OTEL_EXPORTER_OTLP_LOGS_ENDPOINT";
        public const string TenantIdEnvVarName = "TENANT_ID";
        public const string UamiResourceIdEnvVarName = "UAMI_RESOURCE_ID";
    }
}
