namespace WhatsAppMvcComplete.Models;

public enum TemplateStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class Template
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public MessageChannel Channel { get; set; }
    public required string TemplateText { get; set; }
    public TemplateStatus Status { get; set; }
    public string? RejectionReason { get; set; }
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    
    // Navigation properties
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<TemplateRequest> TemplateRequests { get; set; } = new List<TemplateRequest>();
}
