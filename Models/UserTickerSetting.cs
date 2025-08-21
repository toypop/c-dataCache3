using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Binance.Net.Enums; // Per KlineInterval

namespace BinanceDataCacheApp.Models
{
    public class UserTickerSetting
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int UserSubscribedTickerId { get; set; } // Foreign key to UserSubscribedTicker

        [Required]
        [Column(TypeName = "decimal(18, 2)")] // Per precisione nel DB
        public decimal DeclinePercentage { get; set; } = 0m; // Valore di default

        [Required]
        [MaxLength(50)] // Per memorizzare il nome dell'enum KlineInterval
        public string SelectedKlineInterval { get; set; } = KlineInterval.OneMinute.ToString(); // Valore di default

        public DateTime LastModifiedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("UserSubscribedTickerId")]
        public UserSubscribedTicker UserSubscribedTicker { get; set; }
    }
}
