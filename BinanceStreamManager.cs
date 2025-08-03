using System;
using System.Threading.Tasks;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models.Spot.Socket;
using CryptoExchange.Net.Objects.Sockets;
using Microsoft.Extensions.Logging;
using Binance.Net.Interfaces;
using System.Collections.Concurrent;
using System.Linq;
using System.Collections.Generic;
using Binance.Net.Objects.Models.Spot;
using Microsoft.Extensions.Configuration;
using CryptoExchange.Net.Authentication; // Aggiunto per ApiCredentials

namespace BinanceDataCacheApp
{
    /// <summary>
    /// Classe interna per raggruppare tutte le connessioni e sottoscrizioni per un singolo simbolo
    /// </summary>
    internal class SymbolStreamGroup : IDisposable
    {
        public string Symbol { get; }
        public IBinanceSocketClient TickerClient { get; }
        public IBinanceSocketClient KlineShortTermClient { get; } // Per 1m, 5m, 15m
        public IBinanceSocketClient KlineLongTermClient { get; }  // Per 1h, 4h, 1d

        public UpdateSubscription TickerSubscription { get; set; }
        public ConcurrentDictionary<KlineInterval, UpdateSubscription> KlineShortTermSubscriptions { get; } = new();
        public ConcurrentDictionary<KlineInterval, UpdateSubscription> KlineLongTermSubscriptions { get; } = new();

        private readonly ILogger _logger;

        public SymbolStreamGroup(string symbol, ApiCredentials credentials, ILogger logger)
        {
            Symbol = symbol;
            _logger = logger;

            // Inizializza i client socket dedicati per questo simbolo
            TickerClient = new BinanceSocketClient(options => { options.ApiCredentials = credentials; });
            KlineShortTermClient = new BinanceSocketClient(options => { options.ApiCredentials = credentials; });
            KlineLongTermClient = new BinanceSocketClient(options => { options.ApiCredentials = credentials; });
        }

        public async Task StopAllSymbolStreamsAsync()
        {
            try
            {
                var closeTasks = new List<Task>();

                if (TickerSubscription != null)
                {
                    closeTasks.Add(TickerSubscription.CloseAsync());
                    TickerSubscription = null;
                }

                foreach (var sub in KlineShortTermSubscriptions.Values)
                {
                    closeTasks.Add(sub.CloseAsync());
                }
                KlineShortTermSubscriptions.Clear();

                foreach (var sub in KlineLongTermSubscriptions.Values)
                {
                    closeTasks.Add(sub.CloseAsync());
                }
                KlineLongTermSubscriptions.Clear();

                await Task.WhenAll(closeTasks);
                _logger.LogInformation($"Tutti gli stream per {Symbol} sono stati fermati.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Errore durante l'arresto degli stream per {Symbol}.");
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopAllSymbolStreamsAsync().GetAwaiter().GetResult(); // Sincrono per Dispose
                TickerClient?.Dispose();
                KlineShortTermClient?.Dispose();
                KlineLongTermClient?.Dispose();
            }
        }
    }

    /// <summary>
    /// Manager per gestire le connessioni WebSocket a Binance e popolare la cache
    /// </summary>
    public class BinanceStreamManager : IDisposable
    {
        private readonly ILogger<BinanceStreamManager> _logger;
        private readonly BinanceDataCache _cache;
        private readonly BinanceRestClient _restClient;
        private readonly ApiCredentials _apiCredentials; // Credenziali API memorizzate una volta
        
        // Nuovo: Dizionario per gestire i gruppi di stream per ogni simbolo
        private readonly ConcurrentDictionary<string, SymbolStreamGroup> _symbolStreamGroups = new();
        
        private bool _disposed = false;

