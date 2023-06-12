using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Consumer;
using Azure.Messaging.EventHubs.Processor;
using Azure.Storage.Blobs;
using System;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;

namespace AuthZProcessor
{
    public class Program
    {
        private EventProcessorClient? _processorClient;
        private EventHubConsumerClient? _consumerClient;

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

            // Initiate Processor Client
            //
            var _processorClient = new EventProcessorClient(
                storageClient,
                EventHubConsumerClient.DefaultConsumerGroupName,
                Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthNEventHubConnectionString),
                Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthNEventHubName)
            );

            // Initiate Consumer Client
            //
            var _consumerClient = new EventHubConsumerClient(
                EventHubConsumerClient.DefaultConsumerGroupName,
                Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthNEventHubConnectionString),
                Environment.GetEnvironmentVariable(RuntimeEnvVars.AuthNEventHubName)
            );

            // Register handlers for processing events and handling errors
            //
            _processorClient.ProcessEventAsync += ProcessEventHandler;
            _processorClient.ProcessErrorAsync += ProcessErrorHandler;

            // Start the processing
            //
            Console.WriteLine(
                $"Starting the processor, there are currently {GetNumEvents(_consumerClient).Result} events in the queue."
            );
            await _processorClient.StartProcessingAsync();

            // Wait for cancellation signal
            //
            Console.WriteLine("Processor started. Press Ctrl+C to stop.");
            await WaitForCancellationSignalAsync(cts.Token);

            // Stop the processing
            //
            Console.WriteLine(
                $"Stopping the processor, there are currently {GetNumEvents(_consumerClient).Result} events in the queue."
            );
            await _processorClient.StopProcessingAsync();

            Console.WriteLine("Done.");
        }

        static Task ProcessEventHandler(ProcessEventArgs eventArgs)
        {
            Console.WriteLine($"Processing Sequence: {eventArgs.Data.SequenceNumber}");

            string payload = Encoding.UTF8.GetString(eventArgs.Data.Body.ToArray());

            OtlpJsonPayload? otlpJsonPayload = JsonSerializer.Deserialize<OtlpJsonPayload>(payload);

            string? appid = otlpJsonPayload?.GetScopeLogsAttributeIfExists(
                RuntimeConstants.AuthorAppIdAttributeKey
            );
            string? oid = otlpJsonPayload?.GetScopeLogsAttributeIfExists(
                RuntimeConstants.AuthorObjectIdAttributeKey
            );
            string? bearerGraph = otlpJsonPayload?.GetScopeLogsAttributeIfExists(
                RuntimeConstants.AuthorGraphBearerTokenAttributeKey
            );
            string? claimedContainerResourceId = otlpJsonPayload?.GetScopeLogsAttributeIfExists(
                RuntimeConstants.AuthorClaimedContainerResourceId
            );

            // AuthZ is not possible without all of these attributes
            //
            if (
                appid == null
                || oid == null
                || bearerGraph == null
                || claimedContainerResourceId == null
            )
            {
                return Task.CompletedTask;
            }

            Console.WriteLine($"\tReceived valid event: {otlpJsonPayload?.ToString()}");

            // TODO: Do the AuthZ

            // TODO: Write to AuthZ Event Hub

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
