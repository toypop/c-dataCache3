using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity; // Aggiungi questo using

namespace BinanceDataCacheApp.Models
{
    public class User : IdentityUser // Modifica qui
    {
        // Id, Username, PasswordHash, ecc. sono già forniti da IdentityUser
        // Puoi aggiungere proprietà personalizzate qui se necessario
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public ICollection<BinanceApiKey> ApiKeys { get; set; }
        public ICollection<BotConfiguration> BotConfigurations { get; set; }
        public ICollection<TelegramKey> TelegramKeys { get; set; } // Aggiunto per Telegram Keys
        public ICollection<UserSubscribedTicker> UserSubscribedTickers { get; set; } // Aggiunto per i ticker sottoscritti
    }
}
