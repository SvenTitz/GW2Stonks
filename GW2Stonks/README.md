# GW2Stonks

A local tool for finding **profitable crafting opportunities in Guild Wars 2**. It pulls item, recipe, and trading-post data from the GW2 API, stores it locally, and works out whether it's cheaper to **craft** an item (recursively, down through its sub-components) or **buy** it off the trading post — then helps you plan a day's worth of crafting.

> **Status:** early setup. Not hosted anywhere — runs only on your machine.

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
├─ Data/             EF Core: database context, entities, migrations
├─ Gw2Api/           Typed clients + models for the GW2 API
├─ Services/         Catalog sync, price refresh, craft-vs-buy solver, planner
├─ Components/Pages/ Sync · Browse · Planner · Settings (Radzen UI)
└─ Program.cs        Dependency-injection wiring
```

The **craft-vs-buy solver** walks each recipe tree and, for every item, takes the cheaper of *buying it* or *crafting it from its ingredients* — with memoisation so shared sub-components aren't recomputed.

## Roadmap

- [x] **Phase 0 — Foundation:** added Radzen + EF Core (Pomelo) packages, wired DI, created the entity model + `AppDbContext`, generated the `InitialCreate` migration, built the `gw2stonks` schema in MariaDB, and confirmed the app boots and serves with Radzen wired in. ✅
- [ ] **Phase 1 — Data:** GW2 API client, bulk catalog sync, on-demand price refresh, a sync page with progress.
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
