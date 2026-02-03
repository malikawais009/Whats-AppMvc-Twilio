using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }
    public DbSet<MessageLog> MessageLogs { get; set; }
    public DbSet<Template> Templates { get; set; }
    public DbSet<TemplateRequest> TemplateRequests { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Message configuration
        modelBuilder.Entity<Message>()
            .HasOne(m => m.User)
            .WithMany(u => u.Messages)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Conversation)
            .WithMany(c => c.Messages)
            .HasForeignKey(m => m.ConversationId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Message>()
            .HasOne(m => m.Template)
            .WithMany(t => t.Messages)
            .HasForeignKey(m => m.TemplateId)
            .OnDelete(DeleteBehavior.SetNull);

        // MessageLog configuration
        modelBuilder.Entity<MessageLog>()
            .HasOne(ml => ml.Message)
            .WithMany(m => m.Logs)
            .HasForeignKey(ml => ml.MessageId)
            .OnDelete(DeleteBehavior.Cascade);

        // Template configuration
        modelBuilder.Entity<Template>()
            .HasMany(t => t.TemplateRequests)
            .WithOne(tr => tr.Template)
            .HasForeignKey(tr => tr.TemplateId)
            .OnDelete(DeleteBehavior.Cascade);

        // User configuration
        modelBuilder.Entity<User>()
            .HasMany(u => u.Messages)
            .WithOne(m => m.User)
            .HasForeignKey(m => m.UserId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
