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
        
        private readonly ConcurrentDictionary<string, SymbolStreamGroup> _symbolStreamGroups = new();
        
        private bool _disposed = false;

        public BinanceStreamManager(ILogger<BinanceStreamManager> logger, IConfiguration configuration, BinanceDataCache cache)
        {
            _logger = logger;
            _cache = cache;
        }

        public async Task<List<string>> GetAvailableSymbolsAsync()
        {
            using (var tempRestClient = new BinanceRestClient())
            {
                try
                {
                    var exchangeInfo = await tempRestClient.SpotApi.ExchangeData.GetExchangeInfoAsync();
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
        }

        public async Task<bool> StartTickerStreamAsync(string symbol, ApiCredentials credentials)
        {
            if (string.IsNullOrEmpty(symbol))
            {
                _logger?.LogError("Simbolo non valido per ticker stream");
                return false;
            }
            if (credentials == null)
            {
                _logger?.LogError("Credenziali API non fornite per ticker stream.");
                return false;
            }

            string upperSymbol = symbol.ToUpperInvariant();
            if (_symbolStreamGroups.ContainsKey(upperSymbol))
            {
                _logger?.LogInformation($"Gruppo stream per {symbol} già esistente, ticker stream già sottoscritto.");
                return true;
            }

            try
            {
                var streamGroup = new SymbolStreamGroup(upperSymbol, credentials, _logger);
                
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
                    streamGroup.Dispose();
                    return false;
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, $"Eccezione durante avvio ticker stream per {symbol}");
                return false;
            }
        }

        public async Task<bool> StartKlineStreamsForSymbolAsync(string symbol, IEnumerable<KlineInterval> shortTermIntervals, IEnumerable<KlineInterval> longTermIntervals, ApiCredentials credentials)
        {
            string upperSymbol = symbol.ToUpperInvariant();
            if (!_symbolStreamGroups.TryGetValue(upperSymbol, out var streamGroup))
            {
                _logger?.LogError($"Gruppo stream non trovato per il simbolo {symbol}. Impossibile avviare stream kline.");
                return false;
            }
            if (credentials == null)
            {
                _logger?.LogError("Credenziali API non fornite per kline stream.");
                return false;
            }

            bool allSucceeded = true;

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

        public async Task<decimal> GetAssetBalanceAsync(string asset, ApiCredentials credentials)
        {
            if (credentials == null)
            {
                _logger?.LogError("Credenziali API non fornite per recupero saldo.");
                return 0;
            }

            using (var userRestClient = new BinanceRestClient(options => { options.ApiCredentials = credentials; }))
            {
                try
                {
                    var accountInfo = await userRestClient.SpotApi.Account.GetAccountInfoAsync();
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
        }

        public async Task<bool> StopTickerStreamAsync(string symbol)
        {
            string upperSymbol = symbol.ToUpperInvariant();
            if (_symbolStreamGroups.TryRemove(upperSymbol, out var streamGroup))
            {
                await streamGroup.StopAllSymbolStreamsAsync();
                streamGroup.Dispose();
                _logger?.LogInformation($"Tutti gli stream per il simbolo {symbol} sono stati fermati e disposti.");
                return true;
            }
            return false;
        }

        public async Task<bool> StartKlineStreamAsync(string symbol, KlineInterval interval)
        {
            _logger.LogWarning($"StartKlineStreamAsync obsoleto per {symbol}-{interval}. Usare StartKlineStreamsForSymbolAsync.");
            return false;
        }

        public async Task<bool> StopKlineStreamAsync(string symbol, KlineInterval interval)
        {
            _logger.LogWarning($"StopKlineStreamAsync obsoleto per {symbol}-{interval}. Usare StopTickerStreamAsync per fermare tutti gli stream del simbolo.");
            return false;
        }

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
                _logger?.LogInformation($"[BinanceStreamManager] Ticker aggiornato e cachato: {tick.Symbol} = ${tick.LastPrice}");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[BinanceStreamManager] Errore nel processare aggiornamento ticker");
            }
        }

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
                _logger?.LogInformation($"[BinanceStreamManager] Kline aggiornata e cachata: {klineEvent.Symbol} {kline.Interval} - " +
                                 $"Close: ${kline.ClosePrice} (Final: {kline.Final})");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[BinanceStreamManager] Errore nel processare aggiornamento kline");
            }
        }

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
                    streamGroup.Dispose();
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
                    foreach (var streamGroup in _symbolStreamGroups.Values)
                    {
                        streamGroup.StopAllSymbolStreamsAsync().GetAwaiter().GetResult();
                        streamGroup.Dispose();
                    }
                    _symbolStreamGroups.Clear();
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Errore durante dispose degli stream manager");
                }

                _disposed = true;
            }
        }
        #endregion
    }
}
