using System.Text.Json;
using Azure.Messaging.ServiceBus;
using CopilotEvalApi.Models;
using CopilotEvalApi.Repositories;
using CopilotEvalApi.Observability;
using System.Diagnostics;

namespace CopilotEvalApi.Services;

/// <summary>
/// Background service that processes job messages from Service Bus
/// </summary>
public class JobProcessor : BackgroundService
{
    private readonly ILogger<JobProcessor> _logger;
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _serviceProvider;
    private ServiceBusClient? _serviceBusClient;
    private ServiceBusProcessor? _processor;

    public JobProcessor(
        ILogger<JobProcessor> logger,
        IConfiguration configuration,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _configuration = configuration;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("🔄 JobProcessor starting up...");

        // Initialize Service Bus connection
        var connectionString = _configuration.GetConnectionString("ServiceBus") ?? "InMemory";
        
        if (connectionString == "InMemory")
        {
            _logger.LogWarning("⚠️ Service Bus not configured, JobProcessor will not process messages");
            
            // Keep the service running but do nothing
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
            }
            return;
        }

        try
        {
            // Create Service Bus client and processor
            _serviceBusClient = new ServiceBusClient(connectionString);
            
            var processorOptions = new ServiceBusProcessorOptions
            {
                AutoCompleteMessages = false, // We'll handle completion manually
                MaxConcurrentCalls = 5, // Process up to 5 messages concurrently
                PrefetchCount = 10, // Prefetch messages for better throughput
                ReceiveMode = ServiceBusReceiveMode.PeekLock,
                SubQueue = SubQueue.None,
                MaxAutoLockRenewalDuration = TimeSpan.FromMinutes(10) // Auto-renew locks for long-running jobs
            };

            _processor = _serviceBusClient.CreateProcessor("job-messages", processorOptions);

            // Configure event handlers
            _processor.ProcessMessageAsync += ProcessMessageAsync;
            _processor.ProcessErrorAsync += ProcessErrorAsync;

            // Start processing
            await _processor.StartProcessingAsync(stoppingToken);
            _logger.LogInformation("🚌 JobProcessor started and listening for messages on 'job-messages' queue");

            // Keep the service running
            while (!stoppingToken.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError("💥 JobProcessor failed to start: {Error}", ex.Message);
            throw;
        }
        finally
        {
            if (_processor != null)
            {
                await _processor.StopProcessingAsync();
                await _processor.DisposeAsync();
                _logger.LogInformation("🛑 JobProcessor stopped");
            }

            if (_serviceBusClient != null)
            {
                await _serviceBusClient.DisposeAsync();
            }
        }
    }

    private async Task ProcessMessageAsync(ProcessMessageEventArgs args)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        var messageId = args.Message.MessageId;
        
        // Start worker activity with correlation from message
        using var workerActivity = WorkerTelemetry.StartActivityWithCorrelation(
            WorkerTelemetry.Activities.MessageProcess, 
            args.Message.ApplicationProperties);
        
        workerActivity?.SetTag(WorkerTelemetry.Tags.MessageId, messageId);
        workerActivity?.SetTag(WorkerTelemetry.Tags.QueueName, "job-messages");
        workerActivity?.SetTag(WorkerTelemetry.Tags.WorkerId, Environment.MachineName);
        workerActivity?.SetTag(WorkerTelemetry.Tags.DeliveryCount, args.Message.DeliveryCount);

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        WorkerTelemetry.IncrementActiveProcessors();

        try
        {
            // Record message received
            WorkerTelemetry.MessagesReceived.Add(1, new TagList
            {
                { WorkerTelemetry.Tags.QueueName, "job-messages" },
                { WorkerTelemetry.Tags.WorkerId, Environment.MachineName }
            });

            _logger.LogInformation("📨 [Processor {RequestId}] Received message: {MessageId}", requestId, messageId);

            // Parse the job message with telemetry
            using var deserializeActivity = WorkerTelemetry.ActivitySource.StartActivity(WorkerTelemetry.Activities.MessageDeserialize);
            var deserializeStopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            var messageBody = args.Message.Body.ToString();
            var jobMessage = JsonSerializer.Deserialize<JobMessage>(messageBody, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
            });

