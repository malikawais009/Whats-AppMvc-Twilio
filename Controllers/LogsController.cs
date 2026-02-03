using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;

namespace WhatsAppMvcComplete.Controllers;

public class LogsController : Controller
{
    private readonly AppDbContext _db;

    public LogsController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(int? messageId, string? eventType, DateTime? fromDate, DateTime? toDate, int page = 1)
    {
        var query = _db.MessageLogs
            .Include(ml => ml.Message)
            .ThenInclude(m => m!.User)
            .AsQueryable();

        if (messageId.HasValue)
        {
            query = query.Where(ml => ml.MessageId == messageId.Value);
        }

        if (!string.IsNullOrEmpty(eventType))
        {
            if (Enum.TryParse<Models.EventType>(eventType, out var type))
            {
                query = query.Where(ml => ml.EventType == type);
            }
        }

        if (fromDate.HasValue)
        {
            query = query.Where(ml => ml.EventTimestamp >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(ml => ml.EventTimestamp <= toDate.Value);
        }

        const int pageSize = 50;
        var logs = await query
            .OrderByDescending(ml => ml.EventTimestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var totalCount = await query.CountAsync();
        ViewBag.TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
        ViewBag.CurrentPage = page;

        ViewBag.MessageId = messageId;
        ViewBag.EventType = eventType;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        return View(logs);
    }

    public async Task<IActionResult> MessageLogs(int messageId)
    {
        var logs = await _db.MessageLogs
            .Where(ml => ml.MessageId == messageId)
            .OrderByDescending(ml => ml.EventTimestamp)
            .ToListAsync();

        ViewBag.MessageId = messageId;
        return View(logs);
    }

    public async Task<IActionResult> InboundMessages()
    {
        var messages = await _db.Messages
            .Include(m => m.Logs)
            .Where(m => m.Status == Models.MessageStatus.Received)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        return View(messages);
    }

    public async Task<IActionResult> ErrorLogs()
    {
        var logs = await _db.MessageLogs
            .Include(ml => ml.Message)
            .ThenInclude(m => m!.User)
            .Where(ml => ml.EventType == Models.EventType.Failed || ml.ErrorMessage != null)
            .OrderByDescending(ml => ml.EventTimestamp)
            .ToListAsync();

        return View(logs);
    }

    public async Task<IActionResult> RetryAttempts()
    {
        var messages = await _db.Messages
            .Include(m => m.Logs)
            .Where(m => m.RetryCount > 0)
            .OrderByDescending(m => m.RetryCount)
            .ThenByDescending(m => m.CreatedAt)
            .ToListAsync();

        return View(messages);
    }

    public async Task<IActionResult> WebhookPayloads()
    {
        var logs = await _db.MessageLogs
            .Where(ml => ml.WebhookPayload != null)
            .OrderByDescending(ml => ml.EventTimestamp)
            .Take(100)
            .ToListAsync();

        return View(logs);
    }
}
