namespace WhatsAppMvcComplete.Models;

public class TwilioSettings
{
    public required string AccountSid { get; set; }
    public required string AuthToken { get; set; }
    public required string WhatsAppFrom { get; set; }
}
