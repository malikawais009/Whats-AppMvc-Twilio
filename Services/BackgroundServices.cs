using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

/// <summary>
/// Background service for processing scheduled messages and retries
/// </summary>
public class MessageSchedulerService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MessageSchedulerService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    public MessageSchedulerService(IServiceProvider serviceProvider, ILogger<MessageSchedulerService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Message Scheduler Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessScheduledMessagesAsync();
                await ProcessRetriesAsync();
                await Task.Delay(_interval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in message scheduler service");
                await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
            }
        }

        _logger.LogInformation("Message Scheduler Service stopping...");
    }

    private async Task ProcessScheduledMessagesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

        // Find messages that are pending and ready to be sent
        var scheduledMessages = await db.Messages
            .Where(m => m.Status == MessageStatus.Pending 
                     && m.ScheduledAt <= DateTime.UtcNow
                     && m.RetryCount < 3)
            .OrderBy(m => m.ScheduledAt)
            .Take(100)
            .ToListAsync();

        foreach (var message in scheduledMessages)
        {
            try
            {
                _logger.LogInformation("Processing scheduled message {MessageId}", message.Id);
                await messagingService.SendScheduledMessageAsync(message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process scheduled message {MessageId}", message.Id);
            }
        }
    }

    private async Task ProcessRetriesAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

        // Find failed messages that are ready for retry
        var failedMessages = await db.Messages
            .Where(m => m.Status == MessageStatus.Failed 
                     && m.RetryCount < 3
                     && m.ScheduledAt <= DateTime.UtcNow)
            .OrderBy(m => m.ScheduledAt)
            .Take(50)
            .ToListAsync();

        foreach (var message in failedMessages)
        {
            try
            {
                _logger.LogInformation("Retrying failed message {MessageId}, attempt {RetryCount}", 
                    message.Id, message.RetryCount + 1);
                await messagingService.RetryFailedMessageAsync(message.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retry message {MessageId}", message.Id);
            }
        }
    }
}

/// <summary>
/// Service for handling high-volume message processing
/// </summary>
public class HighVolumeProcessingService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<HighVolumeProcessingService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(1);
    private const int BATCH_SIZE = 100;

    public HighVolumeProcessingService(IServiceProvider serviceProvider, ILogger<HighVolumeProcessingService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("High Volume Processing Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessHighVolumeBatchAsync();
                await Task.Delay(_interval, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in high volume processing service");
            }
        }

        _logger.LogInformation("High Volume Processing Service stopping...");
    }

    private async Task ProcessHighVolumeBatchAsync()
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var messagingService = scope.ServiceProvider.GetRequiredService<IMessagingService>();

        // Simulate high-volume processing (1000 messages per minute)
        // Process pending messages in batches
        var pendingMessages = await db.Messages
            .Where(m => m.Status == MessageStatus.Pending 
                     && m.Channel == MessageChannel.WhatsApp)
            .OrderBy(m => m.CreatedAt)
            .Take(BATCH_SIZE)
            .ToListAsync();

        var tasks = pendingMessages.Select(m => 
        {
            try
            {
                return messagingService.SendScheduledMessageAsync(m.Id);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process message {MessageId} in high volume batch", m.Id);
                return Task.CompletedTask;
            }
        });

        await Task.WhenAll(tasks);

        if (pendingMessages.Any())
        {
            _logger.LogInformation("Processed {Count} messages in high volume batch", pendingMessages.Count);
        }
    }
}
