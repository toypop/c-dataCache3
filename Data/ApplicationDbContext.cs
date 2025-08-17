using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using BinanceDataCacheApp.Models;

namespace BinanceDataCacheApp.Data
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // DbSet<User> non è più necessario qui, è gestito da IdentityDbContext
        public DbSet<BinanceApiKey> ApiKeys { get; set; } = default!; // Aggiunto = default!
        public DbSet<BotConfiguration> BotConfigurations { get; set; } = default!; // Aggiunto = default!
        public DbSet<TelegramKey> TelegramKeys { get; set; } = default!; // Aggiunto per Telegram Keys

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure relationships
            modelBuilder.Entity<BinanceApiKey>()
                .HasOne(ak => ak.User)
                .WithMany(u => u.ApiKeys)
                .HasForeignKey(ak => ak.UserId);

            modelBuilder.Entity<BotConfiguration>()
                .HasOne(bc => bc.User)
                .WithMany(u => u.BotConfigurations)
                .HasForeignKey(bc => bc.UserId);

            modelBuilder.Entity<TelegramKey>()
                .HasOne(tk => tk.User)
                .WithMany(u => u.TelegramKeys)
                .HasForeignKey(tk => tk.UserId);
        }
    }
}
