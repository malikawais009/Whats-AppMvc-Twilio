using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Services;

/// <summary>
/// Service for integrating with Meta Graph API for WhatsApp Business templates
/// </summary>
public interface IMetaApiService
{
    Task<bool> SubmitTemplateAsync(int templateId);
    Task<MetaTemplateStatus?> GetTemplateStatusAsync(string metaTemplateId);
    Task<bool> SyncTemplateStatusAsync(int templateId);
    Task<bool> SyncContentSidFromMetaAsync(int templateId);
    Task<List<MetaTemplateStatus>> GetAllTemplateStatusesAsync();
}

public class MetaApiService : IMetaApiService
{
    private readonly IConfiguration _configuration;
    private readonly AppDbContext _db;
    private readonly ILogger<MetaApiService> _logger;
    private readonly HttpClient _httpClient;
    private readonly string _accessToken;
    private readonly string _businessAccountId;
    private readonly string _apiVersion;
    private readonly string _baseUrl;

    public MetaApiService(IConfiguration configuration, AppDbContext db, ILogger<MetaApiService> logger, HttpClient httpClient)
    {
        _configuration = configuration;
        _db = db;
        _logger = logger;
        _httpClient = httpClient;
        _accessToken = configuration["Meta:AccessToken"] ?? throw new InvalidOperationException("Meta:AccessToken not configured");
        _businessAccountId = configuration["Meta:BusinessAccountId"] ?? throw new InvalidOperationException("Meta:BusinessAccountId not configured");
        _apiVersion = configuration["Meta:ApiVersion"] ?? "v18.0";
        _baseUrl = $"https://graph.facebook.com/{_apiVersion}";
        
        // Configure HttpClient
        httpClient.BaseAddress = new Uri(_baseUrl);
        httpClient.DefaultRequestHeaders.Clear();
    }

