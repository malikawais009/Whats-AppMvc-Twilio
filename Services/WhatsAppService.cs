using Microsoft.Extensions.Options;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

public class WhatsAppService : IWhatsAppService
{
    private readonly TwilioSettings _settings;

    public WhatsAppService(IOptions<TwilioSettings> settings)
    {
        _settings = settings.Value;
        TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);
    }

    // Send a normal freeform message (within 24h window)
    public async Task SendAsync(string to, string message)
    {
        await MessageResource.CreateAsync(
            from: new PhoneNumber(_settings.WhatsAppFrom),
            to: new PhoneNumber($"whatsapp:{to}"),
            body: message
        );
    }

    // Send a template message (for first messages / outside 24h)
    public async Task SendTemplateAsync(string to, string templateName, string[] templateParams)
    {
        // Twilio requires templates via API (body is null)
        // Template API may vary depending on your Twilio version
        // For now, we pass body as null, template must exist in Twilio

        await MessageResource.CreateAsync(
            from: new PhoneNumber(_settings.WhatsAppFrom),
            to: new PhoneNumber($"whatsapp:{to}"),
            body: null // body is null because template is used
                       // You will need to configure templateName in Twilio Console
        );
    }
}