        /// <summary>
        /// Costruttore del manager stream
        /// </summary>
        /// <param name="logger">Logger per il debugging</param>
        /// <param name="configuration">Configurazione dell'applicazione per le API Key</param>
        public BinanceStreamManager(ILogger<BinanceStreamManager> logger, IConfiguration configuration)
        {
            _logger = logger;
            _cache = BinanceDataCache.Instance;
            
            var apiKey = configuration["Binance:ApiKey"];
            var secretKey = configuration["Binance:SecretKey"];

            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
            {
                _logger.LogCritical("Binance API Key o Secret Key non configurate. Assicurati di averle impostate tramite User Secrets (dotnet user-secrets set \"Binance:ApiKey\" \"YOUR_API_KEY\").");
                throw new InvalidOperationException("Binance API Key o Secret Key non configurate.");
            }
            _apiCredentials = new ApiCredentials(apiKey, secretKey); // Inizializza le credenziali una volta

            // Inizializza il client REST di Binance con le credenziali (REST client rimane unico)
            _restClient = new BinanceRestClient(options =>
            {
                options.ApiCredentials = _apiCredentials;
            });
        }

        /// <summary>
        /// Recupera tutti i simboli di trading disponibili da Binance.
        /// </summary>
        /// <returns>Una lista di stringhe contenente i simboli, o una lista vuota in caso di errore.</returns>
        public async Task<List<string>> GetAvailableSymbolsAsync()
        {
            try
            {
                var exchangeInfo = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
                if (exchangeInfo.Success && exchangeInfo.Data != null)
                {
                    return exchangeInfo.Data.Symbols.Select(s => s.Name).ToList();
                }
                else
                {
                    _logger?.LogError($"Errore nel recuperare le informazioni di scambio: {exchangeInfo.Error?.Message}");
                    return new List<string>();
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Eccezione durante il recupero dei simboli di scambio");
                return new List<string>();
            }
        }

        /// <summary>
        /// Avvia lo stream ticker per un simbolo specifico
        /// </summary>
        /// <param name="symbol">Simbolo da monitorare (es. "BTCUSDT")</param>
        /// <returns>True se la sottoscrizione è avvenuta con successo</returns>
        public async Task<bool> StartTickerStreamAsync(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                _logger?.LogError("Simbolo non valido per ticker stream");
                return false;
            }

            string upperSymbol = symbol.ToUpperInvariant();
            if (_symbolStreamGroups.ContainsKey(upperSymbol))
            {
                _logger?.LogInformation($"Gruppo stream per {symbol} già esistente, ticker stream già sottoscritto.");
                return true; // Il ticker è già gestito all'interno del gruppo esistente
            }

            try
            {
                var streamGroup = new SymbolStreamGroup(upperSymbol, _apiCredentials, _logger);
                
                // Sottoscrizione al ticker stream di Binance con il client dedicato
                var subscriptionResult = await streamGroup.TickerClient.SpotApi.ExchangeData
                    .SubscribeToTickerUpdatesAsync(new[] { upperSymbol }, OnTickerUpdate);                

                if (subscriptionResult.Success)
                {
                    streamGroup.TickerSubscription = subscriptionResult.Data;
                    _symbolStreamGroups.TryAdd(upperSymbol, streamGroup);
                    _logger?.LogInformation($"Ticker stream avviato per {symbol}");
                    return true;
                }
                else
                {
                    _logger?.LogError($"Errore avvio ticker stream per {symbol}: {subscriptionResult.Error?.Message}");
                    streamGroup.Dispose(); // Pulisci il gruppo se fallisce
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Eccezione durante avvio ticker stream per {symbol}");
                return false;
            }
        }

        /// <summary>
        /// Avvia gli stream kline specifici per un simbolo, divisi in breve e lungo termine.
        /// Questo metodo deve essere chiamato DOPO StartTickerStreamAsync.
        /// </summary>
        /// <param name="symbol">Il simbolo per cui avviare gli stream kline.</param>
        /// <param name="shortTermIntervals">Lista di intervalli kline per il client a breve termine (es. 1m, 5m, 15m).</param>
        /// <param name="longTermIntervals">Lista di intervalli kline per il client a lungo termine (es. 1h, 4h, 1d).</param>
        /// <returns>True se tutti gli stream kline sono stati avviati con successo, altrimenti False.</returns>
        public async Task<bool> StartKlineStreamsForSymbolAsync(string symbol, IEnumerable<KlineInterval> shortTermIntervals, IEnumerable<KlineInterval> longTermIntervals)
        {
            string upperSymbol = symbol.ToUpperInvariant();
            if (!_symbolStreamGroups.TryGetValue(upperSymbol, out var streamGroup))
            {
                _logger?.LogError($"Gruppo stream non trovato per il simbolo {symbol}. Impossibile avviare stream kline.");
                return false;
            }

            bool allSucceeded = true;

            // Avvia stream kline a breve termine
            foreach (var interval in shortTermIntervals)
            {
                string key = $"{upperSymbol}_{interval}";
                if (streamGroup.KlineShortTermSubscriptions.ContainsKey(interval)) continue;

                try
                {
                    var subscriptionResult = await streamGroup.KlineShortTermClient.SpotApi.ExchangeData
                        .SubscribeToKlineUpdatesAsync(upperSymbol, interval, OnKlineUpdate);

                    if (subscriptionResult.Success)
                    {
                        streamGroup.KlineShortTermSubscriptions.TryAdd(interval, subscriptionResult.Data);
                        _logger?.LogInformation($"Kline stream avviato per {symbol} - {interval} (Breve Termine)");
                    }
                    else
                    {
                        _logger?.LogError($"Errore avvio kline stream per {symbol}-{interval}: {subscriptionResult.Error?.Message}");
                        allSucceeded = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Eccezione durante avvio kline stream per {symbol}-{interval} (Breve Termine)");
                    allSucceeded = false;
                }
            }

            // Avvia stream kline a lungo termine
            foreach (var interval in longTermIntervals)
            {
                string key = $"{upperSymbol}_{interval}";
                if (streamGroup.KlineLongTermSubscriptions.ContainsKey(interval)) continue;

                try
                {
                    var subscriptionResult = await streamGroup.KlineLongTermClient.SpotApi.ExchangeData
                        .SubscribeToKlineUpdatesAsync(upperSymbol, interval, OnKlineUpdate);

                    if (subscriptionResult.Success)
                    {
                        streamGroup.KlineLongTermSubscriptions.TryAdd(interval, subscriptionResult.Data);
                        _logger?.LogInformation($"Kline stream avviato per {symbol} - {interval} (Lungo Termine)");
                    }
                    else
                    {
                        _logger?.LogError($"Errore avvio kline stream per {symbol}-{interval}: {subscriptionResult.Error?.Message}");
                        allSucceeded = false;
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, $"Eccezione durante avvio kline stream per {symbol}-{interval} (Lungo Termine)");
                    allSucceeded = false;
                }
            }
            return allSucceeded;
        }

        /// <summary>
        /// Recupera il saldo disponibile di un asset specifico (es. USDT).
        /// </summary>
        /// <param name="asset">L'asset di cui recuperare il saldo (es. "USDT")</param>
        /// <returns>Il saldo disponibile dell'asset, o 0 se non trovato o in caso di errore.</returns>
        public async Task<decimal> GetAssetBalanceAsync(string asset)
        {
            try
            {
                var accountInfo = await _restClient.SpotApi.Account.GetAccountInfoAsync();
                if (accountInfo.Success && accountInfo.Data != null)
                {
                    var balance = accountInfo.Data.Balances.FirstOrDefault(b => b.Asset.Equals(asset, StringComparison.OrdinalIgnoreCase));
                    return balance?.Available ?? 0;
                }
                else
                {
                    _logger?.LogError($"Errore nel recuperare le informazioni del conto per il saldo di {asset}: {accountInfo.Error?.Message}");
                    return 0;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Eccezione durante il recupero del saldo di {asset}");
                return 0;
            }
        }

        /// <summary>
        /// Ferma lo stream ticker per un simbolo specifico
        /// </summary>
        /// <param name="symbol">Simbolo da fermare</param>
        /// <returns>True se la sottoscrizione è stata fermata con successo</returns>
        public async Task<bool> StopTickerStreamAsync(string symbol)
        {
            string upperSymbol = symbol.ToUpperInvariant();
            if (_symbolStreamGroups.TryRemove(upperSymbol, out var streamGroup))
            {
                await streamGroup.StopAllSymbolStreamsAsync(); // Ferma tutti gli stream del gruppo
                streamGroup.Dispose(); // Dispone i client socket
                _logger?.LogInformation($"Tutti gli stream per il simbolo {symbol} sono stati fermati e disposti.");
                return true;
            }
            return false;
        }

        // Questo metodo non sarà più usato direttamente dall'hub, ma la sua logica è stata incorporata in StartKlineStreamsForSymbolAsync
        // Rimane per ora ma sarà rimosso o modificato in futuro se non più necessario.
        public async Task<bool> StartKlineStreamAsync(string symbol, KlineInterval interval)
        {
            _logger.LogWarning($"StartKlineStreamAsync obsoleto per {symbol}-{interval}. Usare StartKlineStreamsForSymbolAsync.");
            return false; // Forziamo l'uso del nuovo metodo
        }

        // Questo metodo non sarà più usato direttamente dall'hub, ma la sua logica è stata incorporata in StopTickerStreamAsync.
        // Rimane per ora ma sarà rimosso o modificato in futuro se non più necessario.
        public async Task<bool> StopKlineStreamAsync(string symbol, KlineInterval interval)
        {
            _logger.LogWarning($"StopKlineStreamAsync obsoleto per {symbol}-{interval}. Usare StopTickerStreamAsync per fermare tutti gli stream del simbolo.");
            return false; // Forziamo l'uso del nuovo metodo
        }

        /// <summary>
        /// Callback per gli aggiornamenti ticker
        /// </summary>
        private void OnTickerUpdate(DataEvent<IBinanceTick> tickerEvent)
        {
            try
            {
                var tick = tickerEvent.Data;
                var tickerData = new TickerData(
                    tick.Symbol,
                    tick.LastPrice,
                    tick.Volume,
                    tick.PriceChange,
                    tick.PriceChangePercent
                );
                _cache.SetTickerData(tickerData);
                _logger?.LogDebug($"Ticker aggiornato: {tick.Symbol} = ${tick.LastPrice}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Errore nel processare aggiornamento ticker");
            }
        }

        /// <summary>
        /// Callback per gli aggiornamenti kline
        /// </summary>
        private void OnKlineUpdate(DataEvent<IBinanceStreamKlineData> klineEvent)
        {
            try
            {
                var kline = klineEvent.Data.Data;
                var klineData = new KlineData(
                    klineEvent.Symbol,
                    kline.Interval,
                    kline.OpenTime,
                    kline.CloseTime,
                    kline.OpenPrice,
                    kline.HighPrice,
                    kline.LowPrice,
                    kline.ClosePrice,
                    kline.Volume,
                    kline.Final
                );
                _cache.SetKlineData(klineData);
                _logger?.LogDebug($"Kline aggiornata: {klineEvent.Symbol} {kline.Interval} - " +
                                 $"Close: ${kline.ClosePrice} (Final: {kline.Final})");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Errore nel processare aggiornamento kline");
            }
        }

        /// <summary>
        /// Ferma tutti gli stream attivi di TUTTI i simboli
        /// </summary>
        public async Task StopAllStreamsAsync()
        {
            try
            {
                var closeTasks = new List<Task>();
                foreach (var streamGroup in _symbolStreamGroups.Values)
                {
                    closeTasks.Add(streamGroup.StopAllSymbolStreamsAsync());
                }
                await Task.WhenAll(closeTasks);
                
                foreach (var streamGroup in _symbolStreamGroups.Values)
                {
                    streamGroup.Dispose(); // Dispone i client socket per ogni gruppo
                }
                _symbolStreamGroups.Clear();

                _logger?.LogInformation("Tutti gli stream per tutti i simboli sono stati fermati e disposti.");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Errore durante la chiusura di tutti gli stream");
            }
        }

        #region IDisposable Implementation
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed && disposing)
            {
                try
                {
                    // Ferma tutti gli stream in modo sincrono per il Dispose
                    foreach (var streamGroup in _symbolStreamGroups.Values)
                    {
                        streamGroup.StopAllSymbolStreamsAsync().GetAwaiter().GetResult();
                        streamGroup.Dispose(); // Assicurati che anche i client vengano disposti
                    }
                    _symbolStreamGroups.Clear();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Errore durante dispose degli stream manager");
                }

                _restClient?.Dispose();
                _disposed = true;
            }
        }
        #endregion
    }
} 