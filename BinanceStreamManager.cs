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
using Binance.Net.Objects.Models.Spot; // Aggiunto per BinanceSymbol
using Microsoft.Extensions.Configuration; // Aggiunto per accedere alla configurazione

namespace BinanceDataCacheApp
{
    /// <summary>
    /// Manager per gestire le connessioni WebSocket a Binance e popolare la cache
    /// </summary>
    public class BinanceStreamManager : IDisposable
    {
        private readonly IBinanceSocketClient _socketClient;
        private readonly ILogger<BinanceStreamManager> _logger;
        private readonly BinanceDataCache _cache;
        private readonly BinanceRestClient _restClient; // Nuovo: per chiamate REST
        
        // Riferimenti alle sottoscrizioni attive per poterle chiudere
        private readonly ConcurrentDictionary<string, UpdateSubscription> _tickerSubscriptions = new();
        private readonly ConcurrentDictionary<string, UpdateSubscription> _klineSubscriptions = new();
        
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
            
            // Recupera le API Key dalla configurazione
            var apiKey = configuration["Binance:ApiKey"];
            var secretKey = configuration["Binance:SecretKey"];

            // Verifica che le API Key e Secret Key siano state caricate
            if (string.IsNullOrEmpty(apiKey) || string.IsNullOrEmpty(secretKey))
            {
                _logger.LogCritical("Binance API Key o Secret Key non configurate. Assicurati di averle impostate tramite User Secrets (dotnet user-secrets set \"Binance:ApiKey\" \"YOUR_API_KEY\").");
                throw new InvalidOperationException("Binance API Key o Secret Key non configurate.");
            }

