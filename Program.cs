using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Hubs;
using WhatsAppMvcComplete.Models;
using WhatsAppMvcComplete.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddSignalR(options => { options.EnableDetailedErrors = true; });
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.Configure<TwilioSettings>(
    builder.Configuration.GetSection("Twilio"));

builder.Services.AddScoped<IWhatsAppService, WhatsAppService>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseAuthorization();

app.MapControllers();
app.MapHub<ChatHub>("/chatHub");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Conversations}/{action=Index}/{id?}");

app.Run();
