using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options) { }

    public DbSet<Conversation> Conversations { get; set; }
    public DbSet<Message> Messages { get; set; }
}
