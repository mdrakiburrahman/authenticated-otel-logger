using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace AuthenticatedOtelLogger
{
    public class AuthorizationHeaderHandler : DelegatingHandler
    {
        public enum TokenType
        {
            BearerTelemetry,
            BearerGraph
        }

        private readonly AuthorizationEnvironmentOptions _options;
        private AuthenticationResult? _bearerTelemetryAuthenticationResult;
        private AuthenticationResult? _bearerGraphAuthenticationResult;
        private static readonly TimeSpan MinimumValidityPeriod = TimeSpan.FromMinutes(2);
        public const string GraphHeader = "X-MS-AUTHORIZATION-GRAPH";
        public const string ContainerResourceIdHeader = "X-MS-ARC-CONTAINER-RESOURCE-ID";

        public AuthorizationHeaderHandler(
            HttpMessageHandler innerHandler,
            AuthorizationEnvironmentOptions options =
                AuthorizationEnvironmentOptions.ServicePrincipal
        )
            : base(innerHandler)
        {
            _options = options;
            _bearerTelemetryAuthenticationResult = null;
            _bearerGraphAuthenticationResult = null;
        }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            // AuthN
            //
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                GetAccessToken(TokenType.BearerTelemetry)
            );

            // AuthZ
            //
            request.Headers.Add(GraphHeader, $"Bearer {GetAccessToken(TokenType.BearerGraph)}");

            string arcContainerResourceId =
                Environment.GetEnvironmentVariable(RuntimeEnvVars.ArcContainerResourceId)
                ?? "NoAuthZDemo";
            request.Headers.Add(ContainerResourceIdHeader, arcContainerResourceId);

            return base.Send(request, cancellationToken);
        }

        private string? GetAccessToken(TokenType tokenType)
        {
            // Determine scopes
            //
            string scope;
            switch (tokenType)
            {
                case TokenType.BearerTelemetry:

                    var arcDataOpenTelemetryClientId = Environment.GetEnvironmentVariable(
                        RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName
                    );
                    if (arcDataOpenTelemetryClientId == null)
                        throw new ArgumentNullException(
                            $"Environment variable {RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName} is null."
                        );

                    scope = arcDataOpenTelemetryClientId;

                    break;

                case TokenType.BearerGraph:

                    scope = "https://graph.microsoft.com";

                    break;

                default:

                    throw new ArgumentOutOfRangeException(nameof(tokenType), tokenType, null);
            }

            // Set authentication result
            //
            var authenticationResult = tokenType switch
            {
                TokenType.BearerTelemetry => _bearerTelemetryAuthenticationResult,
                TokenType.BearerGraph => _bearerGraphAuthenticationResult,
                _ => throw new ArgumentOutOfRangeException(nameof(tokenType), tokenType, null)
            };

            bool tokenExpiredOrAboutToExpire;
            if (authenticationResult != null)
            {
                tokenExpiredOrAboutToExpire =
                    authenticationResult?.ExpiresOn < DateTimeOffset.UtcNow + MinimumValidityPeriod;
            }
            else
            {
                tokenExpiredOrAboutToExpire = true;
            }

            Console.WriteLine(
                "=========================================================================================================="
            );
            if (tokenExpiredOrAboutToExpire)
            {
                Console.WriteLine($"[Scope: {scope}] Refreshing Scoped Azure AD token");
                authenticationResult = GetAuthenticationResultAsync(_options, scope).Result;
            }
            if (authenticationResult == null)
            {
                Console.WriteLine("Running in NoAuth mode, returning bogus token.");
                Console.WriteLine(
                    "=========================================================================================================="
                );
                return "NoAuthNDemo";
            }

            Console.WriteLine(
                $"[Scope: {scope}] Token expires in [HH:MM:SS] {authenticationResult?.ExpiresOn - DateTimeOffset.UtcNow}."
            );
            Console.WriteLine(
                "=========================================================================================================="
            );

            // Update cache
            //
            switch (tokenType)
            {
                case TokenType.BearerTelemetry:
                    _bearerTelemetryAuthenticationResult = authenticationResult;
                    break;
                case TokenType.BearerGraph:
                    _bearerGraphAuthenticationResult = authenticationResult;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(tokenType), tokenType, null);
            }

            return authenticationResult?.AccessToken;
        }

        private static async Task<AuthenticationResult> GetAuthenticationResultAsync(
            AuthorizationEnvironmentOptions options,
            string scope
        )
        {
            var clientId = Environment.GetEnvironmentVariable(RuntimeEnvVars.ClientIdEnvVarName);
            var clientSecret = Environment.GetEnvironmentVariable(
                RuntimeEnvVars.ClientSecretEnvVarName
            );
            var tenantId = Environment.GetEnvironmentVariable(RuntimeEnvVars.TenantIdEnvVarName);
            var uamiClientId = Environment.GetEnvironmentVariable(
                RuntimeEnvVars.UamiClientIdEnvVarName
            );

            IConfidentialClientApplication confidentialClientApplication;
            IManagedIdentityApplication managedIdApplication;
            AuthenticationResult authenticationResult;

            switch (options)
            {
                case AuthorizationEnvironmentOptions.ServicePrincipal:

                    if (clientId == null || clientSecret == null || tenantId == null)
                        throw new ArgumentNullException(
                            $"Environment variable {RuntimeEnvVars.ClientIdEnvVarName}, {RuntimeEnvVars.ClientSecretEnvVarName}, or {RuntimeEnvVars.TenantIdEnvVarName} is null."
                        );

                    confidentialClientApplication = ConfidentialClientApplicationBuilder
                        .Create(clientId)
                        .WithClientSecret(clientSecret)
                        .WithAuthority($"https://login.microsoftonline.com/{tenantId}", true)
                        .WithExperimentalFeatures()
                        .Build();

                    authenticationResult = await confidentialClientApplication
                        .AcquireTokenForClient(new string[] { $"{scope}/.default" })
                        .ExecuteAsync()
                        .ConfigureAwait(false);

                    break;

                case AuthorizationEnvironmentOptions.SystemAssignedIdentity:

                    managedIdApplication = ManagedIdentityApplicationBuilder
                        .Create()
                        // Azure Container Apps does not work without this
                        .WithExperimentalFeatures()
                        .Build();

                    authenticationResult = await managedIdApplication
                        .AcquireTokenForManagedIdentity(scope)
                        .ExecuteAsync()
                        .ConfigureAwait(false);

                    break;

                case AuthorizationEnvironmentOptions.SystemAssignedIdentityWithCertificate:

                    // ============== This is pretty much hard coded for Arc Kubernetes' implementation =================
                    var arcK8scertObj = Environment.GetEnvironmentVariable(
                        RuntimeEnvVars.ArcK8sCertEnvVarName
                    );
                    var arcK8sclientId = Environment.GetEnvironmentVariable(
                        RuntimeEnvVars.ArcK8sClientIdEnvVarName
                    );

                    if (arcK8scertObj == null || arcK8sclientId == null)
                        throw new ArgumentNullException(
                            $"Environment variable {RuntimeEnvVars.ArcK8sCertEnvVarName} or {RuntimeEnvVars.ArcK8sClientIdEnvVarName} cannot be null in {AuthorizationEnvironmentOptions.SystemAssignedIdentityWithCertificate}."
                        );

                    var (Certificate, _) = CertificateUtility.ExtractCert(arcK8scertObj);
                    // ==================================================================================================

                    confidentialClientApplication = ConfidentialClientApplicationBuilder
                        .Create(arcK8sclientId)
                        .WithCertificate(Certificate)
                        .WithAuthority($"https://login.microsoftonline.com/{tenantId}", true)
                        .Build();

                    authenticationResult = await confidentialClientApplication
                        .AcquireTokenForClient(new string[] { $"{scope}/.default" })
                        .ExecuteAsync()
                        .ConfigureAwait(false);

                    break;

                case AuthorizationEnvironmentOptions.UserAssignedIdentity:

                    if (uamiClientId == null)
                        throw new ArgumentNullException(
                            $"Environment variable {RuntimeEnvVars.UamiClientIdEnvVarName} is null."
                        );

                    managedIdApplication = ManagedIdentityApplicationBuilder
                        .Create(uamiClientId)
                        // Azure Container Apps does not work without this
                        .WithExperimentalFeatures()
                        .Build();

                    authenticationResult = await managedIdApplication
                        .AcquireTokenForManagedIdentity(scope)
                        .ExecuteAsync()
                        .ConfigureAwait(false);

                    break;

                case AuthorizationEnvironmentOptions.NoAuth:

                    authenticationResult = null;
                    break;

                default:
                    throw new ArgumentException(
                        $"Invalid AuthorizationEnvironmentOptions value {options}"
                    );
            }

            return authenticationResult;
        }
    }
}
