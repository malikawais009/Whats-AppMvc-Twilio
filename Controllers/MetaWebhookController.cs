using Microsoft.AspNetCore.Mvc;
using WhatsAppMvcComplete.Services;

namespace WhatsAppMvcComplete.Controllers;

/// <summary>
/// Webhook controller for Meta platform notifications
/// </summary>
[Route("api/meta/webhook")]
[ApiController]
public class MetaWebhookController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IMetaWebhookHandler _webhookHandler;
    private readonly ILogger<MetaWebhookController> _logger;

    private string VerifyToken => _configuration["Meta:VerifyToken"] ?? "";

    public MetaWebhookController(
        IConfiguration configuration,
        IMetaWebhookHandler webhookHandler,
        ILogger<MetaWebhookController> logger)
    {
        _configuration = configuration;
        _webhookHandler = webhookHandler;
        _logger = logger;
    }

    /// <summary>
    /// GET: Verify webhook with Meta
    /// </summary>
    [HttpGet]
    public IActionResult Get([FromQuery] string? mode, [FromQuery] string? token, [FromQuery] string? challenge)
    {
        _logger.LogInformation("Meta webhook verification request received. Mode: {Mode}, Token: {Token}", mode, token);

        if (mode == "subscribe" && token == VerifyToken)
        {
            _logger.LogInformation("Meta webhook verified successfully");
            return Ok(challenge);
        }

        _logger.LogWarning("Meta webhook verification failed. Mode: {Mode}, Token: {Token}", mode, token);
        return BadRequest("Webhook verification failed");
    }

    /// <summary>
    /// POST: Receive webhook notifications from Meta
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Post([FromBody] object data)
    {
        try
        {
            _logger.LogInformation("Meta webhook notification received");
            
            // Log the webhook payload for debugging
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            _logger.LogDebug("Meta webhook payload: {Payload}", json);

            // Process the webhook data
            await _webhookHandler.HandleStatusUpdateAsync(data);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Meta webhook");
            return StatusCode(500, "Error processing webhook");
        }
    }
}
