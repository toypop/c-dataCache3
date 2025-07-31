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
            await _streamManager.StartTickerStreamAsync(symbol);
        }

        // Metodo chiamato dal client per disiscriversi da un ticker
        public async Task UnsubscribeFromTicker(string symbol)
        {
            _logger.LogInformation($"Client disiscritto da: {symbol}");
            await _streamManager.StopTickerStreamAsync(symbol);
        }

        // Nuovo metodo per ottenere i dati Kline per un simbolo e intervallo specifici
        public async Task<KlineData> GetKlineData(string symbol, KlineInterval interval)
        {
            _logger.LogInformation($"Richiesta dati Kline per {symbol} - {interval}");

            // Assicurati che lo stream Kline sia avviato per questo simbolo e intervallo
            await _streamManager.StartKlineStreamAsync(symbol, interval);
            
            // Recupera i dati dalla cache (potrebbero non essere immediatamente disponibili se lo stream è appena iniziato)
            return _cache.GetKlineData(symbol, interval);
        }

        /// <summary>
        /// Restituisce la lista di tutti i simboli di trading disponibili.
        /// </summary>
        /// <returns>Una lista di stringhe con i simboli.</returns>
        public async Task<List<string>> GetAvailableSymbols()
        {
            _logger.LogInformation("Richiesta simboli disponibili dal client.");
            return await _streamManager.GetAllSymbolsAsync();
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