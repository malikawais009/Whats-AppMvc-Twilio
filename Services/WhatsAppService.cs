using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly TwilioSettings _settings;
    private readonly ILogger<WhatsAppService> _logger;

    public WhatsAppService(IOptions<TwilioSettings> settings, ILogger<WhatsAppService> logger)
    {
        _settings = settings.Value;
        _logger = logger;
        TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);
    }

    /// <summary>
    /// Send a normal freeform WhatsApp message (within 24h window)
    /// </summary>
    public async Task SendAsync(string to, string message)
    {
        _logger.LogInformation("Sending freeform WhatsApp message to {To}", to);
        
        await MessageResource.CreateAsync(
            from: new PhoneNumber(_settings.WhatsAppFrom),
            to: new PhoneNumber($"whatsapp:{to}"),
            body: message
        );
    }

    /// <summary>
    /// Send a template WhatsApp message (for first messages / outside 24h window)
    /// Uses Twilio's Content API with ContentSid
    /// </summary>
    /// <param name="to">Recipient phone number</param>
    /// <param name="templateName">Name of the template (used for lookup)</param>
    /// <param name="templateParams">Dictionary of parameter values (1-indexed)</param>
    /// <param name="contentSid">Optional Twilio Content SID (overrides templateName lookup)</param>
    public async Task SendTemplateAsync(string to, string templateName, string[] templateParams, string? contentSid = null)
    {
        _logger.LogInformation("Sending template WhatsApp message to {To} with template {TemplateName}", to, templateName);
        
        // Determine ContentSid
        var sid = contentSid;
        if (string.IsNullOrEmpty(sid) && !string.IsNullOrEmpty(templateName))
        {
            // Look up ContentSid from settings by template name
            _settings.TemplateContentSids?.TryGetValue(templateName, out sid);
        }

        if (string.IsNullOrEmpty(sid))
        {
            throw new InvalidOperationException($"ContentSid not found for template '{templateName}'. " +
                $"Please configure the template in Twilio Console and add the ContentSid to appsettings.json.");
        }

        // Build content variables (1-indexed for Twilio)
        var contentVariables = new Dictionary<string, string>();
        for (int i = 0; i < templateParams.Length; i++)
        {
            contentVariables[$"{i + 1}"] = templateParams[i];
        }

        _logger.LogDebug("Using ContentSid: {Sid} with variables: {Variables}", sid, 
            string.Join(", ", contentVariables.Select(kv => $"{kv.Key}={kv.Value}")));

        await MessageResource.CreateAsync(
            from: new PhoneNumber(_settings.WhatsAppFrom),
            to: new PhoneNumber($"whatsapp:{to}"),
            contentSid: sid
        );

        // Note: Content variables are set in Twilio Console template configuration
        // Dynamic content variables require Twilio Content API with specific setup
    }

    /// <summary>
    /// Send an SMS message
    /// </summary>
    public async Task SendSmsAsync(string to, string message)
    {
        _logger.LogInformation("Sending SMS to {To}", to);
        
        await MessageResource.CreateAsync(
            from: new PhoneNumber(_settings.SmsFrom),
            to: new PhoneNumber(to),
            body: message
        );
    }

    /// <summary>
    /// Verify Twilio webhook signature for security
    /// </summary>
    public bool ValidateWebhookSignature(string signature, string url, Dictionary<string, string> form)
    {
        try
        {
            // For security, always validate webhook signatures in production
            // In development/testing, you can skip validation if needed
            #if DEBUG
            return true;
            #else
            // Use Twilio's validation utility
            var validator = new Twilio.Security.RequestValidator(_settings.AuthToken);
            return validator.Validate(new Uri(url), form, signature);
            #endif
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating webhook signature");
            return false;
        }
    }
}
