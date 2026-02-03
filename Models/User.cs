namespace WhatsAppMvcComplete.Models;

public class User
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public required string Phone { get; set; }
    public string? WhatsAppNumber { get; set; }
    public string? Email { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public ICollection<Message> Messages { get; set; } = new List<Message>();
}
