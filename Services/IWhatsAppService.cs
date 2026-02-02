namespace WhatsAppMvcComplete.Services;

public interface IWhatsAppService
{
    Task SendAsync(string to, string message);
    Task SendTemplateAsync(string to, string templateName, string[] templateParams);
}
