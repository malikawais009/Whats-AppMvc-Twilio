using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;
using WhatsAppMvcComplete.Services;

namespace WhatsAppMvcComplete.Controllers;

public class TemplatesController : Controller
{
    private readonly ITemplateService _templateService;
    private readonly AppDbContext _db;
    private readonly ILogger<TemplatesController> _logger;

    public TemplatesController(ITemplateService templateService, AppDbContext db, ILogger<TemplatesController> logger)
    {
        _templateService = templateService;
        _db = db;
        _logger = logger;
    }

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
                    _ => await _templateService.GetAllTemplatesAsync()
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
        return View(templates);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(string name, string templateText)
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
                "CurrentUser" // In production, get from auth
            );

            TempData["Success"] = "Template created successfully. It is now pending approval.";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to create template: {ex.Message}";
            return View();
        }
    }

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

    public async Task<IActionResult> PendingApproval()
    {
        var templates = await _templateService.GetPendingTemplatesAsync();
        return View(templates);
    }

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

    public async Task<IActionResult> ApprovedTemplates()
    {
        var templates = await _templateService.GetApprovedTemplatesAsync();
        return View(templates);
    }
}
