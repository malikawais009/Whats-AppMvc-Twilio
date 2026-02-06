namespace WhatsAppMvcComplete.Services;

public interface IWhatsAppService
{
    /// <summary>
    /// Send a freeform WhatsApp message (within 24h window)
    /// </summary>
    Task SendAsync(string to, string message);

    /// <summary>
    /// Send a template WhatsApp message (for first messages / outside 24h)
    /// </summary>
    Task SendTemplateAsync(string to, string templateName, string[] templateParams, string? contentSid = null);

    /// <summary>
    /// Send an SMS message
    /// </summary>
    Task SendSmsAsync(string to, string message);

    /// <summary>
    /// Validate Twilio webhook signature
    /// </summary>
    bool ValidateWebhookSignature(string signature, string url, Dictionary<string, string> form);
}
