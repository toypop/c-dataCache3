using Microsoft.EntityFrameworkCore;
using BinanceDataCacheApp.Models;

namespace BinanceDataCacheApp.Data
{
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<User> Users { get; set; }
        public DbSet<BinanceApiKey> ApiKeys { get; set; }
        public DbSet<BotConfiguration> BotConfigurations { get; set; }

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
        }
    }
}
