namespace WhatsAppMvcComplete.Services;

/// <summary>
/// Configuration settings for Meta Graph API
/// </summary>
public class MetaApiSettings
{
    /// <summary>
    /// Meta Access Token for the WhatsApp Business Account
    /// </summary>
    public required string AccessToken { get; set; }

    /// <summary>
    /// Meta Business Account ID
    /// </summary>
    public required string BusinessAccountId { get; set; }

    /// <summary>
    /// WhatsApp Phone Number ID for sending messages
    /// </summary>
    public string? PhoneNumberId { get; set; }

    /// <summary>
    /// Meta Graph API version
    /// </summary>
    public string ApiVersion { get; set; } = "v18.0";

    /// <summary>
    /// Webhook verification token
    /// </summary>
    public string? VerifyToken { get; set; }
}
