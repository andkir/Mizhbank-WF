using MizhbankNotifier;
using MizhbankNotifier.Configuration;
using MizhbankNotifier.Services;

// Single-instance guard
using var mutex = new Mutex(true, "Global\\MizhbankNotifier_SingleInstance", out bool createdNew);
using var showSignal = new EventWaitHandle(false, EventResetMode.AutoReset,
    "Global\\MizhbankNotifier_ShowWindow");
if (!createdNew)
{
    showSignal.Set();   // tell the running instance to show its window
    return;
}

// Generate icon file if requested (used during setup)
if (args.Length > 0 && args[0] == "--generate-icon")
{
    var iconPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "app.ico");
    AppIcon.GenerateIcoFile(Path.GetFullPath(iconPath));
    Console.WriteLine($"Icon generated at {Path.GetFullPath(iconPath)}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<ApiUrls>(builder.Configuration.GetSection(ApiUrls.SectionName));

builder.Services.AddHttpClient<MizhbankService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddHttpClient<BlackMarketService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddHttpClient<EurMizhbankService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddHttpClient<EurBlackMarketService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddHttpClient<ForexService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddHttpClient<FinanceUaService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddHttpClient<BankRateService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/122.0.0.0 Safari/537.36");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "uk-UA,uk;q=0.9,en-US;q=0.8,en;q=0.7");
    client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Encoding", "gzip, deflate, br");
    client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua",
        "\"Chromium\";v=\"122\", \"Not(A:Brand\";v=\"24\", \"Google Chrome\";v=\"122\"");
    client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-mobile", "?0");
    client.DefaultRequestHeaders.TryAddWithoutValidation("sec-ch-ua-platform", "\"Windows\"");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    UseCookies          = true,
    CookieContainer     = new System.Net.CookieContainer(),
    AllowAutoRedirect   = true,
    AutomaticDecompression = System.Net.DecompressionMethods.All,
});
builder.Services.AddSingleton<RateStore>();
builder.Services.AddSingleton<BlackMarketRateStore>();
builder.Services.AddSingleton<EurBlackMarketRateStore>();
builder.Services.AddSingleton<EurRateStore>();
builder.Services.AddSingleton<ForexRateStore>();
builder.Services.AddSingleton<FinanceUaRateStore>();
builder.Services.AddSingleton<BankRateStore>();
builder.Services.AddSingleton<BankRateHistoryService>();
builder.Services.AddSingleton<DailyRateDb>();
builder.Services.AddSingleton<TrayIconService>();
builder.Services.AddSingleton<BankRateWebViewFetcher>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

// Clean up tray icon on exit
host.Services.GetRequiredService<TrayIconService>().Dispose();
