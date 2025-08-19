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
using BinanceDataCacheApp.Data; // Aggiungi questo using
using Microsoft.EntityFrameworkCore; // Aggiungi questo using
using Microsoft.AspNetCore.Identity; // Aggiungi questo using
using BinanceDataCacheApp.Models; // Aggiungi questo using
using BinanceDataCacheApp.Services; // Aggiungi questo using

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
builder.Services.AddSingleton<IEncryptionService, EncryptionService>(); // Registra il servizio di crittografia
builder.Services.AddSignalR();
builder.Services.AddHostedService<BinanceStreamHostedService>();

// Registra il servizio di notifica Telegram come Scoped
builder.Services.AddScoped<ITelegramNotificationService, TelegramNotificationService>();

// Configura il DbContext per PostgreSQL
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Aggiungi i servizi Identity
builder.Services.AddDefaultIdentity<User>(options => options.SignIn.RequireConfirmedAccount = false)
    .AddEntityFrameworkStores<ApplicationDbContext>();

// Aggiungi il supporto per le Razor Pages
builder.Services.AddRazorPages();

var app = builder.Build();

// Configura la pipeline di richiesta HTTP
// app.UseDefaultFiles(); // Rimosso per garantire che la logica di autenticazione venga applicata alla root
app.UseStaticFiles(); // Abilita il servizio di file statici dalla cartella wwwroot

// Aggiungi i middleware di routing, autenticazione e autorizzazione nell'ordine corretto
app.UseRouting(); // Assicurati che UseRouting sia prima di UseAuthentication/UseAuthorization
app.UseAuthentication();
app.UseAuthorization();

// Mappa le Razor Pages
app.MapRazorPages();

// Mappa la pagina Index (dashboard) e proteggila
app.MapGet("/Index", async context =>
{
    if (context.User.Identity.IsAuthenticated)
    {
        // Se l'utente è autenticato, servi il contenuto di wwwroot/index.html
        await context.Response.SendFileAsync(
            app.Environment.WebRootFileProvider.GetFileInfo("index.html").PhysicalPath);
    }
    else
    {
        // Se l'utente non è autenticato, reindirizza alla pagina di atterraggio
        context.Response.Redirect("/Landing");
    }
});

// Reindirizza la root alla logica di /Index (che gestirà autenticazione/reindirizzamento)
app.MapGet("/", async context =>
{
    context.Response.Redirect("/Index");
});

// Proteggi il TickerHub
app.MapHub<TickerHub>("/tickerHub").RequireAuthorization();

// Applica le migrazioni del database all'avvio
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    dbContext.Database.Migrate();
}

app.Run();


// Classe HostedService per avviare e fermare lo stream Binance in background
public class BinanceStreamHostedService : IHostedService
{
    private readonly BinanceStreamManager _streamManager;
    private readonly ILogger<BinanceStreamHostedService> _logger;
    private readonly BinanceDataCache _cache;
    private readonly IHubContext<TickerHub> _hubContext;
    private readonly IServiceScopeFactory _scopeFactory; // Usa IServiceScopeFactory per risolvere servizi scoped

