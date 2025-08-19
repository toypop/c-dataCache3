using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace BinanceDataCacheApp.Models
{
    public class UserSubscribedTicker
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public string UserId { get; set; }

        [Required]
        [MaxLength(20)] // E.g., "BTCUSDT"
        public string Symbol { get; set; }

        public DateTime SubscribedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("UserId")]
        public User User { get; set; }
    }
}
