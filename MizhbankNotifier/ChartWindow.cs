using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using MizhbankNotifier.Models;
using MizhbankNotifier.Services;

namespace MizhbankNotifier;

public class ChartWindow : Form
{
    private readonly RateStore            _interbankStore;
    private readonly BlackMarketRateStore _blackMarketStore;
    private readonly BankRateStore        _bankRateStore;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly PictureBox _canvas;

    private int _activeTab    = 0;
    private int _hoveredIndex = -1;  // index into _combinedRates / _combinedSellPts
    private RectangleF   _plotRect;
    private RectangleF[] _tabRects = new RectangleF[3];

    // ── Bank table state ───────────────────────────────────────────────────────
    private int  _bankCurrency    = 1;    // 1 = USD, 2 = EUR
    private int  _bankSortCol     = 1;    // 0=name, 1=updated, 2=buy, 3=sell
    private bool _bankSortAsc     = false; // descending = newest first
    private int  _bankScrollOffset = 0;
    private int  _bankHoveredRow   = -1;
    private RectangleF[] _bankRowRects  = [];
    private RectangleF   _btnUsd, _btnEur;
    private RectangleF[] _bankColHeaders = new RectangleF[4];

    // Kept across frames for hover hit-testing
    private PointF[]?                   _combinedSellPts;
    private PointF[]?                   _combinedBuyPts;
    private IReadOnlyList<InterbankRate>? _combinedRates;
    private int                          _prevCount;   // how many points belong to prev session

    // ── Palette ────────────────────────────────────────────────────────────────
    private static readonly Color BgColor        = Color.FromArgb(15, 20, 40);
    private static readonly Color GridColor       = Color.FromArgb(40, 55, 90);
    private static readonly Color BuyColor        = Color.FromArgb(0, 210, 160);
    private static readonly Color SellColor       = Color.FromArgb(255, 90, 100);
    private static readonly Color PrevBuyColor    = Color.FromArgb(70, 130, 115);
    private static readonly Color PrevSellColor   = Color.FromArgb(150, 80, 85);
    private static readonly Color LabelColor      = Color.FromArgb(160, 180, 220);
    private static readonly Color TitleColor      = Color.White;
    private static readonly Color TooltipBg       = Color.FromArgb(250, 28, 36, 64);
    private static readonly Color TooltipBorder   = Color.FromArgb(70, 95, 150);
    private static readonly Color CrosshairColor  = Color.FromArgb(80, 120, 180, 220);
    private static readonly Color TabActiveBg     = Color.FromArgb(40, 55, 100);
    private static readonly Color TabBorder       = Color.FromArgb(55, 75, 130);
    private static readonly Color GapLineColor    = Color.FromArgb(55, 100, 140, 180);

    private static readonly string[] TabLabels = { "Міжбанк", "Чорний ринок", "В банках" };
    private const int TabBarH = 44;
    private const float GapFraction = 0.05f;  // 5% of plot width for the session gap

    public ChartWindow(RateStore interbankStore, BlackMarketRateStore blackMarketStore,
        BankRateStore bankRateStore)
    {
        _interbankStore   = interbankStore;
        _blackMarketStore = blackMarketStore;
        _bankRateStore    = bankRateStore;

        Text = "Курси USD/UAH";
        Size = new Size(960, 540);
        MinimumSize = new Size(600, 400);
        BackColor = BgColor;
        StartPosition = FormStartPosition.CenterScreen;
        Icon = AppIcon.Create();

        _canvas = new PictureBox { Dock = DockStyle.Fill, BackColor = BgColor };
        _canvas.Paint      += OnCanvasPaint;
        _canvas.MouseMove  += OnMouseMove;
        _canvas.MouseClick += OnMouseClick;
        _canvas.MouseLeave += (_, _) => { _hoveredIndex = -1; _bankHoveredRow = -1; _canvas.Invalidate(); };
        _canvas.MouseWheel += OnMouseWheel;
        Controls.Add(_canvas);

        Resize += (_, _) => _canvas.Invalidate();

        _refreshTimer = new System.Windows.Forms.Timer { Interval = 30_000 };
        _refreshTimer.Tick += (_, _) => _canvas.Invalidate();
        _refreshTimer.Start();

        // Repaint immediately whenever any store receives new data (fires on background thread).
        _interbankStore.DataChanged   += OnDataChanged;
        _blackMarketStore.DataChanged += OnDataChanged;
        _bankRateStore.DataChanged    += OnDataChanged;
    }

    private void OnDataChanged()
    {
        if (_canvas.IsHandleCreated)
            _canvas.BeginInvoke(_canvas.Invalidate);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _interbankStore.DataChanged   -= OnDataChanged;
        _blackMarketStore.DataChanged -= OnDataChanged;
        _bankRateStore.DataChanged    -= OnDataChanged;
        _refreshTimer.Stop();
        base.OnFormClosed(e);
    }

