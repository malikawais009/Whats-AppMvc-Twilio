using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Hubs;
using WhatsAppMvcComplete.Models;
using WhatsAppMvcComplete.Services;

namespace WhatsAppMvcComplete.Controllers
{
    public class ConversationsController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IWhatsAppService _wa;
        private readonly IHubContext<ChatHub> _hub;

        public ConversationsController(
            AppDbContext db,
            IWhatsAppService wa,
            IHubContext<ChatHub> hub)
        {
            _db = db;
            _wa = wa;
            _hub = hub;
        }

        public IActionResult Index()
        {
            return View(_db.Conversations
                .OrderByDescending(c => c.LastMessageAt)
                .ToList());
        }

        public IActionResult Chat(int id)
        {
            return View(_db.Conversations
                .Include(c => c.Messages)
                .First(c => c.Id == id));
        }

        // 🔁 Reply to existing conversation
        [HttpPost]
        public async Task<IActionResult> Reply(int conversationId, string message)
        {
            var convo = _db.Conversations.Find(conversationId);
            if (convo == null) return NotFound();

            _db.Messages.Add(new Message
            {
                ConversationId = conversationId,
                Body = message,
                IsInbound = false,
                CreatedAt = DateTime.UtcNow
            });

            convo.LastMessageAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _hub.Clients
                .Group($"conversation-{conversationId}")
                .SendAsync("ReceiveMessage", new
                {
                    body = message,
                    inbound = false,
                    time = DateTime.UtcNow.ToString("HH:mm")
                });

            await _wa.SendAsync(
                convo.PhoneNumber.Replace("whatsapp:", ""),
                message);

            return Ok(); // ✅ NO REDIRECT
        }

        // 🆕 Start new conversation
        [HttpPost]
        public async Task<IActionResult> Start(string phone, string message)
        {
            phone = phone.StartsWith("whatsapp:")
                ? phone
                : $"whatsapp:{phone}";

            var convo = _db.Conversations
                .FirstOrDefault(c => c.PhoneNumber == phone);

            bool isNew = convo == null;

            if (isNew)
            {
                convo = new Conversation
                {
                    PhoneNumber = phone,
                    LastMessageAt = DateTime.UtcNow
                };
                _db.Conversations.Add(convo);
                await _db.SaveChangesAsync();
            }

            _db.Messages.Add(new Message
            {
                ConversationId = convo.Id,
                Body = message,
                IsInbound = false,
                CreatedAt = DateTime.UtcNow
            });

            convo.LastMessageAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _hub.Clients
                .Group($"conversation-{convo.Id}")
                .SendAsync("ReceiveMessage", new
                {
                    body = message,
                    inbound = false,
                    time = DateTime.UtcNow.ToString("HH:mm")
                });

            if (isNew)
            {
                await _wa.SendTemplateAsync(
                    phone.Replace("whatsapp:", ""),
                    "welcome_template",
                    new[] { message });
            }
            else
            {
                await _wa.SendAsync(
                    phone.Replace("whatsapp:", ""),
                    message);
            }

            return Ok(); // ✅ NO REDIRECT
        }

    }
}
