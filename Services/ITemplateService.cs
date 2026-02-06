using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

public interface ITemplateService
{
    // Basic CRUD
    Task<Template> CreateTemplateAsync(string name, string templateText, string createdBy, MessageChannel channel = MessageChannel.WhatsApp, string? category = null, string? language = null);
    Task<Template?> GetTemplateByIdAsync(int id);
    Task<IEnumerable<Template>> GetAllTemplatesAsync();
    Task<bool> UpdateTemplateAsync(int id, string name, string templateText);
    Task<bool> DeleteTemplateAsync(int id);
    
    // Approval Workflow
    Task<TemplateRequest> RequestApprovalAsync(int templateId, string requestedBy);
    Task<bool> ApproveTemplateAsync(int templateId, string approvedBy, string? comments = null);
    Task<bool> RejectTemplateAsync(int templateId, string approvedBy, string reason);
    
    // Query Methods
    Task<IEnumerable<Template>> GetPendingTemplatesAsync();
    Task<IEnumerable<Template>> GetApprovedTemplatesAsync();
    Task<IEnumerable<Template>> GetTemplatesByStatusAsync(TemplateStatus status);
    
    // Meta/Twilio Integration
    Task<bool> SyncToTwilioAsync(int templateId);
    Task<bool> UpdateContentSidAsync(int templateId, string contentSid);
    Task<IEnumerable<Tuple<Template, string>>> GetTemplatesNeedingSyncAsync();
}

public class TemplateService : ITemplateService
{
    private readonly AppDbContext _db;
    private readonly TwilioSettings _twilioSettings;
    private readonly ILogger<TemplateService> _logger;

    public TemplateService(AppDbContext db, IOptions<TwilioSettings> twilioSettings, ILogger<TemplateService> logger)
    {
        _db = db;
        _twilioSettings = twilioSettings.Value;
        _logger = logger;
    }

    public async Task<Template> CreateTemplateAsync(string name, string templateText, string createdBy, MessageChannel channel = MessageChannel.WhatsApp, string? category = null, string? language = null)
    {
        var template = new Template
        {
            Name = name,
            Channel = channel,
            TemplateText = templateText,
            Status = TemplateStatus.Draft,
            Category = category ?? "MARKETING",
            Language = language ?? "en_US",
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
            CreatedBy = createdBy
        };

        _db.Templates.Add(template);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Template {TemplateId} created by {CreatedBy}", template.Id, createdBy);

        return template;
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

    public async Task<bool> UpdateTemplateAsync(int id, string name, string templateText)
    {
        var template = await _db.Templates.FindAsync(id);
        if (template == null) return false;

        // If template was already approved, it needs re-approval
        if (template.Status == TemplateStatus.Approved)
        {
            template.Status = TemplateStatus.Pending;
        }

        template.Name = name;
        template.TemplateText = templateText;
        template.UpdatedAt = DateTime.UtcNow;
        
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> DeleteTemplateAsync(int id)
    {
        var template = await _db.Templates.FindAsync(id);
        if (template == null) return false;

        if (template.Status == TemplateStatus.Approved)
        {
            throw new InvalidOperationException("Cannot delete an approved template. Archive it instead.");
        }

        _db.Templates.Remove(template);
        await _db.SaveChangesAsync();
        return true;
    }

    public async Task<TemplateRequest> RequestApprovalAsync(int templateId, string requestedBy)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null) throw new ArgumentException("Template not found");

        if (template.Status != TemplateStatus.Draft && template.Status != TemplateStatus.Rejected)
        {
            throw new InvalidOperationException("Only draft or rejected templates can be submitted for approval");
        }

        template.Status = TemplateStatus.Pending;
        template.SubmittedAt = DateTime.UtcNow;

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
        template.ApprovedBy = approvedBy;
        template.ApprovedAt = DateTime.UtcNow;
        template.UpdatedAt = DateTime.UtcNow;
        
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

        // Auto-sync to Twilio if ContentSid is configured
        if (!string.IsNullOrEmpty(template.TwilioContentSid))
        {
            await SyncToTwilioAsync(templateId);
        }

        return true;
    }

    public async Task<bool> RejectTemplateAsync(int templateId, string approvedBy, string reason)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null) return false;

        template.Status = TemplateStatus.Rejected;
        template.RejectionReason = reason;
        template.UpdatedAt = DateTime.UtcNow;

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
            .OrderByDescending(t => t.SubmittedAt ?? t.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Template>> GetApprovedTemplatesAsync()
    {
        return await _db.Templates
            .Where(t => t.Status == TemplateStatus.Approved)
            .OrderByDescending(t => t.ApprovedAt ?? t.CreatedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<Template>> GetTemplatesByStatusAsync(TemplateStatus status)
    {
        return await _db.Templates
            .Where(t => t.Status == status)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();
    }

    public async Task<bool> SyncToTwilioAsync(int templateId)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null || template.Status != TemplateStatus.Approved)
        {
            _logger.LogWarning("Cannot sync template {TemplateId} - not found or not approved", templateId);
            return false;
        }

        try
        {
            // Update the settings with the ContentSid mapping
            if (_twilioSettings.TemplateContentSids == null)
            {
                _twilioSettings.TemplateContentSids = new Dictionary<string, string>();
            }

            // Use template name as key, but in production you'd use the ContentSid
            if (!string.IsNullOrEmpty(template.TwilioContentSid))
            {
                _twilioSettings.TemplateContentSids[template.Name] = template.TwilioContentSid;
                _logger.LogInformation("Template {TemplateId} synced to Twilio with ContentSid {ContentSid}", 
                    templateId, template.TwilioContentSid);
                return true;
            }
            else
            {
                _logger.LogWarning("Template {TemplateId} has no ContentSid configured", templateId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync template {TemplateId} to Twilio", templateId);
            return false;
        }
    }

    public async Task<bool> UpdateContentSidAsync(int templateId, string contentSid)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null) return false;

        template.TwilioContentSid = contentSid;
        template.UpdatedAt = DateTime.UtcNow;
        
        await _db.SaveChangesAsync();
        _logger.LogInformation("Template {TemplateId} ContentSid updated to {ContentSid}", templateId, contentSid);

        return true;
    }

    public async Task<IEnumerable<Tuple<Template, string>>> GetTemplatesNeedingSyncAsync()
    {
        return await _db.Templates
            .Where(t => t.Status == TemplateStatus.Approved && string.IsNullOrEmpty(t.TwilioContentSid))
            .Select(t => Tuple.Create(t, $"Template {t.Name} needs ContentSid configured"))
            .ToListAsync();
    }
}
