using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Hubs;
using WhatsAppMvcComplete.Models;
using WhatsAppMvcComplete.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR(options => { options.EnableDetailedErrors = true; });

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

// Twilio Settings
builder.Services.Configure<TwilioSettings>(
    builder.Configuration.GetSection("Twilio"));

// Meta API Settings
builder.Services.Configure<MetaApiSettings>(
    builder.Configuration.GetSection("Meta"));

// HttpClient factory
builder.Services.AddHttpClient();

// Register MetaApiService
builder.Services.AddScoped<IMetaApiService, MetaApiService>();

// Register MetaWebhookHandler
builder.Services.AddScoped<IMetaWebhookHandler, MetaWebhookHandler>();

// Services
builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();
builder.Services.AddScoped<IMessagingService, MessagingService>();
builder.Services.AddScoped<ITemplateService, TemplateService>();
builder.Services.AddScoped<ITwilioTemplateService, TwilioTemplateService>();

// Background Services
builder.Services.AddHostedService<MessageSchedulerService>();
builder.Services.AddHostedService<HighVolumeProcessingService>();
builder.Services.AddHostedService<MetaSyncBackgroundService>();

// Session (optional for future auth)
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// Logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Dashboard}/{action=Index}/{id?}");

// Initialize database
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    try
    {
        context.Database.EnsureCreated();
        app.Logger.LogInformation("Database initialized successfully");
    }
    catch (Exception ex)
    {
        app.Logger.LogError(ex, "Error initializing database");
    }
}

app.Run();
