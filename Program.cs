using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Binance.Net.Clients;
using Binance.Net.Enums;
using Binance.Net.Interfaces.Clients;
using Binance.Net.Objects.Models.Spot.Socket;
using CryptoExchange.Net.Authentication;
using CryptoExchange.Net.Sockets;
using CryptoExchange.Net.Objects;
using Microsoft.Extensions.Logging;
using CryptoExchange.Net.Objects.Sockets;
using Binance.Net.Interfaces;
using BinanceDataCacheApp;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Binance.Net.Enums;
using System;
using System.Linq;

var builder = WebApplication.CreateBuilder(args);

// Configurazione del logging
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

// Aggiungi servizi alla pipeline
builder.Services.AddSingleton(BinanceDataCache.Instance); // Registra l'istanza singleton esistente
builder.Services.AddSingleton<BinanceStreamManager>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<BinanceStreamHostedService>();

var app = builder.Build();

// Configura la pipeline di richiesta HTTP
app.UseDefaultFiles(); // Permette di servire index.html per default
app.UseStaticFiles(); // Abilita il servizio di file statici dalla cartella wwwroot

app.MapHub<TickerHub>("/tickerHub");

app.Run();


// Classe HostedService per avviare e fermare lo stream Binance in background
public class BinanceStreamHostedService : IHostedService
{
    private readonly BinanceStreamManager _streamManager;
    private readonly ILogger<BinanceStreamHostedService> _logger;
    private readonly BinanceDataCache _cache;
    private readonly IHubContext<TickerHub> _hubContext;

    public BinanceStreamHostedService(
        BinanceStreamManager streamManager,
        ILogger<BinanceStreamHostedService> logger,
        BinanceDataCache cache,
        IHubContext<TickerHub> hubContext) // Aggiungi IHubContext qui
    {
        _streamManager = streamManager;
        _logger = logger;
        _cache = cache;
        _hubContext = hubContext; // Inizializza l'HubContext

        _cache.OnTickerUpdated += async (tickerData) =>
        {
            // Invia l'aggiornamento ai client SignalR direttamente
            await _hubContext.Clients.All.SendAsync("ReceiveTickerUpdate", tickerData);
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Binance Stream Hosted Service avviato.");
        // Avvia gli stream per BTCUSDT
        await _streamManager.StartTickerStreamAsync("BTCUSDT");
        
        // Avvia gli stream Kline per BTCUSDT per tutti gli intervalli disponibili
        foreach (KlineInterval interval in Enum.GetValues(typeof(KlineInterval)))
        {
            // Salta gli intervalli che non vogliamo sottoscrivere
            if (interval == KlineInterval.OneSecond) continue;

            await _streamManager.StartKlineStreamAsync("BTCUSDT", interval);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Binance Stream Hosted Service in arresto.");
        await _streamManager.StopAllStreamsAsync();
        _streamManager.Dispose();
    }
}
