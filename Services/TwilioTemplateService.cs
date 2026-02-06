using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using Twilio;
using Twilio.Rest.Api.V2010.Account;
using Twilio.Types;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

/// <summary>
/// Service for managing WhatsApp templates using Twilio Content API
/// 
/// Flow: Dashboard → Twilio Content API → Twilio → Meta review → Approval
/// 
/// References:
/// - Twilio Console: https://console.twilio.com/us1/develop/sms/content-template-builder
/// - Twilio Content API Docs: https://www.twilio.com/docs/content
/// </summary>
public interface ITwilioTemplateService
{
    /// <summary>
    /// Create a WhatsApp template via Twilio Content API
    /// Returns the ContentSid (HX...) for the created template
    /// </summary>
    Task<(bool Success, string? ContentSid, string? ErrorMessage)> CreateTemplateAsync(Template template);

    /// <summary>
    /// Check template approval status from Twilio
    /// Returns: pending, approved, rejected
    /// </summary>
    Task<(string Status, string? FriendlyName, string? ErrorMessage)> GetApprovalStatusAsync(string contentSid);

    /// <summary>
    /// List all templates from Twilio Content API
    /// Filters for approved templates
    /// </summary>
    Task<List<TwilioTemplateInfo>> ListApprovedTemplatesAsync();

    /// <summary>
    /// Send a WhatsApp template message using an approved template
    /// </summary>
    Task<(bool Success, string? MessageSid, string? ErrorMessage)> SendTemplateMessageAsync(
        string toPhoneNumber,
        string contentSid,
        Dictionary<string, string> parameters);

    /// <summary>
    /// Sync local template with Twilio approval status
    /// </summary>
    Task<bool> SyncTemplateStatusAsync(int templateId);

    /// <summary>
    /// Sync ContentSid from Meta for a template that has MetaTemplateId but no TwilioContentSid
    /// </summary>
    Task<bool> SyncContentSidFromMetaAsync(int templateId);
}

public class TwilioTemplateService : ITwilioTemplateService
{
    private readonly TwilioSettings _settings;
    private readonly AppDbContext _db;
    private readonly ILogger<TwilioTemplateService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly string _businessAccountId;

    public TwilioTemplateService(
        IOptions<TwilioSettings> settings,
        AppDbContext db,
        ILogger<TwilioTemplateService> logger,
        HttpClient httpClient,
        IConfiguration configuration)
    {
        _settings = settings.Value;
        _db = db;
        _logger = logger;
        _httpClient = httpClient;

        // Meta API Configuration for template management
        _accessToken = configuration["Meta:AccessToken"] ?? "";
        _businessAccountId = configuration["Meta:BusinessAccountId"] ?? "";

        // Configure HttpClient for Meta API
        httpClient.BaseAddress = new Uri($"https://graph.facebook.com/v18.0");
        httpClient.DefaultRequestHeaders.Clear();
    }

