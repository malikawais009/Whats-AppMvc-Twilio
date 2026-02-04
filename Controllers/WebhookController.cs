using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Hubs;
using WhatsAppMvcComplete.Models;
using WhatsAppMvcComplete.Services;

namespace WhatsAppMvcComplete.Controllers;

[ApiController]
[Route("api/twilio")]
public class WebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IMessagingService _messagingService;
    private readonly IConfiguration _configuration;
    private readonly ILogger<WebhookController> _logger;
    private readonly IHubContext<ChatHub> _hubContext;
    public WebhookController(
        AppDbContext db,
        IMessagingService messagingService,
        IConfiguration configuration,
        ILogger<WebhookController> logger,
        IHubContext<ChatHub> hubContext)
    {
        _db = db;
        _messagingService = messagingService;
        _configuration = configuration;
        _logger = logger;
        _hubContext = hubContext;
    }

    /// <summary>
    /// Handle incoming WhatsApp messages
    /// </summary>
    [HttpPost("webhook/incoming")]
    public async Task<IActionResult> IncomingWebhook()
    {
        var form = await Request.ReadFormAsync();
        
        var from = form["From"].ToString();
        var body = form["Body"].ToString();
        var messageSid = form["MessageSid"].ToString();
        var to = form["To"].ToString();

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(body))
        {
            _logger.LogWarning("Received empty webhook - From: {From}, Body: {Body}", from, body);
            return Ok();
        }

        // Check idempotency - prevent duplicate processing
        var idempotencyKey = GenerateIdempotencyKey(messageSid, from, body);
        if (await IsDuplicateAsync(idempotencyKey))
        {
            _logger.LogInformation("Duplicate webhook ignored - Key: {Key}", idempotencyKey);
            return Ok();
        }

        // Create conversation if doesn't exist
        var conversation = await _db.Conversations
            .FirstOrDefaultAsync(c => c.PhoneNumber == from);

        if (conversation == null)
        {
            conversation = new Conversation
            {
                PhoneNumber = from,
                LastMessageAt = DateTime.UtcNow
            };
            _db.Conversations.Add(conversation);
            await _db.SaveChangesAsync();
        }

        // Store inbound message
        var message = new Message
        {
            ConversationId = conversation.Id,
            UserId = null, // Will be mapped if user exists
            Channel = MessageChannel.WhatsApp,
            MessageText = body,
            Status = MessageStatus.Received,
            TwilioMessageId = messageSid,
            CreatedAt = DateTime.UtcNow,
            RetryCount = 0,
            IsInbound = true
        };

        _db.Messages.Add(message);
        await _db.SaveChangesAsync(); // Save to get the message ID

        // Log the received event
        var log = new MessageLog
        {
            MessageId = message.Id,
            EventType = EventType.Received,
            EventTimestamp = DateTime.UtcNow,
            WebhookPayload = $"{{\"From\": \"{from}\", \"Body\": \"{body}\", \"MessageSid\": \"{messageSid}\"}}"
        };

        _db.MessageLogs.Add(log);
        await _db.SaveChangesAsync();
        await _hubContext.Clients
                .Group($"conversation-{conversation.Id}")
                .SendAsync("ReceiveMessage", new
                {
                    conversationId = conversation.Id,
                    body = body,
                    inbound = true,
                    time = message.CreatedAt.ToLocalTime().ToString("HH:mm")
                });
        // Mark idempotency key as processed
        await MarkAsProcessedAsync(idempotencyKey, message.Id);

        _logger.LogInformation("Received WhatsApp message {MessageSid} from {From}", messageSid, from);

        return Ok();
    }

    /// <summary>
    /// Handle delivery status callbacks
    /// </summary>
    [HttpPost("webhook/status")]
    public async Task<IActionResult> StatusWebhook()
    {
        var form = await Request.ReadFormAsync();
        
        var messageSid = form["MessageSid"].ToString();
        var messageStatus = form["MessageStatus"].ToString();
        var errorCode = form["ErrorCode"].ToString();

        if (string.IsNullOrEmpty(messageSid))
        {
            return BadRequest("Missing MessageSid");
        }

        // Validate webhook authenticity (Twilio signature validation)
        if (!ValidateTwilioSignature())
        {
            _logger.LogWarning("Invalid webhook signature for message {MessageSid}", messageSid);
            return Unauthorized();
        }

        // Check idempotency
        var idempotencyKey = $"status_{messageSid}_{messageStatus}";
        if (await IsDuplicateAsync(idempotencyKey))
        {
            _logger.LogInformation("Duplicate status webhook ignored - Key: {Key}", idempotencyKey);
            return Ok();
        }

        var message = await _db.Messages
            .FirstOrDefaultAsync(m => m.TwilioMessageId == messageSid);

        if (message == null)
        {
            _logger.LogWarning("Message not found for TwilioSid {MessageSid}", messageSid);
            return NotFound();
        }

        // Map Twilio status to our status
        var eventType = messageStatus.ToLowerInvariant() switch
        {
            "delivered" => EventType.Delivered,
            "read" => EventType.Read,
            "failed" => EventType.Failed,
            "sent" => EventType.Sent,
            "queued" => EventType.Queued,
            _ => EventType.Sent
        };

        message.Status = eventType switch
        {
            EventType.Delivered => MessageStatus.Delivered,
            EventType.Read => MessageStatus.Read,
            EventType.Failed => MessageStatus.Failed,
            _ => message.Status
        };

        var log = new MessageLog
        {
            MessageId = message.Id,
            EventType = eventType,
            EventTimestamp = DateTime.UtcNow,
            WebhookPayload = $"{{\"MessageSid\": \"{messageSid}\", \"Status\": \"{messageStatus}\", \"ErrorCode\": \"{errorCode}\"}}"
        };

        if (eventType == EventType.Failed)
        {
            log.ErrorMessage = $"Twilio Error Code: {errorCode}";
        }

        _db.MessageLogs.Add(log);
        await _db.SaveChangesAsync();

        // Handle permanent failures
        if (eventType == EventType.Failed && (errorCode == "123" || errorCode == "21211" || errorCode == "21612"))
        {
            _logger.LogError("Permanent failure for message {MessageSid}. ErrorCode: {ErrorCode}", messageSid, errorCode);
            // TODO: Notify admin of permanent failure
        }

        await MarkAsProcessedAsync(idempotencyKey, message.Id);

        _logger.LogInformation("Processed status webhook {Status} for message {MessageSid}", messageStatus, messageSid);

        return Ok();
    }

    /// <summary>
    /// Handle message callback (legacy)
    /// </summary>
    [HttpPost("webhook/message")]
    public async Task<IActionResult> MessageWebhook()
    {
        var form = await Request.ReadFormAsync();
        var payload = $"From={form["From"]}&To={form["To"]}&Body={form["Body"]}&MessageSid={form["MessageSid"]}";
        
        return await ProcessWebhookPayloadAsync(payload, "Received");
    }

    /// <summary>
    /// API endpoint to send SMS message
    /// </summary>
    [HttpPost("api/send-sms")]
    public async Task<IActionResult> SendSms([FromBody] SendSmsRequest request)
    {
        try
        {
            if (request == null || request.UserId <= 0 || string.IsNullOrEmpty(request.Message))
            {
                return BadRequest(new { error = "Invalid request. UserId and Message are required." });
            }

            var user = await _db.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            var message = await _messagingService.SendSmsAsync(request.UserId, request.Message, request.ScheduledAt);
            
            return Ok(new { 
                success = true, 
                messageId = message.Id, 
                twilioMessageId = message.TwilioMessageId,
                status = message.Status.ToString(),
                scheduledAt = message.ScheduledAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending SMS via API");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// API endpoint to send WhatsApp message
    /// </summary>
    [HttpPost("api/send-whatsapp")]
    public async Task<IActionResult> SendWhatsApp([FromBody] SendWhatsAppRequest request)
    {
        try
        {
            if (request == null || request.UserId <= 0 || string.IsNullOrEmpty(request.Message))
            {
                return BadRequest(new { error = "Invalid request. UserId and Message are required." });
            }

            var user = await _db.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            if (string.IsNullOrEmpty(user.WhatsAppNumber))
            {
                return BadRequest(new { error = "User WhatsApp number not found" });
            }

            var message = await _messagingService.SendWhatsAppAsync(request.UserId, request.Message, request.ScheduledAt);
            
            return Ok(new { 
                success = true, 
                messageId = message.Id, 
                twilioMessageId = message.TwilioMessageId,
                status = message.Status.ToString(),
                scheduledAt = message.ScheduledAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending WhatsApp via API");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// API endpoint to send WhatsApp template message
    /// </summary>
    [HttpPost("api/send-template")]
    public async Task<IActionResult> SendTemplate([FromBody] SendTemplateRequest request)
    {
        try
        {
            if (request == null || request.UserId <= 0 || request.TemplateId <= 0)
            {
                return BadRequest(new { error = "Invalid request. UserId and TemplateId are required." });
            }

            var user = await _db.Users.FindAsync(request.UserId);
            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            var parameters = request.Parameters ?? new Dictionary<string, string>();
            var message = await _messagingService.SendWhatsAppTemplateAsync(request.UserId, request.TemplateId, parameters, request.ScheduledAt);
            
            return Ok(new { 
                success = true, 
                messageId = message.Id, 
                twilioMessageId = message.TwilioMessageId,
                status = message.Status.ToString(),
                scheduledAt = message.ScheduledAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending template via API");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    /// <summary>
    /// API endpoint to retry a failed message
    /// </summary>
    [HttpPost("api/retry-message")]
    public async Task<IActionResult> RetryMessage([FromBody] RetryMessageRequest request)
    {
        try
        {
            if (request == null || request.MessageId <= 0)
            {
                return BadRequest(new { error = "MessageId is required." });
            }

            var result = await _messagingService.RetryFailedMessageAsync(request.MessageId);
            
            if (result)
            {
                return Ok(new { success = true, message = "Retry initiated" });
            }
            else
            {
                return BadRequest(new { error = "Failed to retry message. Check if maximum retries exceeded or message not found." });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrying message via API");
            return StatusCode(500, new { error = ex.Message });
        }
    }

    private async Task<IActionResult> ProcessWebhookPayloadAsync(string payload, string eventType)
    {
        await _messagingService.ProcessWebhookPayloadAsync(payload, eventType);
        return Ok();
    }

    private string GenerateIdempotencyKey(string messageSid, string from, string body)
    {
        var input = $"{messageSid}:{from}:{body}";
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private async Task<bool> IsDuplicateAsync(string key)
    {
        // In production, use Redis or a dedicated table for idempotency keys
        // For now, check recent logs
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        return await _db.MessageLogs
            .AnyAsync(ml => ml.WebhookPayload != null && 
                          ml.WebhookPayload.Contains(key) && 
                          ml.EventTimestamp > fiveMinutesAgo);
    }

    private async Task MarkAsProcessedAsync(string key, int messageId)
    {
        // In production, store in Redis/Distributed cache
        // For now, just log it
        _logger.LogDebug("Marked key {Key} as processed for message {MessageId}", key, messageId);
    }

    private bool ValidateTwilioSignature()
    {
        // In production, validate Twilio signature:
        // var signature = Request.Headers["X-Twilio-Signature"].ToString();
        // return TwilioClient.ValidateRequest(_configuration["Twilio:AuthToken"], signature, Request.Url, Request.Form);

        // For development, always return true
        return true;
    }
}

// Request DTOs for API endpoints
public class SendSmsRequest
{
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
}

public class SendWhatsAppRequest
{
    public int UserId { get; set; }
    public string Message { get; set; } = string.Empty;
    public DateTime? ScheduledAt { get; set; }
}

public class SendTemplateRequest
{
    public int UserId { get; set; }
    public int TemplateId { get; set; }
    public Dictionary<string, string>? Parameters { get; set; }
    public DateTime? ScheduledAt { get; set; }
}

public class RetryMessageRequest
{
    public int MessageId { get; set; }
}
