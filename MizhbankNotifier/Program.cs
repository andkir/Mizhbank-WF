using MizhbankNotifier;
using MizhbankNotifier.Services;

// Generate icon file if requested (used during setup)
if (args.Length > 0 && args[0] == "--generate-icon")
{
    var iconPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "app.ico");
    AppIcon.GenerateIcoFile(Path.GetFullPath(iconPath));
    Console.WriteLine($"Icon generated at {Path.GetFullPath(iconPath)}");
    return;
}

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<MizhbankService>(client =>
{
    client.DefaultRequestHeaders.UserAgent.ParseAdd(
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
});
builder.Services.AddSingleton<RateStore>();
builder.Services.AddSingleton<TrayIconService>();
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();

// Clean up tray icon on exit
host.Services.GetRequiredService<TrayIconService>().Dispose();
