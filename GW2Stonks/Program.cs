using System.Linq.Dynamic.Core;
using System.Threading.RateLimiting;
using GW2Stonks.Components;
using GW2Stonks.Data;
using GW2Stonks.Datawars2;
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
            builder.Services.AddSingleton<ProfitService>();
            builder.Services.AddHostedService<PriceRefreshBackgroundService>();

            // datawars2.ie volume source (separate host): typed client + cached-volume refresh.
            builder.Services.Configure<Datawars2Options>(
                builder.Configuration.GetSection(Datawars2Options.SectionName));

            var dwHttp = builder.Services.AddHttpClient<Datawars2Client>((sp, http) =>
            {
                var o = sp.GetRequiredService<IOptions<Datawars2Options>>().Value;
                http.BaseAddress = new Uri(o.ApiBaseUrl);
            });
            dwHttp.AddStandardResilienceHandler();

            builder.Services.AddScoped<VolumeService>();
            builder.Services.AddHostedService<VolumeRefreshBackgroundService>();

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

            // Craft-profit breakdown for one item, or the top profitable items:
            //   dotnet run -- profit <itemId> [instant|orders]
            //   dotnet run -- profit top [instant|orders]
            if (args.Length > 0 && string.Equals(args[0], "profit", StringComparison.OrdinalIgnoreCase))
            {
                var mode = args.Length > 2 &&
                    (args[2].StartsWith("order", StringComparison.OrdinalIgnoreCase) ||
                     args[2].StartsWith("buy", StringComparison.OrdinalIgnoreCase))
                    ? PricingMode.BuyOrders
                    : PricingMode.InstantBuy;

                if (args.Length > 1 && string.Equals(args[1], "top", StringComparison.OrdinalIgnoreCase))
                {
                    await RunProfitTopCliAsync(app.Services, mode);
                    return;
                }
                if (args.Length < 2 || !int.TryParse(args[1], out var pid))
                {
                    Console.WriteLine("Usage: dotnet run -- profit <itemId|top> [instant|orders]");
                    return;
                }
                await RunProfitCliAsync(app.Services, pid, mode);
                return;
            }

            // Refresh cached trading-post volume, or show one item's cached volume:
            //   dotnet run -- volume refresh
            //   dotnet run -- volume <itemId>
            if (args.Length > 0 && string.Equals(args[0], "volume", StringComparison.OrdinalIgnoreCase))
            {
                await RunVolumeCliAsync(app.Services, args.Length > 1 ? args[1] : "refresh");
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

        private static async Task RunProfitCliAsync(IServiceProvider services, int itemId, PricingMode mode)
        {
            using var scope = services.CreateScope();
            var profit = scope.ServiceProvider.GetRequiredService<ProfitService>();

            var tree = await profit.BuildTreeAsync(itemId, mode);
            if (tree is null)
            {
                Console.WriteLine($"Item {itemId} not found.");
                return;
            }

            Console.WriteLine($"Craft breakdown for '{tree.Name}' (#{itemId}), mode={mode}:");
            PrintCraftNode(tree, 0);

            var row = (await profit.GetRowsAsync(mode)).FirstOrDefault(r => r.Id == itemId);
            if (row is not null)
            {
                Console.WriteLine();
                Console.WriteLine($"Craft cost {Coin.Format(row.CraftCost)} | Net sell {Coin.Format(row.NetSell)} | " +
                    $"Profit {Coin.Format(row.Profit)} ({row.Margin:0.#}%)");
            }
        }

        private static async Task RunProfitTopCliAsync(IServiceProvider services, PricingMode mode)
        {
            using var scope = services.CreateScope();
            var profit = scope.ServiceProvider.GetRequiredService<ProfitService>();
            var rows = await profit.GetRowsAsync(mode);
            var top = rows.Where(r => r.Profit is > 0).OrderByDescending(r => r.Profit).Take(15).ToList();

            Console.WriteLine($"Top craft-profit items ({mode}) — {rows.Count(r => r.Profit is > 0):N0} profitable of {rows.Count:N0} priced:");
            Console.WriteLine($"{"Profit",-13} {"Margin",-9} {"Craft",-13} {"Net sell",-13} Name");
            foreach (var r in top)
                Console.WriteLine($"{Coin.Format(r.Profit),-13} {r.Margin,6:0.#}%  {Coin.Format(r.CraftCost),-13} {Coin.Format(r.NetSell),-13} {r.Name}");
        }

        private static async Task RunVolumeCliAsync(IServiceProvider services, string arg)
        {
            using var scope = services.CreateScope();

            if (string.Equals(arg, "refresh", StringComparison.OrdinalIgnoreCase))
            {
                var volume = scope.ServiceProvider.GetRequiredService<VolumeService>();
                var progress = new Progress<SyncProgress>(p =>
                    Console.Write($"\r{p.Phase}: {p.Done:N0}/{p.Total:N0}   "));
                var n = await volume.RefreshVolumesAsync(progress);
                Console.WriteLine();
                Console.WriteLine($"Refreshed volume for {n:N0} items.");
                return;
            }

            if (!int.TryParse(arg, out var id))
            {
                Console.WriteLine("Usage: dotnet run -- volume <refresh|itemId>");
                return;
            }

            var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
            await using var db = await dbf.CreateDbContextAsync();
            var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id);
            var v = await db.ItemVolumes.AsNoTracking().FirstOrDefaultAsync(x => x.ItemId == id);
            if (v is null)
            {
                Console.WriteLine($"No volume cached for item {id} ({item?.Name}). Run: dotnet run -- volume refresh");
                return;
            }
            var sellThrough = v.SoldPerDay > 0 ? (v.SupplyNow / (double)v.SoldPerDay).ToString("0.#") + " days" : "—";
            Console.WriteLine($"{item?.Name} (#{id}): sold/day={v.SoldPerDay:N0}, bought/day={v.BoughtPerDay:N0}, " +
                $"supply={v.SupplyNow:N0}, demand={v.DemandNow:N0}, sell-through={sellThrough}");
        }

        private static void PrintCraftNode(GW2Stonks.Models.CraftNode node, int depth)
        {
            var indent = new string(' ', depth * 2);
            Console.WriteLine($"{indent}{node.Count}x {node.Name}  " +
                $"[buy {Coin.Format(node.UnitBuy)} / craft {Coin.Format(node.UnitCraft)} -> {node.Decision} {Coin.Format(node.UnitEffective)}]");
            foreach (var child in node.Children)
                PrintCraftNode(child, depth + 1);
        }
    }
}