    /// <summary>
    /// Create a WhatsApp template via Twilio Content API
    /// 
    /// Note: Twilio Content API creates templates that are submitted to Meta
    /// This uses the Meta Graph API via Twilio's integration
    /// </summary>
    public async Task<(bool Success, string? ContentSid, string? ErrorMessage)> CreateTemplateAsync(Template template)
    {
        try
        {
            _logger.LogInformation("Creating template '{TemplateName}' via Twilio Content API", template.Name);

            // Map category to Meta format
            var metaCategory = template.Category?.ToUpper() switch
            {
                "MARKETING" => "MARKETING",
                "AUTHENTICATION" => "AUTHENTICATION",
                "OTP" => "AUTHENTICATION",
                "TRANSACTIONAL" => "UTILITY",
                "UTILITY" => "UTILITY",
                "SUPPORT" => "UTILITY",
                _ => "MARKETING"
            };

            // Clean template name for Meta
            var cleanName = System.Text.RegularExpressions.Regex.Replace(
                template.Name.ToLower(), @"[^a-z0-9_]", "_");
            cleanName = System.Text.RegularExpressions.Regex.Replace(cleanName, "_+", "_").Trim('_');

            // Build request body for Meta API
            var requestBody = new
            {
                name = cleanName,
                language = template.Language ?? "en_US",
                category = metaCategory,
                components = new[]
                {
                    new
                    {
                        type = "body",
                        text = template.TemplateText
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody, Formatting.Indented);
            var url = $"/{_businessAccountId}/message_templates?access_token={_accessToken}";
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                dynamic responseObj = JsonConvert.DeserializeObject(responseJson)!;
                
                var contentSid = (string)responseObj.id;
                _logger.LogInformation("Template created successfully. ContentSid: {ContentSid}", contentSid);
                return (true, contentSid, null);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to create template. Status: {Status}, Response: {Response}", 
                    response.StatusCode, errorContent);
                return (false, null, $"API Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error creating template '{TemplateName}'", template.Name);
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Check template approval status from Twilio/Meta
    /// 
    /// Status values:
    /// - pending: Template submitted to Meta, awaiting review
    /// - approved: Template approved by Meta, ready to use
    /// - rejected: Template rejected by Meta
    /// </summary>
    public async Task<(string Status, string? FriendlyName, string? ErrorMessage)> GetApprovalStatusAsync(string contentSid)
    {
        try
        {
            _logger.LogInformation("Checking approval status for template {ContentSid}", contentSid);

            var url = $"/{contentSid}?fields=id,name,status,category&access_token={_accessToken}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                dynamic responseObj = JsonConvert.DeserializeObject(responseJson)!;

                var status = (string)responseObj.status;
                var friendlyName = (string?)responseObj.name;

                _logger.LogInformation("Template {ContentSid} status: {Status}", contentSid, status);
                return (status.ToLower(), friendlyName, null);
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                return ("error", null, $"API Error: {errorContent}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting status for template {ContentSid}", contentSid);
            return ("error", null, ex.Message);
        }
    }

    /// <summary>
    /// List all approved templates from Twilio/Meta
    /// </summary>
    public async Task<List<TwilioTemplateInfo>> ListApprovedTemplatesAsync()
    {
        var templates = new List<TwilioTemplateInfo>();

        try
        {
            _logger.LogInformation("Fetching approved templates from Twilio/Meta");

            var url = $"/{_businessAccountId}/message_templates?access_token={_accessToken}";
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                dynamic responseObj = JsonConvert.DeserializeObject(responseJson)!;

                if (responseObj.data != null)
                {
                    foreach (var template in responseObj.data)
                    {
                        var status = (string)template.status;
                        if (status.Equals("approved", StringComparison.OrdinalIgnoreCase))
                        {
                            templates.Add(new TwilioTemplateInfo
                            {
                                Sid = template.id,
                                FriendlyName = template.name,
                                Status = status,
                                Category = template.category,
                                Language = template.language
                            });
                        }
                    }
                }

                _logger.LogInformation("Found {Count} approved templates", templates.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error listing templates");
        }

        return templates;
    }

    /// <summary>
    /// Send a WhatsApp template message using Twilio
    /// </summary>
    public async Task<(bool Success, string? MessageSid, string? ErrorMessage)> SendTemplateMessageAsync(
        string toPhoneNumber,
        string contentSid,
        Dictionary<string, string> parameters)
    {
        try
        {
            _logger.LogInformation("Sending template message to {PhoneNumber} using template {ContentSid}", 
                toPhoneNumber, contentSid);

            if (string.IsNullOrWhiteSpace(toPhoneNumber))
            {
                return (false, null, "Phone number is required");
            }

            if (string.IsNullOrWhiteSpace(contentSid))
            {
                return (false, null, "ContentSid is required");
            }

            // Initialize Twilio client
            TwilioClient.Init(_settings.AccountSid, _settings.AuthToken);

            // Build content variables as JSON string
            // Format: {"1": "value1", "2": "value2"}
            var contentVariables = new Dictionary<string, string>();
            int index = 1;
            foreach (var param in parameters)
            {
                contentVariables[index.ToString()] = param.Value;
                index++;
            }
            var contentVariablesJson = JsonConvert.SerializeObject(contentVariables);

            // Send the WhatsApp message
            var message = await MessageResource.CreateAsync(
                to: new PhoneNumber($"whatsapp:{toPhoneNumber}"),
                from: new PhoneNumber($"whatsapp:{_settings.WhatsAppFrom}"),
                contentSid: contentSid,
                contentVariables: contentVariablesJson
            );

            _logger.LogInformation("Message sent successfully. MessageSid: {MessageSid}", message.Sid);
            return (true, message.Sid, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message to {PhoneNumber}", toPhoneNumber);
            return (false, null, ex.Message);
        }
    }

    /// <summary>
    /// Sync local template with Twilio approval status
    /// </summary>
    public async Task<bool> SyncTemplateStatusAsync(int templateId)
    {
        try
        {
            var template = await _db.Templates.FindAsync(templateId);
            if (template == null)
            {
                _logger.LogWarning("Template {TemplateId} not found", templateId);
                return false;
            }

            if (string.IsNullOrEmpty(template.TwilioContentSid))
            {
                _logger.LogWarning("Template {TemplateId} has no TwilioContentSid", templateId);
                return false;
            }

            var (status, _, error) = await GetApprovalStatusAsync(template.TwilioContentSid);
            if (!string.IsNullOrEmpty(error))
            {
                return false;
            }

            // Map status to local status
            template.Status = status switch
            {
                "approved" => TemplateStatus.Approved,
                "rejected" => TemplateStatus.Rejected,
                "pending" => TemplateStatus.Pending,
                _ => TemplateStatus.Pending
            };

            template.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Synced template {TemplateId} status to {Status}", templateId, template.Status);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing template {TemplateId}", templateId);
            return false;
        }
    }

    /// <summary>
    /// Sync ContentSid from Meta for a template that has MetaTemplateId but no TwilioContentSid
    /// This is useful when templates were created directly on Meta/Twilio console
    /// </summary>
    public async Task<bool> SyncContentSidFromMetaAsync(int templateId)
    {
        try
        {
            var template = await _db.Templates.FindAsync(templateId);
            if (template == null)
            {
                _logger.LogWarning("Template {TemplateId} not found", templateId);
                return false;
            }

            if (string.IsNullOrEmpty(template.MetaTemplateId))
            {
                _logger.LogWarning("Template {TemplateId} has no MetaTemplateId", templateId);
                return false;
            }

            if (!string.IsNullOrEmpty(template.TwilioContentSid))
            {
                _logger.LogInformation("Template {TemplateId} already has TwilioContentSid: {ContentSid}", 
                    templateId, template.TwilioContentSid);
                return true;
            }

            // The MetaTemplateId IS the ContentSid in Twilio's system
            // Map MetaTemplateId to TwilioContentSid
            template.TwilioContentSid = template.MetaTemplateId;
            template.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            _logger.LogInformation("Synced ContentSid from MetaTemplateId for template {TemplateId}. ContentSid: {ContentSid}", 
                templateId, template.TwilioContentSid);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing ContentSid from Meta for template {TemplateId}", templateId);
            return false;
        }
    }
}

/// <summary>
/// Represents template information from Twilio/Meta
/// </summary>
public class TwilioTemplateInfo
{
    public string Sid { get; set; } = "";
    public string FriendlyName { get; set; } = "";
    public string Status { get; set; } = "";
    public string Category { get; set; } = "";
    public string Language { get; set; } = "";
}
