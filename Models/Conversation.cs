using System.ComponentModel.DataAnnotations;

namespace WhatsAppMvcComplete.Models;

public class Conversation
{
    public int Id { get; set; }
    public string PhoneNumber { get; set; }
    public DateTime LastMessageAt { get; set; }
    public List<Message> Messages { get; set; } = new();
}