    public BinanceStreamHostedService(
        BinanceStreamManager streamManager,
        ILogger<BinanceStreamHostedService> logger,
        BinanceDataCache cache,
        IHubContext<TickerHub> hubContext,
        IServiceScopeFactory scopeFactory) // Inietta IServiceScopeFactory
    {
        _streamManager = streamManager;
        _logger = logger;
        _cache = cache;
        _hubContext = hubContext; 
        _scopeFactory = scopeFactory; // Inizializza serviceProvider

        _cache.OnTickerUpdated += async (tickerData) =>
        {
            // Invia l'aggiornamento ai client SignalR che sono nel gruppo del simbolo specifico
            await _hubContext.Clients.Group(tickerData.Symbol).SendAsync("ReceiveTickerUpdate", tickerData);

            // TODO: Questa è la logica dove dovresti implementare l'invio di messaggi Telegram per utente
            // Ogni utente potrebbe avere le proprie soglie di notifica.
            // Per fare questo, avresti bisogno di:
            // 1. Un meccanismo per ottenere tutti gli UserId che hanno abilitato le notifiche Telegram
            // 2. Per ogni userId, recuperare le loro impostazioni TelegramKey e BotConfiguration (per le soglie di prezzo)
            // 3. Valutare se il tickerData corrente soddisfa le condizioni di notifica per quel singolo utente
            // 4. Chiamare _telegramService.SendMessageAsync(userId, message) solo se le condizioni sono soddisfatte.
            //
            // Esempio di come potresti ottenere i servizi scoped (UserManager, DbContext) qui, per riferimento:
            using (var scope = _scopeFactory.CreateScope()) // Crea un nuovo scope per i servizi scoped
            {
                 var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
                 var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                 var encryptionService = scope.ServiceProvider.GetRequiredService<IEncryptionService>();
                 var telegramServiceScoped = scope.ServiceProvider.GetRequiredService<ITelegramNotificationService>();
                 // Esempio: recupera un utente specifico e le sue impostazioni Telegram
                 // Qui dovrai iterare su tutti gli utenti che hanno notifche Telegram abilitate
                 var usersWithTelegramNotifications = await dbContext.TelegramKeys
                    .Where(tk => tk.SendNotifications)
                    .Select(tk => tk.UserId)
                    .Distinct()
                    .ToListAsync();

                 foreach (var userId in usersWithTelegramNotifications)
                 {
                     var user = await userManager.FindByIdAsync(userId);
                     if (user != null) 
                     {
                         // Recupera le impostazioni di Telegram per questo utente
                         var telegramKey = await dbContext.TelegramKeys
                            .Where(tk => tk.UserId == userId && tk.SendNotifications)
                            .FirstOrDefaultAsync();
                        
                         if (telegramKey != null)
                         {
                            // Qui puoi aggiungere la tua logica per le soglie di prezzo, ecc.
                            // Ad esempio, se tickerData.Symbol è quello che l'utente vuole monitorare
                            // e il prezzo è sotto una certa soglia configurata per l'utente.
                            if (tickerData.Symbol == "BTCUSDT" && tickerData.Price < 20000) // Esempio: Sostituisci con logica utente-specifica
                            {
                                var message = $"🚨 **Avviso BTCUSDT:** Il prezzo è sceso a **{tickerData.Price:F2}$** per {user.UserName}!";
                                await telegramServiceScoped.SendMessageAsync(userId, message); // Usa il servizio scoped
                                _logger.LogInformation($"Inviato avviso Telegram a {user.UserName} per {tickerData.Symbol}");
                            }
                         }
                     }
                 }
            }

            // Rimuovi l'invio del messaggio hardcoded precedente
            // if (tickerData.Symbol == "BTCUSDT" && tickerData.Price < 20000)
            // {
            //     var message = $"🚨 **Avviso BTCUSDT:** Il prezzo è sceso a **{tickerData.Price:F2}$**!";
            //     await _telegramService.SendMessageAsync(message);
            // }
        };
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Binance Stream Hosted Service avviato.");
        // Rimuovi l'invio del messaggio di test all'avvio
        // await _telegramService.SendMessageAsync("**TEST:** L'applicazione Binance Data Cache è stata avviata con successo!");
        // Gli stream Binance non vengono più avviati globalmente all'avvio dell'applicazione.
        // Vengono avviati per utente tramite TickerHub quando l'utente sottoscrive un ticker.
        // Rimuovi le chiamate a StartTickerStreamAsync e StartKlineStreamAsync qui.
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Binance Stream Hosted Service in arresto.");
        await _streamManager.StopAllStreamsAsync();
        _streamManager.Dispose();
    }
}
