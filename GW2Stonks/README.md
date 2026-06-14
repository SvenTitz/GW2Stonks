# GW2Stonks

A local tool for finding **profitable crafting opportunities in Guild Wars 2**. It pulls item, recipe, and trading-post data from the GW2 API, stores it locally, and works out whether it's cheaper to **craft** an item (recursively, down through its sub-components) or **buy** it off the trading post — then helps you plan a day's worth of crafting.

> **Status:** Phases 0–1 done — catalog + prices sync into MariaDB and a browsable Items grid works. Not hosted anywhere — runs only on your machine.

---

## What it does (planned)

1. **Sync** the GW2 catalog (items + recipes) and trading-post prices into a local database.
2. **Browse for profit** — pick a category of items and see each one's profit if you craft it and sell it. For every ingredient the tool decides on its own whether it's cheaper to *craft the sub-component* or *buy it from the trading post*.
3. **Plan your day** — pick the items you want to craft, hand the tool your account API key, and get back a **shopping list** and the **ordered crafting steps** to make them.

## Tech stack

| Area      | Choice                                            |
|-----------|---------------------------------------------------|
| App       | .NET 10 Blazor Web App (Interactive Server)       |
| UI        | Radzen Blazor components                           |
| Database  | MariaDB 10.11                                      |
| Data access | EF Core 9 via the Pomelo MySQL/MariaDB provider (runs on the .NET 10 runtime; Pomelo has no 10.x line yet) |
| Data source | [GW2 API v2](https://wiki.guildwars2.com/wiki/API:2) |

## Key design decisions

- **Pricing is configurable** — the tool shows **both** a *conservative* number (buy materials instantly, sell instantly) and an *optimistic* one (place buy/sell orders and wait). All sell figures account for the trading post's ~15% tax.
- **Your stock counts** — the shopping list subtracts materials you already own (material storage, bank, inventories) using your account API key.
- **Catalog synced in bulk, prices on demand** — items and recipes are downloaded once; the fast-changing trading-post prices are refreshed when you ask.
- **Daily-limited materials** (time-gated ascended mats) are **deferred** to a later phase.

## How it's organised

```
GW2Stonks/
├─ Data/             EF Core: AppDbContext, entities, migrations
├─ Gw2Api/           Typed GW2 API client, DTOs, rate-limiting handler, options
├─ Services/         Catalog/price sync, background refresh, shared queries (solver/planner later)
├─ Models/           View models (e.g. the items grid row)
├─ Util/             Helpers (e.g. gold/silver/copper formatting)
├─ Components/Pages/ Home · Items  (Browse · Planner · Settings later)
└─ Program.cs        DI wiring + headless `sync`/`query` CLI commands
```

The **craft-vs-buy solver** walks each recipe tree and, for every item, takes the cheaper of *buying it* or *crafting it from its ingredients* — with memoisation so shared sub-components aren't recomputed.

## Roadmap

- [x] **Phase 0 — Foundation:** added Radzen + EF Core (Pomelo) packages, wired DI, created the entity model + `AppDbContext`, generated the `InitialCreate` migration, built the `gw2stonks` schema in MariaDB, and confirmed the app boots and serves with Radzen wired in. ✅
- [x] **Phase 1 — Data:** GW2 API client (batched, rate-limited, resilient), bulk catalog sync (items + recipes) and a 5-minute background price refresh, plus an **Items** page — a server-side paged/filtered/sorted Radzen grid with icons and buy/sell prices in gold/silver/copper. Verified end-to-end against the live API: 73,923 items · 13,139 recipes · 27,940 prices. ✅
- [ ] **Phase 2 — Profit browser:** the craft-vs-buy solver and a category-filtered grid of items with profit columns.
- [ ] **Phase 3 — Planner:** shopping list + ordered crafting steps, using your account key to subtract owned stock.
- [ ] **Phase 4 — Later:** daily-cooldown awareness and polish.

## Getting started

You'll need:

- The [.NET 10 SDK](https://dotnet.microsoft.com/download)
- A local **MariaDB** server (developed against 10.11)
- The EF Core CLI: `dotnet tool install --global dotnet-ef`
- A **GW2 API key** ([create one here](https://account.arena.net/applications)) — only needed later, for the planner's account features

Secrets (the database connection string and, later, the GW2 API key) are kept in **.NET user-secrets**, not in source control. A passwordless placeholder connection string lives in `appsettings.json` purely so EF migrations can be generated offline — user-secrets overrides it at runtime.

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
  dotnet run -- sync all       # items, then recipes, then prices
  dotnet run -- sync items     # or just one set: items | recipes | prices
  dotnet run -- query Wood     # diagnostic: list items whose name contains "Wood"
  ```

Tunable settings live under the `Gw2` section of `appsettings.json` (`PriceRefreshMinutes`, `MaxConcurrency`, `RequestsPerSecond`, `BatchSize`). A client-side rate limiter keeps requests well under the GW2 API's ~600/min limit.
