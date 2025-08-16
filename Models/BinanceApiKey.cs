using System;
using System.ComponentModel.DataAnnotations;

namespace BinanceDataCacheApp.Models
{
    public class BinanceApiKey
    {
        public int Id { get; set; }
        public string UserId { get; set; } // Foreign key to User (changed to string)
        
        [Required]
        public string ApiKey { get; set; } // This will be stored encrypted
        
        [Required]
        public string SecretKey { get; set; } // This will be stored encrypted
        
        public string Description { get; set; } // e.g., "Main Trading Key", "Bot 1 Key"
        public bool IsActive { get; set; } = false; // Nuovo campo per indicare se la chiave è attiva
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime LastUsedAt { get; set; }

        // Navigation property
        public User User { get; set; }
    }
}
