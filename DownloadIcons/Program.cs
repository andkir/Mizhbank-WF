using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

var outDir = @"C:\ai-apps\mizhbank\MizhbankNotifier\Resources\OrgIcons";

var direct = new Dictionary<string, string>
{
    { "radabank",    "https://www.radabank.com.ua/favicon-32x32.png" },
    { "poltavabank", "https://poltavabank.com/wp-content/uploads/2020/01/cropped-ico-32x32.png" },
};

using var http = new HttpClient();
http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
http.Timeout = TimeSpan.FromSeconds(15);

foreach (var (key, url) in direct)
{
    var dest = Path.Combine(outDir, $"{key}.png");
    try
    {
        var bytes = await http.GetByteArrayAsync(url);
        if (bytes.Length > 100 && bytes[0] == 0x89 && bytes[1] == 0x50) // PNG magic
        {
            await File.WriteAllBytesAsync(dest, bytes);
            Console.WriteLine($"  [{key}] saved {bytes.Length} bytes");
        }
        else
        {
            Console.WriteLine($"  [{key}] unexpected format (length={bytes.Length}, first bytes={bytes[0]:X2}{bytes[1]:X2})");
        }
    }
    catch (Exception ex) { Console.WriteLine($"  [{key}] FAILED: {ex.Message}"); }
}

Console.WriteLine($"\nTotal icons: {Directory.GetFiles(outDir, "*.png").Length}");
