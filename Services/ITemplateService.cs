using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

public interface ITemplateService
{
    Task<Template> CreateTemplateAsync(string name, string templateText, string createdBy);
    Task<TemplateRequest> RequestApprovalAsync(int templateId, string requestedBy);
    Task<bool> ApproveTemplateAsync(int templateId, string approvedBy, string? comments = null);
    Task<bool> RejectTemplateAsync(int templateId, string approvedBy, string reason);
    Task<IEnumerable<Template>> GetPendingTemplatesAsync();
    Task<IEnumerable<Template>> GetApprovedTemplatesAsync();
    Task<Template?> GetTemplateByIdAsync(int id);
    Task<IEnumerable<Template>> GetAllTemplatesAsync();
}

public class TemplateService : ITemplateService
{
    private readonly AppDbContext _db;
    private readonly ILogger<TemplateService> _logger;

    public TemplateService(AppDbContext db, ILogger<TemplateService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<Template> CreateTemplateAsync(string name, string templateText, string createdBy)
    {
        var template = new Template
        {
            Name = name,
            Channel = MessageChannel.WhatsApp,
            TemplateText = templateText,
            Status = TemplateStatus.Pending,
            CreatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        _db.Templates.Add(template);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Template {TemplateId} created by {CreatedBy}", template.Id, createdBy);

        return template;
    }

    public async Task<TemplateRequest> RequestApprovalAsync(int templateId, string requestedBy)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null) throw new ArgumentException("Template not found");

        var request = new TemplateRequest
        {
            TemplateId = templateId,
            RequestedBy = requestedBy,
            RequestedAt = DateTime.UtcNow,
            Status = RequestStatus.Pending
        };

        _db.TemplateRequests.Add(request);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Approval requested for template {TemplateId} by {RequestedBy}", templateId, requestedBy);

        return request;
    }

    public async Task<bool> ApproveTemplateAsync(int templateId, string approvedBy, string? comments = null)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null) return false;

        template.Status = TemplateStatus.Approved;
        
        var request = await _db.TemplateRequests
            .Where(tr => tr.TemplateId == templateId && tr.Status == RequestStatus.Pending)
            .FirstOrDefaultAsync();

        if (request != null)
        {
            request.Status = RequestStatus.Approved;
            request.ApprovedBy = approvedBy;
            request.ApprovedAt = DateTime.UtcNow;
            request.Comments = comments;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Template {TemplateId} approved by {ApprovedBy}", templateId, approvedBy);

        return true;
    }

    public async Task<bool> RejectTemplateAsync(int templateId, string approvedBy, string reason)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null) return false;

        template.Status = TemplateStatus.Rejected;
        template.RejectionReason = reason;

        var request = await _db.TemplateRequests
            .Where(tr => tr.TemplateId == templateId && tr.Status == RequestStatus.Pending)
            .FirstOrDefaultAsync();

        if (request != null)
        {
            request.Status = RequestStatus.Rejected;
            request.ApprovedBy = approvedBy;
            request.ApprovedAt = DateTime.UtcNow;
            request.Comments = reason;
        }

        await _db.SaveChangesAsync();
        _logger.LogInformation("Template {TemplateId} rejected by {ApprovedBy}. Reason: {Reason}", 
            templateId, approvedBy, reason);

        return true;
    }

    public async Task<IEnumerable<Template>> GetPendingTemplatesAsync()
    {
        return await _db.Templates
            .Where(t => t.Status == TemplateStatus.Pending)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Template>> GetApprovedTemplatesAsync()
    {
        return await _db.Templates
            .Where(t => t.Status == TemplateStatus.Approved)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<Template?> GetTemplateByIdAsync(int id)
    {
        return await _db.Templates.FindAsync(id);
    }

    public async Task<IEnumerable<Template>> GetAllTemplatesAsync()
    {
        return await _db.Templates
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