    private ChartData ActiveData =>
        _activeTab == 0 ? _interbankStore.Data : _blackMarketStore.Data;

    // ── Mouse ──────────────────────────────────────────────────────────────────

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < _tabRects.Length; i++)
        {
            if (_tabRects[i].Contains(e.X, e.Y) && _activeTab != i)
            {
                _activeTab    = i;
                _hoveredIndex = -1;
                _canvas.Invalidate();
                return;
            }
        }

        if (_activeTab == 2)
        {
            if (_btnUsd.Contains(e.X, e.Y) && _bankCurrency != 1)
            {
                _bankCurrency = 1; _bankScrollOffset = 0; _canvas.Invalidate(); return;
            }
            if (_btnEur.Contains(e.X, e.Y) && _bankCurrency != 2)
            {
                _bankCurrency = 2; _bankScrollOffset = 0; _canvas.Invalidate(); return;
            }
            for (int c = 0; c < _bankColHeaders.Length; c++)
            {
                if (_bankColHeaders[c].Contains(e.X, e.Y))
                {
                    if (_bankSortCol == c) _bankSortAsc = !_bankSortAsc;
                    else { _bankSortCol = c; _bankSortAsc = c == 0; }
                    _bankScrollOffset = 0;
                    _canvas.Invalidate();
                    return;
                }
            }
        }
    }

    private void OnMouseWheel(object? sender, MouseEventArgs e)
    {
        if (_activeTab != 2) return;
        var rates = GetBankRates();
        int maxScroll = Math.Max(0, rates.Count - VisibleBankRows());
        _bankScrollOffset = Math.Clamp(_bankScrollOffset - Math.Sign(e.Delta), 0, maxScroll);
        _canvas.Invalidate();
    }

    private int VisibleBankRows()
    {
        int tableTop = TabBarH + 56 + 36;  // header + controls + col header
        int rowH = 36;
        return Math.Max(1, (_canvas.Height - tableTop - 8) / rowH);
    }

    private List<BankRate> GetBankRates()
    {
        var src = _bankCurrency == 1 ? _bankRateStore.Usd : _bankRateStore.Eur;
        var list = src.ToList();
        list = _bankSortCol switch
        {
            0 => _bankSortAsc ? list.OrderBy(r => r.Name).ToList()      : list.OrderByDescending(r => r.Name).ToList(),
            1 => _bankSortAsc ? list.OrderBy(r => r.UpdatedAt).ToList() : list.OrderByDescending(r => r.UpdatedAt).ToList(),
            2 => _bankSortAsc ? list.OrderBy(r => r.Buy).ToList()       : list.OrderByDescending(r => r.Buy).ToList(),
            _ => _bankSortAsc ? list.OrderBy(r => r.Sell).ToList()      : list.OrderByDescending(r => r.Sell).ToList(),
        };
        return list;
    }

    private void OnMouseMove(object? sender, MouseEventArgs e)
    {
        bool overTab = _tabRects.Any(r => r.Contains(e.X, e.Y));
        bool overBtn = _activeTab == 2 && (_btnUsd.Contains(e.X, e.Y) || _btnEur.Contains(e.X, e.Y)
            || _bankColHeaders.Any(h => h.Contains(e.X, e.Y)));
        _canvas.Cursor = (overTab || overBtn) ? Cursors.Hand : Cursors.Default;

        if (_activeTab == 2)
        {
            int hovered = -1;
            for (int i = 0; i < _bankRowRects.Length; i++)
                if (_bankRowRects[i].Contains(e.X, e.Y)) { hovered = i; break; }
            if (hovered != _bankHoveredRow) { _bankHoveredRow = hovered; _canvas.Invalidate(); }
            return;
        }

        var pts = _combinedSellPts;
        if (pts is null || pts.Length == 0 || _plotRect.Width == 0) return;
        if (e.Y < TabBarH) { if (_hoveredIndex != -1) { _hoveredIndex = -1; _canvas.Invalidate(); } return; }

        // Find nearest point by X
        int best = 0;
        float bestDist = float.MaxValue;
        for (int i = 0; i < pts.Length; i++)
        {
            float d = Math.Abs(pts[i].X - e.X);
            if (d < bestDist) { bestDist = d; best = i; }
        }

        if (best != _hoveredIndex)
        {
            _hoveredIndex = best;
            _canvas.Invalidate();
        }
    }

    // ── Drawing ────────────────────────────────────────────────────────────────

    private void OnCanvasPaint(object? sender, PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode     = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

        var w = _canvas.Width;
        var h = _canvas.Height;

        DrawBackground(g, w, h);
        DrawTabs(g, w);

        if (_activeTab == 2)
        {
            DrawBankTable(g, w, h);
            return;
        }

        var data = ActiveData;

        var prev = data.PreviousSession;
        var curr = data.CurrentSession;

        if (prev.Count < 2 && curr.Count < 2)
        {
            DrawCenteredText(g, "Завантаження даних...", w, h);
            return;
        }

        const int ml = 68, mr = 24, mb = 52;
        int mt = TabBarH + 64;
        _plotRect = new RectangleF(ml, mt, w - ml - mr, h - mt - mb);

        // ── Compute session layouts ────────────────────────────────────────────
        // Each session gets a proportional slice of the plot width with a gap between them.
        float gapW = (prev.Count >= 2 && curr.Count >= 2) ? _plotRect.Width * GapFraction : 0f;
        float avail = _plotRect.Width - gapW;

        float prevStartX, prevW, currStartX, currW;
        if (prev.Count >= 2 && curr.Count >= 2)
        {
            float total = prev.Count + curr.Count;
            prevW = avail * prev.Count / total;
            currW = avail - prevW;
        }
        else
        {
            prevW = prev.Count >= 2 ? avail : 0f;
            currW = curr.Count >= 2 ? avail : 0f;
        }
        prevStartX = _plotRect.Left;
        currStartX = prev.Count >= 2 ? prevStartX + prevW + gapW : _plotRect.Left;

        // ── Y range from all available data ───────────────────────────────────
        var allRates = prev.Concat(curr).ToList();
        var (minY, maxY) = YRange(allRates);

        // ── Compute points ────────────────────────────────────────────────────
        PointF[] prevSellPts = [], prevBuyPts = [], currSellPts = [], currBuyPts = [];
        if (prev.Count >= 2)
        {
            prevSellPts = ComputePoints(prev, r => (double)r.Sell, prevStartX, prevW, _plotRect, minY, maxY);
            prevBuyPts  = ComputePoints(prev, r => (double)r.Buy,  prevStartX, prevW, _plotRect, minY, maxY);
        }
        if (curr.Count >= 2)
        {
            currSellPts = ComputePoints(curr, r => (double)r.Sell, currStartX, currW, _plotRect, minY, maxY);
            currBuyPts  = ComputePoints(curr, r => (double)r.Buy,  currStartX, currW, _plotRect, minY, maxY);
        }

        // Store combined for hover
        _prevCount        = prevSellPts.Length;
        _combinedSellPts  = [.. prevSellPts, .. currSellPts];
        _combinedBuyPts   = [.. prevBuyPts,  .. currBuyPts];
        _combinedRates    = allRates;

        // ── Header ────────────────────────────────────────────────────────────
        DrawHeader(g, data.LatestSession, w);
        DrawGrid(g, _plotRect, minY, maxY);

        // ── Previous session (gray) ───────────────────────────────────────────
        if (prevSellPts.Length >= 2)
        {
            DrawFill(g, prevSellPts, _plotRect, PrevSellColor, alpha: 20);
            DrawFill(g, prevBuyPts,  _plotRect, PrevBuyColor,  alpha: 20);
            DrawLine(g, prevSellPts, PrevSellColor, width: 1.5f);
            DrawLine(g, prevBuyPts,  PrevBuyColor,  width: 1.5f);
            DrawDots(g, prevSellPts, PrevSellColor, radius: 2.5f);
            DrawDots(g, prevBuyPts,  PrevBuyColor,  radius: 2.5f);

            // X-axis labels for prev session
            DrawSessionAxis(g, prev, prevStartX, prevW, _plotRect);
        }

        // ── Session gap separator ─────────────────────────────────────────────
        if (gapW > 0 && prevSellPts.Length >= 2 && currSellPts.Length >= 2)
            DrawSessionGap(g, prevStartX + prevW, gapW, _plotRect, prev[^1], curr[0]);

        // ── Current session (colored) ─────────────────────────────────────────
        if (currSellPts.Length >= 2)
        {
            DrawFill(g, currSellPts, _plotRect, SellColor, alpha: 40);
            DrawFill(g, currBuyPts,  _plotRect, BuyColor,  alpha: 40);
            DrawLine(g, currSellPts, SellColor, width: 2f);
            DrawLine(g, currBuyPts,  BuyColor,  width: 2f);
            DrawDots(g, currSellPts, SellColor, radius: 3f);
            DrawDots(g, currBuyPts,  BuyColor,  radius: 3f);

            DrawSessionAxis(g, curr, currStartX, currW, _plotRect);
        }

        // ── Hover ─────────────────────────────────────────────────────────────
        if (_hoveredIndex >= 0 && _combinedSellPts.Length > _hoveredIndex)
        {
            bool isPrev = _hoveredIndex < _prevCount;
            var  sc     = isPrev ? PrevSellColor : SellColor;
            var  bc     = isPrev ? PrevBuyColor  : BuyColor;

            DrawCrosshair(g, _combinedSellPts[_hoveredIndex].X, _plotRect);
            DrawHoverDot(g, _combinedSellPts[_hoveredIndex], sc);
            DrawHoverDot(g, _combinedBuyPts![_hoveredIndex],  bc);
            DrawTooltip(g, _combinedRates[_hoveredIndex],
                _combinedSellPts[_hoveredIndex], _combinedBuyPts[_hoveredIndex],
                w, _plotRect, isPrev);
        }

    }

    // ── Tab bar ────────────────────────────────────────────────────────────────

    private void DrawTabs(Graphics g, int w)
    {
        using var barBrush = new SolidBrush(Color.FromArgb(20, 26, 52));
        g.FillRectangle(barBrush, 0, 0, w, TabBarH);
        using var barLine = new Pen(TabBorder, 1f);
        g.DrawLine(barLine, 0, TabBarH - 1, w, TabBarH - 1);

        using var activeFont   = new Font("Segoe UI", 10f, FontStyle.Bold);
        using var inactiveFont = new Font("Segoe UI", 10f);

        const float tabW = 132f, tabH = 30f, tabY = 7f, startX = 16f, gap = 8f;

        for (int i = 0; i < TabLabels.Length; i++)
        {
            var rx      = startX + i * (tabW + gap);
            var tabRect = new RectangleF(rx, tabY, tabW, tabH);
            _tabRects[i] = tabRect;

            bool active = i == _activeTab;
            using var tabBg = new SolidBrush(active ? TabActiveBg : Color.FromArgb(25, 32, 60));
            FillRoundedRect(g, tabBg, tabRect, 6);

            using var tabBorder = new Pen(active ? Color.FromArgb(80, 110, 190) : TabBorder, 1f);
            DrawRoundedRect(g, tabBorder, tabRect, 6);

            if (active)
            {
                using var accent = new Pen(BuyColor, 2.5f);
                g.DrawLine(accent, rx + 10, tabRect.Bottom - 1, rx + tabW - 10, tabRect.Bottom - 1);
            }

            var font = active ? activeFont : inactiveFont;
            using var labelBrush = new SolidBrush(active ? TitleColor : LabelColor);
            var sz = g.MeasureString(TabLabels[i], font);
            g.DrawString(TabLabels[i], font, labelBrush,
                rx + (tabW - sz.Width) / 2f,
                tabY + (tabH - sz.Height) / 2f);
        }
    }

    // ── Session separator ──────────────────────────────────────────────────────

    private static void DrawSessionGap(Graphics g, float gapStartX, float gapW,
        RectangleF r, InterbankRate lastPrev, InterbankRate firstCurr)
    {
        float midX = gapStartX + gapW / 2f;

        // Dashed vertical boundary lines
        using var boundaryPen = new Pen(GapLineColor, 1f) { DashStyle = DashStyle.Dot };
        g.DrawLine(boundaryPen, gapStartX,        r.Top, gapStartX,        r.Bottom);
        g.DrawLine(boundaryPen, gapStartX + gapW, r.Top, gapStartX + gapW, r.Bottom);

        // Break mark "/ /" in the middle
        using var breakFont  = new Font("Segoe UI", 8f);
        using var breakBrush = new SolidBrush(Color.FromArgb(100, LabelColor));
        var breakStr = "/ /";
        var bsz = g.MeasureString(breakStr, breakFont);
        g.DrawString(breakStr, breakFont, breakBrush, midX - bsz.Width / 2f, r.Top + 4);

        // Date label for each session
        using var dateBrush = new SolidBrush(Color.FromArgb(110, LabelColor));
        var prevDate = lastPrev.Time.ToString("dd.MM");
        var currDate = firstCurr.Time.ToString("dd.MM");
        var pSz = g.MeasureString(prevDate, breakFont);
        g.DrawString(prevDate, breakFont, dateBrush, gapStartX - pSz.Width - 3, r.Top + 4);
        g.DrawString(currDate, breakFont, dateBrush, gapStartX + gapW + 3,      r.Top + 4);
    }

    // ── Bank rates table ───────────────────────────────────────────────────────

    private static readonly Color TableHeaderBg  = Color.FromArgb(28, 36, 68);
    private static readonly Color TableRowEven   = Color.FromArgb(18, 24, 48);
    private static readonly Color TableRowOdd    = Color.FromArgb(22, 30, 58);
    private static readonly Color TableRowHover  = Color.FromArgb(38, 52, 95);
    private static readonly Color TableBorder    = Color.FromArgb(38, 55, 95);
    private static readonly Color OptimalBg      = Color.FromArgb(20, 0, 210, 160);
    private static readonly Color BtnActiveBg    = Color.FromArgb(50, 70, 140);

    private void DrawBankTable(Graphics g, int w, int h)
    {
        const float pad = 20f;
        const float controlY = TabBarH + 14f;
        const float controlH = 28f;
        const float btnW = 60f;

        // ── Currency toggle ───────────────────────────────────────────────────
        using var btnFont   = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var btnBrush  = new SolidBrush(TitleColor);
        using var dimBrush  = new SolidBrush(LabelColor);

        _btnUsd = new RectangleF(pad, controlY, btnW, controlH);
        _btnEur = new RectangleF(pad + btnW + 6f, controlY, btnW, controlH);

        DrawToggleButton(g, _btnUsd, "USD", _bankCurrency == 1, btnFont);
        DrawToggleButton(g, _btnEur, "EUR", _bankCurrency == 2, btnFont);

        // ── Column layout: Name | Оновлено | Купівля | Продаж ────────────────
        const float colHeaderY = TabBarH + 56f;
        const float colHeaderH = 32f;
        const float nameW = 0.37f, updW = 0.13f, buyW = 0.25f, sellW = 0.25f;
        float tableLeft  = pad;
        float tableRight = w - pad;
        float tableW     = tableRight - tableLeft;

        float[] colX =
        [
            tableLeft,
            tableLeft + tableW * nameW,
            tableLeft + tableW * (nameW + updW),
            tableLeft + tableW * (nameW + updW + buyW),
        ];
        float[] colW = [ tableW * nameW, tableW * updW, tableW * buyW, tableW * sellW ];
        string[] colLabels = [ "Банк", "Оновлено", "Купівля", "Продаж" ];

        using var colHeaderFont   = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var colHeaderBrush  = new SolidBrush(TitleColor);
        using var sortBrush       = new SolidBrush(BuyColor);

        for (int c = 0; c < 4; c++)
        {
            _bankColHeaders[c] = new RectangleF(colX[c], colHeaderY, colW[c], colHeaderH);
            using var hdrBg = new SolidBrush(TableHeaderBg);
            g.FillRectangle(hdrBg, _bankColHeaders[c]);

            using var borderPen = new Pen(TableBorder, 1f);
            g.DrawRectangle(borderPen, colX[c], colHeaderY, colW[c], colHeaderH);

            var lbl = colLabels[c];
            if (_bankSortCol == c) lbl += _bankSortAsc ? " ↑" : " ↓";

            var lsz  = g.MeasureString(lbl, colHeaderFont);
            var lblX = c == 0 ? colX[c] + 10 : colX[c] + (colW[c] - lsz.Width) / 2f;
            var brush = _bankSortCol == c ? sortBrush : colHeaderBrush;
            g.DrawString(lbl, colHeaderFont, brush, lblX, colHeaderY + (colHeaderH - lsz.Height) / 2f);
        }

        // ── Rows ──────────────────────────────────────────────────────────────
        var rates  = GetBankRates();
        const float rowH  = 36f;
        float rowTop = colHeaderY + colHeaderH;
        int   visible = Math.Max(1, (int)((h - rowTop - 8) / rowH));
        int   start   = Math.Min(_bankScrollOffset, Math.Max(0, rates.Count - visible));

        _bankRowRects = new RectangleF[Math.Min(visible, rates.Count - start)];

        using var nameFont   = new Font("Segoe UI", 11f);
        using var valueFont  = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var sellBrush  = new SolidBrush(SellColor);
        using var buyBrush   = new SolidBrush(BuyColor);
        using var textBrush  = new SolidBrush(Color.FromArgb(210, 220, 240));

        for (int i = 0; i < _bankRowRects.Length; i++)
        {
            int idx  = start + i;
            if (idx >= rates.Count) break;
            var rate = rates[idx];
            float ry = rowTop + i * rowH;
            var rowRect = new RectangleF(tableLeft, ry, tableW, rowH);
            _bankRowRects[i] = rowRect;

            // Background
            Color bgColor = rate.IsOptimal ? OptimalBg
                : i == _bankHoveredRow ? TableRowHover
                : (i % 2 == 0 ? TableRowEven : TableRowOdd);
            using var rowBg = new SolidBrush(bgColor);
            g.FillRectangle(rowBg, rowRect);

            // Border
            using var borderPen = new Pen(TableBorder, 0.5f);
            g.DrawLine(borderPen, tableLeft, ry + rowH - 0.5f, tableRight, ry + rowH - 0.5f);

            // Icon + Name
            float textY = ry + (rowH - g.MeasureString(rate.Name, nameFont).Height) / 2f;
            const float iconSize = 22f, iconPad = 6f;
            float nameX = colX[0] + iconPad;
            var icon = BankIconStore.Get(rate.Name);
            if (icon is not null)
            {
                float iconY = ry + (rowH - iconSize) / 2f;
                g.DrawImage(icon, nameX, iconY, iconSize, iconSize);
                nameX += iconSize + 5f;
            }
            g.DrawString(rate.Name, nameFont, textBrush, nameX, textY);

            // Updated
            var usz = g.MeasureString(rate.UpdatedAt, nameFont);
            g.DrawString(rate.UpdatedAt, nameFont, textBrush,
                colX[1] + (colW[1] - usz.Width) / 2f,
                ry + (rowH - usz.Height) / 2f);

            // Buy
            var buyStr = rate.Buy > 0 ? $"{rate.Buy:F2}" : "—";
            var bsz = g.MeasureString(buyStr, valueFont);
            g.DrawString(buyStr, valueFont, buyBrush,
                colX[2] + (colW[2] - bsz.Width) / 2f,
                ry + (rowH - bsz.Height) / 2f);

            // Sell
            var sellStr = rate.Sell > 0 ? $"{rate.Sell:F2}" : "—";
            var ssz = g.MeasureString(sellStr, valueFont);
            g.DrawString(sellStr, valueFont, sellBrush,
                colX[3] + (colW[3] - ssz.Width) / 2f,
                ry + (rowH - ssz.Height) / 2f);
        }

        // Scrollbar indicator
        if (rates.Count > visible)
        {
            float sbH = h - rowTop - 8;
            float thumbH = sbH * visible / rates.Count;
            float thumbY = rowTop + (sbH - thumbH) * start / (rates.Count - visible);
            using var sbBg    = new SolidBrush(Color.FromArgb(25, 255, 255, 255));
            using var sbThumb = new SolidBrush(Color.FromArgb(70, 120, 180));
            g.FillRectangle(sbBg,    w - pad + 4, rowTop, 4, sbH);
            g.FillRectangle(sbThumb, w - pad + 4, thumbY, 4, thumbH);
        }

        // No data message
        if (rates.Count == 0)
        {
            var msg = "Завантаження...";
            using var msgFont  = new Font("Segoe UI", 12f);
            using var msgBrush = new SolidBrush(LabelColor);
            var msz = g.MeasureString(msg, msgFont);
            g.DrawString(msg, msgFont, msgBrush, (w - msz.Width) / 2f, rowTop + 40);
        }
    }

    private static void DrawToggleButton(Graphics g, RectangleF rect, string text, bool active, Font font)
    {
        using var bg = new SolidBrush(active ? BtnActiveBg : Color.FromArgb(25, 32, 60));
        FillRoundedRect(g, bg, rect, 5);
        using var border = new Pen(active ? Color.FromArgb(80, 110, 190) : TabBorder, 1f);
        DrawRoundedRect(g, border, rect, 5);
        using var textBrush = new SolidBrush(active ? TitleColor : LabelColor);
        var sz = g.MeasureString(text, font);
        g.DrawString(text, font, textBrush,
            rect.X + (rect.Width - sz.Width) / 2f,
            rect.Y + (rect.Height - sz.Height) / 2f);
    }

    // ── Background & header ────────────────────────────────────────────────────

    private static void DrawBackground(Graphics g, int w, int h)
    {
        g.Clear(BgColor);
        using var gb = new LinearGradientBrush(
            new Rectangle(0, TabBarH, w, 60),
            Color.FromArgb(30, BuyColor), Color.Transparent,
            LinearGradientMode.Vertical);
        g.FillRectangle(gb, 0, TabBarH, w, 60);
    }

    private void DrawHeader(Graphics g, IReadOnlyList<InterbankRate> rates, int w)
    {
        if (rates.Count == 0) return;
        var latest = rates[^1];
        using var titleFont  = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var valueFont  = new Font("Segoe UI", 11f);
        using var smallFont  = new Font("Segoe UI", 8.5f);
        using var titleBrush = new SolidBrush(TitleColor);
        using var timeBrush  = new SolidBrush(LabelColor);
        using var buyBrush   = new SolidBrush(BuyColor);
        using var sellBrush  = new SolidBrush(SellColor);

        var title = _activeTab == 0 ? "Міжбанк USD/UAH" : "Чорний ринок USD/UAH";
        g.DrawString(title, titleFont, titleBrush, 68, TabBarH + 12);
        g.DrawString($"оновлено {latest.Time:HH:mm  dd.MM.yyyy}", smallFont, timeBrush, 68, TabBarH + 36);

        float bx = w - 240f;
        g.DrawString("Купівля", smallFont, buyBrush,  bx, TabBarH + 14);
        g.DrawString($"{latest.Buy:F3}", valueFont, buyBrush, bx, TabBarH + 30);

        float sx = w - 130f;
        g.DrawString("Продаж", smallFont, sellBrush, sx, TabBarH + 14);
        g.DrawString($"{latest.Sell:F3}", valueFont, sellBrush, sx, TabBarH + 30);
    }

    // ── Grid ───────────────────────────────────────────────────────────────────

    private static void DrawGrid(Graphics g, RectangleF r, double minY, double maxY)
    {
        using var gridPen    = new Pen(GridColor) { DashStyle = DashStyle.Dot };
        using var labelBrush = new SolidBrush(LabelColor);
        using var labelFont  = new Font("Segoe UI", 8f);

        for (int i = 0; i <= 4; i++)
        {
            var t   = i / 4.0;
            var val = maxY - t * (maxY - minY);
            var y   = r.Top + (float)t * r.Height;
            g.DrawLine(gridPen, r.Left, y, r.Right, y);
            g.DrawString($"{val:F3}", labelFont, labelBrush, 2, y - 8);
        }
    }

    // ── Session X-axis ─────────────────────────────────────────────────────────

    private static void DrawSessionAxis(Graphics g, IReadOnlyList<InterbankRate> rates,
        float startX, float width, RectangleF r)
    {
        using var axisPen    = new Pen(GridColor, 1.5f);
        using var labelBrush = new SolidBrush(LabelColor);
        using var labelFont  = new Font("Segoe UI", 8f);

        g.DrawLine(axisPen, startX, r.Bottom, startX + width, r.Bottom);

        int ticks = Math.Min(4, rates.Count);
        if (ticks < 2) return;
        for (int i = 0; i < ticks; i++)
        {
            int idx = (int)Math.Round((float)i / (ticks - 1) * (rates.Count - 1));
            var x   = startX + (float)idx / (rates.Count - 1) * width;
            g.DrawLine(axisPen, x, r.Bottom, x, r.Bottom + 4);
            var label = rates[idx].Time.ToString("HH:mm");
            var sz    = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, labelBrush, x - sz.Width / 2, r.Bottom + 6);
        }
    }

    // ── Lines, fills, dots ─────────────────────────────────────────────────────

    private static void DrawFill(Graphics g, PointF[] pts, RectangleF r, Color color, int alpha)
    {
        var poly = new PointF[pts.Length + 2];
        pts.CopyTo(poly, 0);
        poly[^2] = new PointF(pts[^1].X, r.Bottom);
        poly[^1] = new PointF(pts[0].X,  r.Bottom);
        using var brush = new LinearGradientBrush(
            new PointF(0, r.Top), new PointF(0, r.Bottom),
            Color.FromArgb(alpha, color), Color.FromArgb(0, color));
        g.FillPolygon(brush, poly);
    }

    private static void DrawLine(Graphics g, PointF[] pts, Color color, float width = 2f)
    {
        using var pen = new Pen(color, width) { LineJoin = LineJoin.Round };
        g.DrawLines(pen, pts);
    }

    private static void DrawDots(Graphics g, PointF[] pts, Color color, float radius = 3f)
    {
        using var fill = new SolidBrush(color);
        using var ring = new Pen(BgColor, 1.5f);
        foreach (var p in pts)
        {
            g.FillEllipse(fill, p.X - radius, p.Y - radius, radius * 2, radius * 2);
            g.DrawEllipse(ring, p.X - radius, p.Y - radius, radius * 2, radius * 2);
        }
    }

    private static void DrawHoverDot(Graphics g, PointF p, Color color)
    {
        using var glow = new Pen(Color.FromArgb(50, color), 6f);
        g.DrawEllipse(glow, p.X - 8,  p.Y - 8,  16, 16);
        using var ring = new Pen(Color.FromArgb(200, 255, 255, 255), 2f);
        g.DrawEllipse(ring, p.X - 6,  p.Y - 6,  12, 12);
        using var fill = new SolidBrush(color);
        g.FillEllipse(fill, p.X - 5,  p.Y - 5,  10, 10);
    }

    private static void DrawCrosshair(Graphics g, float x, RectangleF r)
    {
        using var pen = new Pen(CrosshairColor, 1f) { DashStyle = DashStyle.Dash };
        g.DrawLine(pen, x, r.Top, x, r.Bottom);
    }

    private static void DrawTooltip(Graphics g, InterbankRate rate,
        PointF sellPt, PointF buyPt, int canvasWidth, RectangleF r, bool isPrevSession)
    {
        using var labelFont = new Font("Segoe UI", 9f);
        using var valueFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(220, 225, 240));

        const float pad = 10f, dotR = 5f, lineH = 22f;
        const float cardW = 148f, cardH = lineH * 2 + pad * 2;

        float x = sellPt.X + 14;
        if (x + cardW > r.Right) x = sellPt.X - cardW - 14;

        float midY = (sellPt.Y + buyPt.Y) / 2f;
        float y    = Math.Clamp(midY - cardH / 2f, r.Top, r.Bottom - cardH);

        var card = new RectangleF(x, y, cardW, cardH);

        using var bgBrush = new SolidBrush(TooltipBg);
        FillRoundedRect(g, bgBrush, card, 7);
        using var borderPen = new Pen(TooltipBorder, 1f);
        DrawRoundedRect(g, borderPen, card, 7);

        // Dim indicator for previous session
        if (isPrevSession)
        {
            using var prevTag  = new Font("Segoe UI", 7f);
            using var prevBrush = new SolidBrush(Color.FromArgb(130, LabelColor));
            g.DrawString("попер. сесія", prevTag, prevBrush, x + pad, y + 2);
        }

        var sc = isPrevSession ? PrevSellColor : SellColor;
        var bc = isPrevSession ? PrevBuyColor  : BuyColor;

        float rowY = y + pad;
        using var sellBrush = new SolidBrush(sc);
        g.FillEllipse(sellBrush, x + pad, rowY + (lineH - dotR * 2) / 2f, dotR * 2, dotR * 2);
        g.DrawString("Продаж: ", labelFont, textBrush, x + pad + dotR * 2 + 5, rowY + 3);
        var sellLabel  = $"{rate.Sell:F3}";
        var sellLabelX = card.Right - pad - g.MeasureString(sellLabel, valueFont).Width;
        g.DrawString(sellLabel, valueFont, sellBrush, sellLabelX, rowY + 3);

        rowY += lineH;
        using var buyBrush = new SolidBrush(bc);
        g.FillEllipse(buyBrush, x + pad, rowY + (lineH - dotR * 2) / 2f, dotR * 2, dotR * 2);
        g.DrawString("Купівля: ", labelFont, textBrush, x + pad + dotR * 2 + 5, rowY + 3);
        var buyLabel  = $"{rate.Buy:F3}";
        var buyLabelX = card.Right - pad - g.MeasureString(buyLabel, valueFont).Width;
        g.DrawString(buyLabel, valueFont, buyBrush, buyLabelX, rowY + 3);

        using var timeFont  = new Font("Segoe UI", 7.5f);
        using var timeBrush = new SolidBrush(Color.FromArgb(130, 155, 200));
        var timeStr = rate.Time.ToString("HH:mm  dd.MM");
        var tsz     = g.MeasureString(timeStr, timeFont);
        g.DrawString(timeStr, timeFont, timeBrush,
            card.Left + (card.Width - tsz.Width) / 2, card.Bottom + 3);
    }

