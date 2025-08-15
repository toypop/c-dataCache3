using System;
using System.ComponentModel.DataAnnotations;

namespace BinanceDataCacheApp.Models
{
    public class BotConfiguration
    {
        public int Id { get; set; }
        public string UserId { get; set; } // Foreign key to User (changed to string)
        
        [Required]
        public string Name { get; set; }
        public string Strategy { get; set; } // e.g., "Scalping", "Arbitrage", "TrendFollowing"
        public string SettingsJson { get; set; } // Store bot-specific settings as JSON
        public bool IsActive { get; set; } = false;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public User User { get; set; }
    }
}
