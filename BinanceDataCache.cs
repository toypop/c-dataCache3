using System;
using System.Collections.Concurrent;
using Binance.Net.Enums;
using System.Collections.Generic; // Added missing import for List

namespace BinanceDataCacheApp
{
    /// <summary>
    /// Modello per rappresentare i dati del ticker in cache
    /// </summary>
    public class TickerData
    {
        public string Symbol { get; set; }
        public decimal Price { get; set; }
        public decimal Volume24h { get; set; }
        public decimal PriceChange24h { get; set; }
        public decimal PriceChangePercent24h { get; set; }
        public DateTime LastUpdateTime { get; set; }
        
        public TickerData(string symbol, decimal price, decimal volume, 
                         decimal priceChange, decimal priceChangePercent)
        {
            Symbol = symbol;
            Price = price;
            Volume24h = volume;
            PriceChange24h = priceChange;
            PriceChange24h = priceChange;
            PriceChangePercent24h = priceChangePercent;
            LastUpdateTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Modello per rappresentare i dati delle candele (Kline) in cache
    /// </summary>
    public class KlineData
    {
        public string Symbol { get; set; }
        public KlineInterval Interval { get; set; }
        public DateTime OpenTime { get; set; }
        public DateTime CloseTime { get; set; }
        public decimal OpenPrice { get; set; }
        public decimal HighPrice { get; set; }
        public decimal LowPrice { get; set; }
        public decimal ClosePrice { get; set; }
        public decimal Volume { get; set; }
        public bool IsClosed { get; set; }
        public DateTime LastUpdateTime { get; set; }

        public KlineData(string symbol, KlineInterval interval, DateTime openTime, 
                        DateTime closeTime, decimal open, decimal high, decimal low, 
                        decimal close, decimal volume, bool isClosed)
        {
            Symbol = symbol;
            Interval = interval;
            OpenTime = openTime;
            CloseTime = closeTime;
            OpenPrice = open;
            HighPrice = high;
            LowPrice = low;
            ClosePrice = close;
            Volume = volume;
            IsClosed = isClosed;
            LastUpdateTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// DataCache thread-safe singleton per gestire i dati provenienti da stream Binance.
    /// Implementa il pattern Singleton lazy con thread-safety garantita.
    /// </summary>
    public sealed class BinanceDataCache
    {
        #region Singleton Implementation
        private static readonly Lazy<BinanceDataCache> _instance = 
            new Lazy<BinanceDataCache>(() => new BinanceDataCache());
        
        /// <summary>
        /// Istanza singleton della cache
        /// </summary>
        public static BinanceDataCache Instance => _instance.Value;
        
        private BinanceDataCache() { }
        #endregion

        #region Thread-safe Data Structures
        /// <summary>
        /// Cache thread-safe per i dati ticker. Chiave: simbolo (es. "BTCUSDT")
        /// </summary>
        private readonly ConcurrentDictionary<string, TickerData> _tickerCache = 
            new ConcurrentDictionary<string, TickerData>();

        /// <summary>
        /// Cache thread-safe per i dati kline. Chiave composta: "SIMBOLO_INTERVALLO" (es. "BTCUSDT_1m")
        /// </summary>
        private readonly ConcurrentDictionary<string, KlineData> _klineCache = 
            new ConcurrentDictionary<string, KlineData>();
        #endregion

        #region Events per notificare aggiornamenti
        /// <summary>
        /// Evento scatenato quando i dati ticker vengono aggiornati
        /// </summary>
        public event Action<TickerData> OnTickerUpdated;
        
        /// <summary>
        /// Evento scatenato quando i dati kline vengono aggiornati
        /// </summary>
        public event Action<KlineData> OnKlineUpdated;
        #endregion

        #region Ticker Data Methods
        /// <summary>
        /// Aggiorna i dati ticker nella cache in modo thread-safe
        /// </summary>
        /// <param name="tickerData">Dati del ticker da memorizzare</param>
        public void SetTickerData(TickerData tickerData)
        {
            if (tickerData == null || string.IsNullOrEmpty(tickerData.Symbol))
                return;

            // AddOrUpdate garantisce thread-safety e atomicità dell\'operazione
            _tickerCache.AddOrUpdate(
                tickerData.Symbol.ToUpperInvariant(),
                tickerData,
                (key, oldValue) => tickerData
            );

            // Notifica gli observers dell\'aggiornamento
            OnTickerUpdated?.Invoke(tickerData);
        }

        /// <summary>
        /// Recupera i dati ticker per un simbolo specifico
        /// </summary>
        /// <param name="symbol">Simbolo da cercare (es. "BTCUSDT")</param>
        /// <returns>Dati del ticker se presenti, null altrimenti</returns>
        public TickerData GetTickerData(string symbol)
        {
            if (string.IsNullOrEmpty(symbol))
                return null;

            _tickerCache.TryGetValue(symbol.ToUpperInvariant(), out var tickerData);
            return tickerData;
        }

        /// <summary>
        /// Verifica se i dati ticker per un simbolo sono presenti e aggiornati
        /// </summary>
        /// <param name="symbol">Simbolo da verificare</param>
        /// <param name="maxAgeSeconds">Età massima dei dati in secondi (default: 60)</param>
        /// <returns>True se i dati sono presenti e freschi</returns>
        public bool IsTickerDataFresh(string symbol, int maxAgeSeconds = 60)
        {
            var data = GetTickerData(symbol);
            if (data == null) return false;

            return (DateTime.UtcNow - data.LastUpdateTime).TotalSeconds <= maxAgeSeconds;
        }
        #endregion

        #region Kline Data Methods
        /// <summary>
        /// Aggiorna i dati kline nella cache in modo thread-safe
        /// </summary>
        /// <param name="klineData">Dati della candela da memorizzare</param>
        public void SetKlineData(KlineData klineData)
        {
            if (klineData == null || string.IsNullOrEmpty(klineData.Symbol))
                return;

            // Creiamo una chiave composita per symbol + interval
            string key = $"{klineData.Symbol.ToUpperInvariant()}_{klineData.Interval}";

            _klineCache.AddOrUpdate(
                key,
                klineData,
                (key, oldValue) => klineData
            );

            OnKlineUpdated?.Invoke(klineData);
        }

        /// <summary>
        /// Recupera i dati kline per un simbolo e intervallo specifici
        /// </summary>
        /// <param name="symbol">Simbolo da cercare</param>
        /// <param name="interval">Intervallo della candela</param>
        /// <returns>Dati della candela se presenti, null altrimenti</returns>
        public KlineData GetKlineData(string symbol, KlineInterval interval)
        {
            if (string.IsNullOrEmpty(symbol))
                return null;

            string key = $"{symbol.ToUpperInvariant()}_{interval}";
            _klineCache.TryGetValue(key, out var klineData);
            return klineData;
        }

        /// <summary>
        /// Verifica se i dati kline sono presenti e aggiornati
        /// </summary>
        /// <param name="symbol">Simbolo da verificare</param>
        /// <param name="interval">Intervallo da verificare</param>
        /// <param name="maxAgeSeconds">Età massima dei dati in secondi (default: basato sull\'intervallo)</param>
        /// <returns>True se i dati sono presenti e freschi</returns>
        public bool IsKlineDataFresh(string symbol, KlineInterval interval, int? maxAgeSeconds = null)
        {
            var data = GetKlineData(symbol, interval);
            if (data == null) return false;

            // Se non specificato, usa un\'età massima basata sull\'intervallo della candela
            int maxAge = maxAgeSeconds ?? GetDefaultMaxAgeForInterval(interval);
            
            return (DateTime.UtcNow - data.LastUpdateTime).TotalSeconds <= maxAge;
        }

        /// <summary>
        /// Determina l\'età massima predefinita basata sull\'intervallo
        /// </summary>
        private int GetDefaultMaxAgeForInterval(KlineInterval interval)
        {
            return interval switch
            {
                KlineInterval.OneSecond => 5,
                KlineInterval.OneMinute => 70,
                KlineInterval.ThreeMinutes => 200,
                KlineInterval.FiveMinutes => 320,
                KlineInterval.FifteenMinutes => 920,
                KlineInterval.ThirtyMinutes => 1820,
                KlineInterval.OneHour => 3620,
                KlineInterval.TwoHour => 7220,
                KlineInterval.FourHour => 14420,
                KlineInterval.SixHour => 21620,
                KlineInterval.EightHour => 28820,
                KlineInterval.TwelveHour => 43220,
                KlineInterval.OneDay => 86420,
                _ => 3600 // Default: 1 ora
            };
        }
        #endregion

        #region Cache Management Methods
        /// <summary>
        /// Restituisce statistiche sulla cache
        /// </summary>
        /// <returns>Stringa con informazioni sullo stato della cache</returns>
        public string GetCacheStats()
        {
            return $"Ticker Cache: {_tickerCache.Count} simboli | " +
                   $"Kline Cache: {_klineCache.Count} serie temporali";
        }

        /// <summary>
        /// Pulisce i dati obsoleti dalla cache
        /// </summary>
        /// <param name="maxAgeHours">Età massima in ore per considerare i dati validi</param>
        /// <returns>Numero di elementi rimossi</returns>
        public int CleanupOldData(int maxAgeHours = 24)
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-maxAgeHours);
            int removedCount = 0;

            // Cleanup ticker data
            var tickersToRemove = new List<string>();
            foreach (var kvp in _tickerCache)
            {
                if (kvp.Value.LastUpdateTime < cutoffTime)
                    tickersToRemove.Add(kvp.Key);
            }

            foreach (var key in tickersToRemove)
            {
                if (_tickerCache.TryRemove(key, out _))
                    removedCount++;
            }

            // Cleanup kline data
            var klinesToRemove = new List<string>();
            foreach (var kvp in _klineCache)
            {
                if (kvp.Value.LastUpdateTime < cutoffTime)
                    klinesToRemove.Add(kvp.Key);
            }

            foreach (var key in klinesToRemove)
            {
                if (_klineCache.TryRemove(key, out _))
                    removedCount++;
            }

            return removedCount;
        }

        /// <summary>
        /// Pulisce completamente la cache
        /// </summary>
        public void ClearCache()
        {
            _tickerCache.Clear();
            _klineCache.Clear();
        }
        #endregion
    }
} 