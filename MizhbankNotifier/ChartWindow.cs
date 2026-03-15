using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using MizhbankNotifier.Models;
using MizhbankNotifier.Services;

namespace MizhbankNotifier;

public class ChartWindow : Form
{
    private readonly RateStore _rateStore;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly PictureBox _canvas;

    private int _hoveredIndex = -1;
    private RectangleF _plotRect;

    // Palette
    private static readonly Color BgColor      = Color.FromArgb(15, 20, 40);
    private static readonly Color GridColor     = Color.FromArgb(40, 55, 90);
    private static readonly Color BuyColor      = Color.FromArgb(0, 210, 160);
    private static readonly Color SellColor     = Color.FromArgb(255, 90, 100);
    private static readonly Color LabelColor    = Color.FromArgb(160, 180, 220);
    private static readonly Color TitleColor    = Color.White;
    private static readonly Color TooltipBg     = Color.FromArgb(250, 28, 36, 64);
    private static readonly Color TooltipBorder = Color.FromArgb(70, 95, 150);
    private static readonly Color CrosshairColor = Color.FromArgb(80, 120, 180, 220);

    public ChartWindow(RateStore rateStore)
    {
        _rateStore = rateStore;

        Text = "Міжбанк USD/UAH";
        Size = new Size(960, 540);
        MinimumSize = new Size(600, 380);
        BackColor = BgColor;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Create();

        _canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = BgColor };
        _canvas.Paint += OnCanvasPaint;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeave += (_, _) => { _hoveredIndex = -1; _canvas.Invalidate(); };
        Controls.Add(_canvas);

