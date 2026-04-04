using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using MizhbankNotifier.Models;
using MizhbankNotifier.Services;

namespace MizhbankNotifier;

public class ChartWindow : Form
{
    private readonly RateStore               _interbankStore;
    private readonly EurRateStore            _eurInterbankStore;
    private readonly BlackMarketRateStore    _blackMarketStore;
    private readonly EurBlackMarketRateStore _eurBlackMarketStore;
    private readonly ForexRateStore          _forexStore;
    private readonly FinanceUaRateStore      _financeUaStore;
    private readonly BankRateStore           _bankRateStore;
    private readonly DailyRateDb             _db;
    private readonly System.Windows.Forms.Timer _refreshTimer;
    private readonly PictureBox _canvas;

    // ── History navigation ─────────────────────────────────────────────────────
    private DateOnly?  _viewDate  = null;   // null = live/today
    private ChartData? _histData  = null;
    private RectangleF _navLeft, _navRight;
    private const float NavBarH = 36f;

    private int _activeTab    = 0;
    private int _hoveredIndex = -1;  // index into _combinedRates / _combinedSellPts
    private readonly ToolTip _navTooltip = new() { InitialDelay = 300, ReshowDelay = 200 };
    private string? _lastNavTip;
    private readonly Action? _onRefresh;
    private RectangleF _refreshBtn;

    private RectangleF   _plotRect;
    private RectangleF[] _tabRects = new RectangleF[4];

    // ── Interbank currency toggle ──────────────────────────────────────────────
    private int        _interbankCurrency = 1;   // 1 = USD, 2 = EUR
    private RectangleF _btnInterbankUsd, _btnInterbankEur;

    // ── Interbank source toggle ───────────────────────────────────────────────
    private int        _interbankSource = 0;     // 0 = Kurs.com.ua, 1 = Finance.ua
    private RectangleF _btnSrcKurs, _btnSrcFinua;

    // ── Black market currency toggle ───────────────────────────────────────────
    private int        _blackMarketCurrency = 1; // 1 = USD, 2 = EUR
    private RectangleF _btnBlackMarketUsd, _btnBlackMarketEur;

    // ── Bank table state ───────────────────────────────────────────────────────
    private int  _bankCurrency    = 1;    // 1 = USD, 2 = EUR
    private int  _bankSortCol     = 3;    // 0=name, 1=updated, 2=buy, 3=sell
    private bool _bankSortAsc     = true;  // ascending = lowest sell first
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

    private static readonly string[] TabLabels = { "Міжбанк", "Чорний ринок", "В банках", "Forex" };
    private const int TabBarH = 44;
    private const float GapFraction = 0.05f;  // 5% of plot width for the session gap

    public ChartWindow(RateStore interbankStore, EurRateStore eurInterbankStore,
        BlackMarketRateStore blackMarketStore, EurBlackMarketRateStore eurBlackMarketStore,
        ForexRateStore forexStore, FinanceUaRateStore financeUaStore,
        BankRateStore bankRateStore, DailyRateDb db, Action? onRefresh = null)
    {
        _interbankStore      = interbankStore;
        _eurInterbankStore   = eurInterbankStore;
        _blackMarketStore    = blackMarketStore;
        _eurBlackMarketStore = eurBlackMarketStore;
        _forexStore          = forexStore;
        _financeUaStore      = financeUaStore;
        _bankRateStore       = bankRateStore;
        _db                  = db;
        _onRefresh           = onRefresh;

        Text = "Курси USD/UAH";
        Size = new Size(1280, 760);
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
        _interbankStore.DataChanged      += OnDataChanged;
        _eurInterbankStore.DataChanged   += OnDataChanged;
        _blackMarketStore.DataChanged    += OnDataChanged;
        _eurBlackMarketStore.DataChanged += OnDataChanged;
        _forexStore.DataChanged          += OnDataChanged;
        _financeUaStore.DataChanged      += OnDataChanged;
        _bankRateStore.DataChanged       += OnDataChanged;
    }

    private void OnDataChanged()
    {
        if (_canvas.IsHandleCreated)
            _canvas.BeginInvoke(_canvas.Invalidate);
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _interbankStore.DataChanged      -= OnDataChanged;
        _eurInterbankStore.DataChanged   -= OnDataChanged;
        _blackMarketStore.DataChanged    -= OnDataChanged;
        _eurBlackMarketStore.DataChanged -= OnDataChanged;
        _forexStore.DataChanged          -= OnDataChanged;
        _financeUaStore.DataChanged      -= OnDataChanged;
        _bankRateStore.DataChanged       -= OnDataChanged;
        _refreshTimer.Stop();
        base.OnFormClosed(e);
    }

