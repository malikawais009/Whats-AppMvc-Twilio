using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;
using WhatsAppMvcComplete.Services;

namespace WhatsAppMvcComplete.Controllers;

public class MessagesController : Controller
{
    private readonly AppDbContext _db;
    private readonly IMessagingService _messagingService;
    private readonly ITemplateService _templateService;
    private readonly ITwilioTemplateService _twilioTemplateService;

    public MessagesController(
        AppDbContext db, 
        IMessagingService messagingService,
        ITemplateService templateService,
        ITwilioTemplateService twilioTemplateService)
    {
        _db = db;
        _messagingService = messagingService;
        _templateService = templateService;
        _twilioTemplateService = twilioTemplateService;
    }

    /// <summary>
    /// Compose a new message
    /// </summary>
    public async Task<IActionResult> Compose(string? templateSid)
    {
        var users = await _db.Users.ToListAsync();
        var templates = await _templateService.GetApprovedTemplatesAsync();
        
        ViewBag.Users = users;
        ViewBag.Templates = templates;
        
        // If templateSid is provided, fetch template details from Twilio
        if (!string.IsNullOrEmpty(templateSid))
        {
            var (status, friendlyName, error) = await _twilioTemplateService.GetApprovalStatusAsync(templateSid);
            if (status == "approved")
            {
                ViewBag.SelectedTemplateSid = templateSid;
                ViewBag.SelectedTemplateName = friendlyName;
            }
        }
        
        return View();
    }

    /// <summary>
    /// Send a message using Twilio ContentSid
    /// This is used when selecting a template from TwilioTemplates page
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> SendWithTemplate(
        int userId, 
        string contentSid, 
        string templateName,
        Dictionary<string, string> templateParameters,
        DateTime? scheduledAt)
    {
        try
        {
            if (userId <= 0)
            {
                TempData["Error"] = "Please select a user";
                return RedirectToAction(nameof(Compose));
            }

            if (string.IsNullOrEmpty(contentSid))
            {
                TempData["Error"] = "Template SID is required";
                return RedirectToAction(nameof(Compose));
            }

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found";
                return RedirectToAction(nameof(Compose));
            }

            // Verify template is approved before sending
            var (status, _, error) = await _twilioTemplateService.GetApprovalStatusAsync(contentSid);
            if (status != "approved")
            {
                TempData["Error"] = $"Template is not approved. Current status: {status}";
                return RedirectToAction(nameof(Compose));
            }

            // Send the template message using Twilio
            var parameters = templateParameters ?? new Dictionary<string, string>();
            var sendResult = await _twilioTemplateService.SendTemplateMessageAsync(
                user.Phone, 
                contentSid, 
                parameters);

            if (sendResult.Success)
            {
                // Log the message in our database
                var message = new Message
                {
                    UserId = userId,
                    MessageText = $"Template: {templateName}",
                    Channel = MessageChannel.WhatsApp,
                    Status = MessageStatus.Sent,
                    TwilioMessageId = sendResult.MessageSid,
                    CreatedAt = DateTime.UtcNow,
                    ScheduledAt = scheduledAt
                };
                _db.Messages.Add(message);
                await _db.SaveChangesAsync();

                TempData["Success"] = scheduledAt.HasValue 
                    ? $"Message scheduled for {scheduledAt.Value:yyyy-MM-dd HH:mm}"
                    : $"Message sent successfully using template '{templateName}'. MessageSid: {sendResult.MessageSid}";
            }
            else
            {
                TempData["Error"] = $"Failed to send message: {sendResult.ErrorMessage}";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to send message: {ex.Message}";
        }

        return RedirectToAction(nameof(Compose));
    }

    [HttpPost]
    public async Task<IActionResult> SendMessage(int userId, string messageText, string channel, 
        int? templateId, DateTime? scheduledAt, Dictionary<string, string>? templateParameters)
    {
        try
        {
            if (string.IsNullOrEmpty(messageText))
            {
                TempData["Error"] = "Message text is required";
                return RedirectToAction(nameof(Compose));
            }

            var user = await _db.Users.FindAsync(userId);
            if (user == null)
            {
                TempData["Error"] = "User not found";
                return RedirectToAction(nameof(Compose));
            }

            var channelEnum = Enum.Parse<MessageChannel>(channel);

            Message msg;
            if (channelEnum == MessageChannel.WhatsApp && templateId.HasValue && templateId.Value > 0)
            {
                var parameters = templateParameters ?? new Dictionary<string, string>();
                msg = await _messagingService.SendWhatsAppTemplateAsync(userId, templateId.Value, parameters, scheduledAt);
            }
            else if (channelEnum == MessageChannel.WhatsApp)
            {
                msg = await _messagingService.SendWhatsAppAsync(userId, messageText, scheduledAt);
            }
            else
            {
                msg = await _messagingService.SendSmsAsync(userId, messageText, scheduledAt);
            }

            TempData["Success"] = scheduledAt.HasValue 
                ? $"Message scheduled for {scheduledAt.Value:yyyy-MM-dd HH:mm}" 
                : "Message sent successfully";
            
            return RedirectToAction(nameof(Compose));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to send message: {ex.Message}";
            return RedirectToAction(nameof(Compose));
        }
    }

    [HttpPost]
    public async Task<IActionResult> RetryMessage(int messageId)
    {
        try
        {
            var result = await _messagingService.RetryFailedMessageAsync(messageId);
            if (result)
            {
                TempData["Success"] = "Message retry initiated";
            }
            else
            {
                TempData["Error"] = "Failed to retry message. Check if maximum retries exceeded.";
            }
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Error retrying message: {ex.Message}";
        }

        return RedirectToAction("Index", "Dashboard");
    }

    public async Task<IActionResult> GetTemplateParameters(int templateId)
    {
        var template = await _templateService.GetTemplateByIdAsync(templateId);
        if (template == null)
        {
            return NotFound();
        }

        // Extract parameter placeholders like {{param_name}}
        var parameters = System.Text.RegularExpressions.Regex.Matches(
            template.TemplateText, 
            @"\{\{(\w+)\}\}"
        ).Cast<System.Text.RegularExpressions.Match>()
         .Select(m => m.Groups[1].Value)
         .Distinct()
         .ToList();

        return Json(parameters);
    }

    public async Task<IActionResult> ScheduledMessages()
    {
        var messages = await _db.Messages
            .Include(m => m.User)
            .Where(m => m.Status == MessageStatus.Pending && m.ScheduledAt.HasValue)
            .OrderBy(m => m.ScheduledAt)
            .ToListAsync();

        return View(messages);
    }

    public async Task<IActionResult> FailedMessages()
    {
        var messages = await _db.Messages
            .Include(m => m.User)
            .Include(m => m.Logs)
            .Where(m => m.Status == MessageStatus.Failed)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        return View(messages);
    }
}
