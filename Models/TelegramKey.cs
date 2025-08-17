using System;
using System.ComponentModel.DataAnnotations;

namespace BinanceDataCacheApp.Models
{
    public class TelegramKey
    {
        public int Id { get; set; }
        public string UserId { get; set; } // Foreign key to User

        [Required]
        public string BotToken { get; set; } // This will be stored encrypted

        [Required]
        public string ChatId { get; set; } // This will be stored encrypted
        
        public bool SendNotifications { get; set; } = false; // Flag per abilitare/disabilitare l'invio di notifiche
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; } // Nullable, updated on send

        // Navigation property
        public User User { get; set; }
    }
}