using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

public interface IMessagingService
{
    Task<Message> SendSmsAsync(int userId, string message, DateTime? scheduledAt = null);
    Task<Message> SendWhatsAppAsync(int userId, string message, DateTime? scheduledAt = null);
    Task<Message> SendWhatsAppTemplateAsync(int userId, int templateId, Dictionary<string, string> parameters, DateTime? scheduledAt = null);
    Task<bool> SendScheduledMessageAsync(int messageId);
    Task<bool> RetryFailedMessageAsync(int messageId);
    Task ProcessWebhookPayloadAsync(string payload, string eventType);
}
