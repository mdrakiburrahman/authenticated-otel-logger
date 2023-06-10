using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;

namespace AuthenticatedOtelLogger
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string otel_endpoint =
                Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT")
                ?? "http://localhost";

            AuthorizationEnvironmentOptions authorizationEnvironment;
            if (
                !Enum.TryParse(
                    Environment.GetEnvironmentVariable("AUTHORIZATION_ENV"),
                    out authorizationEnvironment
                )
            )
            {
                authorizationEnvironment = AuthorizationEnvironmentOptions.LocalDevMachine;
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
                                var client = new HttpClient(
                                    new AuthorizationHeaderHandler(
                                        innerHandler,
                                        authorizationEnvironment
                                    )
                                );
                                client.Timeout = TimeSpan.FromMilliseconds(
                                    otlpOptions.TimeoutMilliseconds
                                );
                                return client;
                            };
                        }
                    );
                    options.IncludeScopes = true;
                });
            });

            var logger = loggerFactory.CreateLogger<Program>();
            using (
                logger.BeginScope(
                    new List<KeyValuePair<string, object>>
                    {
                        new KeyValuePair<string, object>("scope_name", "parent"),
                    }
                )
            )
            {
                int id = 0;
                while (true)
                {
                    logger.LogInformation(
                        $"[With Foo] [Hostname: {Environment.MachineName} | Logging endpoint: {otel_endpoint}] Counter: {++id}"
                    );
                    await Task.Delay(1000);
                }
            }
        }
    }
}
