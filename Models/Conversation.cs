namespace WhatsAppMvcComplete.Models;

public class Conversation
{
    public int Id { get; set; }
    public required string PhoneNumber { get; set; }
    public DateTime LastMessageAt { get; set; }
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
