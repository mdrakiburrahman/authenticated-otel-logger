﻿using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;


namespace AuthenticatedOtelLogger
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            string otel_endpoint = Environment.GetEnvironmentVariable(RuntimeEnvVars.OtelFqdnEnvVarName) ?? "http://localhost";
            string tenant_id = Environment.GetEnvironmentVariable(RuntimeEnvVars.TenantIdEnvVarName) ?? "unknown";

            AuthorizationEnvironmentOptions authorizationEnvironment;
            if (!Enum.TryParse(Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthorizationEnvironmentEnvVarName), out authorizationEnvironment))
            {
                authorizationEnvironment = AuthorizationEnvironmentOptions.ServicePrincipal;
            }

            using var loggerFactory = LoggerFactory.Create(builder =>
            {
                builder.AddOpenTelemetry(options =>
                {
                    options.AddConsoleExporter();
                    options.AddOtlpExporter(
                        (otlpOptions) =>
                        {
                            Console.WriteLine($"Using OTLP endpoint {otel_endpoint}");
                            otlpOptions.Endpoint = new Uri(otel_endpoint);
                            otlpOptions.Protocol = OtlpExportProtocol.HttpProtobuf;
                            otlpOptions.ExportProcessorType = ExportProcessorType.Simple;

                            // OpenTelemetry dotnet does not support setting dynamic
                            // Authorization headers yet. As a result, when the bearer token
                            // expires, it stops uploading logs. OtlpExporterOptions allows
                            // us to set a custom HttpClientFactory, which we use to add the
                            // Authorization header to the HttpClient.
                            //
                            // - Open Issue:
                            //
                            //      https://github.com/open-telemetry/opentelemetry-dotnet/issues/2504
                            //
                            // - Open PR that got abandoned because the maintainers are
                            //   perfectionists that don't believe in solving business
                            //   problems:
                            //
                            //      https://github.com/open-telemetry/opentelemetry-dotnet/pull/4272
                            //
                            otlpOptions.HttpClientFactory = () =>
                            {
                                var innerHandler = new HttpClientHandler();
                                var client = new HttpClient(new AuthorizationHeaderHandler(innerHandler,authorizationEnvironment));
                                client.Timeout = TimeSpan.FromMilliseconds(otlpOptions.TimeoutMilliseconds);
                                return client;
                            };
                        }
                    );
                    options.IncludeScopes = true;
                });
            });

            var arcDataOpenTelemetryClientId = Environment.GetEnvironmentVariable(RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName);
            if (arcDataOpenTelemetryClientId == null) throw new ArgumentNullException($"Environment variable {RuntimeEnvVars.ArcDataOpenTelemetryClientIdEnvVarName} is null.");
            var scope = $"{arcDataOpenTelemetryClientId}/.default";

            var logger = loggerFactory.CreateLogger<Program>();
            using (logger.BeginScope(new List<KeyValuePair<string, object>> { new KeyValuePair<string, object>("scope_name", "parent") }))
            {
                int id = 0;
                while (true)
                {
                    logger.LogInformation($"[Authorization: {authorizationEnvironment} | Scope: {scope} | Tenant: {tenant_id} | Hostname: {Environment.MachineName} | Logging endpoint: {otel_endpoint}] Counter: {++id}");
                    await Task.Delay(1000);
                }
            }
        }
    }
}