    /// <summary>
    /// Submit an approved template to Meta for WhatsApp approval
    /// </summary>
    public async Task<bool> SubmitTemplateAsync(int templateId)
    {
        var template = await _db.Templates.FindAsync(templateId);
        if (template == null)
        {
            _logger.LogWarning("Template {TemplateId} not found", templateId);
            return false;
        }

        if (template.Status != TemplateStatus.Approved)
        {
            _logger.LogWarning("Template {TemplateId} is not approved. Current status: {Status}", templateId, template.Status);
            return false;
        }

        try
        {
            // Map local category to Meta category
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

            // Clean template name (remove special characters)
            var cleanName = template.Name.ToLower()
                .Replace(" ", "_")
                .Replace("-", "_")
                .Replace(".", "_");

            var requestBody = new
            {
                name = cleanName,
                language = template.Language ?? "en_US",
                category = metaCategory,
                components = new[]
                {
                    new
                    {
                        type = "BODY",
                        text = template.TemplateText
                    }
                }
            };

            var json = JsonConvert.SerializeObject(requestBody, Formatting.Indented);
            var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

            _logger.LogDebug("Submitting template to Meta: {RequestBody}", json);

            var response = await _httpClient.PostAsync($"/{_businessAccountId}/message_templates?access_token={_accessToken}", content);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                dynamic responseObj = JsonConvert.DeserializeObject(responseJson)!;
                
                template.MetaTemplateId = responseObj.id;
                template.Status = TemplateStatus.Pending;
                template.SubmittedAt = DateTime.UtcNow;
                template.UpdatedAt = DateTime.UtcNow;
                
                await _db.SaveChangesAsync();
                
                _logger.LogInformation("Template {TemplateId} submitted successfully. MetaTemplateId: {MetaId}", 
                    templateId, template.MetaTemplateId);
                
                return true;
            }
            else
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.LogError("Failed to submit template {TemplateId} to Meta. Status: {Status}, Response: {Response}.\nRequest: {Request}", 
                    templateId, response.StatusCode, errorContent, json);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error submitting template {TemplateId} to Meta", templateId);
            return false;
        }
    }

    /// <summary>
    /// Get template status from Meta API
    /// </summary>
    public async Task<MetaTemplateStatus?> GetTemplateStatusAsync(string metaTemplateId)
    {
        try
        {
            var url = $"/{metaTemplateId}?fields=id,name,status,category&access_token={_accessToken}";
            
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                dynamic responseObj = JsonConvert.DeserializeObject(responseJson)!;
                
                return new MetaTemplateStatus
                {
                    Id = responseObj.id,
                    Name = responseObj.name,
                    Status = responseObj.status,
                    Category = responseObj.category
                };
            }
            else
            {
                _logger.LogWarning("Failed to get template status from Meta. TemplateId: {MetaId}, Status: {Status}", 
                    metaTemplateId, response.StatusCode);
                return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting template status from Meta. TemplateId: {MetaId}", metaTemplateId);
            return null;
        }
    }

    /// <summary>
    /// Sync template status from Meta to local database
    /// </summary>
    public async Task<bool> SyncTemplateStatusAsync(int templateId)
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

        var metaStatus = await GetTemplateStatusAsync(template.MetaTemplateId);
        if (metaStatus == null)
        {
            return false;
        }

        var oldStatus = template.Status;
        
        // Map Meta status to local status
        template.Status = metaStatus.Status switch
        {
            "APPROVED" => TemplateStatus.Approved,
            "REJECTED" => TemplateStatus.Rejected,
            "PENDING" => TemplateStatus.Pending,
            "IN_APPROVAL" => TemplateStatus.Pending,
            "DISABLED" => TemplateStatus.Rejected,
            "DELETED" => TemplateStatus.Rejected,
            _ => TemplateStatus.Pending
        };

        template.UpdatedAt = DateTime.UtcNow;

        // Update rejection reason if rejected
        if (metaStatus.Status == "REJECTED")
        {
            template.RejectionReason = "Rejected by Meta";
        }

        await _db.SaveChangesAsync();
        
        _logger.LogInformation("Synced template {TemplateId} status from {OldStatus} to {NewStatus}", 
            templateId, oldStatus, template.Status);
        
        return true;
    }

    /// <summary>
    /// Sync ContentSid from Meta for approved templates
    /// </summary>
    public async Task<bool> SyncContentSidFromMetaAsync(int templateId)
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

        if (template.Status != TemplateStatus.Approved)
        {
            _logger.LogWarning("Template {TemplateId} is not approved by Meta. Status: {Status}", templateId, template.Status);
            return false;
        }

        try
        {
            // Get template details from Meta to find the ContentSid
            var url = $"/{template.MetaTemplateId}/content_templates?access_token={_accessToken}";
            
            var response = await _httpClient.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var responseJson = await response.Content.ReadAsStringAsync();
                dynamic responseObj = JsonConvert.DeserializeObject(responseJson)!;
                
                if (responseObj.data != null && ((System.Collections.IEnumerable)responseObj.data).GetEnumerator().MoveNext())
                {
                    var contentTemplate = responseObj.data[0];
                    var contentSidValue = (string)contentTemplate.id.ToString();
                    
                    template.TwilioContentSid = contentSidValue;
                    template.UpdatedAt = DateTime.UtcNow;
                    await _db.SaveChangesAsync();
                    
                    _logger.LogInformation("Synced ContentSid for template {TemplateId}", templateId);
                    
                    return true;
                }
            }

            _logger.LogWarning("No ContentSid found for template {TemplateId} in Meta", templateId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing ContentSid from Meta for template {TemplateId}", templateId);
            return false;
        }
    }

    /// <summary>
    /// Get status of all templates from Meta
    /// </summary>
    public async Task<List<MetaTemplateStatus>> GetAllTemplateStatusesAsync()
    {
        var statuses = new List<MetaTemplateStatus>();

        try
        {
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
                        statuses.Add(new MetaTemplateStatus
                        {
                            Id = template.id,
                            Name = template.name,
                            Status = template.status,
                            Category = template.category
                        });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all template statuses from Meta");
        }

        return statuses;
    }
}

/// <summary>
/// Represents template status from Meta API
/// </summary>
public class MetaTemplateStatus
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
    public string Category { get; set; } = "";
}
