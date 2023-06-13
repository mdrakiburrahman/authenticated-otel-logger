namespace AuthZProcessor
{
    public static class RuntimeEnvVars
    {
        public const string StorageConnectionString = "AZURE_STORAGE_CONNECTION_STRING";
        public const string StorageContainerName = "STORAGE_CONTAINER_NAME";
        public const string EventHubNamespaceConnectionString = "EVENT_HUBS_NAMESPACE_CONNECTION_STRING";
        public const string AuthNEventHubName = "AUTHN_HUB_NAME";
        public const string AuthZEventHubName = "AUTHZ_HUB_NAME";

    }
}