            // Inizializza il client socket di Binance con le credenziali
            // Anche se gli stream pubblici non le richiedono, è buona pratica per consistenza e futuri stream privati
            _socketClient = new BinanceSocketClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(apiKey, secretKey);
            });
            
            // Inizializza il client REST di Binance con le credenziali
            _restClient = new BinanceRestClient(options =>
            {
                options.ApiCredentials = new CryptoExchange.Net.Authentication.ApiCredentials(apiKey, secretKey);
            });
        }

        /// <summary>
        /// Recupera tutti i simboli di trading disponibili da Binance.
        /// </summary>
        /// <returns>Una lista di stringhe contenente i simboli, o una lista vuota in caso di errore.</returns>
        public async Task<List<string>> GetAvailableSymbolsAsync() // Metodo rinominato
        {
            try
            {
                var exchangeInfo = await _restClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
                if (exchangeInfo.Success && exchangeInfo.Data != null)
                {
                    return exchangeInfo.Data.Symbols.Select(s => s.Name).ToList(); // Modificato da s.Symbol a s.Name
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

            // Se siamo già sottoscritti a questo simbolo, non fare nulla
            if (_tickerSubscriptions.ContainsKey(symbol.ToUpperInvariant()))
            {
                _logger?.LogInformation($"Già sottoscritto al ticker stream per {symbol}");
                return true;
            }

            try
            {
                // Sottoscrizione al ticker stream di Binance
                var subscriptionResult = await _socketClient.SpotApi.ExchangeData
                    .SubscribeToTickerUpdatesAsync(new[] { symbol }, OnTickerUpdate);                

                if (subscriptionResult.Success)
                {
                    _tickerSubscriptions.TryAdd(symbol.ToUpperInvariant(), subscriptionResult.Data);
                    _logger?.LogInformation($"Ticker stream avviato per {symbol}");
                    return true;
                }
                else
                {
                    _logger?.LogError($"Errore avvio ticker stream per {symbol}: {subscriptionResult.Error?.Message}");
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
            if (string.IsNullOrEmpty(symbol))
            {
                return false;
            }

            if (_tickerSubscriptions.TryRemove(symbol.ToUpperInvariant(), out var subscription))
            {
                await subscription.CloseAsync();
                _logger?.LogInformation($"Ticker stream fermato per {symbol}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Avvia lo stream kline per un simbolo e intervallo specifici
        /// </summary>
        /// <param name="symbol">Simbolo da monitorare</param>
        /// <param name="interval">Intervallo delle candele</param>
        /// <returns>True se la sottoscrizione è avvenuta con successo</returns>
        public async Task<bool> StartKlineStreamAsync(string symbol, KlineInterval interval)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                _logger?.LogError("Simbolo non valido per kline stream");
                return false;
            }

            string key = $"{symbol.ToUpperInvariant()}_{interval}";
            if (_klineSubscriptions.ContainsKey(key))
            {
                _logger?.LogInformation($"Già sottoscritto al kline stream per {symbol}-{interval}");
                return true;
            }

            try
            {
                var subscriptionResult = await _socketClient.SpotApi.ExchangeData
                    .SubscribeToKlineUpdatesAsync(symbol, interval, OnKlineUpdate);

                if (subscriptionResult.Success)
                {
                    _klineSubscriptions.TryAdd(key, subscriptionResult.Data);
                    _logger?.LogInformation($"Kline stream avviato per {symbol} - {interval}");
                    return true;
                }
                else
                {
                    _logger?.LogError($"Errore avvio kline stream per {symbol}: {subscriptionResult.Error?.Message}");
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Eccezione durante avvio kline stream per {symbol}");
                return false;
            }
        }

        /// <summary>
        /// Ferma lo stream kline per un simbolo e intervallo specifici
        /// </summary>
        /// <param name="symbol">Simbolo da fermare</param>
        /// <param name="interval">Intervallo della candela da fermare</param>
        /// <returns>True se la sottoscrizione è stata fermata con successo</returns>
        public async Task<bool> StopKlineStreamAsync(string symbol, KlineInterval interval)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                return false;
            }

            string key = $"{symbol.ToUpperInvariant()}_{interval}";
            if (_klineSubscriptions.TryRemove(key, out var subscription))
            {
                await subscription.CloseAsync();
                _logger?.LogInformation($"Kline stream fermato per {symbol}-{interval}");
                return true;
            }
            return false;
        }

        /// <summary>
        /// Callback per gli aggiornamenti ticker
        /// </summary>
        private void OnTickerUpdate(DataEvent<IBinanceTick> tickerEvent)
        {
            try
            {
                var tick = tickerEvent.Data;
                
                // Crea un oggetto TickerData e lo memorizza nella cache
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
                var kline = klineEvent.Data.Data; // Binance wrappa i dati kline
                
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
                    kline.Final  // Indica se la candela è chiusa
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
        /// Ferma tutti gli stream attivi
        /// </summary>
        public async Task StopAllStreamsAsync()
        {
            try
            {
                // Chiudi tutte le sottoscrizioni ticker
                var closeTickerTasks = _tickerSubscriptions.Values.Select(s => s.CloseAsync()).ToList();
                await Task.WhenAll(closeTickerTasks);
                _tickerSubscriptions.Clear();

                // Chiudi tutte le sottoscrizioni kline
                var closeKlineTasks = _klineSubscriptions.Values.Select(s => s.CloseAsync()).ToList();
                await Task.WhenAll(closeKlineTasks);
                _klineSubscriptions.Clear();

                _logger?.LogInformation("Tutti gli stream sono stati fermati");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Errore durante la chiusura degli stream");
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
                    // Ferma gli stream in modo sincrono (non ideale ma necessario per Dispose)
                    foreach (var subscription in _tickerSubscriptions.Values)
                    {
                        subscription.CloseAsync().GetAwaiter().GetResult();
                    }
                    _tickerSubscriptions.Clear();

                    foreach (var subscription in _klineSubscriptions.Values)
                    {
                        subscription.CloseAsync().GetAwaiter().GetResult();
                    }
                    _klineSubscriptions.Clear();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Errore durante dispose degli stream");
                }

                _socketClient?.Dispose();
                _restClient?.Dispose(); // Smaltisci anche il client REST
                _disposed = true;
            }
        }
        #endregion
    }
} 