            deserializeStopwatch.Stop();
            WorkerTelemetry.MessageDeserializationDuration.Record(deserializeStopwatch.Elapsed.TotalSeconds, new TagList
            {
                { WorkerTelemetry.Tags.MessageType, "JobMessage" },
                { WorkerTelemetry.Tags.Status, jobMessage != null ? "success" : "failed" }
            });

            if (jobMessage == null)
            {
                workerActivity?.SetTag(WorkerTelemetry.Tags.Status, "dead_lettered");
                workerActivity?.SetTag(WorkerTelemetry.Tags.ErrorType, "deserialization_failed");
                
                _logger.LogError("💥 [Processor {RequestId}] Failed to deserialize job message: {MessageId}", requestId, messageId);
                await args.DeadLetterMessageAsync(args.Message, new Dictionary<string, object>
                {
                    ["DeadLetterReason"] = "DESERIALIZATION_FAILED",
                    ["DeadLetterErrorDescription"] = "Unable to deserialize JobMessage from message body"
                });
                return;
            }

            // Add job-specific tags to worker activity
            workerActivity?.SetTag(WorkerTelemetry.Tags.JobId, jobMessage.JobId);
            workerActivity?.SetTag(WorkerTelemetry.Tags.MessageType, jobMessage.MessageType.ToString());
            workerActivity?.SetTag(WorkerTelemetry.Tags.CorrelationId, jobMessage.CorrelationId?.ToString() ?? "");

            _logger.LogInformation("🎯 [Processor {RequestId}] Processing job message - JobId: {JobId}, Type: {MessageType}", 
                requestId, jobMessage.JobId, jobMessage.MessageType);

            // Handle different message types
            switch (jobMessage.MessageType)
            {
                case JobMessageType.JobCreated:
                    await ProcessJobCreatedAsync(jobMessage, requestId, args.CancellationToken);
                    break;

                case JobMessageType.JobStarted:
                case JobMessageType.JobProgress:
                case JobMessageType.JobCompleted:
                case JobMessageType.JobFailed:
                case JobMessageType.JobCancelled:
                    _logger.LogInformation("ℹ️ [Processor {RequestId}] Message type {MessageType} is informational, acknowledging", 
                        requestId, jobMessage.MessageType);
                    break;

                default:
                    _logger.LogWarning("⚠️ [Processor {RequestId}] Unknown message type: {MessageType}", 
                        requestId, jobMessage.MessageType);
                    break;
            }

            // Complete the message
            await args.CompleteMessageAsync(args.Message);
            
            stopwatch.Stop();
            
            // Record successful processing metrics
            WorkerTelemetry.MessagesProcessed.Add(1, new TagList
            {
                { WorkerTelemetry.Tags.MessageType, jobMessage.MessageType.ToString() },
                { WorkerTelemetry.Tags.QueueName, "job-messages" },
                { WorkerTelemetry.Tags.Status, "completed" }
            });
            
