namespace WhatsAppMvcComplete.Models;

public enum EventType
{
    Delivered = 0,
    Failed = 1,
    Read = 2,
    Received = 3,
    Sent = 4,
    Queued = 5
}

public class MessageLog
{
    public int Id { get; set; }
    public int MessageId { get; set; }
    public EventType EventType { get; set; }
    public DateTime EventTimestamp { get; set; }
    public string? WebhookPayload { get; set; }
    public string? ErrorMessage { get; set; }
    
    // Navigation property
    public Message? Message { get; set; }
}
