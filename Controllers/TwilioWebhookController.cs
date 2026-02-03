using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Hubs;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Controllers;

[ApiController]
[Route("api/twilio/webhook")]
public class TwilioWebhookController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IHubContext<ChatHub> _hub;

    public TwilioWebhookController(AppDbContext db, IHubContext<ChatHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    [HttpPost]
    public async Task<IActionResult> Incoming()
    {
        var form = await Request.ReadFormAsync();

        var from = form["From"].ToString(); 
        var body = form["Body"].ToString();

        if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(body))
            return Ok();

        // Get or create conversation
        var convo = _db.Conversations.FirstOrDefault(x => x.PhoneNumber == from);
        if (convo == null)
        {
            convo = new Conversation
            {
                PhoneNumber = from,
                LastMessageAt = DateTime.UtcNow
            };
            _db.Conversations.Add(convo);
            await _db.SaveChangesAsync();
        }

        // Save inbound message
        var msg = new Message
        {
            ConversationId = convo.Id,
            Body = body,
            IsInbound = true,
            CreatedAt = DateTime.UtcNow
        };
        _db.Messages.Add(msg);

        convo.LastMessageAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // ✅ Broadcast via SignalR
        await _hub.Clients
            .Group($"conversation-{convo.Id}")
            .SendAsync("ReceiveMessage", new
            {
                conversationId = convo.Id,
                body = msg.Body,
                inbound = true,
                time = msg.CreatedAt.ToString("HH:mm")
            });

        return Ok();
    }
}