            WorkerTelemetry.MessageProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds, new TagList
            {
                { WorkerTelemetry.Tags.MessageType, jobMessage.MessageType.ToString() },
                { WorkerTelemetry.Tags.Status, "completed" }
            });

            workerActivity?.SetTag(WorkerTelemetry.Tags.Status, "completed");
            
            _logger.LogInformation("✅ [Processor {RequestId}] Message processed successfully: {MessageId}", requestId, messageId);
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            workerActivity?.SetTag(WorkerTelemetry.Tags.Status, "cancelled");
            
            _logger.LogWarning("🚫 [Processor {RequestId}] Message processing cancelled: {MessageId}", requestId, messageId);
            // Don't complete the message, let it be retried
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _logger.LogError("💥 [Processor {RequestId}] Error processing message {MessageId}: {Error}", 
                requestId, messageId, ex.Message);
            _logger.LogError("🔍 [Processor {RequestId}] Exception details: {Details}", requestId, ex.ToString());

            // Determine if we should retry or dead letter
            var deliveryCount = args.Message.DeliveryCount;
            const int maxRetries = 3;

            if (deliveryCount >= maxRetries || !IsRetryableError(ex))
            {
                _logger.LogError("☠️ [Processor {RequestId}] Moving message to dead letter queue after {DeliveryCount} attempts: {MessageId}", 
                    requestId, deliveryCount, messageId);
                
                await args.DeadLetterMessageAsync(args.Message, new Dictionary<string, object>
                {
                    ["DeadLetterReason"] = ex.GetType().Name,
                    ["DeadLetterErrorDescription"] = ex.Message
                });

                // Record dead letter metrics
                WorkerTelemetry.MessagesFailed.Add(1, new TagList
                {
                    { WorkerTelemetry.Tags.MessageType, "unknown" },
                    { WorkerTelemetry.Tags.QueueName, "job-messages" },
                    { WorkerTelemetry.Tags.Status, "dead_lettered" },
                    { WorkerTelemetry.Tags.ErrorType, ex.GetType().Name }
                });

                workerActivity?.SetTag(WorkerTelemetry.Tags.Status, "dead_lettered");
                workerActivity?.SetTag(WorkerTelemetry.Tags.ErrorType, ex.GetType().Name);
            }
            else
            {
                _logger.LogWarning("🔄 [Processor {RequestId}] Abandoning message for retry (attempt {DeliveryCount}/{MaxRetries}): {MessageId}", 
                    requestId, deliveryCount, maxRetries, messageId);
                
                await args.AbandonMessageAsync(args.Message);

                // Record retry metrics
                WorkerTelemetry.MessagesRetried.Add(1, new TagList
                {
                    { WorkerTelemetry.Tags.MessageType, "unknown" },
                    { WorkerTelemetry.Tags.QueueName, "job-messages" },
                    { WorkerTelemetry.Tags.RetryCount, deliveryCount.ToString() }
                });

                workerActivity?.SetTag(WorkerTelemetry.Tags.Status, "retried");
                workerActivity?.SetTag(WorkerTelemetry.Tags.RetryCount, deliveryCount.ToString());
            }
            
            WorkerTelemetry.MessageProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds, new TagList
            {
                { WorkerTelemetry.Tags.MessageType, "unknown" },
                { WorkerTelemetry.Tags.Status, "failed" }
            });
        }
        finally
        {
            WorkerTelemetry.DecrementActiveProcessors();
        }
    }

    private async Task ProcessJobCreatedAsync(JobMessage jobMessage, string requestId, CancellationToken cancellationToken)
    {
        // Start a new activity for job processing with correlation
        using var activity = Telemetry.ActivitySource.StartActivity(Telemetry.Activities.JobProcess);
        activity?.SetTag(Telemetry.Tags.JobId, jobMessage.JobId);
        activity?.SetTag(Telemetry.Tags.CorrelationId, jobMessage.CorrelationId?.ToString() ?? "");
        activity?.SetTag(Telemetry.Tags.Operation, "process");
        activity?.SetTag(Telemetry.Tags.ServiceName, "job-processor");

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        _logger.LogInformation("🆕 [Processor {RequestId}] Processing JobCreated message for job: {JobId}", 
            requestId, jobMessage.JobId);

        try
        {
            // Record metrics for job processing started
            Telemetry.JobsProcessed.Add(1, new TagList
            {
                { Telemetry.Tags.Status, "started" }
            });

            // Create a new scope for dependency injection
            using var scope = _serviceProvider.CreateScope();
            var jobRepository = scope.ServiceProvider.GetRequiredService<IJobRepository>();
            var executionService = scope.ServiceProvider.GetRequiredService<IExecutionService>();
            var queueService = scope.ServiceProvider.GetRequiredService<IJobQueueService>();

            // Get the job details from the database
            var jobEntity = await jobRepository.GetJobByIdAsync(jobMessage.JobId);
            if (jobEntity == null)
            {
                activity?.SetTag(Telemetry.Tags.Status, "failed");
                activity?.SetTag(Telemetry.Tags.ErrorType, "JobNotFound");
                _logger.LogError("❌ [Processor {RequestId}] Job not found in database: {JobId}", requestId, jobMessage.JobId);
                return;
            }

            var job = jobEntity.ToJob();
            activity?.SetTag(Telemetry.Tags.JobType, job.Type.ToString());
            
            _logger.LogInformation("📋 [Processor {RequestId}] Job details loaded - Name: {JobName}, Type: {JobType}, Status: {Status}", 
                requestId, job.Name, job.Type, job.Status);

            // Check if job is in the correct state for processing
            if (job.Status != JobStatus.Pending)
            {
                activity?.SetTag(Telemetry.Tags.Status, "skipped");
                activity?.SetTag("job.status", job.Status.ToString());
                _logger.LogWarning("⚠️ [Processor {RequestId}] Job {JobId} is not in Pending status (current: {Status}), skipping", 
                    requestId, jobMessage.JobId, job.Status);
                return;
            }

            // Send JobStarted message
            var startedMessage = new JobMessage(
                JobId: job.Id,
                MessageType: JobMessageType.JobStarted,
                CreatedAt: DateTimeOffset.UtcNow,
                Payload: new { job.Id, job.Name, job.Type }
            )
            {
                CorrelationId = jobMessage.CorrelationId
            };

            await queueService.EnqueueJobMessageAsync(startedMessage);
            _logger.LogInformation("📤 [Processor {RequestId}] Sent JobStarted message for job: {JobId}", requestId, job.Id);

            // Execute the job with telemetry
            _logger.LogInformation("⚡ [Processor {RequestId}] Starting job execution: {JobId}", requestId, job.Id);
            
            using var executionActivity = Telemetry.ActivitySource.StartActivity(Telemetry.Activities.JobExecute);
            executionActivity?.SetTag(Telemetry.Tags.JobId, job.Id);
            executionActivity?.SetTag(Telemetry.Tags.JobType, job.Type.ToString());
            executionActivity?.SetTag(Telemetry.Tags.CorrelationId, jobMessage.CorrelationId?.ToString() ?? "");
            
            var executionResult = await executionService.ExecuteJobAsync(job, cancellationToken);

            if (executionResult.Success)
            {
                // Update the job entity with artifact reference if available
                if (executionResult.ArtifactBlobReference != null)
                {
                    // Store the blob reference in the job configuration or a new field
                    var updatedEntity = await jobRepository.GetJobByIdAsync(job.Id);
                    if (updatedEntity != null)
                    {
                        // For now, we'll store the blob reference in a simple way
                        // In production, you might want to add a dedicated field to JobEntity
                        _logger.LogInformation("📦 [Processor {RequestId}] Job artifacts stored at: {BlobUrl}", 
                            requestId, executionResult.ArtifactBlobReference.AccessUrl);
                    }
                }

                // Send JobCompleted message
                var completedPayload = new JobCompletedPayload
                {
                    ResultsSummary = executionResult.Results?.Summary,
                    ResultsBlobRef = executionResult.ArtifactBlobReference
                };

                var completedMessage = new JobMessage(
                    JobId: job.Id,
                    MessageType: JobMessageType.JobCompleted,
                    CreatedAt: DateTimeOffset.UtcNow,
                    Payload: completedPayload
                )
                {
                    CorrelationId = jobMessage.CorrelationId,
                    BlobReferences = executionResult.ArtifactBlobReference != null 
                        ? new List<BlobReference> { executionResult.ArtifactBlobReference }
                        : null
                };

                await queueService.EnqueueJobMessageAsync(completedMessage);
                
                // Record success metrics
                stopwatch.Stop();
                Telemetry.JobsCompleted.Add(1, new TagList
                {
                    { Telemetry.Tags.JobType, job.Type.ToString() },
                    { Telemetry.Tags.Status, "success" }
                });
                
                Telemetry.JobProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds, new TagList
                {
                    { Telemetry.Tags.JobType, job.Type.ToString() },
                    { Telemetry.Tags.Status, "success" }
                });

                activity?.SetTag(Telemetry.Tags.Status, "success");
                executionActivity?.SetTag(Telemetry.Tags.Status, "success");
                
                _logger.LogInformation("✅ [Processor {RequestId}] Job completed successfully and notification sent: {JobId}", 
                    requestId, job.Id);
            }
            else
            {
                // Send JobFailed message
                var failedPayload = new JobFailedPayload
                {
                    ErrorDetails = executionResult.ErrorDetails,
                    PartialResults = executionResult.ArtifactBlobReference
                };

                var failedMessage = new JobMessage(
                    JobId: job.Id,
                    MessageType: JobMessageType.JobFailed,
                    CreatedAt: DateTimeOffset.UtcNow,
                    Payload: failedPayload
                )
                {
                    CorrelationId = jobMessage.CorrelationId,
                    RetryCount = jobMessage.RetryCount + 1
                };

                await queueService.EnqueueJobMessageAsync(failedMessage);
                
                // Record failure metrics
                stopwatch.Stop();
                Telemetry.JobsFailed.Add(1, new TagList
                {
                    { Telemetry.Tags.JobType, job.Type.ToString() },
                    { Telemetry.Tags.Status, "failed" }
                });
                
                Telemetry.JobProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds, new TagList
                {
                    { Telemetry.Tags.JobType, job.Type.ToString() },
                    { Telemetry.Tags.Status, "failed" }
                });

                activity?.SetTag(Telemetry.Tags.Status, "failed");
                activity?.SetTag(Telemetry.Tags.ErrorType, "ExecutionFailed");
                executionActivity?.SetTag(Telemetry.Tags.Status, "failed");
                
                _logger.LogError("❌ [Processor {RequestId}] Job failed and notification sent: {JobId}", requestId, job.Id);
            }
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            // Record exception metrics
            Telemetry.JobsFailed.Add(1, new TagList
            {
                { Telemetry.Tags.JobType, "unknown" },
                { Telemetry.Tags.Status, "error" }
            });
            
            Telemetry.JobProcessingDuration.Record(stopwatch.Elapsed.TotalSeconds, new TagList
            {
                { Telemetry.Tags.JobType, "unknown" },
                { Telemetry.Tags.Status, "error" }
            });

            activity?.SetTag(Telemetry.Tags.Status, "error");
            activity?.SetTag(Telemetry.Tags.ErrorType, ex.GetType().Name);

            _logger.LogError("💥 [Processor {RequestId}] Error processing JobCreated message for job {JobId}: {Error}", 
                requestId, jobMessage.JobId, ex.Message);
            throw; // Re-throw to trigger retry/dead letter logic
        }
    }

    private async Task ProcessErrorAsync(ProcessErrorEventArgs args)
    {
        var requestId = Guid.NewGuid().ToString("N")[..8];
        _logger.LogError("💥 [Processor {RequestId}] Service Bus processing error - Source: {Source}, Error: {Error}", 
            requestId, args.ErrorSource, args.Exception.Message);
        _logger.LogError("🔍 [Processor {RequestId}] Exception details: {Details}", requestId, args.Exception.ToString());

        // You could implement additional error handling here, such as:
        // - Sending alerts/notifications
        // - Updating metrics/health checks
        // - Implementing circuit breaker patterns
        
        await Task.CompletedTask;
    }

    private static bool IsRetryableError(Exception exception)
    {
        // Determine if the exception type suggests the operation could be retried
        return exception is not (
            ArgumentException or
            ArgumentNullException or
            NotSupportedException or
            InvalidOperationException or
            JsonException
        );
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🛑 JobProcessor stopping...");
        await base.StopAsync(cancellationToken);
    }
}