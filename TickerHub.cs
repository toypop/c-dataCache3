using Microsoft.AspNetCore.SignalR;
using BinanceDataCacheApp;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Binance.Net.Enums;
using System.Collections.Generic;
using Microsoft.AspNetCore.Identity; // Aggiungi questo using
using BinanceDataCacheApp.Models; // Aggiungi questo using
using BinanceDataCacheApp.Data; // Aggiungi questo using
using BinanceDataCacheApp.Services; // Aggiungi questo using
using System.Linq; // Aggiungi questo using
using Microsoft.EntityFrameworkCore; // Aggiungi questo using
using CryptoExchange.Net.Authentication; // Aggiungi questo using

namespace BinanceDataCacheApp
{
    public class TickerHub : Hub
    {
        private readonly BinanceStreamManager _streamManager;
        private readonly ILogger<TickerHub> _logger;
        private readonly BinanceDataCache _cache;
        private readonly UserManager<User> _userManager; // Per ottenere l'utente corrente
        private readonly ApplicationDbContext _dbContext; // Per interagire con il DB
        private readonly IEncryptionService _encryptionService; // Per crittografare/decrittografare

        public TickerHub(
            BinanceStreamManager streamManager,
            ILogger<TickerHub> logger,
            BinanceDataCache cache,
            UserManager<User> userManager,
            ApplicationDbContext dbContext,
            IEncryptionService encryptionService)
        {
            _streamManager = streamManager;
            _logger = logger;
            _cache = cache;
            _userManager = userManager;
            _dbContext = dbContext;
            _encryptionService = encryptionService;
        }

        private async Task<User> GetCurrentUserAsync()
        {
            var userId = Context.UserIdentifier;
            if (string.IsNullOrEmpty(userId))
            {
                _logger.LogWarning("Tentativo di accedere a un metodo dell'Hub senza utente autenticato.");
                return null;
            }
            return await _userManager.FindByIdAsync(userId);
        }

        private async Task<ApiCredentials> GetUserBinanceApiCredentialsAsync(User user)
        {
            if (user == null) return null;

            var activeApiKey = await _dbContext.ApiKeys
                .Where(ak => ak.UserId == user.Id && ak.IsActive)
                .FirstOrDefaultAsync();

            if (activeApiKey == null)
            {
                _logger.LogWarning($"Nessuna chiave API Binance attiva trovata per l'utente {user.UserName}.");
                return null;
            }

            try
            {
                var decryptedApiKey = _encryptionService.Decrypt(activeApiKey.ApiKey);
                var decryptedSecretKey = _encryptionService.Decrypt(activeApiKey.SecretKey);
                return new ApiCredentials(decryptedApiKey, decryptedSecretKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Errore durante la decrittografia delle chiavi API per l'utente {user.UserName}.");
                return null;
            }
        }

        private async Task<TelegramKey> GetTelegramKeyAsync(User user)
        {
            if (user == null) return null;

            var telegramKey = await _dbContext.TelegramKeys
                .Where(tk => tk.UserId == user.Id)
                .FirstOrDefaultAsync();

            if (telegramKey == null) return null;

            try
            {
                // Decrittografa i valori solo quando vengono recuperati
                telegramKey.BotToken = _encryptionService.Decrypt(telegramKey.BotToken);
                telegramKey.ChatId = _encryptionService.Decrypt(telegramKey.ChatId);
                return telegramKey;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Errore durante la decrittografia delle chiavi Telegram per l'utente {user.UserName}.");
                return null;
            }
        }

        /// <summary>
        /// Salva le impostazioni delle chiavi Telegram per l'utente corrente nel database.
        /// </summary>
        /// <param name="botToken">Il Bot Token di Telegram.</param>
        /// <param name="chatId">La Chat ID di Telegram.</param>
        /// <param name="sendNotifications">Flag per abilitare/disabilitare le notifiche.</param>
        /// <returns>True se le impostazioni sono state salvate con successo, altrimenti False.</returns>
        public async Task<bool> SaveTelegramSettings(string botToken, string chatId, bool sendNotifications)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return false;

            // Cerca se esiste già una TelegramKey per questo utente
            var existingKey = await _dbContext.TelegramKeys
                .Where(tk => tk.UserId == user.Id)
                .FirstOrDefaultAsync();

            try
            {
                // Crittografa i valori prima di salvare
                var encryptedBotToken = _encryptionService.Encrypt(botToken);
                var encryptedChatId = _encryptionService.Encrypt(chatId);

                if (existingKey == null)
                {
                    // Crea una nuova entry se non esiste
                    var newTelegramKey = new TelegramKey
                    {
                        UserId = user.Id,
                        BotToken = encryptedBotToken,
                        ChatId = encryptedChatId,
                        SendNotifications = sendNotifications,
                        CreatedAt = DateTime.UtcNow,
                        LastUsedAt = DateTime.UtcNow
                    };
                    _dbContext.TelegramKeys.Add(newTelegramKey);
                }
                else
                {
                    // Aggiorna l'entry esistente
                    existingKey.BotToken = encryptedBotToken;
                    existingKey.ChatId = encryptedChatId;
                    existingKey.SendNotifications = sendNotifications;
                    existingKey.LastUsedAt = DateTime.UtcNow;
                    _dbContext.TelegramKeys.Update(existingKey);
                }

                await _dbContext.SaveChangesAsync();
                _logger.LogInformation($"Impostazioni Telegram salvate per l'utente {user.UserName}. Notifiche abilitate: {sendNotifications}.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Errore durante il salvataggio delle impostazioni Telegram per l'utente {user.UserName}.");
                return false;
            }
        }

