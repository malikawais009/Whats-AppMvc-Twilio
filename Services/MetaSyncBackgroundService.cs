using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

/// <summary>
/// Background service to sync template statuses from Meta API
/// </summary>
public class MetaSyncBackgroundService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<MetaSyncBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromMinutes(5); // Poll every 5 minutes

    public MetaSyncBackgroundService(IServiceProvider serviceProvider, ILogger<MetaSyncBackgroundService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Meta Sync Background Service starting...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncTemplatesAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Meta template sync");
            }

            await Task.Delay(_interval, stoppingToken);
        }

        _logger.LogInformation("Meta Sync Background Service stopping...");
    }

    private async Task SyncTemplatesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var metaApiService = scope.ServiceProvider.GetRequiredService<IMetaApiService>();

        try
        {
            // Get all templates that have been submitted to Meta
            var submittedTemplates = await dbContext.Templates
                .Where(t => !string.IsNullOrEmpty(t.MetaTemplateId))
                .ToListAsync(cancellationToken);

            if (!submittedTemplates.Any())
            {
                _logger.LogDebug("No templates submitted to Meta to sync");
                return;
            }

            _logger.LogInformation("Syncing {Count} templates from Meta", submittedTemplates.Count);

            var syncedCount = 0;
            var failedCount = 0;

            foreach (var template in submittedTemplates)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    var success = await metaApiService.SyncTemplateStatusAsync(template.Id);
                    if (success)
                    {
                        syncedCount++;
                        
                        // If approved, also sync ContentSid
                        if (template.Status == TemplateStatus.Approved && string.IsNullOrEmpty(template.TwilioContentSid))
                        {
                            await metaApiService.SyncContentSidFromMetaAsync(template.Id);
                        }
                    }
                    else
                    {
                        failedCount++;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error syncing template {TemplateId}", template.Id);
                    failedCount++;
                }

                // Add delay to avoid rate limiting
                await Task.Delay(100, cancellationToken);
            }

            _logger.LogInformation("Meta sync completed. Synced: {Synced}, Failed: {Failed}", syncedCount, failedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Meta sync background service");
        }
    }
}

/// <summary>
/// Webhook handler service for Meta notifications
/// </summary>
public interface IMetaWebhookHandler
{
    Task HandleWebhookAsync(string mode, string token, string challenge, object? entryData);
    Task HandleStatusUpdateAsync(object webhookData);
}

public class MetaWebhookHandler : IMetaWebhookHandler
{
    private readonly AppDbContext _db;
    private readonly ILogger<MetaWebhookHandler> _logger;

    public MetaWebhookHandler(AppDbContext db, ILogger<MetaWebhookHandler> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Handle webhook verification challenge from Meta
    /// </summary>
    public async Task HandleWebhookAsync(string mode, string token, string challenge, object? entryData)
    {
        _logger.LogInformation("Received Meta webhook verification. Mode: {Mode}, Token: {Token}", mode, token);
        await Task.CompletedTask;
    }

    /// <summary>
    /// Handle template status updates from Meta webhook
    /// </summary>
    public async Task HandleStatusUpdateAsync(object webhookData)
    {
        try
        {
            var json = JsonSerializer.Serialize(webhookData);
            _logger.LogInformation("Received Meta status update webhook: {Data}", json);

            // Log the webhook as a message log entry
            var webhookLog = new MessageLog
            {
                MessageId = 0, // System message, no specific message
                EventType = EventType.Received,
                EventTimestamp = DateTime.UtcNow,
                WebhookPayload = json,
                ErrorMessage = null
            };

            _db.MessageLogs.Add(webhookLog);
            await _db.SaveChangesAsync();

            // Process status updates
            await ProcessStatusUpdateAsync(webhookData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling Meta status update webhook");
        }
    }

    private async Task ProcessStatusUpdateAsync(object webhookData)
    {
        try
        {
            // Extract template changes from webhook
            var json = JsonSerializer.Serialize(webhookData);
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.TryGetProperty("entry", out var entries))
            {
                foreach (var entry in entries.EnumerateArray())
                {
                    if (entry.TryGetProperty("changes", out var changes))
                    {
                        foreach (var change in changes.EnumerateArray())
                        {
                            if (change.TryGetProperty("field", out var field) && field.GetString() == "message_templates")
                            {
                                if (change.TryGetProperty("value", out var value))
                                {
                                    if (value.TryGetProperty("message_template_id", out var templateId))
                                    {
                                        var metaTemplateId = templateId.GetString();
                                        if (value.TryGetProperty("message_template_status", out var status))
                                        {
                                            var metaStatus = status.GetString();
                                            if (!string.IsNullOrEmpty(metaTemplateId) && !string.IsNullOrEmpty(metaStatus))
                                            {
                                                await UpdateTemplateStatusAsync(metaTemplateId, metaStatus);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Meta status update");
        }
    }

    private async Task UpdateTemplateStatusAsync(string metaTemplateId, string metaStatus)
    {
        var template = await _db.Templates
            .FirstOrDefaultAsync(t => t.MetaTemplateId == metaTemplateId);

        if (template == null)
        {
            _logger.LogWarning("Template with MetaTemplateId {MetaId} not found", metaTemplateId);
            return;
        }

        template.Status = metaStatus switch
        {
            "APPROVED" => TemplateStatus.Approved,
            "REJECTED" => TemplateStatus.Rejected,
            "PENDING" => TemplateStatus.Pending,
            "IN_APPROVAL" => TemplateStatus.Pending,
            "DISABLED" => TemplateStatus.Rejected,
            "DELETED" => TemplateStatus.Rejected,
            _ => TemplateStatus.Pending
        };

        template.UpdatedAt = DateTime.UtcNow;

        if (metaStatus == "REJECTED")
        {
            template.RejectionReason = "Rejected by Meta";
        }

        await _db.SaveChangesAsync();
        
        _logger.LogInformation("Updated template {TemplateId} status to {Status} based on Meta webhook", 
            template.Id, template.Status);
    }
}
