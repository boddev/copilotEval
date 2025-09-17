using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CopilotEvalWorker.Observability;

/// <summary>
/// Telemetry configuration specific to the CopilotEval worker processes
/// </summary>
public static class WorkerTelemetry
{
    /// <summary>
    /// Activity source for worker distributed tracing
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("CopilotEval.Worker");

    /// <summary>
    /// Meter for worker metrics collection
    /// </summary>
    public static readonly Meter Meter = new("CopilotEval.Worker");

    // Counters for worker-specific operations
    public static readonly Counter<long> MessagesReceived = Meter.CreateCounter<long>(
        "copiloteval_worker_messages_received_total",
        description: "Total number of messages received by the worker");

    public static readonly Counter<long> MessagesProcessed = Meter.CreateCounter<long>(
        "copiloteval_worker_messages_processed_total",
        description: "Total number of messages successfully processed by the worker");

    public static readonly Counter<long> MessagesFailed = Meter.CreateCounter<long>(
        "copiloteval_worker_messages_failed_total",
        description: "Total number of messages that failed processing");

    public static readonly Counter<long> MessagesRetried = Meter.CreateCounter<long>(
        "copiloteval_worker_messages_retried_total",
        description: "Total number of messages retried");

    public static readonly Counter<long> MessagesDeadLettered = Meter.CreateCounter<long>(
        "copiloteval_worker_messages_deadlettered_total",
        description: "Total number of messages sent to dead letter queue");

    // Histograms for worker performance metrics
    public static readonly Histogram<double> MessageProcessingDuration = Meter.CreateHistogram<double>(
        "copiloteval_worker_message_processing_duration_seconds",
        unit: "s",
        description: "Duration of message processing in seconds");

    public static readonly Histogram<double> MessageDeserializationDuration = Meter.CreateHistogram<double>(
        "copiloteval_worker_message_deserialization_duration_seconds", 
        unit: "s",
        description: "Duration of message deserialization in seconds");

    public static readonly Histogram<double> QueueReceiveDuration = Meter.CreateHistogram<double>(
        "copiloteval_worker_queue_receive_duration_seconds",
        unit: "s", 
        description: "Duration to receive messages from queue in seconds");

    // Gauges for worker health metrics
    public static readonly UpDownCounter<long> ActiveConnections = Meter.CreateUpDownCounter<long>(
        "copiloteval_worker_active_connections",
        description: "Current number of active Service Bus connections");

    private static long _activeMessageProcessors = 0;
    public static readonly ObservableGauge<long> ActiveMessageProcessors = Meter.CreateObservableGauge<long>(
        "copiloteval_worker_active_message_processors",
        () => _activeMessageProcessors,
        description: "Current number of messages being processed concurrently");

    private static long _queueDepth = 0;
    public static readonly ObservableGauge<long> QueueDepth = Meter.CreateObservableGauge<long>(
        "copiloteval_worker_queue_depth",
        () => _queueDepth,
        description: "Estimated number of messages waiting in the queue");

    /// <summary>
    /// Common tags for worker telemetry data
    /// </summary>
    public static class Tags
    {
        public const string MessageType = "message.type";
        public const string MessageId = "message.id";
        public const string CorrelationId = "correlation.id";
        public const string ProcessorName = "processor.name";
        public const string QueueName = "queue.name";
        public const string Status = "status";
        public const string ErrorType = "error.type";
        public const string RetryCount = "retry.count";
        public const string DeliveryCount = "delivery.count";
        public const string WorkerId = "worker.id";
        public const string JobType = "job.type";
        public const string JobId = "job.id";
    }

    /// <summary>
    /// Activity names for consistent worker span naming
    /// </summary>
    public static class Activities
    {
        public const string MessageReceive = "worker.message.receive";
        public const string MessageProcess = "worker.message.process";
        public const string MessageDeserialize = "worker.message.deserialize";
        public const string QueueConnect = "worker.queue.connect";
        public const string JobExecution = "worker.job.execution";
        public const string MessageComplete = "worker.message.complete";
        public const string MessageAbandon = "worker.message.abandon";
        public const string MessageDeadLetter = "worker.message.deadletter";
    }

    /// <summary>
    /// Increment active message processors counter
    /// </summary>
    public static void IncrementActiveProcessors() => Interlocked.Increment(ref _activeMessageProcessors);

    /// <summary>
    /// Decrement active message processors counter
    /// </summary>
    public static void DecrementActiveProcessors() => Interlocked.Decrement(ref _activeMessageProcessors);

    /// <summary>
    /// Update queue depth estimate
    /// </summary>
    public static void UpdateQueueDepth(long depth) => Interlocked.Exchange(ref _queueDepth, depth);

    /// <summary>
    /// Helper method to create correlation context for new activities
    /// </summary>
    public static ActivityContext CreateCorrelationContext(string? traceParent, string? traceState)
    {
        if (string.IsNullOrEmpty(traceParent))
        {
            return default;
        }

        if (ActivityContext.TryParse(traceParent, traceState, out var context))
        {
            return context;
        }

        return default;
    }

    /// <summary>
    /// Create a new activity with proper correlation from Service Bus message
    /// </summary>
    public static Activity? StartActivityWithCorrelation(string activityName, IDictionary<string, object> messageProperties)
    {
        var traceParent = messageProperties.TryGetValue("traceparent", out var tp) ? tp?.ToString() : null;
        var traceState = messageProperties.TryGetValue("tracestate", out var ts) ? ts?.ToString() : null;
        
        var parentContext = CreateCorrelationContext(traceParent, traceState);
        
        var activity = parentContext != default 
            ? ActivitySource.StartActivity(activityName, ActivityKind.Consumer, parentContext)
            : ActivitySource.StartActivity(activityName, ActivityKind.Consumer);

        return activity;
    }

    /// <summary>
    /// Dispose method to clean up telemetry resources
    /// </summary>
    public static void Dispose()
    {
        ActivitySource?.Dispose();
        Meter?.Dispose();
    }
}