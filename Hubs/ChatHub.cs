using Microsoft.AspNetCore.SignalR;

namespace WhatsAppMvcComplete.Hubs
{
    public class ChatHub : Hub
    {
        public async Task JoinConversation(int conversationId)
        {
            try
            {
                Console.WriteLine($"Joining conversation: {conversationId}");
                Console.WriteLine($"ConnectionId: {Context.ConnectionId}");

                await Groups.AddToGroupAsync(
                    Context.ConnectionId,
                    $"conversation-{conversationId}"
                );

                Console.WriteLine("Joined group successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ SignalR ERROR:");
                Console.WriteLine(ex.ToString());
                throw;
            }
        }
    }
}
