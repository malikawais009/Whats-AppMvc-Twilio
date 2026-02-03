using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Controllers;

public class DashboardController : Controller
{
    private readonly AppDbContext _db;

    public DashboardController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index(string? statusFilter, string? channelFilter, DateTime? fromDate, DateTime? toDate)
    {
        var query = _db.Messages
            .Include(m => m.User)
            .Include(m => m.Template)
            .AsQueryable();

        // Apply filters
        if (!string.IsNullOrEmpty(statusFilter))
        {
            if (Enum.TryParse<MessageStatus>(statusFilter, out var status))
            {
                query = query.Where(m => m.Status == status);
            }
        }

        if (!string.IsNullOrEmpty(channelFilter))
        {
            if (Enum.TryParse<MessageChannel>(channelFilter, out var channel))
            {
                query = query.Where(m => m.Channel == channel);
            }
        }

        if (fromDate.HasValue)
        {
            query = query.Where(m => m.CreatedAt >= fromDate.Value);
        }

        if (toDate.HasValue)
        {
            query = query.Where(m => m.CreatedAt <= toDate.Value);
        }

        var messages = await query
            .OrderByDescending(m => m.CreatedAt)
            .Take(100)
            .ToListAsync();

        // Statistics
        ViewBag.TotalMessages = await _db.Messages.CountAsync();
        ViewBag.PendingMessages = await _db.Messages.CountAsync(m => m.Status == MessageStatus.Pending);
        ViewBag.SentMessages = await _db.Messages.CountAsync(m => m.Status == MessageStatus.Sent);
        ViewBag.DeliveredMessages = await _db.Messages.CountAsync(m => m.Status == MessageStatus.Delivered);
        ViewBag.FailedMessages = await _db.Messages.CountAsync(m => m.Status == MessageStatus.Failed);
        ViewBag.ReceivedMessages = await _db.Messages.CountAsync(m => m.Status == MessageStatus.Received);
        ViewBag.SmsCount = await _db.Messages.CountAsync(m => m.Channel == MessageChannel.SMS);
        ViewBag.WhatsAppCount = await _db.Messages.CountAsync(m => m.Channel == MessageChannel.WhatsApp);

        // Filter values
        ViewBag.StatusFilter = statusFilter;
        ViewBag.ChannelFilter = channelFilter;
        ViewBag.FromDate = fromDate?.ToString("yyyy-MM-dd");
        ViewBag.ToDate = toDate?.ToString("yyyy-MM-dd");

        return View(messages);
    }

    public async Task<IActionResult> Details(int id)
    {
        // Redirect to MessageDetails for consistency
        return RedirectToAction("MessageDetails", new { id = id });
    }

    public async Task<IActionResult> MessageDetails(int id)
    {
        var message = await _db.Messages
            .Include(m => m.User)
            .Include(m => m.Template)
            .Include(m => m.Logs)
            .FirstOrDefaultAsync(m => m.Id == id);

        if (message == null)
        {
            return NotFound();
        }

        return View(message);
    }

    public async Task<IActionResult> UserMessages(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user == null) return NotFound();

        var messages = await _db.Messages
            .Include(m => m.Logs)
            .Where(m => m.UserId == userId)
            .OrderByDescending(m => m.CreatedAt)
            .ToListAsync();

        ViewBag.User = user;
        return View(messages);
    }
}
