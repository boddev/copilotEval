using Azure.Messaging.ServiceBus;
using System.Text.Json;
using CopilotEvalApi.Models;
using CopilotEvalApi.Observability;
using System.Diagnostics;

namespace CopilotEvalApi.Services;

/// <summary>
/// Service interface for job queue operations
/// </summary>
public interface IJobQueueService
{
    Task EnqueueJobMessageAsync(JobMessage message);
    Task EnqueueJobCreatedAsync(string jobId, Job job, JobPriority priority = JobPriority.Normal);
}

/// <summary>
/// Service implementation for Azure Service Bus job queue operations
/// </summary>
public class JobQueueService : IJobQueueService
{
    private readonly ServiceBusSender _sender;
    private readonly ILogger<JobQueueService> _logger;
    private readonly IConfiguration _configuration;

    public JobQueueService(
        IConfiguration configuration, 
        ILogger<JobQueueService> logger)
    {
        _configuration = configuration;
        _logger = logger;
        
        // Initialize Service Bus client for job-messages queue
        var connectionString = _configuration.GetConnectionString("ServiceBus") ?? "InMemory";
        
        if (connectionString != "InMemory")
        {
            var client = new ServiceBusClient(connectionString);
            _sender = client.CreateSender("job-messages");
            _logger.LogInformation("🚌 ServiceBus client initialized for job-messages queue");
        }
        else
        {
            _logger.LogWarning("⚠️ ServiceBus connection string not configured, using in-memory mode");
            _sender = null!; // Will be handled in-memory
        }
    }

    public async Task EnqueueJobMessageAsync(JobMessage message)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        
        using var activity = Telemetry.ActivitySource.StartActivity(Telemetry.Activities.ServiceBusOperation);
        activity?.SetTag(Telemetry.Tags.JobId, message.JobId);
        activity?.SetTag(Telemetry.Tags.CorrelationId, message.CorrelationId?.ToString() ?? "");
        activity?.SetTag(Telemetry.Tags.Operation, "enqueue");
        activity?.SetTag(Telemetry.Tags.ServiceName, "job-queue");
        activity?.SetTag("messaging.system", "servicebus");
        activity?.SetTag("messaging.destination.name", "job-messages");
        activity?.SetTag("messaging.message.type", message.MessageType.ToString());

        _logger.LogInformation("📤 [Queue {RequestId}] Enqueuing job message - JobId: {JobId}, Type: {MessageType}", 
            requestId, message.JobId, message.MessageType);

        try
        {
            if (_sender != null)
            {
                // Real Service Bus implementation
                var messageJson = JsonSerializer.Serialize(message, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
                });

                var serviceBusMessage = new ServiceBusMessage(messageJson)
                {
                    MessageId = Guid.NewGuid().ToString(),
                    Subject = message.MessageType.ToString(),
                    ContentType = "application/json"
                };

                // Add custom properties for routing/filtering
                serviceBusMessage.ApplicationProperties["job_id"] = message.JobId;
                serviceBusMessage.ApplicationProperties["message_type"] = message.MessageType.ToString();
                serviceBusMessage.ApplicationProperties["correlation_id"] = message.CorrelationId?.ToString() ?? "";

                // Add trace context to message for correlation
                if (Activity.Current != null)
                {
                    serviceBusMessage.ApplicationProperties["traceparent"] = Activity.Current.Id;
                    serviceBusMessage.ApplicationProperties["tracestate"] = Activity.Current.TraceStateString;
                }

                await _sender.SendMessageAsync(serviceBusMessage);
                
                // Record metrics
                Telemetry.JobsEnqueued.Add(1, new TagList
                {
                    { Telemetry.Tags.JobType, message.MessageType.ToString() },
                    { Telemetry.Tags.Status, "success" }
                });

                activity?.SetTag(Telemetry.Tags.Status, "success");
                _logger.LogInformation("✅ [Queue {RequestId}] Successfully sent message to Service Bus queue", requestId);
            }
            else
            {
                // In-memory simulation - just log the message
                _logger.LogInformation("💾 [Queue {RequestId}] IN-MEMORY MODE: Would send message to Service Bus", requestId);
                _logger.LogInformation("📋 [Queue {RequestId}] Message content: {Message}", requestId, JsonSerializer.Serialize(message));
                
                // Record metrics for in-memory mode too
                Telemetry.JobsEnqueued.Add(1, new TagList
                {
                    { Telemetry.Tags.JobType, message.MessageType.ToString() },
                    { Telemetry.Tags.Status, "success_inmemory" }
                });
                
                activity?.SetTag(Telemetry.Tags.Status, "success_inmemory");
            }
        }
        catch (Exception ex)
        {
            // Record failed metrics
            Telemetry.JobsEnqueued.Add(1, new TagList
            {
                { Telemetry.Tags.JobType, message.MessageType.ToString() },
                { Telemetry.Tags.Status, "failed" }
            });

            activity?.SetTag(Telemetry.Tags.Status, "failed");
            activity?.SetTag(Telemetry.Tags.ErrorType, ex.GetType().Name);

            _logger.LogError("💥 [Queue {RequestId}] Error enqueuing job message: {Error}", requestId, ex.Message);
            _logger.LogError("🔍 [Queue {RequestId}] Exception Details: {Details}", requestId, ex.ToString());
            throw;
        }
    }

    public async Task EnqueueJobCreatedAsync(string jobId, Job job, JobPriority priority = JobPriority.Normal)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogInformation("🆕 [Queue {RequestId}] Enqueuing JobCreated message for job: {JobId}", requestId, jobId);

        var payload = new JobCreatedPayload
        {
            Job = job,
            Priority = priority
        };

        var message = new JobMessage(
            JobId: jobId,
            MessageType: JobMessageType.JobCreated,
            CreatedAt: DateTimeOffset.UtcNow,
            Payload: payload
        )
        {
            CorrelationId = Guid.NewGuid()
        };

        await EnqueueJobMessageAsync(message);
        
        _logger.LogInformation("✅ [Queue {RequestId}] JobCreated message enqueued for job: {JobId}", requestId, jobId);
    }

    /// <summary>
    /// Dispose method to clean up Service Bus resources
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_sender != null)
        {
            await _sender.DisposeAsync();
            _logger.LogInformation("🚌 ServiceBus sender disposed");
        }
    }
}