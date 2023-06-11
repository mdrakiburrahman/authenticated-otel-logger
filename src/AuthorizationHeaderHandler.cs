using Microsoft.Identity.Client;
using System.Net.Http.Headers;

namespace AuthenticatedOtelLogger
{
    public class AuthorizationHeaderHandler : DelegatingHandler
    {
        private readonly AuthorizationEnvironmentOptions _options;
        private AuthenticationResult? _authenticationResult;
        private static readonly TimeSpan MinimumValidityPeriod = TimeSpan.FromMinutes(2);

        public AuthorizationHeaderHandler(
            HttpMessageHandler innerHandler,
            AuthorizationEnvironmentOptions options =
                AuthorizationEnvironmentOptions.ServicePrincipal
        )
            : base(innerHandler)
        {
            _options = options;
            _authenticationResult = null;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            System.Threading.CancellationToken cancellationToken
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                GetAccessToken()
            );
            return base.SendAsync(request, cancellationToken);
        }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            request.Headers.Authorization = new AuthenticationHeaderValue(
                "Bearer",
                GetAccessToken()
            );
            return base.Send(request, cancellationToken);
        }

        private string? GetAccessToken()
        {
            bool tokenExpiredOrAboutToExpire;
            if (_authenticationResult != null)
            {
                tokenExpiredOrAboutToExpire =
                    _authenticationResult?.ExpiresOn
                    < DateTimeOffset.UtcNow + MinimumValidityPeriod;
            }
            else
            {
                tokenExpiredOrAboutToExpire = true;
            }

            Console.WriteLine("================================================");
            if (tokenExpiredOrAboutToExpire)
            {
                Console.WriteLine("Refreshing Azure AD token...");
                _authenticationResult = GetAuthenticationResultAsync(_options).Result;
            }
            if (_authenticationResult == null)
            {
                Console.WriteLine("Running in NoAuth mode.");
                Console.WriteLine("================================================");
                return "NoAuth";
            }
            Console.WriteLine(
                $"Token expires in [HH:MM:SS] {_authenticationResult?.ExpiresOn - DateTimeOffset.UtcNow}."
            );
            Console.WriteLine("================================================");
            return _authenticationResult?.AccessToken;
        }

        private static async Task<AuthenticationResult> GetAuthenticationResultAsync(
            AuthorizationEnvironmentOptions options
        )
        {
            var arcDataOpenTelemetryClientId = Environment.GetEnvironmentVariable(
                RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName
            );

            if (arcDataOpenTelemetryClientId == null)
                throw new ArgumentNullException(
                    $"Environment variable {RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName} is null."
                );

            var clientId = Environment.GetEnvironmentVariable(RuntimeEnvVars.ClientIdEnvVarName);
            var clientSecret = Environment.GetEnvironmentVariable(
                RuntimeEnvVars.ClientSecretEnvVarName
            );
            var tenantId = Environment.GetEnvironmentVariable(RuntimeEnvVars.TenantIdEnvVarName);
            var uamiResourceId = Environment.GetEnvironmentVariable(
                RuntimeEnvVars.UamiResourceIdEnvVarName
            );

            IConfidentialClientApplication confidentialClientApplication;
            IManagedIdentityApplication managedIdApplication;
            string scope = arcDataOpenTelemetryClientId;
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

                    managedIdApplication = ManagedIdentityApplicationBuilder.Create().Build();

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

                    if (uamiResourceId == null)
                        throw new ArgumentNullException(
                            $"Environment variable {RuntimeEnvVars.UamiResourceIdEnvVarName} is null."
                        );

                    managedIdApplication = ManagedIdentityApplicationBuilder
                        .Create(uamiResourceId)
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

        private string GetTokenExpirationTimeFormatted()
        {
            return $"Token expires in [HH:MM:SS] {_authenticationResult?.ExpiresOn - DateTimeOffset.UtcNow}.";
        }
    }
}
