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
            builder.Services.AddScoped<AccountService>();
            builder.Services.AddScoped<ProvisionerService>();
            builder.Services.AddSingleton<ProfitService>();
            builder.Services.AddSingleton<CartService>();
            builder.Services.AddSingleton<ProfitFilterState>();
            builder.Services.AddHostedService<CartLoader>();
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

            // Account integration (uses the API key saved via the Settings page):
            //   dotnet run -- account status     show key + owned-stock status
            //   dotnet run -- account refresh    re-read owned materials from the GW2 API
            if (args.Length > 0 && string.Equals(args[0], "account", StringComparison.OrdinalIgnoreCase))
            {
                await RunAccountCliAsync(app.Services, args.Length > 1 ? args[1] : "status");
                return;
            }

            // Analyse the account's current sell listings vs cheaper supply ahead:
            //   dotnet run -- listings [markPercent]   (default 50)
            if (args.Length > 0 && string.Equals(args[0], "listings", StringComparison.OrdinalIgnoreCase))
            {
                var pct = args.Length > 1 && double.TryParse(args[1], System.Globalization.CultureInfo.InvariantCulture, out var p) ? p : 50;
                await RunListingsCliAsync(app.Services, pct);
                return;
            }

            // Profit-grid items whose recursive craft tree contains a given ingredient:
            //   dotnet run -- requires <itemId>
            if (args.Length > 0 && string.Equals(args[0], "requires", StringComparison.OrdinalIgnoreCase))
            {
                if (args.Length < 2 || !int.TryParse(args[1], out var rid))
                {
                    Console.WriteLine("Usage: dotnet run -- requires <itemId>");
                    return;
                }
                await RunRequiresCliAsync(app.Services, rid);
                return;
            }

            // Price the Faction Provisioner trade-in lists (cheapest items to buy for tokens):
            //   dotnet run -- provisioner
            if (args.Length > 0 && string.Equals(args[0], "provisioner", StringComparison.OrdinalIgnoreCase))
            {
                await RunProvisionerCliAsync(app.Services);
                return;
            }

            // Inspect or edit the persisted craft cart:
            //   dotnet run -- cart [list]
            //   dotnet run -- cart add <itemId> <qty>
            //   dotnet run -- cart clear
            if (args.Length > 0 && string.Equals(args[0], "cart", StringComparison.OrdinalIgnoreCase))
            {
                await RunCartCliAsync(app.Services, args.Skip(1).ToList());
                return;
            }

            // Build a craft plan (shopping list + crafting steps) for a cart:
            //   dotnet run -- plan <itemId:qty> [<itemId:qty> ...]
            if (args.Length > 0 && string.Equals(args[0], "plan", StringComparison.OrdinalIgnoreCase))
            {
                await RunPlanCliAsync(app.Services, args.Skip(1));
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

        private static async Task RunPlanCliAsync(IServiceProvider services, IEnumerable<string> specs)
        {
            var cart = new Dictionary<int, int>();
            foreach (var s in specs)
            {
                var parts = s.Split(':');
                if (parts.Length == 2 && int.TryParse(parts[0], out var id) && int.TryParse(parts[1], out var qty) && qty > 0)
                    cart[id] = qty;
            }
            if (cart.Count == 0)
            {
                Console.WriteLine("Usage: dotnet run -- plan <itemId:qty> [<itemId:qty> ...]");
                return;
            }

            using var scope = services.CreateScope();
            var profit = scope.ServiceProvider.GetRequiredService<ProfitService>();
            var account = scope.ServiceProvider.GetRequiredService<AccountService>();
            var owned = await account.GetOwnedAsync();
            var plan = await profit.BuildPlanAsync(cart, PricingMode.InstantBuy, owned);

            Console.WriteLine();
            Console.WriteLine("=== SHOPPING LIST ===");
            foreach (var group in plan.Shopping.GroupBy(s => s.Source))
            {
                Console.WriteLine($"-- {group.Key} --");
                foreach (var line in group)
                    Console.WriteLine($"  {line.Quantity,7:N0}x {line.Name,-42} {Coin.Format(line.TotalPrice)}");
            }
            Console.WriteLine($"Total buy cost: {Coin.Format(plan.TotalBuyCost)}");

            if (plan.OwnedUsed.Count > 0)
            {
                Console.WriteLine();
                Console.WriteLine($"=== FROM YOUR STOCK ({plan.OwnedTypesApplied:N0} type(s), saved {Coin.Format(plan.OwnedSavings)}) ===");
                foreach (var line in plan.OwnedUsed)
                    Console.WriteLine($"  {line.Quantity,7:N0}x {line.Name,-42} {Coin.Format(line.Value)}");
            }

            Console.WriteLine();
            Console.WriteLine("=== CRAFTING STEPS (deepest first) ===");
            foreach (var group in plan.Steps.GroupBy(s => s.Discipline))
            {
                Console.WriteLine($"-- {(string.IsNullOrEmpty(group.Key) ? "(any)" : group.Key)} --");
                foreach (var step in group)
                    Console.WriteLine($"  Craft {step.Quantity,6:N0}x {step.Name,-42} ({step.Crafts} craft(s))");
            }
        }

        private static async Task RunAccountCliAsync(IServiceProvider services, string what)
        {
            using var scope = services.CreateScope();
            var account = scope.ServiceProvider.GetRequiredService<AccountService>();

            if (string.Equals(what, "refresh", StringComparison.OrdinalIgnoreCase))
            {
                var lastPhase = "";
                var progress = new Progress<SyncProgress>(p =>
                {
                    if (p.Phase != lastPhase) { lastPhase = p.Phase; Console.WriteLine(); }
                    Console.Write($"\r{p.Phase}   ");
                });
                try
                {
                    var n = await account.RefreshOwnedAsync(progress);
                    Console.WriteLine();
                    Console.WriteLine($"Owned stock refreshed: {n:N0} item types.");
                }
                catch (Exception ex)
                {
                    Console.WriteLine();
                    Console.WriteLine($"Refresh failed: {ex.Message}");
                }
                return;
            }

            var status = await account.GetStatusAsync();
            Console.WriteLine($"API key saved : {(status.HasKey ? "yes" : "no")}");
            if (status.AccountName is not null) Console.WriteLine($"Account       : {status.AccountName}");
            Console.WriteLine($"Owned types   : {status.OwnedItemTypes:N0}");
            Console.WriteLine($"Owned updated : {(status.OwnedUpdatedUtc is { } ts ? ts.ToLocalTime().ToString("g") : "never")}");
            if (!status.HasKey)
                Console.WriteLine("Set a key on the Settings page, then run: dotnet run -- account refresh");
        }

        private static async Task RunProvisionerCliAsync(IServiceProvider services)
        {
            using var scope = services.CreateScope();
            var svc = scope.ServiceProvider.GetRequiredService<ProvisionerService>();
            var view = await svc.GetAsync();

            foreach (var v in view.Vendors)
            {
                Console.WriteLine();
                Console.WriteLine($"=== {v.Name} — {v.Zone}  ({v.Waypoint} {v.WaypointChatLink}) ===");
                foreach (var t in v.Tabs)
                {
                    Console.WriteLine($"  [{t.Tab}]  CHEAPEST: {(t.ItemName ?? "— none tradable")}  " +
                        $"{(t.UnitPrice is int u ? Coin.Format(u) : "")}  (×7 = {(t.WeeklyCost is int w ? Coin.Format(w) : "-")})");
                    foreach (var o in t.Options)
                        Console.WriteLine($"        {(o.UnitPrice is int up ? Coin.Format(up) : "NO PRICE"),-13} " +
                            $"{o.ItemName}{(o.ItemId is null ? "   <-- NOT IN CATALOG" : "")}");
                }
                Console.WriteLine($"  Weekly total (cheapest×7 across tabs): {(v.WeeklyTotal is int wt ? Coin.Format(wt) : "-")}");
            }

            Console.WriteLine();
            Console.WriteLine("=== DAILY ROTATION MATERIALS (cheapest first) ===");
            foreach (var r in view.DailyMaterials)
                Console.WriteLine($"{(r.CostPerToken is int c ? Coin.Format(c) : "?"),-14} " +
                    $"{(r.UnitPrice is int u ? Coin.Format(u) : "-"),-12} {r.Quantity,6:N0}  {r.ItemName}" +
                    $"{(r.ItemId is null ? "   <-- NOT IN CATALOG" : "")}");
        }

        private static async Task RunCartCliAsync(IServiceProvider services, List<string> args)
        {
            using var scope = services.CreateScope();
            var cart = scope.ServiceProvider.GetRequiredService<CartService>();
            await cart.LoadAsync(); // the hosted CartLoader only runs for the web host, not the CLI

            var cmd = args.Count > 0 ? args[0].ToLowerInvariant() : "list";
            if (cmd == "add" && args.Count >= 3 && int.TryParse(args[1], out var id) && int.TryParse(args[2], out var qty))
            {
                var dbf = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                await using var db = await dbf.CreateDbContextAsync();
                var item = await db.Items.AsNoTracking().FirstOrDefaultAsync(i => i.Id == id);
                cart.Add(id, item?.Name ?? $"Item {id}", item?.IconUrl, qty);
                await cart.SaveAsync();
                Console.WriteLine($"Added {qty}x {item?.Name ?? id.ToString()}.");
            }
            else if (cmd == "clear")
            {
                cart.Clear();
                await cart.SaveAsync();
                Console.WriteLine("Cart cleared.");
            }

            foreach (var i in cart.Items)
                Console.WriteLine($"  {i.Quantity,7:N0}x {i.Name}  (#{i.ItemId})");
            Console.WriteLine($"Cart: {cart.Count} item type(s).");
        }

        private static async Task RunRequiresCliAsync(IServiceProvider services, int itemId)
        {
            using var scope = services.CreateScope();
            var profit = scope.ServiceProvider.GetRequiredService<ProfitService>();

            var set = await profit.GetItemsRequiringAsync(itemId);
            var rows = await profit.GetRowsAsync(PricingMode.InstantBuy);
            var matching = rows.Where(r => set.Contains(r.Id)).OrderByDescending(r => r.Profit).ToList();

            Console.WriteLine($"#{itemId}: {set.Count:N0} items have it in their craft tree; {matching.Count:N0} are in the profit grid.");
            Console.WriteLine($"{"Profit",-13} Name");
            foreach (var r in matching.Take(20))
                Console.WriteLine($"{Coin.Format(r.Profit),-13} {r.Name}");
        }

        private static async Task RunListingsCliAsync(IServiceProvider services, double markPct)
        {
            using var scope = services.CreateScope();
            var account = scope.ServiceProvider.GetRequiredService<AccountService>();

            IReadOnlyList<GW2Stonks.Models.SellListingRow> rows;
            try { rows = await account.GetSellListingsAsync(); }
            catch (Exception ex) { Console.WriteLine($"Could not read listings: {ex.Message}"); return; }

            if (rows.Count == 0) { Console.WriteLine("No active sell listings."); return; }

            static bool IsRelist(GW2Stonks.Models.SellListingRow r, double pct) =>
                r.SoldPerDay is int s && s > 0 && r.UnitsAhead > pct / 100.0 * s;

            Console.WriteLine($"My sell listings ({rows.Count}) — relist when cheaper-ahead > {markPct:0.#}% of daily sales:");
            Console.WriteLine($"{"Relist",-7} {"Qty",6} {"My price",-13} {"Ahead",7} {"Sold/d",7} {"Ahead%",8}  Item");
            foreach (var r in rows)
            {
                var pct = r.AheadVsDailyPct is double a ? a.ToString("0.#") + "%" : "—";
                var sold = r.SoldPerDay is int s ? s.ToString("N0") : "—";
                Console.WriteLine($"{(IsRelist(r, markPct) ? "RELIST" : "ok"),-7} {r.Quantity,6:N0} {Coin.Format(r.Price),-13} " +
                    $"{r.UnitsAhead,7:N0} {sold,7} {pct,8}  {r.Name}");
            }
            Console.WriteLine($"Flagged to relist: {rows.Count(r => IsRelist(r, markPct))}");
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
