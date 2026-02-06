namespace WhatsAppMvcComplete.Services;

public class TwilioSettings
{
    // Twilio Account Credentials
    public required string AccountSid { get; set; }
    public required string AuthToken { get; set; }
    
    // WhatsApp Configuration
    public required string WhatsAppFrom { get; set; }
    
    // SMS Configuration
    public required string SmsFrom { get; set; }
    
    // Meta/Twilio Content API (for template messages)
    // Get these from Twilio Console after Meta approval
    public string? WhatsAppBusinessAccountId { get; set; }
    public Dictionary<string, string>? TemplateContentSids { get; set; }
    
    // Webhook Security
    public string? WebhookSignatureSalt { get; set; }
    
    // Retry Configuration
    public int MaxRetryAttempts { get; set; } = 3;
    public int RetryIntervalSeconds { get; set; } = 60;
}
