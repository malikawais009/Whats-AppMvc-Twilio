using System.ComponentModel.DataAnnotations.Schema;

namespace WhatsAppMvcComplete.Models;

public enum MessageChannel
{
    SMS = 0,
    WhatsApp = 1
}

public enum MessageStatus
{
    Pending = 0,
    Sent = 1,
    Delivered = 2,
    Failed = 3,
    Received = 4,
    Read = 5
}

public class Message
{
    public int Id { get; set; }
    public int? UserId { get; set; }
    public int? ConversationId { get; set; }
    public MessageChannel Channel { get; set; }
    public string MessageText { get; set; } = string.Empty;
    public MessageStatus Status { get; set; }
    public string? TwilioMessageId { get; set; }
    public DateTime? ScheduledAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public int RetryCount { get; set; }
    public int? TemplateId { get; set; }
    
    // Backward compatibility property - not mapped to database
    [NotMapped]
    public string Body
    { 
        get => MessageText ?? string.Empty;
        set => MessageText = value ?? string.Empty;
    }
    public bool IsInbound { get; set; }
    
    // Navigation properties
    public User? User { get; set; }
    public Conversation? Conversation { get; set; }
    public Template? Template { get; set; }
    public ICollection<MessageLog> Logs { get; set; } = new List<MessageLog>();
}
