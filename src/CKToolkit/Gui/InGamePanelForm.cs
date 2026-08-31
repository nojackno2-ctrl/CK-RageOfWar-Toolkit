using System.Drawing;
using System.Linq;
using System.Threading;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.Core.Runtime;
using CKToolkit.Core.Trainer;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>
/// 遊戲中修改器面板（置頂輔助視窗，AGENTS.md §1 輔助視窗例外）。
///
/// 引擎只認 20 個硬編按鍵 id 且不看修飾鍵，筆電無小鍵盤時難以操作。
/// 本面板作為遙控器，點按鈕以 Win32 訊息（PostMessage）把按鍵代送給遊戲視窗；
/// 不改任何設定、不寫任何檔案、由主視窗開關、關掉後不留任何常駐。
/// </summary>
public sealed class InGamePanelForm : Form
{
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;

    /// <summary>把游標移到畫面中央後，最多等多久讓引擎算出該點的地圖座標。</summary>
    private const int SampleTimeoutMs = 200;

    /// <summary>取樣輪詢間隔。</summary>
    private const int SampleStepMs = 10;

    /// <summary>沒有記憶體路徑時，游標要在中央停留多久才歸位（讓腳本讀得到）。</summary>
    private const int CursorHoldMs = 150;

    private readonly Label _status = new();
    private readonly Label _spawnPoint = new();
    private readonly FlowLayoutPanel _speedRow = new();
    private readonly NumericUpDown _speed = new();
    private readonly Button _speedApply = new();
    private readonly TableLayoutPanel _buttons = new();
    private readonly List<Button> _cheatButtons = [];
    private readonly System.Windows.Forms.Timer _timer = new();
    private readonly bool _hasCursorCheats;
    private System.Windows.Forms.Timer? _cursorRestore;
    private Point _savedCursor;
    private IntPtr _memHandle = IntPtr.Zero;
    private IntPtr _memBase = IntPtr.Zero;
    private uint _memPid;
    private string? _memProblem;
    private IntPtr _hwnd = IntPtr.Zero;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WsExNoActivate | WsExToolWindow;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    public InGamePanelForm(TrainerConfig config)
    {
        // 可縮放：作弊數量差很多，固定尺寸不是太擠就是浪費畫面。
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        MinimumSize = new Size(150, 120);
        TopMost = true;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Text = Strings.Get("Gui_Panel_Title");
        BackColor = Color.FromArgb(248, 250, 252);
        Font = new Font("Microsoft JhengHei UI", 9F);
        Padding = new Padding(8);
        Width = 260;

        _status.Dock = DockStyle.Top;
        _status.AutoSize = true;
        _status.Padding = new Padding(4, 4, 4, 8);
        _status.Font = new Font(Font, FontStyle.Bold);

        // 單欄 100% 寬的表格：視窗縮放時按鈕跟著伸縮，不像 FlowLayoutPanel 會被
        // 按鈕的固定寬度卡住。列高 AutoSize，超出高度就捲動。
        _buttons.Dock = DockStyle.Fill;
        _buttons.ColumnCount = 1;
        _buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _buttons.AutoScroll = true;
        _buttons.Padding = new Padding(0);
        _buttons.Margin = new Padding(0);

        _hasCursorCheats = config.Cheats.Any(
            c => c.Enabled && Cheats.CursorPositionCheats.Contains(c.Id));

        int buttonCount = 0;
        foreach (var c in config.Cheats)
        {
            if (!c.Enabled) continue;
            if (!Cheats.ById.TryGetValue(c.Id, out var cheatDef)) continue;

            string id = string.IsNullOrWhiteSpace(c.Key)
                ? cheatDef.DefaultKeyFor(config.NumpadKeys)
                : c.Key;

            uint? vk = KeyMap.VirtualKeyFor(id, config.NumpadKeys);
            if (vk is null) continue;

            string cheatName = Strings.IsChinese ? cheatDef.Name : cheatDef.Id;
            string label = $"{cheatName}（{KeyMap.Display(id, config.NumpadKeys)}）";

            uint virtualKey = vk.Value;
            var btn = new Button
            {
                Text = label,
                Tag = virtualKey,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Height = 30,
                FlatStyle = FlatStyle.Flat,
                BackColor = Color.White,
                ForeColor = Color.FromArgb(15, 23, 42),
                Margin = new Padding(0, 0, 0, 6),
                Cursor = Cursors.Hand
            };
            btn.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
            // 座標取自 MousePtm() 的作弊不能立刻送鍵：游標此刻正停在這顆按鈕上。
            // 見 Cheats.CursorPositionCheats 的說明。
            bool needsCursor = Cheats.CursorPositionCheats.Contains(c.Id);
            btn.Click += (_, _) =>
            {
                if (needsCursor) SpawnAtViewCentre(virtualKey);
                else SendKey(virtualKey);
            };

            _buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _cheatButtons.Add(btn);
            _buttons.Controls.Add(btn, 0, buttonCount);
            buttonCount++;
        }

        if (buttonCount == 0)
        {
            var noCheatsLabel = new Label
            {
                Text = Strings.Get("Gui_Panel_NoCheats"),
                AutoSize = true,
                ForeColor = Color.FromArgb(100, 116, 139),
                Padding = new Padding(4, 8, 4, 8)
            };
            _buttons.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            _buttons.Controls.Add(noCheatsLabel, 0, 0);
        }

        int targetHeight = Math.Clamp(60 + Math.Max(1, buttonCount) * 38, 120, 600);
        Height = targetHeight;

        Controls.Add(_buttons);
        Controls.Add(_speedRow);
        Controls.Add(_spawnPoint);
        Controls.Add(_status);

        _spawnPoint.Dock = DockStyle.Top;
        _spawnPoint.AutoSize = true;
        _spawnPoint.Padding = new Padding(4, 0, 4, 8);
        _spawnPoint.ForeColor = Color.FromArgb(71, 85, 105);
        _spawnPoint.Visible = _hasCursorCheats;

        // 遊戲速度：走 GameSpeed 的主控台路徑（引擎自己執行 SetSpeed(n)）。
        //
        // 不直接寫記憶體是有原因的：SetSpeed 的 handler（.text VA 0x00595530）並不把值
        // 存進變數，而是配置一個命令物件、把速度放進 [obj+0xC]，再丟進 0x0056FE10 的
        // 命令佇列。GetSpeed 讀的 [[0x008AA6C8] + 0xC58] 只是結果。直接寫那個位址會
        // 繞過引擎的簿記，值不會真的改變節奏。所以讓引擎自己跑一次 SetSpeed。
        _speedRow.Dock = DockStyle.Top;
        _speedRow.AutoSize = true;
        _speedRow.WrapContents = false;
        _speedRow.Padding = new Padding(4, 0, 4, 8);
        _speedRow.Margin = new Padding(0);

        var speedLabel = new Label
        {
            Text = Strings.Get("Gui_Panel_Speed"),
            AutoSize = true,
            Margin = new Padding(0, 5, 6, 0),
            ForeColor = Color.FromArgb(71, 85, 105),
        };
        _speed.Minimum = 1;
        _speed.Maximum = 100;
        _speed.Value = 1;
        _speed.Width = 60;
        _speed.Margin = new Padding(0, 1, 6, 0);
        _speedApply.Text = Strings.Get("Gui_Panel_SpeedApply");
        _speedApply.AutoSize = true;
        _speedApply.FlatStyle = FlatStyle.Flat;
        _speedApply.BackColor = Color.White;
        _speedApply.FlatAppearance.BorderColor = Color.FromArgb(203, 213, 225);
        _speedApply.Margin = new Padding(0, 0, 0, 0);
        _speedApply.Click += (_, _) => ApplySpeed();
        _speedRow.Controls.AddRange([speedLabel, _speed, _speedApply]);

        _timer.Interval = 1000;
        _timer.Tick += (_, _) => RefreshConnection();
        _timer.Start();

        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
        };

