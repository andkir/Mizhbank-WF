# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build Commands

```bash
# Build the main app (Release)
cd MizhbankNotifier && dotnet build -c Release

# Build entire solution
dotnet build mizhbank.sln -c Release
```

There are no tests. The only binary that matters is `MizhbankNotifier`.

## Architecture Overview

**MizhbankNotifier** is a Windows tray app (.NET 10 WinForms, `OutputType=WinExe`) that tracks USD/UAH exchange rates from [kurs.com.ua](https://kurs.com.ua) and renders them in a custom GDI+ chart window.

### Threading Model

Two threads matter:

- **Main thread** — runs `host.Run()` from `Program.cs`. The .NET Generic Host + `Worker` BackgroundService live here.
- **STA UI thread** — started by `TrayIconService`. Runs `Application.Run()` with a hidden 1×1 `Form` (`_hiddenHost`) as the message pump. All WinForms controls (tray icon, `ChartWindow`, `WebView2`) must be created and accessed on this thread.

`TrayIconService` posts work to the STA thread via `_hiddenHost.BeginInvoke(...)`.

### Data Flow

```
Worker (BackgroundService)
  ├── MizhbankService          → kurs.com.ua AJAX JSON  → KursChartParser → RateStore
  ├── BlackMarketService       → kurs.com.ua AJAX JSON  → KursChartParser → BlackMarketRateStore
  └── BankRateWebViewFetcher   → WebView2 same-origin fetch (Cloudflare bypass)
                                → BankRateParser (HtmlAgilityPack) → BankRateStore
```

Each store exposes a `DataChanged` event. `ChartWindow` subscribes and calls `_canvas.Invalidate()` via `BeginInvoke` on data change.

`Worker` also fires toast notifications via `Microsoft.Toolkit.Uwp.Notifications` on interbank rate updates. **Do not call `ToastNotificationManagerCompat.Uninstall()` on app exit** — it deregisters the app from Windows and prevents future notifications.

### Cloudflare Bypass

The kurs.com.ua HTML page and bank rate endpoints are Cloudflare-protected. `BankRateWebViewFetcher` loads the page in an embedded WebView2, then uses `ExecuteScriptAsync` to run a `fetch()` call from the page's origin to get the data without triggering CF challenges. Static assets (`/storage/images/`) are not protected and can be fetched with plain `HttpClient`.

### ChartWindow Rendering

`ChartWindow` has three tabs, all rendered in `OnCanvasPaint` via `PictureBox.Paint`:

- **Tab 0 – Міжбанк**: Dual-session line chart. Previous session shown in muted colors. A gap indicator (`/ /`) separates sessions. Hover shows crosshair + tooltip.
- **Tab 1 – Чорний ринок**: Same chart structure as Tab 0.
- **Tab 2 – В банках**: Sortable 4-column table (Банк | Оновлено | Купівля | Продаж). USD/EUR toggle. Bank icons loaded from embedded resources via `BankIconStore`. Rows with a non-`HH:mm` `UpdatedAt` string are considered stale and rendered dimmed+italic.

### Bank Icons

Icons are PNG files embedded as `EmbeddedResource` in `Resources/OrgIcons/`. `BankIconStore.Get(bankName)` matches by checking if the bank name contains any key in `NameMap` (case-insensitive substring). **Order matters** — more specific entries (e.g. `"Полтав"`) must appear before substring-overlapping ones (e.g. `"А-Банк"`, which matches inside `"Полтава-Банк"`).

To add a new bank icon: download the PNG to `Resources/OrgIcons/{key}.png`, the `.csproj` picks up all PNGs there via a glob `EmbeddedResource` include, then add the `{substring} → {key}` mapping in `BankIconStore.NameMap`.

### BankRateStore & History

`BankRateStore` holds separate `List<BankRate>` for USD and EUR. `BankRateHistoryService` persists daily snapshots to `%LOCALAPPDATA%\MizhbankNotifier\history\`.

### DI Registration Notes

- `OrgIconStore` was removed. Bank icons now load from embedded resources at startup in `BankIconStore` (static class, no DI).
- `BankRateWebViewFetcher` must be resolved on the STA thread — it is instantiated inside `TrayIconService` and passed to `Worker` after STA setup.
- Named `HttpClient` `"orgicons"` registration in `Program.cs` is a leftover and can be removed.
