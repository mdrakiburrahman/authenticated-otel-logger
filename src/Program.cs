using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Logs;

string otel_endpoint = Environment.GetEnvironmentVariable("OTEL_EXPORTER_OTLP_LOGS_ENDPOINT") ?? "http://localhost";
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddOpenTelemetry(options =>
    {
        options.AddConsoleExporter();
        options.AddOtlpExporter((options) =>
        {
            Console.WriteLine($"Using OTLP endpoint {otel_endpoint}");
            options.Endpoint = new Uri(otel_endpoint);
            options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            options.ExportProcessorType = ExportProcessorType.Simple;
            options.Headers = "Authorization=Bearer BogusToken123";
        });
        options.IncludeScopes = true;
    });
});

var logger = loggerFactory.CreateLogger<Program>();
using(logger.BeginScope(new List<KeyValuePair<string, object>>
{
    new KeyValuePair<string, object>("scope_name", "parent"),
}))
{
    int id = 0;
    while(true)
    {
        logger.LogInformation($"[With Foo] [Hostname: {Environment.MachineName} | Logging endpoint: {otel_endpoint}] Counter: {++id}");
        await Task.Delay(1000);
    }
}
