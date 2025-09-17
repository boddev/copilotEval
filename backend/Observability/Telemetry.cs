using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace CopilotEvalApi.Observability;

/// <summary>
/// Central telemetry configuration for OpenTelemetry instrumentation
/// </summary>
public static class Telemetry
{
    /// <summary>
    /// Activity source for distributed tracing
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("CopilotEval.Backend");

    /// <summary>
    /// Meter for metrics collection
    /// </summary>
    public static readonly Meter Meter = new("CopilotEval.Backend");

    // Counters for job operations
    public static readonly Counter<long> JobsEnqueued = Meter.CreateCounter<long>(
        "copiloteval_jobs_enqueued_total",
        description: "Total number of jobs enqueued");

    public static readonly Counter<long> JobsProcessed = Meter.CreateCounter<long>(
        "copiloteval_jobs_processed_total", 
        description: "Total number of jobs processed");

    public static readonly Counter<long> JobsCompleted = Meter.CreateCounter<long>(
        "copiloteval_jobs_completed_total",
        description: "Total number of jobs completed successfully");

    public static readonly Counter<long> JobsFailed = Meter.CreateCounter<long>(
        "copiloteval_jobs_failed_total",
        description: "Total number of jobs that failed");

    // Histograms for performance metrics
    public static readonly Histogram<double> JobProcessingDuration = Meter.CreateHistogram<double>(
        "copiloteval_job_processing_duration_seconds",
        unit: "s",
        description: "Duration of job processing in seconds");

    public static readonly Histogram<double> ApiRequestDuration = Meter.CreateHistogram<double>(
        "copiloteval_api_request_duration_seconds",
        unit: "s", 
        description: "Duration of API requests in seconds");

    public static readonly Histogram<double> CopilotApiDuration = Meter.CreateHistogram<double>(
        "copiloteval_copilot_api_duration_seconds",
        unit: "s",
        description: "Duration of Copilot API calls in seconds");

    // Gauges for queue metrics (using a simpler approach)
    public static readonly UpDownCounter<long> QueueLength = Meter.CreateUpDownCounter<long>(
        "copiloteval_queue_length",
        description: "Current number of jobs in the queue");

    private static long _activeJobsCount = 0;
    public static readonly ObservableGauge<long> ActiveJobs = Meter.CreateObservableGauge<long>(
        "copiloteval_active_jobs",
        () => _activeJobsCount,
        description: "Current number of jobs being processed");

    /// <summary>
    /// Increment active jobs counter
    /// </summary>
    public static void IncrementActiveJobs() => Interlocked.Increment(ref _activeJobsCount);

    /// <summary>
    /// Decrement active jobs counter
    /// </summary>
    public static void DecrementActiveJobs() => Interlocked.Decrement(ref _activeJobsCount);

    /// <summary>
    /// Common tags for telemetry data
    /// </summary>
    public static class Tags
    {
        public const string JobId = "job.id";
        public const string JobType = "job.type";
        public const string JobPriority = "job.priority";
        public const string CorrelationId = "correlation.id";
        public const string Operation = "operation";
        public const string Status = "status";
        public const string ErrorType = "error.type";
        public const string ApiEndpoint = "api.endpoint";
        public const string ServiceName = "service.name";
    }

    /// <summary>
    /// Activity names for consistent span naming
    /// </summary>
    public static class Activities
    {
        public const string JobEnqueue = "job.enqueue";
        public const string JobProcess = "job.process";
        public const string JobExecute = "job.execute";
        public const string CopilotApiCall = "copilot.api.call";
        public const string DatabaseOperation = "database.operation";
        public const string ServiceBusOperation = "servicebus.operation";
        public const string SimilarityEvaluation = "similarity.evaluation";
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