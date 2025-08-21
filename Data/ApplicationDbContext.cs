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
        public DbSet<UserSubscribedTicker> UserSubscribedTickers { get; set; } = default!; // Aggiunto per i ticker sottoscritti dagli utenti
        public DbSet<UserTickerSetting> UserTickerSettings { get; set; } = default!; // Nuovo DbSet per le impostazioni dei ticker

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

            modelBuilder.Entity<UserSubscribedTicker>()
                .HasOne(ust => ust.User)
                .WithMany(u => u.UserSubscribedTickers)
                .HasForeignKey(ust => ust.UserId);

            // Configurazione della relazione per UserTickerSetting
            modelBuilder.Entity<UserTickerSetting>()
                .HasOne(uts => uts.UserSubscribedTicker)
                .WithMany(ust => ust.UserTickerSettings)
                .HasForeignKey(uts => uts.UserSubscribedTickerId)
                .OnDelete(DeleteBehavior.Cascade); // Configura la cancellazione a cascata
        }
    }
}
