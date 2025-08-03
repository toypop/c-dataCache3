using Microsoft.AspNetCore.SignalR;
using BinanceDataCacheApp;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Binance.Net.Enums;
using System.Collections.Generic;

namespace BinanceDataCacheApp
{
    public class TickerHub : Hub
    {
        private readonly BinanceStreamManager _streamManager;
        private readonly ILogger<TickerHub> _logger;
        private readonly BinanceDataCache _cache;

        public TickerHub(BinanceStreamManager streamManager, ILogger<TickerHub> logger, BinanceDataCache cache)
        {
            _streamManager = streamManager;
            _logger = logger;
            _cache = cache;
        }

        // Metodo chiamato dal client per sottoscrivere un ticker
        public async Task SubscribeToTicker(string symbol)
        {
            _logger.LogInformation($"Client sottoscritto a: {symbol}");
            bool tickerSubscribed = await _streamManager.StartTickerStreamAsync(symbol);

            if (tickerSubscribed)
            {
                // Definisci gli intervalli Kline per breve e lungo termine
                var shortTermIntervals = new List<KlineInterval> { KlineInterval.OneMinute, KlineInterval.FiveMinutes, KlineInterval.FifteenMinutes };
                var longTermIntervals = new List<KlineInterval> { KlineInterval.OneHour, KlineInterval.FourHour, KlineInterval.OneDay };

                bool klinesStarted = await _streamManager.StartKlineStreamsForSymbolAsync(symbol, shortTermIntervals, longTermIntervals);

                if (klinesStarted)
                {
                    _logger.LogInformation($"Stream Kline avviati con successo per {symbol}.");
                }
                else
                {
                    _logger.LogError($"Errore nell'avvio degli stream Kline per {symbol}. Potrebbero esserci problemi di connessione.");
                    // Considera di disiscrivere il ticker se i Kline non partono, a seconda della logica desiderata
                }
            }
            else
            {
                _logger.LogWarning($"Sottoscrizione ticker per {symbol} non riuscita o già esistente. Non avvio stream Kline.");
            }
        }

        // Metodo chiamato dal client per disiscriversi da un ticker
        public async Task UnsubscribeFromTicker(string symbol)
        {
            _logger.LogInformation($"Client disiscritto da: {symbol}");
            // StopTickerStreamAsync ora ferma tutti gli stream associati a quel simbolo
            await _streamManager.StopTickerStreamAsync(symbol);
        }

        // Nuovo metodo per ottenere i dati Kline per un simbolo e intervallo specifici
        public async Task<KlineData> GetKlineData(string symbol, KlineInterval interval)
        {
            _logger.LogInformation($"Richiesta dati Kline per {symbol} - {interval}");

            // Non è più necessario avviare lo stream qui, si assume che sia già avviato
            // se il ticker è sottoscritto. Recupera direttamente dalla cache.
            // return _cache.GetKlineData(symbol, interval);
            return await Task.FromResult(_cache.GetKlineData(symbol, interval));
        }

        /// <summary>
        /// Restituisce la lista di tutti i simboli di trading disponibili.
        /// </summary>
        /// <returns>Una lista di stringhe con i simboli.</returns>
        public async Task<List<string>> GetAvailableSymbols()
        {
            _logger.LogInformation("Richiesta simboli disponibili dal client.");
            // return await _streamManager.GetAllSymbolsAsync();
            return await _streamManager.GetAvailableSymbolsAsync();
        }

        /// <summary>
        /// Recupera il saldo USDT dell'utente.
        /// </summary>
        /// <returns>Il saldo disponibile in USDT.</returns>
        public async Task<decimal> GetUSDCBalance() // Metodo rinominato
        {
            _logger.LogInformation("Richiesta saldo USDC.");
            return await _streamManager.GetAssetBalanceAsync("USDC");
        }

        // Questo metodo non è più necessario in quanto l'HubContext viene iniettato nel HostedService
        // e l'invio avviene direttamente da lì.
        public static async Task SendTickerUpdate(TickerData tickerData)
        {
            // Il codice di invio è stato spostato nel BinanceStreamHostedService.
            await Task.CompletedTask; // Placeholder
        }
    }
} 