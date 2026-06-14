using System.Linq.Dynamic.Core;
using System.Threading.RateLimiting;
using GW2Stonks.Components;
using GW2Stonks.Data;
using GW2Stonks.Gw2Api;
using GW2Stonks.Services;
using GW2Stonks.Util;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Radzen;

namespace GW2Stonks
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // Add services to the container.
            builder.Services.AddRazorComponents()
                .AddInteractiveServerComponents();

            // Radzen UI services (dialogs, notifications, tooltips, context menus).
            builder.Services.AddRadzenComponents();

            // EF Core against the local MariaDB instance. The connection string lives in
            // user-secrets (see README); a placeholder default is in appsettings.json so
            // migrations can be generated offline. Server version is pinned to avoid a
            // database round-trip at startup / design time.
            var connectionString = builder.Configuration.GetConnectionString("GW2Stonks")
                ?? throw new InvalidOperationException(
                    "Connection string 'GW2Stonks' not found. Set it with: " +
                    "dotnet user-secrets set \"ConnectionStrings:GW2Stonks\" \"<value>\"");
            var serverVersion = new MariaDbServerVersion(new Version(10, 11, 6));
            builder.Services.AddDbContextFactory<AppDbContext>(options =>
                options.UseMySql(connectionString, serverVersion));

            // GW2 API: options, a shared token-bucket rate limiter, and the typed client
            // wrapped with resilience (retry/backoff) on the outside and rate limiting inside
            // (so every retry attempt also acquires a token).
            builder.Services.Configure<Gw2Options>(
                builder.Configuration.GetSection(Gw2Options.SectionName));

            builder.Services.AddSingleton<RateLimiter>(sp =>
            {
                var o = sp.GetRequiredService<IOptions<Gw2Options>>().Value;
                return new TokenBucketRateLimiter(new TokenBucketRateLimiterOptions
                {
                    TokenLimit = Math.Max(1, o.RequestsPerSecond),
                    TokensPerPeriod = Math.Max(1, o.RequestsPerSecond),
                    ReplenishmentPeriod = TimeSpan.FromSeconds(1),
                    AutoReplenishment = true,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                    QueueLimit = 10_000
                });
            });
            builder.Services.AddTransient<RateLimitingHandler>();

            var gw2Http = builder.Services.AddHttpClient<Gw2ApiClient>((sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<Gw2Options>>().Value;
                http.BaseAddress = new Uri(o.ApiBaseUrl);
            });
            gw2Http.AddStandardResilienceHandler();
            gw2Http.AddHttpMessageHandler<RateLimitingHandler>();

            builder.Services.AddScoped<Gw2SyncService>();
            builder.Services.AddHostedService<PriceRefreshBackgroundService>();

            var app = builder.Build();

            // Headless sync mode for terminals / scheduled tasks:
            //   dotnet run -- sync [all|items|recipes|prices]
            // Runs the requested sync against the DB and exits without starting the web server.
            if (args.Length > 0 && string.Equals(args[0], "sync", StringComparison.OrdinalIgnoreCase))
            {
                await RunSyncCliAsync(app.Services, args.Length > 1 ? args[1] : "all");
                return;
            }

            // Diagnostic that exercises the items grid's exact data path:
            //   dotnet run -- query [nameFilter]
            if (args.Length > 0 && string.Equals(args[0], "query", StringComparison.OrdinalIgnoreCase))
            {
                await RunQueryCliAsync(app.Services, args.Length > 1 ? args[1] : "Wood");
                return;
            }

            // Configure the HTTP request pipeline.
            if (!app.Environment.IsDevelopment())
            {
                app.UseExceptionHandler("/Error");
                // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
                app.UseHsts();
            }

            app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
            app.UseHttpsRedirection();

            app.UseAntiforgery();

            app.MapStaticAssets();
            app.MapRazorComponents<App>()
                .AddInteractiveServerRenderMode();

            await app.RunAsync();
        }

        private static async Task RunSyncCliAsync(IServiceProvider services, string what)
        {
            using var scope = services.CreateScope();
            var sync = scope.ServiceProvider.GetRequiredService<Gw2SyncService>();

            var lastPhase = "";
            var progress = new Progress<SyncProgress>(p =>
            {
                if (p.Phase != lastPhase)
                {
                    lastPhase = p.Phase;
                    Console.WriteLine();
                }
                Console.Write($"\r{p.Phase}: {p.Done:N0}/{p.Total:N0} ({p.Percent}%)   ");
            });

            what = what.ToLowerInvariant();
            if (what is "all" or "items") await sync.SyncItemsAsync(progress);
            if (what is "all" or "recipes") await sync.SyncRecipesAsync(progress);
            if (what is "all" or "prices") await sync.RefreshPricesAsync(progress);

            var status = await sync.GetStatusAsync();
            Console.WriteLine();
            Console.WriteLine($"Done. Items={status.ItemCount:N0} Recipes={status.RecipeCount:N0} Prices={status.PriceCount:N0}");
        }

        private static async Task RunQueryCliAsync(IServiceProvider services, string nameFilter)
        {
            using var scope = services.CreateScope();
            var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbf.CreateDbContextAsync();

            // Mirror the items grid's data path: shared projection + dynamic-LINQ filter/sort.
            var query = db.Items.AsNoTracking().ToItemRows()
                .Where("Name.Contains(@0)", nameFilter)
                .OrderBy("Sell desc");

            var total = await query.CountAsync();
            var rows = await query.Take(10).ToListAsync();

            Console.WriteLine($"Items whose name contains \"{nameFilter}\": {total:N0}");
            Console.WriteLine($"{"Id",-7} {"Buy",-12} {"Sell",-12} Name");
            foreach (var r in rows)
                Console.WriteLine($"{r.Id,-7} {Coin.Format(r.Buy),-12} {Coin.Format(r.Sell),-12} {r.Name}");
        }
    }
}