private static void DrawCenteredText(Graphics g, string text, int w, int h)
    {
        using var font  = new Font("Segoe UI", 14f);
        using var brush = new SolidBrush(LabelColor);
        var sz = g.MeasureString(text, font);
        g.DrawString(text, font, brush, (w - sz.Width) / 2, (h - sz.Height) / 2);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    /// <summary>Compute screen points for a session, positioned within [startX, startX+width].</summary>
    private static PointF[] ComputePoints(IReadOnlyList<InterbankRate> rates,
        Func<InterbankRate, double> selector,
        float startX, float width,
        RectangleF r, double minY, double maxY)
    {
        var pts = new PointF[rates.Count];
        for (int i = 0; i < rates.Count; i++)
        {
            var x = rates.Count > 1
                ? startX + (float)i / (rates.Count - 1) * width
                : startX + width / 2f;
            var t = (selector(rates[i]) - minY) / (maxY - minY);
            var y = r.Bottom - (float)t * r.Height;
            pts[i] = new PointF(x, y);
        }
        return pts;
    }

    private static (double min, double max) YRange(IEnumerable<InterbankRate> rates)
    {
        var allVals = rates.SelectMany(r => new[] { (double)r.Buy, (double)r.Sell }).ToList();
        var min     = allVals.Min();
        var max     = allVals.Max();
        var pad     = (max - min) * 0.15;
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
        path.AddArc(r.X,               r.Y,               rad * 2, rad * 2, 180, 90);
        path.AddArc(r.Right - rad * 2, r.Y,               rad * 2, rad * 2, 270, 90);
        path.AddArc(r.Right - rad * 2, r.Bottom - rad * 2, rad * 2, rad * 2, 0,   90);
        path.AddArc(r.X,               r.Bottom - rad * 2, rad * 2, rad * 2, 90,  90);
        path.CloseFigure();
        return path;
    }
}
