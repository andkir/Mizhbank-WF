using System.Drawing;
using System.Reflection;

namespace MizhbankNotifier.Services;

/// <summary>
/// Loads bank logo PNGs from embedded resources and caches them.
/// Match is done by substring of the bank name (case-insensitive).
/// </summary>
internal static class BankIconStore
{
    // (substring to match in bank name, resource key = filename without .png)
    private static readonly (string Sub, string Key)[] NameMap =
    [
        ("Приват",     "privatbank"),
        ("PrivatBank", "privatbank"),
        ("Ощад",       "oschadbank"),
        ("Укрексім",   "ukreximbank"),
        ("UkrExim",    "ukreximbank"),
        ("ПУМБ",       "pumb"),
        ("PUMB",       "pumb"),
        ("Райффайзен", "raiffeisen"),
        ("Raiffeisen", "raiffeisen"),
        ("Укрсиббанк", "ukrsibbank"),
        ("UkrSib",     "ukrsibbank"),
        ("ОТП",        "otpbank"),
        ("OTP",        "otpbank"),
        ("Укргаз",     "ukrgasbank"),
        ("Агріколь",   "creditagricole"),
        ("Agricole",   "creditagricole"),
        ("Правекс",    "pravex"),
        ("Pravex",     "pravex"),
        ("Sense",      "sensebank"),
        ("Сенс",       "sensebank"),
        ("Ідея",       "ideabank"),
        ("Idea",       "ideabank"),
        ("Полтав",     "poltavabank"),
        ("Poltava",    "poltavabank"),
        ("А-Банк",     "abank"),
        ("A-Bank",     "abank"),
        ("Abank",      "abank"),
        ("Південний",  "pivdenny"),
        ("Індустріал", "industrialbank"),
        ("ТАС",        "tascombank"),
        ("Таском",     "tascombank"),
        ("Tascom",     "tascombank"),
        ("Кредо",      "kredobank"),
        ("Kredo",      "kredobank"),
        ("Глобус",     "globusbank"),
        ("Globus",     "globusbank"),
        ("Моно",       "monobank"),
        ("Mono",       "monobank"),
        ("ізі",        "izibank"),
        ("Izi",        "izibank"),
        ("Рада",       "radabank"),
        ("Rada",       "radabank"),
    ];

    private static readonly Dictionary<string, Image?> Cache = new();

    public static Image? Get(string bankName)
    {
        foreach (var (sub, key) in NameMap)
        {
            if (bankName.Contains(sub, StringComparison.OrdinalIgnoreCase))
                return LoadKey(key);
        }
        return null;
    }

    private static Image? LoadKey(string key)
    {
        if (Cache.TryGetValue(key, out var cached)) return cached;

        var resourceName = $"MizhbankNotifier.Resources.OrgIcons.{key}.png";
        try
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
            if (stream is null) { Cache[key] = null; return null; }

            // Copy to MemoryStream so the original stream can be closed.
            // Then create a Bitmap copy so GDI+ is fully stream-independent.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            using var tmp = Image.FromStream(ms);
            var img = new Bitmap(tmp); // stream-independent copy
            Cache[key] = img;
            return img;
        }
        catch
        {
            Cache[key] = null;
            return null;
        }
    }
}
