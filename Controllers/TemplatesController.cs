using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;
using WhatsAppMvcComplete.Services;

namespace WhatsAppMvcComplete.Controllers;

public class TemplatesController : Controller
{
    private readonly ITemplateService _templateService;
    private readonly ITwilioTemplateService _twilioTemplateService;
    private readonly AppDbContext _db;
    private readonly ILogger<TemplatesController> _logger;

    public TemplatesController(
        ITemplateService templateService, 
        ITwilioTemplateService twilioTemplateService,
        AppDbContext db, 
        ILogger<TemplatesController> logger)
    {
        _templateService = templateService;
        _twilioTemplateService = twilioTemplateService;
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// List all templates with optional status filter
    /// </summary>
    public async Task<IActionResult> Index(string? status)
    {
        IEnumerable<Template> templates;

        if (!string.IsNullOrEmpty(status))
        {
            if (Enum.TryParse<TemplateStatus>(status, out var templateStatus))
            {
                templates = templateStatus switch
                {
                    TemplateStatus.Pending => await _templateService.GetPendingTemplatesAsync(),
                    TemplateStatus.Approved => await _templateService.GetApprovedTemplatesAsync(),
                    _ => await _templateService.GetTemplatesByStatusAsync(templateStatus)
                };
            }
            else
            {
                templates = await _templateService.GetAllTemplatesAsync();
            }
        }
        else
        {
            templates = await _templateService.GetAllTemplatesAsync();
        }

        ViewBag.StatusFilter = status;
        ViewBag.StatusCounts = new Dictionary<string, int>
        {
            ["All"] = await _templateService.GetAllTemplatesAsync().ContinueWith(t => t.Result.Count()),
            ["Pending"] = (await _templateService.GetPendingTemplatesAsync()).Count(),
            ["Approved"] = (await _templateService.GetApprovedTemplatesAsync()).Count()
        };

        return View(templates.ToList());
    }

    /// <summary>
    /// Show template creation form
    /// </summary>
    public IActionResult Create()
    {
        return View();
    }

    /// <summary>
    /// Create a new template
    /// Template is automatically created on Twilio Content Template Builder
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Create(string name, string templateText, string? category, string? language)
    {
        try
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(templateText))
            {
                TempData["Error"] = "Name and template text are required";
                return View();
            }

            var template = await _templateService.CreateTemplateAsync(
                name,
                templateText,
                "CurrentUser", // In production, get from auth
                MessageChannel.WhatsApp,
                category,
                language
            );

            // Automatically create template on Twilio Content Template Builder
            try
            {
                var (success, contentSid, error) = await _twilioTemplateService.CreateTemplateAsync(template);
                
                if (success && !string.IsNullOrEmpty(contentSid))
                {
                    // Update local template with Twilio ContentSid
                    template.TwilioContentSid = contentSid;
                    template.Status = TemplateStatus.Pending; // Awaiting Meta approval via Twilio
                    template.SubmittedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();

                    _logger.LogInformation("Template {TemplateId} created on Twilio. ContentSid: {ContentSid}", 
                        template.Id, contentSid);

                    TempData["Success"] = $"Template created successfully on Twilio Content Template Builder. " +
                        $"ContentSid: {contentSid}. " +
                        $"The template has been submitted to Meta for approval. Check the details page for status updates.";
                }
                else
                {
                    _logger.LogError("Failed to create template on Twilio. Error: {Error}", error);
                    TempData["Warning"] = $"Template created locally but failed to create on Twilio. Error: {error}. " +
                        "You can retry from the template details page.";
                }
            }
            catch (Exception twilioEx)
            {
                _logger.LogError(twilioEx, "Error creating template on Twilio");
                TempData["Warning"] = "Template created locally but failed to create on Twilio. You can retry from the template details page.";
            }

            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to create template: {ex.Message}";
            return View();
        }
    }

    /// <summary>
    /// Show template details
    /// </summary>
    public async Task<IActionResult> Details(int id)
    {
        var template = await _templateService.GetTemplateByIdAsync(id);
        if (template == null)
        {
            return NotFound();
        }

        var requests = await _db.TemplateRequests
            .Where(tr => tr.TemplateId == id)
            .OrderByDescending(tr => tr.RequestedAt)
            .ToListAsync();

        ViewBag.Requests = requests;
        return View(template);
    }

    /// <summary>
    /// Show pending approval templates
    /// </summary>
    public async Task<IActionResult> PendingApproval()
    {
        var templates = await _templateService.GetPendingTemplatesAsync();
        return View("PendingApproval", templates.ToList());
    }

    /// <summary>
    /// Show approved templates from local database
    /// </summary>
    public async Task<IActionResult> ApprovedTemplates()
    {
        var templates = await _templateService.GetApprovedTemplatesAsync();
        return View("ApprovedTemplates", templates.ToList());
    }

    /// <summary>
    /// List approved templates from Twilio for selection
    /// Use this to get templates to send WhatsApp messages
    /// </summary>
    public async Task<IActionResult> TwilioTemplates()
    {
        try
        {
            var templates = await _twilioTemplateService.ListApprovedTemplatesAsync();
            return View("TwilioTemplates", templates);
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to fetch templates from Twilio: {ex.Message}";
            return View("TwilioTemplates", new List<TwilioTemplateInfo>());
        }
    }

    /// <summary>
    /// Sync template status from Twilio
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SyncFromTwilio(int templateId)
    {
        try
        {
            var result = await _twilioTemplateService.SyncTemplateStatusAsync(templateId);
            if (result)
            {
                TempData["Success"] = "Template status synced from Twilio.";
            }
            else
            {
                TempData["Error"] = "Failed to sync template status.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to sync: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id = templateId });
    }

    /// <summary>
    /// Submit template for approval
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RequestApproval(int templateId)
    {
        try
        {
            await _templateService.RequestApprovalAsync(templateId, "CurrentUser");
            TempData["Success"] = "Approval request submitted successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to submit approval request: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Approve a template for use
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Approve(int templateId, string? comments)
    {
        try
        {
            var result = await _templateService.ApproveTemplateAsync(templateId, "Admin", comments);
            if (result)
            {
                TempData["Success"] = "Template approved successfully.";
            }
            else
            {
                TempData["Error"] = "Template not found.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to approve template: {ex.Message}";
        }

        return RedirectToAction(nameof(PendingApproval));
    }

    /// <summary>
    /// Reject a template
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Reject(int templateId, string reason)
    {
        try
        {
            if (string.IsNullOrEmpty(reason))
            {
                TempData["Error"] = "Rejection reason is required";
                return RedirectToAction(nameof(PendingApproval));
            }

            var result = await _templateService.RejectTemplateAsync(templateId, "Admin", reason);
            if (result)
            {
                TempData["Success"] = "Template rejected.";
            }
            else
            {
                TempData["Error"] = "Template not found.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to reject template: {ex.Message}";
        }

        return RedirectToAction(nameof(PendingApproval));
    }

    /// <summary>
    /// Show edit form
    /// </summary>
    public async Task<IActionResult> Edit(int id)
    {
        var template = await _templateService.GetTemplateByIdAsync(id);
        if (template == null)
        {
            return NotFound();
        }

        return View(template);
    }

    /// <summary>
    /// Update template
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Edit(int id, string name, string templateText)
    {
        try
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(templateText))
            {
                TempData["Error"] = "Name and template text are required";
                return RedirectToAction(nameof(Edit), new { id });
            }

            var result = await _templateService.UpdateTemplateAsync(id, name, templateText);
            if (result)
            {
                TempData["Success"] = "Template updated successfully.";
                return RedirectToAction(nameof(Details), new { id });
            }
            else
            {
                TempData["Error"] = "Template not found.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to update template: {ex.Message}";
        }

        return RedirectToAction(nameof(Edit), new { id });
    }

    /// <summary>
    /// Show form to update ContentSid
    /// </summary>
    public async Task<IActionResult> UpdateContentSid(int id)
    {
        var template = await _templateService.GetTemplateByIdAsync(id);
        if (template == null)
        {
            return NotFound();
        }

        ViewBag.Template = template;
        return View();
    }

    /// <summary>
    /// Update ContentSid for template
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> UpdateContentSid(int id, string contentSid)
    {
        try
        {
            if (string.IsNullOrEmpty(contentSid))
            {
                TempData["Error"] = "ContentSid is required";
                return RedirectToAction(nameof(UpdateContentSid), new { id });
            }

            var result = await _templateService.UpdateContentSidAsync(id, contentSid);
            if (result)
            {
                TempData["Success"] = "ContentSid updated successfully.";
                return RedirectToAction(nameof(Details), new { id });
            }
            else
            {
                TempData["Error"] = "Template not found.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to update ContentSid: {ex.Message}";
        }

        return RedirectToAction(nameof(UpdateContentSid), new { id });
    }

    /// <summary>
    /// Sync approved template to Twilio
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SyncToTwilio(int templateId)
    {
        try
        {
            var result = await _templateService.SyncToTwilioAsync(templateId);
            if (result)
            {
                TempData["Success"] = "Template synced to Twilio successfully.";
            }
            else
            {
                TempData["Error"] = "Template not approved or no ContentSid configured.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to sync template: {ex.Message}";
        }

        return RedirectToAction(nameof(Details), new { id = templateId });
    }

    /// <summary>
    /// Show templates that need syncing
    /// </summary>
    public async Task<IActionResult> NeedsSync()
    {
        var templates = await _templateService.GetTemplatesNeedingSyncAsync();
        return View("NeedsSync", templates.Select(t => t.Item1).ToList());
    }

    /// <summary>
    /// Delete a draft template
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            await _templateService.DeleteTemplateAsync(id);
            TempData["Success"] = "Template deleted successfully.";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to delete template: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