        RefreshConnection();
    }

    /// <summary>
    /// FormClosed 只在視窗真的顯示過並關閉時才觸發，所以計時器的釋放不能只掛在那裡：
    /// 建構後未顯示就 Dispose（例如 SelfTest）會留下一個沒人停的計時器。
    /// AGENTS.md §1 的輔助視窗例外要求「關掉後不留任何常駐」，這裡補上最後一道保證。
    /// </summary>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Stop();
            _timer.Dispose();
            _cursorRestore?.Stop();
            _cursorRestore?.Dispose();
            ReleaseMemory();
        }
        base.Dispose(disposing);
    }

    /// <summary>
    /// 把遊戲速度設成目前選的倍率。1 倍走 GameSpeed.Restore（Apply 對 1 以下是 no-op，
    /// 那是分析器「只加速」的語意，面板需要能調回正常速度）。
    /// </summary>
    private void ApplySpeed()
    {
        uint pid = GameWindow.GetProcessId(_hwnd);
        if (pid == 0) { RefreshConnection(); return; }

        int multiplier = (int)_speed.Value;
        var outcome = multiplier <= 1
            ? GameSpeed.Restore(pid, GameSpeed.Method.Console)
            : GameSpeed.Apply(pid, multiplier, GameSpeed.Method.Console);

        _status.Text = outcome.Message;
        _status.ForeColor = outcome.Success
            ? Color.FromArgb(22, 163, 74)
            : Color.FromArgb(190, 18, 60);
    }

    /// <summary>
    /// 在遊戲畫面中央生成。
    ///
    /// 不自己換算螢幕像素到地圖座標（那要重建引擎的相機轉換），而是讓遊戲自己算：
    /// 把游標暫時移到畫面中央，引擎處理滑鼠移動時就會把該點的地圖座標寫進
    /// MousePtm 讀的那個快取（見 GameMemory 的說明），取樣後游標立刻歸位。
    ///
    /// 歸位本身會再產生一次滑鼠移動、把快取蓋掉，所以歸位之後要把取樣到的座標
    /// 寫回去釘住，然後才送鍵。沒有記憶體路徑時退而求其次：讓游標在中央多停
    /// CursorHoldMs 再歸位，靠時間差讓腳本先讀到。
    /// </summary>
    private void SpawnAtViewCentre(uint virtualKey)
    {
        if (_hwnd == IntPtr.Zero) { RefreshConnection(); return; }
        if (!GameWindow.TryGetWindowRect(_hwnd, out Rectangle rect) || rect.Width <= 0)
        {
            SendKey(virtualKey);
            return;
        }

        EnsureMemory();
        var centre = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
        _savedCursor = Cursor.Position;
        Cursor.Position = centre;

        GameMemory.MapPoint? sampled = SampleMapPoint();
        if (sampled is { } point)
        {
            Cursor.Position = _savedCursor;
            GameMemory.TryWriteMousePoint(_memHandle, _memBase, point);
            SendKey(virtualKey);
            _spawnPoint.Text = Strings.Get("Gui_Panel_SpawnPointAt", point.X, point.Y);
            return;
        }

        // 退路：讀不到座標就把游標留在中央送鍵，稍後再歸位。
        SendKey(virtualKey);
        _cursorRestore?.Stop();
        _cursorRestore?.Dispose();
        _cursorRestore = new System.Windows.Forms.Timer { Interval = CursorHoldMs };
        _cursorRestore.Tick += (_, _) =>
        {
            Cursor.Position = _savedCursor;
            _cursorRestore?.Stop();
            _cursorRestore?.Dispose();
            _cursorRestore = null;
        };
        _cursorRestore.Start();
    }

    /// <summary>
    /// 等引擎把游標所在點換算好。連續兩次讀到相同的值才算穩定——移動當下讀到的
    /// 可能還是舊值。逾時就回傳 null，由呼叫端走退路。
    /// </summary>
    private GameMemory.MapPoint? SampleMapPoint()
    {
        if (_memHandle == IntPtr.Zero) return null;

        GameMemory.MapPoint? previous = null;
        for (int waited = 0; waited <= SampleTimeoutMs; waited += SampleStepMs)
        {
            Thread.Sleep(SampleStepMs);
            if (!GameMemory.TryReadMousePoint(_memHandle, _memBase, out var current)) continue;
            if (previous is { } p && p == current) return current;
            previous = current;
        }
        return previous;
    }

    /// <summary>依目前的遊戲行程建立／維持記憶體連線；遊戲換了行程就重新連。</summary>
    private void EnsureMemory()
    {
        uint pid = GameWindow.GetProcessId(_hwnd);
        if (pid == 0) { ReleaseMemory(); return; }
        if (_memHandle != IntPtr.Zero && pid == _memPid) return;

        ReleaseMemory();
        _memHandle = GameMemory.Open(pid, out _memBase, out _memProblem);
        _memPid = _memHandle != IntPtr.Zero ? pid : 0;
        UpdateSpawnPointLabel();
    }

    private void ReleaseMemory()
    {
        GameMemory.Close(_memHandle);
        _memHandle = IntPtr.Zero;
        _memBase = IntPtr.Zero;
        _memPid = 0;
    }

    private void UpdateSpawnPointLabel()
    {
        if (!_hasCursorCheats) return;
        _spawnPoint.Text = _memHandle == IntPtr.Zero && _memProblem is not null
            ? Strings.Get("Gui_Panel_SpawnPointUnavailable")
            : Strings.Get("Gui_Panel_SpawnPointCentre");
    }

    private void RefreshConnection()
    {
        _hwnd = GameWindow.Find();
        bool ok = _hwnd != IntPtr.Zero;
        foreach (var b in _cheatButtons) b.Enabled = ok;
        _speed.Enabled = ok;
        _speedApply.Enabled = ok;

        if (_hasCursorCheats) { EnsureMemory(); UpdateSpawnPointLabel(); }

        _status.Text = Strings.Get(ok ? "Gui_Panel_GameConnected" : "Gui_Panel_GameNotFound");
        _status.ForeColor = ok ? Color.FromArgb(22, 163, 74) : Color.FromArgb(100, 116, 139);
    }

    private void SendKey(uint virtualKey)
    {
        if (_hwnd == IntPtr.Zero) { RefreshConnection(); return; }
        if (!GameWindow.PostKey(_hwnd, virtualKey))
            _status.Text = Strings.Get("Gui_Panel_SendFailed");
    }

    public void PositionNearGame()
    {
        _hwnd = GameWindow.Find();
        if (_hwnd != IntPtr.Zero && GameWindow.TryGetWindowRect(_hwnd, out var rect))
        {
            int x = rect.Right - Width - 16;
            int y = rect.Top + 16;
            Location = new Point(x, y);
        }
        else
        {
            var wa = Screen.PrimaryScreen?.WorkingArea ?? Screen.GetWorkingArea(this);
            int x = wa.Right - Width - 16;
            int y = wa.Top + 16;
            Location = new Point(x, y);
        }
    }
}
