using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace AuthenticatedOtelLogger
{
    public class AuthorizationHeaderHandler : DelegatingHandler
    {
        public enum TokenType
        {
            BearerTelemetry
        }

        private readonly AuthorizationEnvironmentOptions _options;
        private AuthenticationResult? _bearerTelemetryAuthenticationResult;
        private static readonly TimeSpan MinimumValidityPeriod = TimeSpan.FromMinutes(2);

        public AuthorizationHeaderHandler(
            HttpMessageHandler innerHandler,
            AuthorizationEnvironmentOptions options = AuthorizationEnvironmentOptions.ServicePrincipal
        )
            : base(GetHandlerWithoutSslValidation(innerHandler, true))
        {
            _options = options;
            _bearerTelemetryAuthenticationResult = null;
        }

        private static HttpMessageHandler GetHandlerWithoutSslValidation(HttpMessageHandler handler, bool disableSslCheck)
        {
            if (!disableSslCheck) return handler;

            if (handler is HttpClientHandler clientHandler)
            {
                // Disable SSL certificate validation
                clientHandler.ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true;
                return clientHandler;
            }

            // If the innerHandler isn't HttpClientHandler and you still want to disable SSL validation,
            // create a new one with SSL validation disabled
            return new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
            };
        }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            try
            {
                // For local proxying, run:
                //
                // >>> kubectl port-forward service/opentelemetry-collector-...-svc-internal -n "...-telemetry" 8080:8080
                //
                // Based on what your OTEL URL is, e.g. https://github.com/open-telemetry/opentelemetry-collector/blob/97be125dcc46f303975fc8fda558db80bbd94d20/receiver/otlpreceiver/testdata/config.yaml#L48
                //
                // >>> e.g. http://127.0.0.1:8080/v1/logs
                //
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", GetAccessToken(TokenType.BearerTelemetry));
                request.Headers.Add("Original-Uri", request?.RequestUri?.ToString());
                request.Headers.Add("Original-Method", request.Method.ToString());
                request.Headers.Add("X-Forwarded-For", "99.238.40.160");
                
                return base.Send(request, default);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Exception occurred: {ex.Message}");
                throw;
            }
        }

        private string? GetAccessToken(TokenType tokenType)
        {
            // Determine scopes
            //
            string scope;
            switch (tokenType)
            {
                case TokenType.BearerTelemetry:

                    var arcDataOpenTelemetryClientId = Environment.GetEnvironmentVariable(RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName);
                    if (arcDataOpenTelemetryClientId == null) throw new ArgumentNullException($"Environment variable {RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName} is null.");
                    scope = arcDataOpenTelemetryClientId;
                    break;

                default:

                    throw new ArgumentOutOfRangeException(nameof(tokenType), tokenType, null);
            }

            // Set authentication result
            //
            var authenticationResult = tokenType switch
            {
                TokenType.BearerTelemetry => _bearerTelemetryAuthenticationResult,
                _ => throw new ArgumentOutOfRangeException(nameof(tokenType), tokenType, null)
            };

            bool tokenExpiredOrAboutToExpire;
            if (authenticationResult != null)
            {
                tokenExpiredOrAboutToExpire = authenticationResult?.ExpiresOn < DateTimeOffset.UtcNow + MinimumValidityPeriod;
            }
            else
            {
                tokenExpiredOrAboutToExpire = true;
            }

            Console.WriteLine("==========================================================================================================");
            if (tokenExpiredOrAboutToExpire)
            {
                Console.WriteLine($"[Scope: {scope}] Refreshing Scoped Azure AD token");
                authenticationResult = GetAuthenticationResultAsync(_options, scope).Result;
            }
            if (authenticationResult == null)
            {
                Console.WriteLine("Running in NoAuth mode, returning bogus token.");
                Console.WriteLine("==========================================================================================================");
                return "NoAuthNDemo";
            }

            Console.WriteLine("==========================================================================================================");

            // Update cache
            //
            switch (tokenType)
            {
                case TokenType.BearerTelemetry:_bearerTelemetryAuthenticationResult = authenticationResult;
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
            var clientSecret = Environment.GetEnvironmentVariable(RuntimeEnvVars.ClientSecretEnvVarName);
            var tenantId = Environment.GetEnvironmentVariable(RuntimeEnvVars.TenantIdEnvVarName);
            var uamiClientId = Environment.GetEnvironmentVariable(RuntimeEnvVars.UamiClientIdEnvVarName);

            IConfidentialClientApplication confidentialClientApplication;
            IManagedIdentityApplication managedIdApplication;
            AuthenticationResult authenticationResult;

            switch (options)
            {
                case AuthorizationEnvironmentOptions.ServicePrincipal:

                    if (clientId == null || clientSecret == null || tenantId == null)
                        throw new ArgumentNullException($"Environment variable {RuntimeEnvVars.ClientIdEnvVarName}, {RuntimeEnvVars.ClientSecretEnvVarName}, or {RuntimeEnvVars.TenantIdEnvVarName} is null.");

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
                        throw new ArgumentNullException($"Environment variable {RuntimeEnvVars.ArcK8sCertEnvVarName} or {RuntimeEnvVars.ArcK8sClientIdEnvVarName} cannot be null in {AuthorizationEnvironmentOptions.SystemAssignedIdentityWithCertificate}.");

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
                        throw new ArgumentNullException($"Environment variable {RuntimeEnvVars.UamiClientIdEnvVarName} is null.");

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
                    throw new ArgumentException($"Invalid AuthorizationEnvironmentOptions value {options}");
            }

            return authenticationResult;
        }
    }
}
