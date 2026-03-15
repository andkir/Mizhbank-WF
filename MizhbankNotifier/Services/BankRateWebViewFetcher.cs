using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using MizhbankNotifier.Models;

namespace MizhbankNotifier.Services;

/// <summary>
/// Fetches bank cash rates via an embedded Chromium browser (WebView2),
/// bypassing Cloudflare by running a same-origin fetch() inside the page.
/// </summary>
public class BankRateWebViewFetcher : IDisposable
{
    private readonly TrayIconService             _tray;
    private readonly ILogger<BankRateWebViewFetcher> _logger;

    private WebView2? _wv;
    private bool      _ready;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private const string WarmUrl = "https://kurs.com.ua/";
    private const string AjaxUrl =
        "https://kurs.com.ua/ajax/organizations/cash" +
        "?currency_from={0}&currency_to=undef" +
        "&organizations=MAJOR&show_optimal=1&current_page=money.index";

    public BankRateWebViewFetcher(TrayIconService tray, ILogger<BankRateWebViewFetcher> logger)
    {
        _tray   = tray;
        _logger = logger;
    }

    // ── Init ─────────────────────────────────────────────────────────────────

    public async Task InitAsync(CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        _tray.PostToUI(async () =>
        {
            try
            {
                var udDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MizhbankNotifier", "webview2");
                Directory.CreateDirectory(udDir);

                var env = await CoreWebView2Environment.CreateAsync(null, udDir);

                _wv = new WebView2 { Dock = DockStyle.Fill };
                _tray.HiddenHost.Controls.Add(_wv);
                await _wv.EnsureCoreWebView2Async(env);

                _wv.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                _wv.CoreWebView2.Settings.AreDevToolsEnabled            = false;
                _wv.CoreWebView2.Settings.IsStatusBarEnabled            = false;
                _wv.CoreWebView2.NewWindowRequested += (_, e) => e.Handled = true;

                // Navigate to kurs.com.ua so CF solves the JS challenge and sets cookies.
                _logger.LogInformation("BankRateWebViewFetcher: warming up…");
                var warmTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                EventHandler<CoreWebView2NavigationCompletedEventArgs> onWarm = null!;
                onWarm = (_, e) =>
                {
                    _wv.NavigationCompleted -= onWarm;
                    _logger.LogInformation(
                        "BankRateWebViewFetcher: warm-up done (success={S})", e.IsSuccess);
                    warmTcs.TrySetResult();
                };
                _wv.NavigationCompleted += onWarm;
                _wv.CoreWebView2.Navigate(WarmUrl);

                // Wait up to 30 s for CF challenge + page load.
                await warmTcs.Task.WaitAsync(TimeSpan.FromSeconds(30));

                _ready = true;
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "BankRateWebViewFetcher: init failed");
                tcs.TrySetException(ex);
            }
        });

        try   { await tcs.Task.WaitAsync(TimeSpan.FromSeconds(45), ct); }
        catch (Exception ex)
              { _logger.LogError(ex, "BankRateWebViewFetcher: InitAsync timed out or failed"); }
    }

    // ── Fetch ─────────────────────────────────────────────────────────────────

    /// <summary>Fetch bank rates for <paramref name="currency"/> (1=USD, 2=EUR).</summary>
    public async Task<List<BankRate>?> FetchRatesAsync(int currency, CancellationToken ct = default)
    {
        if (!_ready || _wv == null)
        {
            _logger.LogWarning("BankRateWebViewFetcher: not ready, skipping fetch");
            return null;
        }

        // Serialize so USD and EUR fetches don't race over the same WebView2 instance.
        await _lock.WaitAsync(ct);
        try
        {
            var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
            var url = string.Format(AjaxUrl, currency);

            _tray.PostToUI(async () =>
            {
                try
                {
                    // Bridge: page posts the JSON back to us via postMessage.
                    EventHandler<CoreWebView2WebMessageReceivedEventArgs> onMsg = null!;
                    onMsg = (_, e) =>
                    {
                        _wv.CoreWebView2.WebMessageReceived -= onMsg;
                        tcs.TrySetResult(e.TryGetWebMessageAsString());
                    };
                    _wv.CoreWebView2.WebMessageReceived += onMsg;

                    // Run a same-origin fetch() inside the browser; result → postMessage.
                    var script = $$"""
                        fetch('{{url}}', {
                            headers: {
                                'Accept':           'application/json, text/javascript, */*; q=0.01',
                                'X-Requested-With': 'XMLHttpRequest',
                                'Referer':          'https://kurs.com.ua/money.index'
                            }
                        })
                        .then(r  => r.ok ? r.text() : Promise.reject('HTTP ' + r.status))
                        .then(txt => window.chrome.webview.postMessage(txt))
                        .catch(err => window.chrome.webview.postMessage('__ERR__:' + err));
                        """;
                    await _wv.ExecuteScriptAsync(script);
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            });

            ct.Register(() => tcs.TrySetCanceled());

            string? json;
            try   { json = await tcs.Task.WaitAsync(TimeSpan.FromSeconds(20), ct); }
            catch (TimeoutException)
            {
                _logger.LogWarning("BankRateWebViewFetcher: fetch timed out (currency={C})", currency);
                return null;
            }

            if (json is null || json.StartsWith("__ERR__:"))
            {
                _logger.LogWarning("BankRateWebViewFetcher: fetch error — {Msg}", json);
                return null;
            }

            return BankRateParser.Parse(json, _logger);
        }
        finally { _lock.Release(); }
    }

    public void Dispose() => _wv?.Dispose();
}