    private ChartData ActiveData => _viewDate.HasValue
        ? (_histData ?? ChartData.Empty)
        : _activeTab switch
        {
            0 => _interbankSource == 1
                ? _financeUaStore.Data
                : (_interbankCurrency == 1 ? _interbankStore.Data : _eurInterbankStore.Data),
            1 => _blackMarketCurrency == 1 ? _blackMarketStore.Data    : _eurBlackMarketStore.Data,
            3 => _forexStore.Data,
            _ => _blackMarketStore.Data,
        };

    private string ActiveType => _activeTab switch
    {
        0 => _interbankSource == 1
            ? "interbank_usd_finua"
            : (_interbankCurrency == 1 ? "interbank_usd" : "interbank_eur"),
        3 => "forex_eurusd",
        _ => _blackMarketCurrency == 1 ? "blackmarket_usd" : "blackmarket_eur",
    };

    // ── Mouse ──────────────────────────────────────────────────────────────────

    private void OnMouseClick(object? sender, MouseEventArgs e)
    {
        for (int i = 0; i < _tabRects.Length; i++)
        {
            if (_tabRects[i].Contains(e.X, e.Y) && _activeTab != i)
            {
                _activeTab    = i;
                _hoveredIndex = -1;
                _viewDate     = null;
                _histData     = null;
                _canvas.Invalidate();
                return;
            }
        }

        // Navigation arrows (chart tabs only: 0, 1, 3)
        if (_activeTab is 0 or 1 or 3)
        {
            if (_navLeft.Contains(e.X, e.Y))  { NavLeft();  return; }
            if (_navRight.Contains(e.X, e.Y)) { NavRight(); return; }
        }

        // Refresh button (all tabs)
        if (_refreshBtn.Contains(e.X, e.Y) && _onRefresh is not null)
        {
            _viewDate = null;
            _histData = null;
            _hoveredIndex = -1;
            _onRefresh();
            _canvas.Invalidate();
            return;
        }

        if (_activeTab == 0)
        {
            if (_btnSrcKurs.Contains(e.X, e.Y) && _interbankSource != 0)
            {
                _interbankSource = 0; _hoveredIndex = -1;
                ReloadHistoricalIfActive(); _canvas.Invalidate(); return;
            }
            if (_btnSrcFinua.Contains(e.X, e.Y) && _interbankSource != 1)
            {
                _interbankSource = 1; _hoveredIndex = -1;
                ReloadHistoricalIfActive(); _canvas.Invalidate(); return;
            }
            if (_interbankSource == 0)
            {
                if (_btnInterbankUsd.Contains(e.X, e.Y) && _interbankCurrency != 1)
                {
                    _interbankCurrency = 1; _hoveredIndex = -1;
                    ReloadHistoricalIfActive(); _canvas.Invalidate(); return;
                }
                if (_btnInterbankEur.Contains(e.X, e.Y) && _interbankCurrency != 2)
                {
                    _interbankCurrency = 2; _hoveredIndex = -1;
                    ReloadHistoricalIfActive(); _canvas.Invalidate(); return;
                }
            }
        }

        if (_activeTab == 1)
        {
            if (_btnBlackMarketUsd.Contains(e.X, e.Y) && _blackMarketCurrency != 1)
            {
                _blackMarketCurrency = 1; _hoveredIndex = -1;
                ReloadHistoricalIfActive(); _canvas.Invalidate(); return;
            }
            if (_btnBlackMarketEur.Contains(e.X, e.Y) && _blackMarketCurrency != 2)
            {
                _blackMarketCurrency = 2; _hoveredIndex = -1;
                ReloadHistoricalIfActive(); _canvas.Invalidate(); return;
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
        bool overNav = _activeTab is 0 or 1 or 3 && (_navLeft.Contains(e.X, e.Y) || _navRight.Contains(e.X, e.Y));
        bool overRefresh = _refreshBtn.Contains(e.X, e.Y);
        bool overBtn = overRefresh
            || (_activeTab == 0 && (_btnSrcKurs.Contains(e.X, e.Y) || _btnSrcFinua.Contains(e.X, e.Y)
                || _btnInterbankUsd.Contains(e.X, e.Y) || _btnInterbankEur.Contains(e.X, e.Y)))
            || (_activeTab == 1 && (_btnBlackMarketUsd.Contains(e.X, e.Y) || _btnBlackMarketEur.Contains(e.X, e.Y)))
            || (_activeTab == 2 && (_btnUsd.Contains(e.X, e.Y) || _btnEur.Contains(e.X, e.Y)
            || _bankColHeaders.Any(h => h.Contains(e.X, e.Y))));
        _canvas.Cursor = (overTab || overBtn || overNav) ? Cursors.Hand : Cursors.Default;

        // Tooltips for nav buttons and refresh
        {
            string? tip = null;
            if (_refreshBtn.Contains(e.X, e.Y))
            {
                tip = "Оновити дані";
            }
            else if (_activeTab is 0 or 1 or 3)
            {
                if (_navLeft.Contains(e.X, e.Y))
                {
                    var prev = _viewDate?.AddDays(-1) ?? DateOnly.FromDateTime(DateTime.Today).AddDays(-1);
                    tip = prev.ToString("dd.MM.yyyy");
                }
                else if (_navRight.Contains(e.X, e.Y) && _viewDate.HasValue)
                {
                    var next = _viewDate.Value.AddDays(1);
                    tip = next == DateOnly.FromDateTime(DateTime.Today) ? "Сьогодні" : next.ToString("dd.MM.yyyy");
                }
            }
            if (tip != _lastNavTip)
            {
                _lastNavTip = tip;
                _navTooltip.SetToolTip(_canvas, tip);
            }
        }

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
        if (e.Y < TabBarH || e.Y > _plotRect.Bottom) { if (_hoveredIndex != -1) { _hoveredIndex = -1; _canvas.Invalidate(); } return; }

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

        if (_activeTab != 3)
            DrawRefreshButton(g);

        if (_activeTab == 0)
            DrawInterbankCurrencyToggle(g);
        else if (_activeTab == 1)
            DrawBlackMarketCurrencyToggle(g);
        // Tab 3 (Forex) has no currency toggle or refresh button

        var data = ActiveData;

        var prev = data.PreviousSession;
        var curr = data.CurrentSession;

        if (prev.Count < 2 && curr.Count < 2)
        {
            var msg = _viewDate.HasValue ? "Немає даних за цей день" : "Завантаження даних...";
            DrawCenteredText(g, msg, w, h);
            DrawNavBar(g, w, h);
            return;
        }

        const int ml = 68, mr = 24, mb = 52;  // 52 = NavBarH(36) + original padding(16)
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
        DrawHeader(g, data, w);
        DrawGrid(g, _plotRect, minY, maxY, _activeTab == 3 ? "F4" : "F3");

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
                w, _plotRect, isPrev, isForex: _activeTab == 3);
        }

        DrawNavBar(g, w, h);
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

        // ── Title ────────────────────────────────────────────────────────────
        using var titleFont  = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var titleBrush = new SolidBrush(TitleColor);
        g.DrawString("Курс валют провідних банків України", titleFont, titleBrush, pad, controlY + 2f);
        var titleSz = g.MeasureString("Курс валют провідних банків України", titleFont);

        float toggleX = pad + titleSz.Width + 16f;
        _btnUsd = new RectangleF(toggleX, controlY, btnW, controlH);
        _btnEur = new RectangleF(toggleX + btnW + 6f, controlY, btnW, controlH);

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

        using var nameFont        = new Font("Segoe UI", 11f);
        using var nameFontItalic  = new Font("Segoe UI", 11f, FontStyle.Italic);
        using var valueFont       = new Font("Segoe UI", 11f, FontStyle.Bold);
        using var valueFontItalic = new Font("Segoe UI", 11f, FontStyle.Bold | FontStyle.Italic);
        using var sellBrush       = new SolidBrush(SellColor);
        using var buyBrush        = new SolidBrush(BuyColor);
        using var textBrush       = new SolidBrush(Color.FromArgb(210, 220, 240));
        using var staleSellBrush  = new SolidBrush(Color.FromArgb(110, SellColor.R, SellColor.G, SellColor.B));
        using var staleBuyBrush   = new SolidBrush(Color.FromArgb(110, BuyColor.R,  BuyColor.G,  BuyColor.B));
        using var staleTextBrush  = new SolidBrush(Color.FromArgb(110, 140, 160));
        using var prevFont        = new Font("Segoe UI", 8f);
        using var prevRateBrush   = new SolidBrush(Color.FromArgb(100, 150, 160, 180));

        // Pre-compute best buy (highest) and best sell (lowest) among non-stale rows
        var today = DateOnly.FromDateTime(DateTime.Now);
        decimal bestBuy  = 0m, bestSell = decimal.MaxValue;
        foreach (var r in rates)
        {
            bool fresh = r.UpdatedDate is null || r.UpdatedDate == today;
            if (fresh)
            {
                if (r.Buy  > 0) bestBuy  = Math.Max(bestBuy,  r.Buy);
                if (r.Sell > 0) bestSell = Math.Min(bestSell, r.Sell);
            }
        }
        if (bestSell == decimal.MaxValue) bestSell = 0m;
        using var bestCellBrush = new SolidBrush(Color.FromArgb(50, 30, 210, 100));

        // Build previous-rate lookup by bank name for trend arrows
        var prevRates = (_bankCurrency == 1 ? _bankRateStore.PreviousUsd : _bankRateStore.PreviousEur)
            .ToDictionary(r => r.Name, r => r);

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

            // Stale = updated on a previous day
            bool isStale = rate.UpdatedDate.HasValue && rate.UpdatedDate.Value < today;
            if (isStale)
            {
                using var dimRow = new SolidBrush(Color.FromArgb(100, (int)BgColor.R, (int)BgColor.G, (int)BgColor.B));
                g.FillRectangle(dimRow, rowRect);
            }
            var rowNameFont  = isStale ? nameFontItalic  : nameFont;
            var rowValFont   = isStale ? valueFontItalic : valueFont;
            var rowTextBrush = isStale ? staleTextBrush  : textBrush;
            var rowBuyBrush  = isStale ? staleBuyBrush   : buyBrush;
            var rowSellBrush = isStale ? staleSellBrush  : sellBrush;

            // Best-rate cell highlight (drawn over row bg, under text)
            if (!isStale)
            {
                if (bestBuy  > 0 && rate.Buy  == bestBuy)
                    g.FillRectangle(bestCellBrush, colX[2], ry, colW[2], rowH);
                if (bestSell > 0 && rate.Sell == bestSell)
                    g.FillRectangle(bestCellBrush, colX[3], ry, colW[3], rowH);
            }

            // Icon + Name
            float textY = ry + (rowH - g.MeasureString(rate.Name, rowNameFont).Height) / 2f;
            const float iconSize = 22f, iconPad = 6f;
            float nameX = colX[0] + iconPad;
            var icon = BankIconStore.Get(rate.Name);
            if (icon is not null)
            {
                float iconY = ry + (rowH - iconSize) / 2f;
                g.DrawImage(icon, nameX, iconY, iconSize, iconSize);
                nameX += iconSize + 5f;
            }
            g.DrawString(rate.Name, rowNameFont, rowTextBrush, nameX, textY);

            // Updated
            var usz = g.MeasureString(rate.UpdatedAt, rowNameFont);
            g.DrawString(rate.UpdatedAt, rowNameFont, rowTextBrush,
                colX[1] + (colW[1] - usz.Width) / 2f,
                ry + (rowH - usz.Height) / 2f);

            // Buy
            prevRates.TryGetValue(rate.Name, out var prevRate);
            var buyStr = rate.Buy > 0 ? $"{rate.Buy:F2}" : "—";
            var bsz    = g.MeasureString(buyStr, rowValFont);
            float buyMidY = ry + rowH / 2f;
            bool hasPrev = prevRate is not null && !isStale;
            float buyMainY = hasPrev ? ry + (rowH - bsz.Height) / 2f - 5f : ry + (rowH - bsz.Height) / 2f;
            float buyX = colX[2] + (colW[2] - bsz.Width) / 2f;
            g.DrawString(buyStr, rowValFont, rowBuyBrush, buyX, buyMainY);
            if (hasPrev)
            {
                DrawTrendArrow(g, rate.Buy, prevRate!.Buy, buyX + bsz.Width + 2, buyMainY + bsz.Height / 2f);
                if (prevRate.Buy > 0)
                {
                    var prevBuyStr = $"{prevRate.Buy:F2}";
                    var pbsz = g.MeasureString(prevBuyStr, prevFont);
                    g.DrawString(prevBuyStr, prevFont, prevRateBrush,
                        colX[2] + (colW[2] - pbsz.Width) / 2f, buyMainY + bsz.Height - 2f);
                }
            }

            // Sell
            var sellStr = rate.Sell > 0 ? $"{rate.Sell:F2}" : "—";
            var ssz     = g.MeasureString(sellStr, rowValFont);
            float sellMainY = hasPrev ? ry + (rowH - ssz.Height) / 2f - 5f : ry + (rowH - ssz.Height) / 2f;
            float sellX = colX[3] + (colW[3] - ssz.Width) / 2f;
            g.DrawString(sellStr, rowValFont, rowSellBrush, sellX, sellMainY);
            if (hasPrev)
            {
                DrawTrendArrow(g, rate.Sell, prevRate!.Sell, sellX + ssz.Width + 2, sellMainY + ssz.Height / 2f);
                if (prevRate.Sell > 0)
                {
                    var prevSellStr = $"{prevRate.Sell:F2}";
                    var pssz = g.MeasureString(prevSellStr, prevFont);
                    g.DrawString(prevSellStr, prevFont, prevRateBrush,
                        colX[3] + (colW[3] - pssz.Width) / 2f, sellMainY + ssz.Height - 2f);
                }
            }
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

        // Stale-data overlay: dim the whole table when last fetch was not today
        var fetchedAt = _bankCurrency == 1 ? _bankRateStore.FetchedAtUsd : _bankRateStore.FetchedAtEur;
        bool tableIsStale = fetchedAt.HasValue && fetchedAt.Value.ToLocalTime().Date < DateTime.Today;
        if (tableIsStale && rates.Count > 0)
        {
            var tableRect = new RectangleF(tableLeft, colHeaderY, tableW, h - colHeaderY);
            using var dimOverlay = new SolidBrush(Color.FromArgb(120, (int)BgColor.R, (int)BgColor.G, (int)BgColor.B));
            g.FillRectangle(dimOverlay, tableRect);

            using var staleFont  = new Font("Segoe UI", 10f, FontStyle.Italic);
            using var staleLbl   = new SolidBrush(LabelColor);
            var lbl = "дані за вчора";
            var lsz = g.MeasureString(lbl, staleFont);
            g.DrawString(lbl, staleFont, staleLbl,
                tableLeft + (tableW - lsz.Width) / 2f,
                colHeaderY + 10f);
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

    // ── Interbank currency toggle ──────────────────────────────────────────────

    private void DrawInterbankCurrencyToggle(Graphics g)
    {
        const float btnW = 56f, btnH = 28f, controlY = TabBarH + 14f;
        const float srcBtnW = 96f;

        using var btnFont = new Font("Segoe UI", 9f, FontStyle.Bold);

        // ── Currency toggle (right side, only for Kurs.com.ua which has EUR) ──
        if (_interbankSource == 0)
        {
            float startX = _canvas.Width - 310f;
            _btnInterbankUsd = new RectangleF(startX,            controlY, btnW, btnH);
            _btnInterbankEur = new RectangleF(startX + btnW + 6f, controlY, btnW, btnH);

            DrawToggleButton(g, _btnInterbankUsd, "USD", _interbankCurrency == 1, btnFont);
            DrawToggleButton(g, _btnInterbankEur, "EUR", _interbankCurrency == 2, btnFont);
        }
        else
        {
            _btnInterbankUsd = RectangleF.Empty;
            _btnInterbankEur = RectangleF.Empty;
        }

        // ── Source toggle (to the left of currency/refresh buttons) ───────────
        float srcStartX = _canvas.Width - 310f - srcBtnW * 2 - 6f - 60f;
        _btnSrcKurs  = new RectangleF(srcStartX,                controlY, srcBtnW, btnH);
        _btnSrcFinua = new RectangleF(srcStartX + srcBtnW + 6f, controlY, srcBtnW, btnH);

        DrawToggleButton(g, _btnSrcKurs,  "Kurs.com.ua",  _interbankSource == 0, btnFont);
        DrawToggleButton(g, _btnSrcFinua, "Finance.ua",   _interbankSource == 1, btnFont);
    }

    // ── Black market currency toggle ───────────────────────────────────────────

    private void DrawBlackMarketCurrencyToggle(Graphics g)
    {
        const float btnW = 56f, btnH = 28f, controlY = TabBarH + 14f;
        float startX = _canvas.Width - 310f;
        _btnBlackMarketUsd = new RectangleF(startX,            controlY, btnW, btnH);
        _btnBlackMarketEur = new RectangleF(startX + btnW + 6f, controlY, btnW, btnH);

        using var btnFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        DrawToggleButton(g, _btnBlackMarketUsd, "USD", _blackMarketCurrency == 1, btnFont);
        DrawToggleButton(g, _btnBlackMarketEur, "EUR", _blackMarketCurrency == 2, btnFont);
    }

    // ── Refresh button ──────────────────────────────────────────────────────────

    private static readonly Bitmap RefreshIcon = LoadRefreshIcon();

    private static Bitmap LoadRefreshIcon()
    {
        var asm = typeof(ChartWindow).Assembly;
        using var stream = asm.GetManifestResourceStream("MizhbankNotifier.Resources.sync.png")!;
        return new Bitmap(stream);
    }

    private void DrawRefreshButton(Graphics g)
    {
        const float size = 28f, controlY = TabBarH + 14f;
        float x = _canvas.Width - 348f;
        _refreshBtn = new RectangleF(x, controlY, size, size);

        // Icon centered in the button area
        float ix = x + (size - RefreshIcon.Width)  / 2f;
        float iy = controlY + (size - RefreshIcon.Height) / 2f;
        g.DrawImage(RefreshIcon, ix, iy, RefreshIcon.Width, RefreshIcon.Height);
    }

    // ── Day navigation ─────────────────────────────────────────────────────────

    private void LoadHistoricalFor(DateOnly date)
    {
        var curr = _db.LoadDayChart(date, ActiveType) ?? [];
        var prev = _db.LoadDayChart(date.AddDays(-1), ActiveType) ?? [];
        _histData = new ChartData(curr, prev);
        _viewDate = date;
        _hoveredIndex = -1;
    }

    private void ReloadHistoricalIfActive()
    {
        if (_viewDate.HasValue) LoadHistoricalFor(_viewDate.Value);
    }

    private void NavLeft()
    {
        var dates  = _db.GetAvailableDates(ActiveType);
        var cutoff = _viewDate ?? DateOnly.FromDateTime(DateTime.Today);
        var target = dates.Where(d => d < cutoff).DefaultIfEmpty(DateOnly.MinValue).Max();
        if (target == DateOnly.MinValue) return;   // nothing older
        LoadHistoricalFor(target);
        _canvas.Invalidate();
    }

    private void NavRight()
    {
        if (!_viewDate.HasValue) return;           // already live
        var dates  = _db.GetAvailableDates(ActiveType);
        var today  = DateOnly.FromDateTime(DateTime.Today);
        var target = dates.Where(d => d > _viewDate.Value).DefaultIfEmpty(DateOnly.MaxValue).Min();
        if (target == DateOnly.MaxValue || target >= today)
        {
            _viewDate = null; _histData = null;    // snap back to live
        }
        else
        {
            LoadHistoricalFor(target);
        }
        _hoveredIndex = -1;
        _canvas.Invalidate();
    }

    private void DrawNavBar(Graphics g, int w, int h)
    {
        float navTop = h - NavBarH;

        const float btnW = 28f, btnH = 28f;
        float navCenterY = navTop + NavBarH / 2f - 2f;
        float centerX    = w / 2f;

        var dateStr = _viewDate.HasValue
            ? _viewDate.Value.ToString("dd.MM.yyyy")
            : "Сьогодні";

        using var dateFont = new Font("Segoe UI", 10.5f, _viewDate.HasValue ? FontStyle.Regular : FontStyle.Italic);
        var textSz   = g.MeasureString(dateStr, dateFont);
        float labelX = centerX - textSz.Width / 2f;

        _navLeft  = new RectangleF(labelX - btnW - 10f, navCenterY - btnH / 2f, btnW, btnH);
        _navRight = new RectangleF(labelX + textSz.Width + 10f, navCenterY - btnH / 2f, btnW, btnH);

        // Determine enablement
        var dates     = _db.GetAvailableDates(ActiveType);
        var today     = DateOnly.FromDateTime(DateTime.Today);
        var cutoff    = _viewDate ?? today;
        bool leftEnabled  = dates.Any(d => d < cutoff);
        bool rightEnabled = _viewDate.HasValue;

        DrawNavButton(g, _navLeft,  "‹", leftEnabled);
        DrawNavButton(g, _navRight, "›", rightEnabled);

        using var dateBrush = new SolidBrush(
            _viewDate.HasValue ? TitleColor : Color.FromArgb(180, LabelColor));
        g.DrawString(dateStr, dateFont, dateBrush,
            labelX, navCenterY - textSz.Height / 2f);
    }

    private void DrawNavButton(Graphics g, RectangleF rect, string symbol, bool enabled)
    {
        using var bg     = new SolidBrush(Color.FromArgb(enabled ? 45 : 18, TitleColor));
        using var border = new Pen(Color.FromArgb(enabled ? 70 : 25, 130, 170), 1f);
        using var font   = new Font("Segoe UI", 14f, FontStyle.Bold);
        using var brush  = new SolidBrush(Color.FromArgb(enabled ? 220 : 55, TitleColor));

        g.FillRectangle(bg, rect);
        g.DrawRectangle(border, rect.X, rect.Y, rect.Width, rect.Height);
        var sz = g.MeasureString(symbol, font);
        g.DrawString(symbol, font, brush,
            rect.X + (rect.Width  - sz.Width)  / 2f,
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

    private void DrawHeader(Graphics g, ChartData data, int w)
    {
        var rates = data.LatestSession;
        if (rates.Count == 0) return;
        var latest = rates[^1];
        // In historical mode there is no meaningful "previous poll" to compare against.
        var prev = _viewDate.HasValue ? null : _activeTab switch
        {
            0 => _interbankCurrency == 1   ? _interbankStore.PreviousLatest   : _eurInterbankStore.PreviousLatest,
            1 => _blackMarketCurrency == 1 ? _blackMarketStore.PreviousLatest : _eurBlackMarketStore.PreviousLatest,
            3 => _forexStore.PreviousLatest,
            _ => _blackMarketStore.PreviousLatest,
        };

        using var titleFont  = new Font("Segoe UI", 13f, FontStyle.Bold);
        using var valueFont  = new Font("Segoe UI", 11f);
        using var smallFont  = new Font("Segoe UI", 8.5f);
        using var titleBrush = new SolidBrush(TitleColor);
        using var timeBrush  = new SolidBrush(LabelColor);
        using var buyBrush   = new SolidBrush(BuyColor);
        using var sellBrush  = new SolidBrush(SellColor);

        var currency = _activeTab switch
        {
            0 => _interbankCurrency == 2   ? "EUR" : "USD",
            3 => "EUR",
            _ => _blackMarketCurrency == 2 ? "EUR" : "USD",
        };
        var title = _activeTab switch
        {
            0 => $"Міжбанк {currency}/UAH",
            3 => "Курс Forex (EUR/USD)",
            _ => $"Чорний ринок {currency}/UAH",
        };
        g.DrawString(title, titleFont, titleBrush, 68, TabBarH + 12);
        g.DrawString($"оновлено {latest.Time:HH:mm  dd.MM.yyyy}", smallFont, timeBrush, 68, TabBarH + 36);

        // ── Session delta (in the empty middle gap) ────────────────────────────
        var prevSession = data.PreviousSession;
        if (prevSession.Count > 0)
        {
            var prevLast  = prevSession[^1];

            const float dX = 310f;
            using var dimLabelBrush  = new SolidBrush(Color.FromArgb(110, LabelColor));
            using var deltaValueFont = new Font("Segoe UI", 10f);

            g.DrawString("від попер. сесії", smallFont, dimLabelBrush, dX, TabBarH + 11);

            using var buyΔBrush  = new SolidBrush(BuyColor);
            using var sellΔBrush = new SolidBrush(SellColor);

            const float arrowW = 9f;   // 7px triangle + 2px gap
            const float deltaY = TabBarH + 27;
            const float arrowCY = deltaY + 8f;  // vertical centre of the 10pt text

            if (_activeTab == 3)
            {
                // Forex: single rate delta
                decimal rateΔ = latest.Buy - prevLast.Buy;
                var rateΔStr = $"Курс {rateΔ:+0.0000;-0.0000}";
                g.DrawString(rateΔStr, deltaValueFont, buyΔBrush, dX, deltaY);
                var rateΔSz = g.MeasureString(rateΔStr, deltaValueFont);
                DrawTrendArrow(g, latest.Buy, prevLast.Buy, dX + rateΔSz.Width - 4, arrowCY);
            }
            else
            {
                decimal buyΔ  = latest.Buy  - prevLast.Buy;
                decimal sellΔ = latest.Sell - prevLast.Sell;

                var buyΔStr  = $"Купівля {buyΔ:+0.000;-0.000}";
                var sellΔStr = $"Продаж {sellΔ:+0.000;-0.000}";

                g.DrawString(buyΔStr, deltaValueFont, buyΔBrush, dX, deltaY);
                var buyΔSz = g.MeasureString(buyΔStr, deltaValueFont);
                DrawTrendArrow(g, latest.Buy, prevLast.Buy, dX + buyΔSz.Width - 4, arrowCY);

                float sell2X = dX + buyΔSz.Width + arrowW + 8;
                g.DrawString(sellΔStr, deltaValueFont, sellΔBrush, sell2X, deltaY);
                var sellΔSz = g.MeasureString(sellΔStr, deltaValueFont);
                DrawTrendArrow(g, latest.Sell, prevLast.Sell, sell2X + sellΔSz.Width - 4, arrowCY);
            }
        }

        // ── Купівля / Продаж (right side) ─────────────────────────────────────
        if (_activeTab == 3)
        {
            // Forex: single rate, no buy/sell split
            float rx = w - 90f;
            g.DrawString("Курс", smallFont, buyBrush, rx, TabBarH + 14);
            var rateStr = $"{latest.Buy:F4}";
            g.DrawString(rateStr, valueFont, buyBrush, rx, TabBarH + 30);
            var rateSz = g.MeasureString(rateStr, valueFont);
            DrawTrendArrow(g, latest.Buy, prev?.Buy ?? 0, rx + rateSz.Width + 2, TabBarH + 30 + rateSz.Height / 2f);
        }
        else
        {
            float bx = w - 140f;
            g.DrawString("Купівля", smallFont, buyBrush,  bx, TabBarH + 14);
            var buyStr = $"{latest.Buy:F3}";
            g.DrawString(buyStr, valueFont, buyBrush, bx, TabBarH + 30);
            var buySz = g.MeasureString(buyStr, valueFont);
            DrawTrendArrow(g, latest.Buy, prev?.Buy ?? 0, bx + buySz.Width + 2, TabBarH + 30 + buySz.Height / 2f);

            float sx = w - 64f;
            g.DrawString("Продаж", smallFont, sellBrush, sx, TabBarH + 14);
            var sellStr = $"{latest.Sell:F3}";
            g.DrawString(sellStr, valueFont, sellBrush, sx, TabBarH + 30);
            var sellSz = g.MeasureString(sellStr, valueFont);
            DrawTrendArrow(g, latest.Sell, prev?.Sell ?? 0, sx + sellSz.Width + 2, TabBarH + 30 + sellSz.Height / 2f);
        }
    }

    // ── Grid ───────────────────────────────────────────────────────────────────

    private static void DrawGrid(Graphics g, RectangleF r, double minY, double maxY, string fmt = "F3")
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
            g.DrawString(val.ToString(fmt), labelFont, labelBrush, 2, y - 8);
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
            g.DrawLine(axisPen, x, r.Bottom - 4, x, r.Bottom);
            var label = rates[idx].Time.ToString("HH:mm");
            var sz    = g.MeasureString(label, labelFont);
            g.DrawString(label, labelFont, labelBrush, x - sz.Width / 2, r.Bottom - sz.Height - 5);
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
        PointF sellPt, PointF buyPt, int canvasWidth, RectangleF r, bool isPrevSession,
        bool isForex = false)
    {
        using var labelFont = new Font("Segoe UI", 9f);
        using var valueFont = new Font("Segoe UI", 9f, FontStyle.Bold);
        using var textBrush = new SolidBrush(Color.FromArgb(220, 225, 240));

        const float pad = 10f, dotR = 5f, lineH = 22f, timeRowH = 18f;
        int rows = isForex ? 1 : 2;
        float cardW = 148f, cardH = lineH * rows + pad * 2 + timeRowH;

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

        if (isForex)
        {
            // Single rate line
            using var rateBrush = new SolidBrush(bc);
            g.FillEllipse(rateBrush, x + pad, rowY + (lineH - dotR * 2) / 2f, dotR * 2, dotR * 2);
            g.DrawString("Курс: ", labelFont, textBrush, x + pad + dotR * 2 + 5, rowY + 3);
            var rateLabel  = $"{rate.Buy:F4}";
            var rateLabelX = card.Right - pad - g.MeasureString(rateLabel, valueFont).Width;
            g.DrawString(rateLabel, valueFont, rateBrush, rateLabelX, rowY + 3);
        }
        else
        {
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
        }

        using var timeFont  = new Font("Segoe UI", 7.5f);
        using var timeBrush = new SolidBrush(Color.FromArgb(130, 155, 200));
        var timeStr = rate.Time.ToString("HH:mm");
        var tsz     = g.MeasureString(timeStr, timeFont);
        g.DrawString(timeStr, timeFont, timeBrush,
            card.Left + (card.Width - tsz.Width) / 2, card.Bottom - timeRowH + 3);
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

    // ── Trend arrow ────────────────────────────────────────────────────────────

    /// <summary>Draws a small ▲ (green) or ▼ (red) triangle to the right of (x, centerY).</summary>
    private static void DrawTrendArrow(Graphics g, decimal current, decimal previous, float x, float centerY)
    {
        if (previous == 0 || current == previous) return;
        bool up    = current > previous;
        var  color = up ? BuyColor : SellColor;
        const float w = 7f, h = 7f;
        float cx = x + w / 2f;
        PointF[] tri = up
            ? [new(cx, centerY - h / 2f), new(cx - w / 2f, centerY + h / 2f), new(cx + w / 2f, centerY + h / 2f)]
            : [new(cx, centerY + h / 2f), new(cx - w / 2f, centerY - h / 2f), new(cx + w / 2f, centerY - h / 2f)];
        using var brush = new SolidBrush(color);
        g.FillPolygon(brush, tri);
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
