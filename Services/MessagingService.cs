using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

public class MessagingService : IMessagingService
{
    private readonly AppDbContext _db;
    private readonly TwilioSettings _settings;
    private readonly ILogger<MessagingService> _logger;
    private readonly SemaphoreSlim _throttle = new(10, 10); // Limit concurrent requests

    public MessagingService(AppDbContext db, IOptions<TwilioSettings> settings, ILogger<MessagingService> logger)
    {
        _db = db;
        _settings = settings.Value;
        _logger = logger;
        TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);
    }

    public async Task<Message> SendSmsAsync(int userId, string message, DateTime? scheduledAt = null)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) throw new ArgumentException("User not found");

        var msg = new Message
        {
            UserId = userId,
            Channel = MessageChannel.SMS,
            MessageText = message,
            Status = scheduledAt.HasValue ? MessageStatus.Pending : MessageStatus.Sent,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        if (!scheduledAt.HasValue)
        {
            await ExecuteSendAsync(msg, user.Phone);
        }

        return msg;
    }

    public async Task<Message> SendWhatsAppAsync(int userId, string message, DateTime? scheduledAt = null)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) throw new ArgumentException("User not found");
        if (string.IsNullOrEmpty(user.WhatsAppNumber))
            throw new ArgumentException("User WhatsApp number not found");

        var msg = new Message
        {
            UserId = userId,
            Channel = MessageChannel.WhatsApp,
            MessageText = message,
            Status = scheduledAt.HasValue ? MessageStatus.Pending : MessageStatus.Sent,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0
        };

        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        if (!scheduledAt.HasValue)
        {
            await ExecuteSendAsync(msg, user.WhatsAppNumber);
        }

        return msg;
    }

    public async Task<Message> SendWhatsAppTemplateAsync(int userId, int templateId, Dictionary<string, string> parameters, DateTime? scheduledAt = null)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) throw new ArgumentException("User not found");

        var template = await _db.Templates.FindAsync(templateId);
        if (template == null) throw new ArgumentException("Template not found");
        if (template.Status != TemplateStatus.Approved)
            throw new InvalidOperationException("Template is not approved");

        if (string.IsNullOrEmpty(user.WhatsAppNumber))
            throw new ArgumentException("User WhatsApp number not found");

        // Replace template placeholders with actual values
        var messageText = template.TemplateText;
        foreach (var param in parameters)
        {
            messageText = messageText.Replace($"{{{{{param.Key}}}}}", param.Value);
        }

        var msg = new Message
        {
            UserId = userId,
            Channel = MessageChannel.WhatsApp,
            MessageText = messageText,
            Status = scheduledAt.HasValue ? MessageStatus.Pending : MessageStatus.Sent,
            ScheduledAt = scheduledAt,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0,
            TemplateId = templateId
        };

        _db.Messages.Add(msg);
        await _db.SaveChangesAsync();

        if (!scheduledAt.HasValue)
        {
            await ExecuteSendAsync(msg, user.WhatsAppNumber, templateId);
        }

        return msg;
    }

    public async Task<bool> SendScheduledMessageAsync(int messageId)
    {
        var message = await _db.Messages.FindAsync(messageId);
        if (message == null || message.Status != MessageStatus.Pending)
            return false;

        var user = await _db.Users.FindAsync(message.UserId);
        if (user == null) return false;

        var phoneNumber = message.Channel == MessageChannel.SMS ? user.Phone : user.WhatsAppNumber;
        if (string.IsNullOrEmpty(phoneNumber))
            return false;

        return await ExecuteSendAsync(message, phoneNumber);
    }

    public async Task<bool> RetryFailedMessageAsync(int messageId)
    {
        var message = await _db.Messages.FindAsync(messageId);
        if (message == null || message.Status != MessageStatus.Failed)
            return false;

        if (message.RetryCount >= 3)
        {
            _logger.LogWarning("Message {MessageId} has exceeded maximum retry attempts", messageId);
            return false;
        }

        var user = await _db.Users.FindAsync(message.UserId);
        if (user == null) return false;

        message.RetryCount++;
        message.Status = MessageStatus.Pending;
        message.ScheduledAt = DateTime.UtcNow.AddMinutes(Math.Pow(2, message.RetryCount)); // Exponential backoff
        await _db.SaveChangesAsync();

        _logger.LogInformation("Message {MessageId} scheduled for retry attempt {RetryCount}", messageId, message.RetryCount);

        return await SendScheduledMessageAsync(messageId);
    }

    public async Task ProcessWebhookPayloadAsync(string payload, string eventType)
    {
        try
        {
            var document = System.Text.Json.JsonDocument.Parse(payload);
            var root = document.RootElement;

            // Extract TwilioMessageId for idempotency
            var messageSid = root.GetProperty("MessageSid").GetString();
            
            // Check if already processed (idempotency)
            var existingLog = await _db.MessageLogs
                .Include(ml => ml.Message)
                .FirstOrDefaultAsync(ml => ml.Message != null && ml.Message.TwilioMessageId == messageSid && 
                    (ml.EventType == EventType.Delivered || ml.EventType == EventType.Read));

            if (existingLog != null)
            {
                _logger.LogInformation("Webhook already processed for message {MessageSid}", messageSid);
                return;
            }

            // Find existing message by TwilioMessageId
            var message = await _db.Messages.FirstOrDefaultAsync(m => m.TwilioMessageId == messageSid);

            if (message != null)
            {
                var eventTypeEnum = Enum.Parse<EventType>(eventType, true);
                message.Status = eventTypeEnum switch
                {
                    EventType.Delivered => MessageStatus.Delivered,
                    EventType.Read => MessageStatus.Read,
                    EventType.Failed => MessageStatus.Failed,
                    _ => message.Status
                };

                var log = new MessageLog
                {
                    MessageId = message.Id,
                    EventType = eventTypeEnum,
                    EventTimestamp = DateTime.UtcNow,
                    WebhookPayload = payload
                };

                _db.MessageLogs.Add(log);
                await _db.SaveChangesAsync();

                _logger.LogInformation("Processed webhook event {EventType} for message {MessageId}", eventType, message.Id);
            }
            else
            {
                _logger.LogWarning("No message found with TwilioMessageId {MessageSid}", messageSid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhook payload");
        }
    }

    private async Task<bool> ExecuteSendAsync(Message message, string phoneNumber, int? templateId = null)
    {
        await _throttle.WaitAsync();
        try
        {
            MessageResource result;

            if (message.Channel == MessageChannel.SMS)
            {
                result = await MessageResource.CreateAsync(
                    from: new PhoneNumber(_settings.WhatsAppFrom),
                    to: new PhoneNumber(phoneNumber),
                    body: message.MessageText
                );
            }
            else if (templateId.HasValue)
            {
                // Template message - body is null, template configured in Twilio
                result = await MessageResource.CreateAsync(
                    from: new PhoneNumber(_settings.WhatsAppFrom),
                    to: new PhoneNumber($"whatsapp:{phoneNumber}"),
                    body: null
                );
            }
            else
            {
                result = await MessageResource.CreateAsync(
                    from: new PhoneNumber(_settings.WhatsAppFrom),
                    to: new PhoneNumber($"whatsapp:{phoneNumber}"),
                    body: message.MessageText
                );
            }

            message.TwilioMessageId = result.Sid;
            message.Status = MessageStatus.Sent;

            // Log sent event
            var sentLog = new MessageLog
            {
                MessageId = message.Id,
                EventType = EventType.Sent,
                EventTimestamp = DateTime.UtcNow,
                WebhookPayload = $"{{\"MessageSid\": \"{result.Sid}\", \"Status\": \"{result.Status}\"}}"
            };

            _db.MessageLogs.Add(sentLog);
            await _db.SaveChangesAsync();

            _logger.LogInformation("Message {MessageId} sent successfully. TwilioSid: {TwilioSid}", message.Id, result.Sid);
            return true;
        }
        catch (Exception ex)
        {
            message.Status = MessageStatus.Failed;
            message.RetryCount++;

            // Log failed event
            var failedLog = new MessageLog
            {
                MessageId = message.Id,
                EventType = EventType.Failed,
                EventTimestamp = DateTime.UtcNow,
                ErrorMessage = ex.Message,
                WebhookPayload = $"{{\"Error\": \"{ex.Message}\"}}"
            };

            _db.MessageLogs.Add(failedLog);
            await _db.SaveChangesAsync();

            _logger.LogError(ex, "Failed to send message {MessageId}", message.Id);
            return false;
        }
        finally
        {
            _throttle.Release();
        }
    }
}