        Resize += (_, _) => _canvas.Invalidate();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _refreshTimer.Tick += (_, _) => _canvas.Invalidate();
        _refreshTimer.Start();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _refreshTimer.Stop();
        base.OnFormClosed(e);
    }

    // ── Mouse ──────────────────────────────────────────────────────────────────

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        var rates = _rateStore.Rates;
        if (rates.Count < 2 || _plotRect.Width == 0) return;

        // Find nearest data point by X
        var relX = e.X - _plotRect.Left;
        var idx = (int)Math.Round(relX / _plotRect.Width * (rates.Count - 1));
        idx = Math.Clamp(idx, 0, rates.Count - 1);

        if (idx != _hoveredIndex)
        {
            _hoveredIndex = idx;
            _canvas.Invalidate();
        }
    }

    // ── Drawing ────────────────────────────────────────────────────────────────

    private void OnCanvasPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var rates = _rateStore.Rates;
        var w = _canvas.Width;
        var h = _canvas.Height;

        DrawBackground(g, w, h);

        if (rates.Count < 2)
        {
            DrawCenteredText(g, "Завантаження даних...", w, h);
            return;
        }

        const int ml = 68, mr = 24, mt = 64, mb = 52;
        _plotRect = new RectangleF(ml, mt, w - ml - mr, h - mt - mb);

        var (minY, maxY) = YRange(rates);
        var sellPts = ComputePoints(rates, r => (double)r.Sell, _plotRect, minY, maxY);
        var buyPts  = ComputePoints(rates, r => (double)r.Buy,  _plotRect, minY, maxY);

        DrawHeader(g, rates, w);
        DrawGrid(g, rates, _plotRect, minY, maxY);
        DrawAxes(g, rates, _plotRect);
        DrawFill(g, sellPts, _plotRect, SellColor);
        DrawFill(g, buyPts,  _plotRect, BuyColor);
        DrawLine(g, sellPts, SellColor);
        DrawLine(g, buyPts,  BuyColor);
        DrawDots(g, sellPts, SellColor);
        DrawDots(g, buyPts,  BuyColor);

        if (_hoveredIndex >= 0)
        {
            DrawCrosshair(g, sellPts[_hoveredIndex].X, _plotRect);
            DrawHoverDot(g, sellPts[_hoveredIndex], SellColor);
            DrawHoverDot(g, buyPts[_hoveredIndex],  BuyColor);
            DrawTooltip(g, rates[_hoveredIndex], sellPts[_hoveredIndex],
                buyPts[_hoveredIndex], w, _plotRect);
        }

        DrawLegend(g, w, mt);
    }

    private static void DrawBackground(Graphics g, int w, int h)
    {
        g.Clear(BgColor);
        using var gb = new LinearGradientBrush(
            new Rectangle(0, 0, w, 60),
            Color.FromArgb(30, BuyColor), Color.Transparent,
            LinearGradientMode.Vertical);
        g.FillRectangle(gb, 0, 0, w, 60);
    }

    private static void DrawHeader(Graphics g, IReadOnlyList<InterbankRate> rates, int w)
    {
        var latest = rates[^1];
        using var titleFont  = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var valueFont  = new Font("Segoe UI", 11f);
        using var smallFont  = new Font("Segoe UI", 8.5f);
        using var titleBrush = new SolidBrush(TitleColor);
        using var timeBrush  = new SolidBrush(LabelColor);
        using var buyBrush   = new SolidBrush(BuyColor);
        using var sellBrush  = new SolidBrush(SellColor);

        g.DrawString("Міжбанк USD/UAH", titleFont, titleBrush, 68, 12);
        g.DrawString($"оновлено {latest.Time:HH:mm  dd.MM.yyyy}", smallFont, timeBrush, 68, 36);

        float bx = w - 240f;
        g.DrawString("Купівля", smallFont, buyBrush, bx, 14);
        g.DrawString($"{latest.Buy:F3}", valueFont, buyBrush, bx, 30);

        float sx = w - 130f;
        g.DrawString("Продаж", smallFont, sellBrush, sx, 14);
        g.DrawString($"{latest.Sell:F3}", valueFont, sellBrush, sx, 30);
    }

    private static void DrawGrid(Graphics g, IReadOnlyList<InterbankRate> rates,
        RectangleF r, double minY, double maxY)
    {
        using var gridPen    = new Pen(GridColor) { DashStyle = DashStyle.Dot };
        using var labelBrush = new SolidBrush(LabelColor);
        using var labelFont  = new Font("Segoe UI", 8f);

        for (int i = 0; i <= 4; i++)
        {
            var t = i / 4.0;
            var val = maxY - t * (maxY - minY);
            var y = r.Top + (float)t * r.Height;
            g.DrawLine(gridPen, r.Left, y, r.Right, y);
            g.DrawString($"{val:F3}", labelFont, labelBrush, 2, y - 8);
        }
    }

    private static void DrawAxes(Graphics g, IReadOnlyList<InterbankRate> rates, RectangleF r)
    {
        using var axisPen    = new Pen(GridColor, 1.5f);
        using var labelBrush = new SolidBrush(LabelColor);
        using var labelFont  = new Font("Segoe UI", 8f);

        g.DrawLine(axisPen, r.Left, r.Bottom, r.Right, r.Bottom);

        int ticks = Math.Min(8, rates.Count);
        float step = (rates.Count - 1f) / (ticks - 1);
        for (int i = 0; i < ticks; i++)
        {
            int idx = (int)Math.Round(i * step);
            var x = r.Left + (float)idx / (rates.Count - 1) * r.Width;
            g.DrawLine(axisPen, x, r.Bottom, x, r.Bottom + 4);
            var label = rates[idx].Time.ToString("HH:mm");
            var sz = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, labelBrush, x - sz.Width / 2, r.Bottom + 6);
        }
    }

    private static void DrawFill(Graphics g, PointF[] pts, RectangleF r, Color color)
    {
        var poly = new PointF[pts.Length + 2];
        pts.CopyTo(poly, 0);
        poly[^2] = new PointF(pts[^1].X, r.Bottom);
        poly[^1] = new PointF(pts[0].X,  r.Bottom);
        using var brush = new LinearGradientBrush(
            new PointF(0, r.Top), new PointF(0, r.Bottom),
            Color.FromArgb(40, color), Color.FromArgb(0, color));
        g.FillPolygon(brush, poly);
    }

    private static void DrawLine(Graphics g, PointF[] pts, Color color)
    {
        using var pen = new Pen(color, 2f) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }

    private static void DrawDots(Graphics g, PointF[] pts, Color color)
    {
        using var fill = new SolidBrush(color);
        using var ring = new Pen(BgColor, 1.5f);
        foreach (var p in pts)
        {
            g.FillEllipse(fill, p.X - 3, p.Y - 3, 6, 6);
            g.DrawEllipse(ring, p.X - 3, p.Y - 3, 6, 6);
        }
    }

    private static void DrawHoverDot(Graphics g, PointF p, Color color)
    {
        // Outer glow ring
        using var glow = new Pen(Color.FromArgb(50, color), 6f);
        g.DrawEllipse(glow, p.X - 8, p.Y - 8, 16, 16);
        // White ring
        using var ring = new Pen(Color.FromArgb(200, 255, 255, 255), 2f);
        g.DrawEllipse(ring, p.X - 6, p.Y - 6, 12, 12);
        // Filled center
        using var fill = new SolidBrush(color);
        g.FillEllipse(fill, p.X - 5, p.Y - 5, 10, 10);
    }

    private static void DrawCrosshair(Graphics g, float x, RectangleF r)
    {
        using var pen = new Pen(CrosshairColor, 1f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, x, r.Top, x, r.Bottom);
    }

    private static void DrawTooltip(Graphics g, InterbankRate rate,
        PointF sellPt, PointF buyPt, int canvasWidth, RectangleF r)
    {
        using var labelFont = new Font("Segoe UI", 9f);
        using var valueFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(220, 225, 240));

        const float pad = 10f, dotR = 5f, lineH = 22f;
        const float cardW = 148f, cardH = lineH * 2 + pad * 2;

        // Position to the right of cursor, flip left if near edge
        float x = sellPt.X + 14;
        if (x + cardW > r.Right) x = sellPt.X - cardW - 14;

        // Vertically center between the two dots
        float midY = (sellPt.Y + buyPt.Y) / 2f;
        float y = midY - cardH / 2f;
        y = Math.Clamp(y, r.Top, r.Bottom - cardH);

        var card = new RectangleF(x, y, cardW, cardH);

        // Card background
        using var bgBrush = new SolidBrush(TooltipBg);
        FillRoundedRect(g, bgBrush, card, 7);
        using var borderPen = new Pen(TooltipBorder, 1f);
        DrawRoundedRect(g, borderPen, card, 7);

        // Row: Продаж
        float rowY = y + pad;
        using var sellBrush = new SolidBrush(SellColor);
        g.FillEllipse(sellBrush, x + pad, rowY + (lineH - dotR * 2) / 2f, dotR * 2, dotR * 2);
        g.DrawString("Продаж: ", labelFont, textBrush, x + pad + dotR * 2 + 5, rowY + 3);
        var sellLabel = $"{rate.Sell:F3}";
        var sellLabelX = card.Right - pad - g.MeasureString(sellLabel, valueFont).Width;
        g.DrawString(sellLabel, valueFont, sellBrush, sellLabelX, rowY + 3);

        // Row: Купівля
        rowY += lineH;
        using var buyBrush = new SolidBrush(BuyColor);
        g.FillEllipse(buyBrush, x + pad, rowY + (lineH - dotR * 2) / 2f, dotR * 2, dotR * 2);
        g.DrawString("Купівля: ", labelFont, textBrush, x + pad + dotR * 2 + 5, rowY + 3);
        var buyLabel = $"{rate.Buy:F3}";
        var buyLabelX = card.Right - pad - g.MeasureString(buyLabel, valueFont).Width;
        g.DrawString(buyLabel, valueFont, buyBrush, buyLabelX, rowY + 3);

        // Time label below card
        using var timeFont  = new Font("Segoe UI", 7.5f);
        using var timeBrush = new SolidBrush(Color.FromArgb(130, 155, 200));
        var timeStr = rate.Time.ToString("HH:mm");
        var tsz = g.MeasureString(timeStr, timeFont);
        g.DrawString(timeStr, timeFont, timeBrush,
            card.Left + (card.Width - tsz.Width) / 2, card.Bottom + 3);
    }

    private static void DrawLegend(Graphics g, int w, int mt)
    {
        using var font     = new Font("Segoe UI", 9f);
        using var buyBrush  = new SolidBrush(BuyColor);
        using var sellBrush = new SolidBrush(SellColor);
        using var buyPen    = new Pen(BuyColor, 2.5f);
        using var sellPen   = new Pen(SellColor, 2.5f);

        float lx = w - 210f, ly = (float)mt + 8;
        g.DrawLine(buyPen,  lx, ly + 6, lx + 20, ly + 6);
        g.DrawString("Купівля", font, buyBrush, lx + 26, ly);
        g.DrawLine(sellPen, lx + 95, ly + 6, lx + 115, ly + 6);
        g.DrawString("Продаж", font, sellBrush, lx + 121, ly);
    }

    private static void DrawCenteredText(Graphics g, string text, int w, int h)
    {
        using var font  = new Font("Segoe UI", 14f);
        using var brush = new SolidBrush(LabelColor);
        var sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (w - sz.Width) / 2, (h - sz.Height) / 2);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static PointF[] ComputePoints(IReadOnlyList<InterbankRate> rates,
        Func<InterbankRate, double> selector, RectangleF r, double minY, double maxY)
    {
        var pts = new PointF[rates.Count];
        for (int i = 0; i < rates.Count; i++)
        {
            var x = r.Left + (float)i / (rates.Count - 1) * r.Width;
            var t = (selector(rates[i]) - minY) / (maxY - minY);
            var y = r.Bottom - (float)t * r.Height;
            pts[i] = new PointF(x, y);
        }
        return pts;
    }

    private static (double min, double max) YRange(IReadOnlyList<InterbankRate> rates)
    {
        var allVals = rates.SelectMany(r => new[] { (double)r.Buy, (double)r.Sell }).ToList();
        var min = allVals.Min();
        var max = allVals.Max();
        var pad = (max - min) * 0.15;
        if (pad < 0.01) pad = 0.05;
        return (min - pad, max + pad);
    }

    private static void FillRoundedRect(Graphics g, Brush brush, RectangleF r, float radius)
    {
        using var path = RoundedRectPath(r, radius);
        g.FillPath(brush, path);
    }

    private static void DrawRoundedRect(Graphics g, Pen pen, RectangleF r, float radius)
    {
        using var path = RoundedRectPath(r, radius);
        g.DrawPath(pen, path);
    }

    private static GraphicsPath RoundedRectPath(RectangleF r, float rad)
    {
        var path = new GraphicsPath();
        path.AddArc(r.X, r.Y, rad * 2, rad * 2, 180, 90);
        path.AddArc(r.Right - rad * 2, r.Y, rad * 2, rad * 2, 270, 90);
        path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0, 90);
        path.AddArc(r.X, r.Bottom - rad * 2, rad * 2, rad * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}
