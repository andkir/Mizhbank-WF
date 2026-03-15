using System.Drawing;
using System.Drawing.Imaging;

namespace MizhbankNotifier;

public static class AppIcon
{
    public static Icon Create()
    {
        var bmp = CreateBitmap(32);
        var handle = bmp.GetHicon();
        return Icon.FromHandle(handle);
    }

    public static void GenerateIcoFile(string path)
    {
        // ICO format: header + directory entry + PNG data for each size
        int[] sizes = [16, 32, 48, 256];
        var pngDataList = new List<byte[]>();

        foreach (var size in sizes)
        {
            using var bmp = CreateBitmap(size);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngDataList.Add(ms.ToArray());
        }

        using var output = File.Create(path);
        using var writer = new BinaryWriter(output);

        // ICO header
        writer.Write((short)0);          // reserved
        writer.Write((short)1);          // type: icon
        writer.Write((short)sizes.Length); // image count

        // Calculate data offset: header(6) + entries(16 each)
        var dataOffset = 6 + sizes.Length * 16;

        // Directory entries
        for (int i = 0; i < sizes.Length; i++)
        {
            var size = sizes[i];
            writer.Write((byte)(size < 256 ? size : 0)); // width
            writer.Write((byte)(size < 256 ? size : 0)); // height
            writer.Write((byte)0);   // color palette
            writer.Write((byte)0);   // reserved
            writer.Write((short)1);  // color planes
            writer.Write((short)32); // bits per pixel
            writer.Write(pngDataList[i].Length); // data size
            writer.Write(dataOffset);            // data offset
            dataOffset += pngDataList[i].Length;
        }

        // Image data
        foreach (var pngData in pngDataList)
        {
            writer.Write(pngData);
        }
    }

    private static Bitmap CreateBitmap(int size)
    {
        var bmp = new Bitmap(size, size);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;
        g.Clear(Color.FromArgb(0, 149, 136)); // teal
        var fontSize = size * 0.56f;
        using var font = new Font("Arial", fontSize, FontStyle.Bold);
        using var brush = new SolidBrush(Color.White);
        using var sf = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        g.DrawString("₴", font, brush, new RectangleF(0, 0, size, size), sf);
        return bmp;
    }
}
