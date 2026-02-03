namespace WhatsAppMvcComplete.Models;

public enum RequestStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}

public class TemplateRequest
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public string RequestedBy { get; set; } = string.Empty;
    public DateTime RequestedAt { get; set; }
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public RequestStatus Status { get; set; }
    public string? Comments { get; set; }
    
    // Navigation property
    public Template? Template { get; set; }
}
