using System;
using System.Collections.Generic;

namespace BinanceDataCacheApp.Models
{
    public class User
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string PasswordHash { get; set; } // Store hashed passwords, not plain text
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<BinanceApiKey> ApiKeys { get; set; }
        public ICollection<BotConfiguration> BotConfigurations { get; set; }
    }
}
