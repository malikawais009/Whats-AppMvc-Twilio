namespace WhatsAppMvcComplete.Models;

public class Message
{
    public int Id { get; set; }
    public int ConversationId { get; set; }
    public Conversation Conversation { get; set; }
    public string Body { get; set; }
    public bool IsInbound { get; set; }
    public DateTime CreatedAt { get; set; }
}
