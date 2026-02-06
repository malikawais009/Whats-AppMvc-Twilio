namespace WhatsAppMvcComplete.Models;

public enum TemplateStatus
{
    Draft = -1,
    Pending = 0,
    Approved = 1,
    Rejected = 2,
    Archived = 3
}

public class Template
{
    public int Id { get; set; }
    
    // Basic Info
    public required string Name { get; set; }
    public MessageChannel Channel { get; set; }
    public required string TemplateText { get; set; }
    public TemplateStatus Status { get; set; }
    
    // Meta/Twilio Integration
    public string? MetaTemplateId { get; set; }      // Meta Business Manager Template ID
    public string? TwilioContentSid { get; set; }    // Twilio Content SID (HX...)
    public string? Language { get; set; }            // e.g., "en_US"
    public string? Category { get; set; }             // e.g., "MARKETING", "TRANSACTIONAL"
    
    // Approval Info
    public string? RejectionReason { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    
    // Tracking
    public string? CreatedBy { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    // Navigation properties
    public ICollection<Message> Messages { get; set; } = new List<Message>();
    public ICollection<TemplateRequest> TemplateRequests { get; set; } = new List<TemplateRequest>();
}
