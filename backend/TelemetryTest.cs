using System.Diagnostics;
using CopilotEvalApi.Observability;

// Simple test to verify telemetry is working
Console.WriteLine("Testing OpenTelemetry instrumentation...");

// Test basic activity creation
using var activity = Telemetry.ActivitySource.StartActivity("test.operation");
activity?.SetTag("test.type", "demo");
activity?.SetTag("test.id", "12345");

Console.WriteLine($"Activity created: {activity?.OperationName}");
Console.WriteLine($"Activity ID: {activity?.Id}");
Console.WriteLine($"Trace ID: {activity?.TraceId}");

// Test metrics
Telemetry.JobsEnqueued.Add(1, new TagList
{
    { "job.type", "test" },
    { "status", "success" }
});

Console.WriteLine("Metrics recorded successfully");

// Test worker telemetry
using var workerActivity = WorkerTelemetry.ActivitySource.StartActivity("test.worker.operation");
workerActivity?.SetTag("worker.id", Environment.MachineName);

WorkerTelemetry.MessagesReceived.Add(1, new TagList
{
    { "queue.name", "test-queue" }
});

Console.WriteLine("Worker telemetry recorded successfully");

Console.WriteLine("✅ OpenTelemetry instrumentation test completed successfully!");