using System.Drawing;
using System.Windows.Forms;

namespace MizhbankNotifier.Services;

public class TrayIconService : IDisposable
{
    private NotifyIcon? _notifyIcon;
    private Thread? _uiThread;
    private Form? _hiddenHost;
    private readonly ManualResetEventSlim _uiReady = new(false);
    private readonly IHostApplicationLifetime _lifetime;
    private readonly RateStore _rateStore;
    private readonly BlackMarketRateStore _blackMarketStore;
    private readonly BankRateStore _bankRateStore;
    private ChartWindow? _chartWindow;

    public TrayIconService(IHostApplicationLifetime lifetime, RateStore rateStore,
        BlackMarketRateStore blackMarketStore, BankRateStore bankRateStore)
    {
        _lifetime = lifetime;
        _rateStore = rateStore;
        _blackMarketStore = blackMarketStore;
        _bankRateStore = bankRateStore;
    }

    public void Start()
    {
        _uiThread = new Thread(RunTrayIcon);
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.IsBackground = true;
        _uiThread.Start();
    }

    public void UpdateTooltip(string text)
    {
        if (_notifyIcon is not null)
            _notifyIcon.Text = text.Length > 63 ? text[..63] : text;
    }

    // Post work to the WinForms STA thread. Safe to call from any thread.
    public void PostToUI(Action action)
    {
        _uiReady.Wait();
        _hiddenHost!.BeginInvoke(action);
    }

    // The invisible host control for WebView2 and other UI-thread objects.
    public Control HiddenHost { get { _uiReady.Wait(); return _hiddenHost!; } }

    private void RunTrayIcon()
    {
        // Create an invisible 1×1 form so we have a message-loop handle for BeginInvoke.
        _hiddenHost = new Form
        {
            Text            = "",
            Width           = 1,
            Height          = 1,
            Opacity         = 0,
            ShowInTaskbar   = false,
            FormBorderStyle = FormBorderStyle.None,
            WindowState     = FormWindowState.Minimized,
        };
        _hiddenHost.Show(); // creates HWND
        _hiddenHost.Hide(); // invisible, handle still alive
        _uiReady.Set();     // unblocks any waiting PostToUI callers

        // When the host begins shutdown, stop the WinForms message loop cleanly
        // so Application.Run() returns before the main thread exits.
        _lifetime.ApplicationStopping.Register(() =>
            _hiddenHost.BeginInvoke(Application.Exit));

        _notifyIcon = new NotifyIcon
        {
            Icon = AppIcon.Create(),
            Text = "Міжбанк — завантаження...",
            Visible = true,
            ContextMenuStrip = CreateMenu()
        };

        // Double-click also opens the chart window
        _notifyIcon.DoubleClick += (_, _) => OpenChartWindow();

        OpenChartWindow();

        Application.Run();
    }

    private ContextMenuStrip CreateMenu()
    {
        var menu = new ContextMenuStrip
        {
            BackColor = Color.FromArgb(22, 30, 58),
            ForeColor = Color.FromArgb(210, 220, 240),
            Renderer = new DarkMenuRenderer()
        };

        var openItem = new ToolStripMenuItem("Відкрити графік")
        {
            Font = new Font("Segoe UI", 9.5f)
        };
        openItem.Click += (_, _) => OpenChartWindow();

        var separator = new ToolStripSeparator();

        var exitItem = new ToolStripMenuItem("Вихід")
        {
            Font = new Font("Segoe UI", 9.5f)
        };
        exitItem.Click += (_, _) => _lifetime.StopApplication();

        menu.Items.Add(openItem);
        menu.Items.Add(separator);
        menu.Items.Add(exitItem);

        return menu;
    }

    private void OpenChartWindow()
    {
        if (_chartWindow is { IsDisposed: false })
        {
            _chartWindow.BringToFront();
            _chartWindow.Focus();
            return;
        }

        _chartWindow = new ChartWindow(_rateStore, _blackMarketStore, _bankRateStore);
        _chartWindow.Show();
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
    }

    // ── Dark theme renderer for the context menu ──────────────────────────────

    private sealed class DarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color BgColor   = Color.FromArgb(22, 30, 58);
        private static readonly Color HoverColor = Color.FromArgb(40, 55, 100);
        private static readonly Color BorderColor = Color.FromArgb(50, 70, 120);

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            var rect = new Rectangle(2, 0, e.Item.Width - 4, e.Item.Height);
            using var brush = new SolidBrush(e.Item.Selected ? HoverColor : BgColor);
            e.Graphics.FillRectangle(brush, rect);
        }

        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using var brush = new SolidBrush(BgColor);
            e.Graphics.FillRectangle(brush, e.AffectedBounds);
        }

        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using var pen = new Pen(BorderColor);
            var r = new Rectangle(e.AffectedBounds.X, e.AffectedBounds.Y,
                e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
            e.Graphics.DrawRectangle(pen, r);
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            var y = e.Item.Height / 2;
            using var pen = new Pen(BorderColor);
            e.Graphics.DrawLine(pen, 8, y, e.Item.Width - 8, y);
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Color.FromArgb(210, 220, 240);
            base.OnRenderItemText(e);
        }
    }
}
