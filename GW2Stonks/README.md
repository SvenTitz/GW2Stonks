# GW2Stonks

A local tool for finding **profitable crafting opportunities in Guild Wars 2**. It pulls item, recipe, and trading-post data from the GW2 API, stores it locally, and works out whether it's cheaper to **craft** an item (recursively, down through its sub-components) or **buy** it off the trading post — then helps you plan a day's worth of crafting.

> **Status:** Phases 0–5 done — catalog + prices sync into MariaDB, a browsable Items grid, a craft-vs-buy **profit browser** with craft-tree breakdown, cached trading-post **liquidity** (sold/day, sell-through), a **daily craft planner** (filter → fill a cart → get a consolidated shopping list by source and crafting steps by discipline), **account integration** (a saved GW2 API key lets the planner subtract materials you already own), a **My listings** page that flags your trading-post sell listings that have been undercut, and a **Provisioner tokens** page (cheapest items/materials to trade for tokens at each Faction Provisioner). Not hosted anywhere — runs only on your machine.

---

## What it does

1. **Sync** the GW2 catalog (items + recipes) and trading-post prices into a local database.
2. **Browse for profit** — pick a category of items and see each one's profit if you craft it and sell it. For every ingredient the tool decides on its own whether it's cheaper to *craft the sub-component* or *buy it from the trading post*.
3. **Plan your day** — pick the items you want to craft and get back a **shopping list** (grouped by source) and the **ordered crafting steps** (grouped by discipline) to make them. Save your account API key once and the planner subtracts materials you already own from the shopping list.
4. **Watch your sell listings** — see everything you currently have for sale, how much cheaper supply sits ahead of each listing, and get a **relist** flag when that supply exceeds a settable % of the item's daily sales (so you know which listings have been undercut and won't sell).

## Tech stack

