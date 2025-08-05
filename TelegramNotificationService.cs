using Telegram.Bot;
using Telegram.Bot.Types;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;

namespace BinanceDataCacheApp
{
    public interface ITelegramNotificationService
    {
        Task SendMessageAsync(string message);
    }

    public class TelegramNotificationService : ITelegramNotificationService
    {
        private readonly ITelegramBotClient _botClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TelegramNotificationService> _logger;
        private readonly long _chatId;

        public TelegramNotificationService(ITelegramBotClient botClient, IConfiguration configuration, ILogger<TelegramNotificationService> logger)
        {
            _botClient = botClient;
            _configuration = configuration;
            _logger = logger;

            // Leggi l'ID della chat da configurazione
            var chatIdString = _configuration["Telegram:ChatId"];
            if (long.TryParse(chatIdString, out long chatId))
            {
                _chatId = chatId;
            }
            else
            {
                _logger.LogError("Telegram Chat ID non configurato o non valido. L'invio di messaggi Telegram non sarà possibile.");
                _chatId = 0; // Imposta un valore predefinito o gestisci l'errore come preferisci
            }
        }

        public async Task SendMessageAsync(string message)
        {
            if (_chatId == 0)
            {
                _logger.LogWarning("Impossibile inviare il messaggio Telegram: Chat ID non valido.");
                return;
            }

            try
            {
                await _botClient.SendTextMessageAsync(
                    chatId: _chatId,
                    text: message,
                    parseMode: Telegram.Bot.Types.Enums.ParseMode.Html // Per supportare il grassetto, ecc.
                );
                _logger.LogInformation($"Messaggio Telegram inviato con successo alla chat {_chatId}.");
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, $"Errore durante l'invio del messaggio Telegram alla chat {_chatId}: {ex.Message}");
            }
        }
    }
}