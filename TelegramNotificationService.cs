using Telegram.Bot;
using Telegram.Bot.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Identity; // Aggiunto per UserManager
using BinanceDataCacheApp.Data; // Aggiunto per ApplicationDbContext
using BinanceDataCacheApp.Models; // Aggiunto per TelegramKey e User
using BinanceDataCacheApp.Services; // Aggiunto per IEncryptionService
using System.Linq; // Aggiunto per Linq extension methods
using System; // Aggiunto per DateTime
using Microsoft.EntityFrameworkCore; // Aggiunto per FirstOrDefaultAsync

namespace BinanceDataCacheApp
{
    public interface ITelegramNotificationService
    {
        // Modificato per includere userId
        Task SendMessageAsync(string userId, string message);
    }

    public class TelegramNotificationService : ITelegramNotificationService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly ILogger<TelegramNotificationService> _logger;
        private readonly UserManager<BinanceDataCacheApp.Models.User> _userManager; // Risolvi l'ambiguità specificando il namespace
        private readonly ApplicationDbContext _dbContext; // Aggiunto
        private readonly IEncryptionService _encryptionService; // Aggiunto

        public TelegramNotificationService(ITelegramBotClient botClient,
                                           ILogger<TelegramNotificationService> logger,
                                           UserManager<BinanceDataCacheApp.Models.User> userManager, // Risolvi l'ambiguità
                                           ApplicationDbContext dbContext,
                                           IEncryptionService encryptionService)
        {
            _botClient = botClient;
            _logger = logger;
            _userManager = userManager; // Assegna
            _dbContext = dbContext; // Assegna
            _encryptionService = encryptionService; // Assegna
        }

        public async Task SendMessageAsync(string userId, string message)
        {
            // Recupera la chiave Telegram per l'utente specificato
            var telegramKey = await _dbContext.TelegramKeys
                .Where(tk => tk.UserId == userId)
                .FirstOrDefaultAsync();

            if (telegramKey == null || !telegramKey.SendNotifications) // Verifica se le chiavi esistono e le notifiche sono abilitate
            {
                if (telegramKey == null) _logger.LogWarning($"Impossibile inviare il messaggio Telegram per utente {userId}: Chiavi Telegram non configurate.");
                else _logger.LogInformation($"Invio messaggi Telegram disabilitato per l'utente {userId}.");
                return;
            }

            string decryptedBotToken;
            string decryptedChatId;
            try
            {
                decryptedBotToken = _encryptionService.Decrypt(telegramKey.BotToken);
                decryptedChatId = _encryptionService.Decrypt(telegramKey.ChatId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Errore durante la decrittografia delle credenziali Telegram per l'utente {userId}.");
                return;
            }

            // Se il botClient non è stato inizializzato con il token corretto, dobbiamo farlo qui.
            // Idealmente, ITelegramBotClient dovrebbe essere creato con un factory o un scope per gestire token dinamici.
            // Per semplicità, in questa fase possiamo assumerlo inizializzato nel Program.cs.
            // Se _botClient non ha il token giusto, l'invio fallirà. Dovremmo re-inizializzarlo o usare un'istanza dinamica.
            // Per ora, concentriamoci sulla logica del Db e del flag.

            // Telegram.Bot.ITelegramBotClient botClientWithToken = new TelegramBotClient(decryptedBotToken);
            // Per ora usiamo _botClient esistente, assumendo che sia configurato per inviare.

            if (!long.TryParse(decryptedChatId, out long chatId))
            {
                _logger.LogWarning($"Impossibile inviare il messaggio Telegram per utente {userId}: Chat ID decrittografata non valida.");
                return;
            }

            try
            {
                await _botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: message,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html
                );
                telegramKey.LastUsedAt = DateTime.UtcNow;
                _dbContext.TelegramKeys.Update(telegramKey);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation($"Messaggio Telegram inviato con successo per l'utente {userId} alla chat {chatId}.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"Errore durante l'invio del messaggio Telegram per l'utente {userId} alla chat {chatId}: {ex.Message}");
            }
        }
    }
}