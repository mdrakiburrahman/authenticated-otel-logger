using System.Text.Json;
using System.Text.Json.Serialization;

namespace AuthZProcessor
{
    public class OtlpJsonPayload
    {
        [JsonPropertyName("resourceLogs")]
        public List<ResourceLog> ResourceLogs { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }

        public string? GetScopeLogsAttributeIfExists(string key)
        {
            return ResourceLogs
                .SelectMany(resourceLog => resourceLog.ScopeLogs)
                .SelectMany(scopeLog => scopeLog.LogRecords)
                .SelectMany(logRecord => logRecord.Attributes)
                .FirstOrDefault(attribute => attribute.Key == key)?.Value.StringValue;
        }
    }

    public class ResourceLog
    {
        [JsonPropertyName("resource")]
        public Resource Resource { get; set; }

        [JsonPropertyName("scopeLogs")]
        public List<ScopeLog> ScopeLogs { get; set; }
    }

    public class Resource
    {
        [JsonPropertyName("attributes")]
        public List<Attribute> Attributes { get; set; }
    }

    public class Attribute
    {
        [JsonPropertyName("key")]
        public string Key { get; set; }

        [JsonPropertyName("value")]
        public Value Value { get; set; }
    }

    public class Value
    {
        [JsonPropertyName("stringValue")]
        public string StringValue { get; set; }
    }

    public class ScopeLog
    {
        [JsonPropertyName("scope")]
        public object ScopeObject { get; set; }

        [JsonPropertyName("logRecords")]
        public List<LogRecord> LogRecords { get; set; }
    }

    public class LogRecord
    {
        [JsonPropertyName("timeUnixNano")]
        public string TimeUnixNano { get; set; }

        [JsonPropertyName("observedTimeUnixNano")]
        public string ObservedTimeUnixNano { get; set; }

        [JsonPropertyName("severityNumber")]
        public int SeverityNumber { get; set; }

        [JsonPropertyName("severityText")]
        public string SeverityText { get; set; }

        [JsonPropertyName("body")]
        public Value Body { get; set; }

        [JsonPropertyName("attributes")]
        public List<Attribute> Attributes { get; set; }

        [JsonPropertyName("traceId")]
        public string TraceId { get; set; }

        [JsonPropertyName("spanId")]
        public string SpanId { get; set; }
    }
}
