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
        private readonly ILogger<ConversationsController> _logger;

        public ConversationsController(
            AppDbContext db,
            IWhatsAppService wa,
            IHubContext<ChatHub> hub,
            ILogger<ConversationsController> logger)
        {
            _db = db;
            _wa = wa;
            _hub = hub;
            _logger = logger;
        }

        public IActionResult Index()
        {
            return View(_db.Conversations
                .Include(c => c.Messages)
                .OrderByDescending(c => c.LastMessageAt)
                .ToList());
        }

        public IActionResult Chat(int id)
        {
            var conversation = _db.Conversations
                .Include(c => c.Messages)
                .ThenInclude(m => m.Logs)
                .FirstOrDefault(c => c.Id == id);
            
            if (conversation == null)
            {
                return NotFound();
            }
            
            return View(conversation);
        }

        // Send reply from chat view
        [HttpPost]
        public async Task<IActionResult> SendReply(int conversationId, string phoneNumber, string messageText)
        {
            try
            {
                var conversation = _db.Conversations.Find(conversationId);
                if (conversation == null)
                {
                    return NotFound("Conversation not found");
                }

                // Clean the phone number - remove "whatsapp:" prefix if present
                var cleanPhone = phoneNumber.Replace("whatsapp:", "").Replace("+", "");

                // Find user by phone number
                var user = await _db.Users.FirstOrDefaultAsync(u => 
                    u.Phone.Contains(cleanPhone) || 
                    (u.WhatsAppNumber != null && u.WhatsAppNumber.Contains(cleanPhone)));

                // Create outbound message
                var message = new Message
                {
                    UserId = user?.Id, // Set UserId if user found
                    ConversationId = conversationId,
                    MessageText = messageText,
                    Channel = MessageChannel.WhatsApp,
                    Status = MessageStatus.Sent,
                    IsInbound = false,
                    CreatedAt = DateTime.UtcNow,
                    RetryCount = 0
                };

                _db.Messages.Add(message);
                conversation.LastMessageAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                // Send via Twilio
                //var cleanPhone = phoneNumber.Replace("whatsapp:", "").Replace("+", "");
                await _wa.SendAsync(cleanPhone, messageText);

                // Update message status
                message.Status = MessageStatus.Sent;
                await _db.SaveChangesAsync();

                // Notify via SignalR
                await _hub.Clients
                    .Group($"conversation-{conversationId}")
                    .SendAsync("ReceiveMessage", new
                    {
                        conversationId = conversationId,
                        body = messageText,
                        inbound = false,
                        time = DateTime.UtcNow.ToString("HH:mm")
                    });

                _logger.LogInformation("Reply sent to {Phone}", phoneNumber);

                return RedirectToAction("Chat", new { id = conversationId });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending reply");
                TempData["Error"] = $"Failed to send message: {ex.Message}";
                return RedirectToAction("Chat", new { id = conversationId });
            }
        }

        // 🔁 Reply to existing conversation
        [HttpPost]
        public async Task<IActionResult> Reply(int conversationId, string message)
        {
            var convo = _db.Conversations.Find(conversationId);
            if (convo == null) return NotFound();

            // Clean the phone number - remove "whatsapp:" prefix if present
            var cleanPhone = convo.PhoneNumber.Replace("whatsapp:", "").Replace("+", "");

            // Find user by phone number
            var user = await _db.Users.FirstOrDefaultAsync(u => 
                u.Phone.Contains(cleanPhone) || 
                (u.WhatsAppNumber != null && u.WhatsAppNumber.Contains(cleanPhone)));

            _db.Messages.Add(new Message
            {
                UserId = user?.Id, // Set UserId if user found
                ConversationId = conversationId,
                MessageText = message,
                Channel = MessageChannel.WhatsApp,
                Status = MessageStatus.Sent,
                IsInbound = false,
                CreatedAt = DateTime.UtcNow,
                RetryCount = 0
            });

            convo.LastMessageAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _hub.Clients
                .Group($"conversation-{conversationId}")
                .SendAsync("ReceiveMessage", new
                {
                    conversationId = conversationId,
                    body = message,
                    inbound = false,
                    time = DateTime.UtcNow.ToString("HH:mm")
                });

            await _wa.SendAsync(
                convo.PhoneNumber.Replace("whatsapp:", ""),
                message);

            return Ok();
        }

        // 🆕 Start new conversation
        [HttpPost]
        public async Task<IActionResult> Start(string phone, string message)
        {
            var originalPhone = phone;
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

            // Clean the phone number - remove "whatsapp:" prefix if present
            var cleanPhone = originalPhone.Replace("whatsapp:", "").Replace("+", "");

            // Find user by phone number
            var user = await _db.Users.FirstOrDefaultAsync(u => 
                u.Phone.Contains(cleanPhone) || 
                (u.WhatsAppNumber != null && u.WhatsAppNumber.Contains(cleanPhone)));

            _db.Messages.Add(new Message
            {
                UserId = user?.Id ?? null,
                ConversationId = convo.Id,
                MessageText = message,
                Channel = MessageChannel.WhatsApp,
                Status = MessageStatus.Sent,
                IsInbound = false,
                CreatedAt = DateTime.UtcNow,
                RetryCount = 0
            });

            convo.LastMessageAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            await _hub.Clients
                .Group($"conversation-{convo.Id}")
                .SendAsync("ReceiveMessage", new
                {
                    conversationId = convo.Id,
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

            return Ok();
        }

    }
}
