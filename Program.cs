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
using System;
using System.Linq;
using Telegram.Bot; // Aggiungi questo using

var builder = WebApplication.CreateBuilder(args);

// Aggiungi User Secrets in fase di sviluppo
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

// Aggiungi la configurazione per Telegram Bot
var telegramBotToken = builder.Configuration["Telegram:BotToken"];

// Crea un logger per l'uso immediato in Program.cs
var logger = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<Program>();

if (string.IsNullOrEmpty(telegramBotToken))
{
    logger.LogError("Il token del bot Telegram non è configurato. Assicurati di aver impostato la variabile d'ambiente 'Telegram:BotToken' o un user secret.");
    // Puoi scegliere di lanciare un'eccezione, terminare l'app, o semplicemente non abilitare la funzionalità Telegram.
    // Per ora, l'applicazione continuerà senza la funzionalità Telegram.
}
else
{
    builder.Services.AddSingleton<ITelegramBotClient>(new TelegramBotClient(telegramBotToken));
    logger.LogInformation("Telegram Bot Client configurato con successo.");
}


// Configurazione del logging
builder.Services.AddLogging(configure =>
{
    configure.AddConsole();
    // Filtra i log di Microsoft.Hosting.Lifetime per essere almeno Information
    configure.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Information);
});

// Aggiungi servizi alla pipeline
builder.Services.AddSingleton<BinanceDataCache>();
builder.Services.AddSingleton<BinanceStreamManager>();
builder.Services.AddSignalR();
builder.Services.AddHostedService<BinanceStreamHostedService>();

// Registra il servizio di notifica Telegram
builder.Services.AddSingleton<ITelegramNotificationService, TelegramNotificationService>();

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
    private readonly ITelegramNotificationService _telegramService; // Aggiungi questa linea

    public BinanceStreamHostedService(
        BinanceStreamManager streamManager,
        ILogger<BinanceStreamHostedService> logger,
        BinanceDataCache cache,
        IHubContext<TickerHub> hubContext,
        ITelegramNotificationService telegramService) // Aggiungi telegramService qui
    {
        _streamManager = streamManager;
        _logger = logger;
        _cache = cache;
        _hubContext = hubContext; // Inizializza l'HubContext
        _telegramService = telegramService; // Inizializza il servizio Telegram

        _cache.OnTickerUpdated += async (tickerData) =>
        {
            // Invia l'aggiornamento ai client SignalR direttamente
            await _hubContext.Clients.All.SendAsync("ReceiveTickerUpdate", tickerData);

            // Esempio: invia un messaggio Telegram se il prezzo di BTCUSDT scende sotto un certo livello
            // Questa logica può essere raffinata in base alla percentuale di discesa che hai menzionato
            if (tickerData.Symbol == "BTCUSDT" && tickerData.Price < 20000)
            {
                var message = $"🚨 **Avviso BTCUSDT:** Il prezzo è sceso a **{tickerData.Price:F2}$**!";
                await _telegramService.SendMessageAsync(message);
            }
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Binance Stream Hosted Service avviato.");
        await _telegramService.SendMessageAsync("**TEST:** L'applicazione Binance Data Cache è stata avviata con successo!");
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