        /// <summary>
        /// Verifica se l'utente corrente ha delle chiavi Telegram configurate e se l'invio di notifiche è abilitato.
        /// </summary>
        /// <returns>True se le chiavi Telegram sono configurate e le notifiche sono abilitate, altrimenti False.</returns>
        public async Task<bool> HasTelegramKeysConfiguredAndNotificationsEnabled()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return false;

            var telegramKey = await _dbContext.TelegramKeys
                .Where(tk => tk.UserId == user.Id)
                .FirstOrDefaultAsync();

            return telegramKey != null && telegramKey.SendNotifications;
        }

        /// <summary>
        /// Recupera le impostazioni Telegram (Bot Token, Chat ID, SendNotifications) per l'utente corrente.
        /// </summary>
        /// <returns>Un oggetto contenente BotToken, ChatId e SendNotifications, o null se non configurato.</returns>
        public async Task<object> GetTelegramSettings()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return null;

            var telegramKey = await _dbContext.TelegramKeys
                .Where(tk => tk.UserId == user.Id)
                .FirstOrDefaultAsync();

            if (telegramKey == null) return null;

            try
            {
                // Decrittografa i valori prima di inviarli al client
                var decryptedBotToken = _encryptionService.Decrypt(telegramKey.BotToken);
                var decryptedChatId = _encryptionService.Decrypt(telegramKey.ChatId);

                return new { BotToken = decryptedBotToken, ChatId = decryptedChatId, SendNotifications = telegramKey.SendNotifications };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Errore durante il recupero e la decrittografia delle impostazioni Telegram per l'utente {user.UserName}.");
                return null;
            }
        }

        /// <summary>
        /// Salva le chiavi API e Secret Key di Binance per l'utente corrente nel database.
        /// Disattiva tutte le altre chiavi per lo stesso utente.
        /// </summary>
        /// <param name="apiKey">La chiave API di Binance.</param>
        /// <param name="secretKey">La Secret Key di Binance.</param>
        /// <param name="description">Una descrizione per la chiave.</param>
        /// <returns>True se le chiavi sono state salvate con successo, altrimenti False.</returns>
        public async Task<bool> SaveBinanceApiKeys(string apiKey, string secretKey, string description)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return false;

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
            {
                _logger.LogWarning($"Tentativo di salvare chiavi API vuote per l'utente {user.UserName}.");
                return false;
            }

            try
            {
                // Crittografa le chiavi
                var encryptedApiKey = _encryptionService.Encrypt(apiKey);
                var encryptedSecretKey = _encryptionService.Encrypt(secretKey);

                // Disattiva tutte le chiavi esistenti per questo utente
                var existingKeys = await _dbContext.ApiKeys
                    .Where(ak => ak.UserId == user.Id)
                    .ToListAsync();

                foreach (var key in existingKeys)
                {
                    key.IsActive = false;
                }

                // Crea e aggiungi la nuova chiave attiva
                var newApiKey = new BinanceApiKey
                {
                    UserId = user.Id,
                    ApiKey = encryptedApiKey,
                    SecretKey = encryptedSecretKey,
                    Description = description,
                    IsActive = true, // Imposta questa chiave come attiva
                    CreatedAt = DateTime.UtcNow,
                    LastUsedAt = DateTime.UtcNow // Sarà aggiornato all'uso effettivo
                };

                _dbContext.ApiKeys.Add(newApiKey);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation($"Chiavi API Binance salvate e attivate per l'utente {user.UserName}.");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Errore durante il salvataggio delle chiavi API per l'utente {user.UserName}.");
                return false;
            }
        }

        /// <summary>
        /// Verifica se l'utente corrente ha delle chiavi API Binance attive configurate.
        /// </summary>
        /// <returns>True se l'utente ha chiavi API attive, altrimenti False.</returns>
        public async Task<bool> HasBinanceApiKeysConfigured()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return false;

            return await _dbContext.ApiKeys.AnyAsync(ak => ak.UserId == user.Id && ak.IsActive);
        }

        // Metodo chiamato dal client per sottoscrivere un ticker
        public async Task SubscribeToTicker(string symbol)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return;

            var credentials = await GetUserBinanceApiCredentialsAsync(user);
            if (credentials == null)
            {
                _logger.LogWarning($"Impossibile sottoscrivere {symbol}: Chiavi API Binance non configurate o non valide per l'utente {user.UserName}.");
                // Potresti inviare un messaggio al client qui per informarlo
                await Clients.Caller.SendAsync("ShowNotification", "Errore: Chiavi API Binance non configurate o non valide.", "error");
                return;
            }

            _logger.LogInformation($"Client sottoscritto a: {symbol} per utente {user.UserName}");
            bool tickerSubscribed = await _streamManager.StartTickerStreamAsync(symbol, credentials);

            if (tickerSubscribed)
            {
                var shortTermIntervals = new List<KlineInterval> { KlineInterval.OneMinute, KlineInterval.FiveMinutes, KlineInterval.FifteenMinutes };
                var longTermIntervals = new List<KlineInterval> { KlineInterval.OneHour, KlineInterval.FourHour, KlineInterval.OneDay };

                bool klinesStarted = await _streamManager.StartKlineStreamsForSymbolAsync(symbol, shortTermIntervals, longTermIntervals, credentials);

                if (klinesStarted)
                {
                    _logger.LogInformation($"Stream Kline avviati con successo per {symbol} per utente {user.UserName}.");
                }
                else
                {
                    _logger.LogError($"Errore nell'avvio degli stream Kline per {symbol} per utente {user.UserName}.");
                    await Clients.Caller.SendAsync("ShowNotification", $"Errore nell'avvio degli stream Kline per {symbol}.", "error");
                }
            }
            else
            {
                _logger.LogWarning($"Sottoscrizione ticker per {symbol} non riuscita o già esistente per utente {user.UserName}.");
                await Clients.Caller.SendAsync("ShowNotification", $"Sottoscrizione ticker per {symbol} non riuscita o già esistente.", "warning");
            }
        }

        // Metodo chiamato dal client per disiscriversi da un ticker
        public async Task UnsubscribeFromTicker(string symbol)
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return;

            _logger.LogInformation($"Client disiscritto da: {symbol} per utente {user.UserName}");
            await _streamManager.StopTickerStreamAsync(symbol); // StopTickerStreamAsync ferma tutti gli stream associati a quel simbolo
        }

        // Nuovo metodo per ottenere i dati Kline per un simbolo e intervallo specifici
        public async Task<KlineData> GetKlineData(string symbol, KlineInterval interval)
        {
            _logger.LogInformation($"Richiesta dati Kline per {symbol} - {interval}");
            return await Task.FromResult(_cache.GetKlineData(symbol, interval));
        }

        /// <summary>
        /// Restituisce la lista di tutti i simboli di trading disponibili.
        /// </summary>
        /// <returns>Una lista di stringhe con i simboli.</returns>
        public async Task<List<string>> GetAvailableSymbols()
        {
            _logger.LogInformation("Richiesta simboli disponibili dal client.");
            // Per ottenere i simboli disponibili, non sono necessarie le credenziali utente,
            // ma il BinanceStreamManager potrebbe aver bisogno di un client REST inizializzato.
            // Se GetAvailableSymbolsAsync nel manager richiede credenziali, dovremmo passargliele.
            // Per ora, assumiamo che possa funzionare senza credenziali specifiche dell'utente per questa operazione.
            return await _streamManager.GetAvailableSymbolsAsync();
        }

        /// <summary>
        /// Recupera il saldo USDT dell'utente.
        /// </summary>
        /// <returns>Il saldo disponibile in USDT.</returns>
        public async Task<decimal> GetUSDCBalance()
        {
            var user = await GetCurrentUserAsync();
            if (user == null) return 0;

            var credentials = await GetUserBinanceApiCredentialsAsync(user);
            if (credentials == null)
            {
                _logger.LogWarning($"Impossibile recuperare il saldo USDC: Chiavi API Binance non configurate o non valide per l'utente {user.UserName}.");
                await Clients.Caller.SendAsync("ShowNotification", "Errore: Chiavi API Binance non configurate o non valide per il saldo.", "error");
                return 0;
            }

            _logger.LogInformation($"Richiesta saldo USDC per utente {user.UserName}.");
            return await _streamManager.GetAssetBalanceAsync("USDC", credentials);
        }

        // Questo metodo non è più necessario in quanto l'HubContext viene iniettato nel HostedService
        // e l'invio avviene direttamente da lì.
        public static async Task SendTickerUpdate(TickerData tickerData)
        {
            await Task.CompletedTask; // Placeholder
        }
    }
}
