using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Messaging.EventHubs.Producer;
using Azure.Storage.Blobs;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace AuthZProcessor
{
    public class Program
    {
        static async Task Main(string[] args)
        {
            // Create a cancellation token source to handle termination signals
            //
            var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, eventArgs) =>
            {
                eventArgs.Cancel = true; // Cancel the default termination behavior
                cts.Cancel(); // Cancel the ongoing operation
            };

            // Create a blob container client that the event processor will use
            //
            BlobContainerClient storageClient = new BlobContainerClient(
                Environment.GetEnvironmentVariable(RuntimeEnvVars.StorageConnectionString),
                Environment.GetEnvironmentVariable(RuntimeEnvVars.StorageContainerName)
            );

            // Initiate Processor Client - AuthN
            //
            var processorClient = new EventProcessorClient(
                storageClient,
                EventHubConsumerClient.DefaultConsumerGroupName,
                Environment.GetEnvironmentVariable(
                    RuntimeEnvVars.EventHubNamespaceConnectionString
                ),
                Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthNEventHubName)
            );

            // Initiate Consumer Client - AuthN
            //
            var consumerClient = new EventHubConsumerClient(
                EventHubConsumerClient.DefaultConsumerGroupName,
                Environment.GetEnvironmentVariable(
                    RuntimeEnvVars.EventHubNamespaceConnectionString
                ),
                Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthNEventHubName)
            );

            // Initiate Producer Client - AuthZ
            //
            var producerClient = new EventHubProducerClient(
                Environment.GetEnvironmentVariable(
                    RuntimeEnvVars.EventHubNamespaceConnectionString
                ),
                Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthZEventHubName)
            );

            // Register handlers for processing events and handling errors
            //
            processorClient.ProcessEventAsync += async (eventArgs) =>
            {
                await ProcessEventHandler(eventArgs, producerClient);
            };
            processorClient.ProcessErrorAsync += ProcessErrorHandler;

            // Start the processing
            //
            Console.WriteLine(
                $"Starting the processor, there are currently {GetNumEvents(consumerClient).Result} events in the queue."
            );
            await processorClient.StartProcessingAsync();

            // Wait for cancellation signal
            //
            Console.WriteLine("Processor started. Press Ctrl+C to stop.");
            await WaitForCancellationSignalAsync(cts.Token);

            // Stop the processing
            //
            Console.WriteLine(
                $"Stopping the processor, there are currently {GetNumEvents(consumerClient).Result} events in the queue."
            );
            await processorClient.StopProcessingAsync();

            // Close the clients
            //
            await consumerClient.CloseAsync();
            await producerClient.CloseAsync();

            Console.WriteLine("Done.");
        }

        static async Task<Task> ProcessEventHandler(
            ProcessEventArgs eventArgs,
            EventHubProducerClient producerClient
        )
        {
            Console.WriteLine($"Processing Sequence: {eventArgs.Data.SequenceNumber}");

            string payload = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());

            OtlpJsonPayload? otlpJsonPayload =
                System.Text.Json.JsonSerializer.Deserialize<OtlpJsonPayload>(payload);

            string? appid = otlpJsonPayload?.GetScopeLogsAttributeIfExists(
                RuntimeConstants.AuthorAppIdAttributeKey
            );
            string? oid = otlpJsonPayload?.GetScopeLogsAttributeIfExists(
                RuntimeConstants.AuthorObjectIdAttributeKey
            );
            string? claims = otlpJsonPayload?.GetScopeLogsAttributeIfExists(
                RuntimeConstants.AuthorClaimsProvenJwt
            );

            // AuthZ is not possible without all of these attributes
            //
            if (appid == null || oid == null || claims == null)
            {
                return Task.CompletedTask;
            }

            // Get ContainerResourceId
            //
            var decodedClaims = Encoding.UTF8.GetString(Convert.FromBase64String(claims));
            JObject jsonObject = JObject.Parse(decodedClaims);
            JArray claimsArray = (JArray)jsonObject["claims"];
            string? containerResourceId = null;

            containerResourceId = claimsArray
                .FirstOrDefault(
                    claim => (string)claim["typ"] == RuntimeConstants.ContainerResourceIdJwtKey
                )
                ?.Value<string>("val");

            if (containerResourceId == null)
                return Task.CompletedTask;

            Console.WriteLine(
                $"Valid claim: App Id: {appid}, Object Id: {oid}, Managed Id: {containerResourceId}"
            );

            // Write payload to AuthZ Event Hub
            //
            var eventData = new EventData(Encoding.UTF8.GetBytes(payload));
            await producerClient.SendAsync(new List<EventData> { eventData });

            return Task.CompletedTask;
        }

        static Task ProcessErrorHandler(ProcessErrorEventArgs eventArgs)
        {
            Console.WriteLine(
                $"\tPartition '{eventArgs.PartitionId}': an unhandled exception was encountered. This was not expected to happen."
            );
            Console.WriteLine(eventArgs.Exception.Message);
            return Task.CompletedTask;
        }

        static async Task<long> GetNumEvents(EventHubConsumerClient consumerClient)
        {
            var partitionProperties = await consumerClient.GetPartitionPropertiesAsync("0");
            return partitionProperties.LastEnqueuedSequenceNumber;
        }

        static async Task WaitForCancellationSignalAsync(CancellationToken cancellationToken)
        {
            // Wait indefinitely or until cancellation signal
            //
            await Task.Delay(-1, cancellationToken);
        }
    }
}