| Area      | Choice                                            |
|-----------|---------------------------------------------------|
| App       | .NET 10 Blazor Web App (Interactive Server)       |
| UI        | Radzen Blazor components (Material theme, light/dark toggle in the header) |
| Database  | MariaDB 10.11                                      |
| Data access | EF Core 9 via the Pomelo MySQL/MariaDB provider (runs on the .NET 10 runtime; Pomelo has no 10.x line yet) |
| Data sources | [GW2 API v2](https://wiki.guildwars2.com/wiki/API:2) (items, recipes, prices) · [datawars2.ie](https://datawars2.ie) (daily sales volume — the GW2 API has none) |

## Key design decisions

- **Pricing** — the crafted output is always valued at the **sell-listing** price (you list it and wait), minus the trading post's ~15% tax. A toggle controls only **how materials are bought**: *Instant buy* (pay the sell-listing) vs *Buy orders* (place bids and wait for the lower price).
- **Your stock counts** — once you save a GW2 API key (Settings page), a manual **refresh** reads your material storage, bank, shared inventory and character bags, and the planner subtracts that stock from the shopping list. The key is stored in the local DB and survives restarts; counts are cached and only re-read when you press refresh. Cart items themselves are always crafted in full — only their ingredients are covered by stock.
- **Catalog synced in bulk, prices on demand** — items and recipes are downloaded once; the fast-changing trading-post prices are refreshed when you ask.
- **Daily-limited materials** (time-gated ascended mats like Lump of Mithrillium) are **never crafted, always bought** — a recipe that directly needs one is disallowed, so its output (e.g. Deldrimor Steel Ingot) becomes buy-only too. Items that merely use a *bought* ascended mat can still be crafted.
- **Liquidity matters** — daily sales volume (from datawars2.ie) drives a sold/day, sell-through, and "relist buffer" view so you only craft things that actually sell.

## How it's organised

```
GW2Stonks/
├─ Data/             EF Core: AppDbContext, entities (incl. AppSetting, OwnedItem, CartEntry), migrations
├─ Gw2Api/           Typed GW2 API client (catalog + authenticated account endpoints), DTOs, rate-limiting handler, options
├─ Datawars2/        Typed datawars2.ie client (daily sales-volume source)
├─ Services/         Sync, background refresh, craft-cost solver, profit/volume/cart/account/provisioner services + BOM planner
├─ Models/           View models (items grid row, profit row, craft-tree node, cart, craft plan, sell-listing row, provisioner)
├─ Util/             Helpers (e.g. gold/silver/copper formatting)
├─ Components/Pages/ Home · Items · Craft profit · Planner · My listings · Provisioner tokens · Settings
└─ Program.cs        DI wiring + headless `sync`/`query`/`profit`/`volume`/`plan`/`cart`/`account`/`listings`/`requires`/`provisioner` CLI commands
```

The **craft-vs-buy solver** walks each recipe tree and, for every item, takes the cheaper of *buying it* or *crafting it from its ingredients* — with memoisation so shared sub-components aren't recomputed.

## Roadmap

- [x] **Phase 0 — Foundation:** added Radzen + EF Core (Pomelo) packages, wired DI, created the entity model + `AppDbContext`, generated the `InitialCreate` migration, built the `gw2stonks` schema in MariaDB, and confirmed the app boots and serves with Radzen wired in. ✅
- [x] **Phase 1 — Data:** GW2 API client (batched, rate-limited, resilient), bulk catalog sync (items + recipes) and a 5-minute background price refresh, plus an **Items** page — a server-side paged/filtered/sorted Radzen grid with icons and buy/sell prices in gold/silver/copper. Verified end-to-end against the live API: 73,923 items · 13,139 recipes · 27,940 prices. ✅
- [x] **Phase 2 — Profit browser:** recursive, memoised craft-vs-buy solver (`min(buy, craft)` down the recipe tree, cycle-safe, Instant-buy/Buy-orders material pricing, output sold at the listing price minus 15% tax, with a seed list of vendor-bought material prices), a **Craft profit** page with discipline/type/name filters sorted by profit, and an expandable per-item **craft-tree breakdown** showing each sub-component's buy-vs-craft call. ✅
- [x] **Phase 2.5 — Liquidity:** since the GW2 API has no sales-volume endpoint, cache daily trading-post volume from **datawars2.ie** (batched, ~30 requests) into MariaDB and refresh every 12h. The profit grid gains **sold/day** + **sell-through days** columns and a **"min sold/day"** filter, so high-margin-but-illiquid items (that never actually sell) drop out. ✅
- [x] **Phase 3 — Planner:** richer profit filters (min profit in copper / margin / sold-day, max sell-through, **relist buffer**, multi-select disciplines & types) in a tidy filter card; a per-item **relist buffer** stat (how many times you can relist before the 5% fee eats the profit); a **"Fill cart"** that adds `round(sold/day × %)` of each filtered item; an editable craft **cart**; and a **Planner** page that explodes the cart into a consolidated **shopping list grouped by source** (Trading Post / Vendor) and **crafting steps grouped by discipline** (deepest-first, batch-rounded), with a **cost / revenue / profit** summary, **final-vs-intermediate** labelling, and disciplines chosen to **minimise character switching**. Every planner item name is a **GW2 wiki link** with a one-click **copy** button (paste into the in-game trading-post search). Time-gated daily mats (Lump of Mithrillium → Deldrimor Steel Ingot, …) are forced buy-only. Profit filters persist across navigation, there's a **Profit/day** stat (craft-% × sold/day × profit), per-row **+/− cart** controls, and the planner's "to craft" grid shows per-item **cost/revenue/profit** with a totals footer. Item names are wiki links **everywhere** (Items, Profit, Planner), and there's a **dark mode** (default) toggle. *(Subtracting owned stock via the account API key is the next step.)* ✅
- [x] **Phase 4 — Account stock:** a **Settings** page where you save a GW2 API key (persisted in the local DB, validated against `/v2/tokeninfo`) and press **Refresh owned materials** to sum your material storage, bank, shared inventory and every character's bags into a cached `OwnedItem` table. The planner then **subtracts owned stock** from the shopping list (cart items are still crafted in full; only ingredients/intermediates are covered), shows a **"From your stock"** list of exactly which materials were used (quantity + value saved), and reports the total **owned-stock savings**. A "Subtract items I own" toggle on the planner turns it off for comparison. CLI: `account status` / `account refresh`. ✅
- [x] **Phase 5 — My listings:** a **My listings** page that reads your active trading-post sell listings (needs the `tradingpost` key permission), and for each one counts the **cheaper supply ahead** of it from the public order book. It compares that to the item's **daily sales** (fetching+caching volume on demand for items outside the craftable set, e.g. raw mats) and flags a listing to **relist** when the cheaper supply exceeds a **settable %** of daily sales (default 50%). Items that don't sell (0/day) aren't flagged — relisting can't help a dead market. CLI: `listings [pct]`. The profit page's **Fill cart** button now also **subtracts quantities you already have listed for sale** (e.g. fill wants 5 but you already list 2 → adds 3), so you only craft the shortfall. ✅
- [x] **Requires-ingredient filter:** the Craft profit page has a searchable **"Requires ingredient"** dropdown — pick any material and the grid narrows to items whose **recursive craft tree contains it** (computed by BFS over a reverse-ingredient index built into the cached profit snapshot). CLI: `requires <itemId>`. ✅
- [x] **Persistent cart:** the craft cart is written through to a `CartEntry` table on every change and reloaded at startup, so it survives restarts. CLI: `cart`. ✅
- [x] **Provisioner tokens:** a **Provisioner tokens** page for the three Heart of Maguuma Faction Provisioners (Natomi/Verdant Brink, Rakatin/Auric Basin, Supply Assistant/Tangled Depths). Each vendor's faction tabs hold the items you can trade for a token (1 each, 7/week per tab); the page prices all of them **live** — costing each from the **average of its 7 cheapest sell listings** (the real cost of buying 7, walking the order book) — and shows the **cheapest item to buy per tab**, its **buy-7 cost**, and a per-vendor weekly total — plus a copy-paste item name and a **waypoint chat-link** button. The daily-rotation crafting materials are listed with prices below. Vendor/tab/item data is compiled from the wiki in `ProvisionerData` (not in the GW2 API). CLI: `provisioner`. ✅
- [ ] **Phase 6 — Later:** polish (e.g. per-NPC vendor sources, owned-stock auto-refresh, persist the relist %, plural grammar).

## Getting started

You'll need:

- The [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A local **MariaDB** server (developed against 10.11)
- The EF Core CLI: `dotnet tool install --global dotnet-ef`
- A **GW2 API key** ([create one here](https://account.arena.net/applications)) — optional; needed for the account features. Give it **account** + **inventories** (planner's "subtract items I own"; add **characters** to also count character bags) and **tradingpost** (the "My listings" page). You enter and save it on the **Settings** page in the app, not in config.

The database **connection string** is kept in **.NET user-secrets**, not in source control. A passwordless placeholder connection string lives in `appsettings.json` purely so EF migrations can be generated offline — user-secrets overrides it at runtime. The **GW2 API key** is entered in the app and stored in the local database (it survives restarts and stays on your machine).

**First-time setup:**

```powershell
# 1. Point the app at your MariaDB instance (adjust user/password to yours).
dotnet user-secrets set "ConnectionStrings:GW2Stonks" "Server=localhost;Port=3306;Database=gw2stonks;User ID=root;Password=YOUR_PASSWORD"

# 2. Create the database and schema.
dotnet ef database update

# 3. Run the app.
dotnet run
```

The schema is managed by EF Core migrations (in `Migrations/`). `dotnet ef database update` creates the `gw2stonks` database if it doesn't exist.

## Syncing data

The database starts empty. Populate it either way:

- **In the app:** open the **Items** page and click **Sync catalog** (pulls ~74k items + ~13k recipes, takes ~1–2 min), then **Refresh prices**. After that, prices refresh automatically every 5 minutes in the background.
- **From a terminal** (handy for first-time load or a scheduled task):

  ```powershell
  dotnet run -- sync all          # items, then recipes, then prices
  dotnet run -- sync items        # or just one set: items | recipes | prices
  dotnet run -- query Wood        # diagnostic: list items whose name contains "Wood"
  dotnet run -- profit top        # top profitable craft items (add 'orders' for buy-order pricing)
  dotnet run -- profit 19684      # craft-cost breakdown + profit for one item id
  dotnet run -- volume refresh    # pull daily sales volume from datawars2.ie into the DB
  dotnet run -- volume 19684      # show cached sold/day, supply, sell-through for one item
  dotnet run -- plan 45882:5 19684:10   # shopping list + crafting steps for a cart (id:qty …)
  dotnet run -- requires 19684    # profit-grid items whose craft tree uses item 19684 (Mithril Ingot)
  dotnet run -- cart              # show the persisted cart (also: cart add <id> <qty> | cart clear)
  dotnet run -- provisioner       # cheapest items/materials to trade for Provisioner Tokens, priced
  dotnet run -- account status    # show whether a key is saved + owned-stock summary
  dotnet run -- account refresh   # re-read owned materials from the GW2 API (uses the saved key)
  dotnet run -- listings 50       # your sell listings + relist flags (cheaper-ahead > 50% of daily sales)
  ```

  Volume also refreshes automatically every 12 hours in the background, and there's a **Refresh volume** button on the Craft profit page. Tunables live under the `Datawars2` section of `appsettings.json`. The owned-materials cache (`account refresh`) is **manual only** — refresh it after a crafting session so the planner's stock counts stay current.

Tunable settings live under the `Gw2` section of `appsettings.json` (`PriceRefreshMinutes`, `MaxConcurrency`, `RequestsPerSecond`, `BatchSize`). A client-side rate limiter keeps requests well under the GW2 API's ~600/min limit.
