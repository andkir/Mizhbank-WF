# MizhbankNotifier

A Windows system tray application that tracks UAH exchange rates in real time and displays them in a custom GDI+ chart window.

## Features

- **Interbank rates** (USD/UAH, EUR/UAH) — from kurs.com.ua and finance.ua
- **Black market rates** (USD/UAH, EUR/UAH) — from kurs.com.ua
- **EUR/USD Forex** — from kurs.com.ua
- **Bank cash rates** — buy/sell prices from Ukrainian retail banks, with bank logos
- **Windows toast notifications** on interbank rate changes (before 17:00)
- **Rate history** — SQLite database of daily closing rates; bank rates persisted per-day to disk
- **Single-instance** — launching a second copy brings the existing window to focus

## Requirements

- Windows 10 (build 17763) or later
- .NET 10 runtime
- WebView2 runtime (for Cloudflare-bypass bank rate fetching)

## Build

```bash
cd MizhbankNotifier
dotnet build -c Release
```

The output binary is `MizhbankNotifier.exe` (`OutputType=WinExe`).

## Usage

Run `MizhbankNotifier.exe`. A tray icon appears; click it to open the chart window.

The window has three tabs:

| Tab | Content |
|-----|---------|
| **Міжбанк** | Dual-session line chart of interbank USD/UAH (and EUR/UAH). Previous session shown in muted colors with a `/ /` gap separator. Hover for crosshair + tooltip. |
| **Чорний ринок** | Same chart structure for black-market USD/UAH and EUR/UAH. |
| **В банках** | Sortable table — Банк / Оновлено / Купівля / Продаж. Toggle between USD and EUR. Bank logos shown inline. Stale rows (non-`HH:mm` timestamp) are dimmed and italicized. |

Right-click the tray icon for a manual refresh or to exit.

## Data Sources

| Source | Endpoint | Data |
|--------|----------|------|
| kurs.com.ua | AJAX JSON chart API | Interbank USD/EUR, black market USD/EUR, EUR/USD forex |
| kurs.com.ua | Bank-rates AJAX (Cloudflare-protected) | Retail bank buy/sell rates |
| finance.ua | Daily interbank JSON | Secondary interbank USD feed |

The bank-rate endpoint is Cloudflare-protected. The app loads it inside an embedded WebView2, then uses `ExecuteScriptAsync` to issue a same-origin `fetch()` from the page's context, bypassing CF challenges.

## Poll Intervals

| Data | Interval |
|------|----------|
| Interbank, black market, forex | Every 5 minutes |
| Bank cash rates | Every 10 minutes |

## Adding a Bank Icon

1. Place a PNG in `MizhbankNotifier/Resources/OrgIcons/{key}.png`  
   (the `.csproj` picks up all PNGs there via a glob `EmbeddedResource` include).
2. Add a `("{substring}", "{key}")` entry to `NameMap` in [BankIconStore.cs](MizhbankNotifier/Services/BankIconStore.cs).  
   **Order matters** — put more specific substrings before any that would match inside them (e.g. `"Полтав"` before `"А-Банк"`).

## Architecture

```
Program.cs
└── Worker (BackgroundService)
    ├── MizhbankService        → kurs.com.ua JSON → KursChartParser → RateStore
    ├── EurMizhbankService     → kurs.com.ua JSON → KursChartParser → EurRateStore
    ├── BlackMarketService     → kurs.com.ua JSON → KursChartParser → BlackMarketRateStore
    ├── EurBlackMarketService  → kurs.com.ua JSON → KursChartParser → EurBlackMarketRateStore
    ├── ForexService           → kurs.com.ua JSON → KursChartParser → ForexRateStore
    ├── FinanceUaService       → finance.ua JSON  → FinanceUaRateStore
    └── BankRateWebViewFetcher → WebView2 fetch   → BankRateParser  → BankRateStore

TrayIconService (STA UI thread)
└── ChartWindow (GDI+ PictureBox, 3 tabs)
```

Two threads:
- **Main thread** — .NET Generic Host + `Worker` BackgroundService.
- **STA UI thread** — started by `TrayIconService`, runs `Application.Run()`. All WinForms controls live here; cross-thread calls use `BeginInvoke`.

## Data Persistence

- **`%LOCALAPPDATA%\MizhbankNotifier\rates.db`** — SQLite database of daily closing rates and intraday chart data.
- **`%LOCALAPPDATA%\MizhbankNotifier\history\`** — daily JSON snapshots of bank cash rates.

## Dependencies

| Package | Purpose |
|---------|---------|
| `Microsoft.Extensions.Hosting` | Generic Host / DI / BackgroundService |
| `Microsoft.Extensions.Http` | Typed `HttpClient` factory |
| `Microsoft.Web.WebView2` | Embedded Chromium for Cloudflare bypass |
| `HtmlAgilityPack` | Bank-rate HTML parsing |
| `Microsoft.Data.Sqlite` | Daily rate history database |
| `Microsoft.Toolkit.Uwp.Notifications` | Windows toast notifications |
