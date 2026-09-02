using System.Drawing;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.Core.Runtime;
using CKToolkit.Core.Trainer;
using CKToolkit.I18n;

namespace CKToolkit.Gui;

/// <summary>
/// 遊戲中修改器面板（置頂輔助視窗，AGENTS.md §1 輔助視窗例外）。
///
/// <para><b>兩條觸發路徑</b></para>
///
/// <list type="number">
///   <item>
///     <b>執行期腳本通道（主要，ISSUE-068）</b>：點按鈕就把該作弊的 VS 腳本原文直接送進
///     遊戲，由引擎自己的編譯器編譯並在主執行緒執行（見 <c>src/CKPerf/script.cpp</c>）。
///     這條路完全不經過按鍵，所以引擎那 20 個硬編按鍵代號的上限、遊戲保留鍵、原版
///     scdebug 佔用鍵通通不再是限制——18 個作弊全部都能列在面板上並且都能按。
///   </item>
///   <item>
///     <b>代送按鍵（備援）</b>：通道不可用時（沒注入 <c>ckperf.dll</c>、或簽章對不上而
///     被停用），退回原本的 <c>PostMessage</c> 送鍵。這條路只服務「真的有綁到鍵」的作弊，
///     也就是回到 ISSUE-068 之前的能力範圍。
///   </item>
/// </list>
///
/// 兩條路都不改任何設定、不寫任何檔案，由主視窗開關、關掉後不留任何常駐。
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
    private Point _savedCursor;
    private bool _cursorMoved;
    private bool _isSpawning;
    private CancellationTokenSource? _spawnCts;
    private IntPtr _memHandle = IntPtr.Zero;
    private IntPtr _memBase = IntPtr.Zero;
    private uint _memPid;
    private string? _memProblem;
    private IntPtr _hwnd = IntPtr.Zero;

    /// <summary>作弊參數解析所需的原始選單，與 <c>BuildScDebug</c> 用的是同一份規則。</summary>
    private readonly List<CheatSelection> _selections;

    /// <summary>見 <see cref="Cheats.PlayerExpression"/>；建構時固定，執行期不變。</summary>
    private readonly string _playerExpression;

    /// <summary>目前這一場的腳本通道客戶端；通道不可用時為 null。</summary>
    private ScriptChannel? _channel;

    /// <summary>最後一次探測的結果，決定按鈕走通道還是走送鍵。</summary>
    private bool _channelReady;

    /// <summary><see cref="_channel"/> 是為哪個行程建立的；行程換了就要重建。</summary>
    private uint _channelPid;

    /// <summary>目前有沒有一個探測在飛（避免每秒疊一個）。</summary>
    private bool _probing;

    // 測試專用內部接縫（避免測試時操作真實滑鼠或行程記憶體）
    internal Func<Point>? GetCursorPositionSeam;
    internal Action<Point>? SetCursorPositionSeam;
    internal Func<int, CancellationToken, Task>? DelayAsyncSeam;
    internal Func<IntPtr, (bool success, Rectangle rect)>? GetWindowRectSeam;
    internal Func<IntPtr, uint, bool>? PostKeySeam;
    internal Func<IntPtr, IntPtr, (bool success, GameMemory.MapPoint point)>? ReadMousePointSeam;
    internal Func<IntPtr, IntPtr, GameMemory.MapPoint, bool>? WriteMousePointSeam;
    internal Func<string, ScriptRunOutcome>? RunScriptSeam;
    internal bool IsSpawningActive => _isSpawning;
    internal bool IsScriptChannelReady => _channelReady;

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
        AutoScaleMode = AutoScaleMode.Dpi;
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

        _playerExpression = Cheats.PlayerExpression(config.PlayerMode, config.FixedPlayer);

        // 執行期通道不看「有沒有勾選啟用」——那個勾選的意思是「要不要把它寫進
        // SCDEBUG.XML 佔一個按鍵」，跟「能不能從面板按一下」是兩回事。面板列出全部
        // 作弊，這正是 ISSUE-068 要解決的「面板上沒有那些按鈕」。
        _selections = config.Cheats
            .Where(c => Cheats.ById.ContainsKey(c.Id))
            .Select(c => new CheatSelection
            {
                Id = c.Id,
                Key = c.Key,
                Parameters = c.Parameters.ToDictionary(
                    p => p.Key, p => (object)p.Value, StringComparer.Ordinal),
            })
            .ToList();

        // 設定檔沒列到的作弊（新版新增、或使用者從未動過）也要能按，一律吃預設參數。
        foreach (var known in Cheats.All)
        {
            if (_selections.All(s => s.Id != known.Id))
                _selections.Add(new CheatSelection { Id = known.Id });
        }

        _hasCursorCheats = _selections.Any(s => Cheats.CursorPositionCheats.Contains(s.Id));

        int buttonCount = 0;
        foreach (var selection in _selections)
        {
            if (!Cheats.ById.TryGetValue(selection.Id, out var cheatDef)) continue;

            string id = string.IsNullOrWhiteSpace(selection.Key)
                ? cheatDef.DefaultKeyFor(config.NumpadKeys)
                : selection.Key;

            // 綁不到鍵不再是「不列出來」的理由；只是備援路徑不可用而已。
            uint? vk = KeyMap.VirtualKeyFor(id, config.NumpadKeys);

            string cheatName = TrainerStrings.GetCheatName(cheatDef.Id, cheatDef.Name);
            string label = vk is null
                ? cheatName
                : $"{cheatName}（{KeyMap.Display(id, config.NumpadKeys)}）";

            var btn = new Button
            {
                Text = label,
                Tag = vk,
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

            // 座標取自 MousePtm() 的作弊不能立刻觸發：游標此刻正停在這顆按鈕上。
            // 見 Cheats.CursorPositionCheats 與 Cheats.BuildRuntimeScript 的說明。
            bool needsCursor = Cheats.CursorPositionCheats.Contains(selection.Id);
            var captured = selection;
            btn.Click += async (_, _) => await TriggerCheatAsync(captured, cheatDef, vk, needsCursor);

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

        FormClosing += (_, _) => CancelActiveSpawn();

        FormClosed += (_, _) =>
        {
            _timer.Stop();
            _timer.Dispose();
            CancelActiveSpawn();
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
            CancelActiveSpawn();
            ReleaseMemory();
        }
        base.Dispose(disposing);
    }

    private void CancelActiveSpawn()
    {
        // CTS 由非同步操作在 finally 唯一釋放；關窗或連線中斷只送出取消訊號，
        // 避免與 Task.Delay 的 continuation 同時 Dispose。
        try { _spawnCts?.Cancel(); } catch (ObjectDisposedException) { }
        RestoreCursorIfNeeded();
    }

    private void RestoreCursorIfNeeded()
    {
        if (!_cursorMoved) return;
        try
        {
            SetCursorPosition(_savedCursor);
            _cursorMoved = false;
        }
        catch { }
    }

    private Point GetCursorPosition() =>
        GetCursorPositionSeam?.Invoke() ?? Cursor.Position;

    private void SetCursorPosition(Point p)
    {
        if (SetCursorPositionSeam is not null)
            SetCursorPositionSeam(p);
        else
            Cursor.Position = p;
    }

    private Task DelayAsync(int millisecondsDelay, CancellationToken cancellationToken) =>
        DelayAsyncSeam is not null
            ? DelayAsyncSeam(millisecondsDelay, cancellationToken)
            : Task.Delay(millisecondsDelay, cancellationToken);

    private bool TryGetWindowRect(IntPtr hwnd, out Rectangle rect)
    {
        if (GetWindowRectSeam is not null)
        {
            var res = GetWindowRectSeam(hwnd);
            rect = res.rect;
            return res.success;
        }
        return GameWindow.TryGetWindowRect(hwnd, out rect);
    }

    private bool PostKey(IntPtr hwnd, uint virtualKey) =>
        PostKeySeam is not null
            ? PostKeySeam(hwnd, virtualKey)
            : GameWindow.PostKey(hwnd, virtualKey);

    private bool TryReadMousePoint(IntPtr handle, IntPtr baseAddress, out GameMemory.MapPoint point)
    {
        if (ReadMousePointSeam is not null)
        {
            var res = ReadMousePointSeam(handle, baseAddress);
            point = res.point;
            return res.success;
        }
        return GameMemory.TryReadMousePoint(handle, baseAddress, out point);
    }

    private bool TryWriteMousePoint(IntPtr handle, IntPtr baseAddress, GameMemory.MapPoint point) =>
        WriteMousePointSeam is not null
            ? WriteMousePointSeam(handle, baseAddress, point)
            : GameMemory.TryWriteMousePoint(handle, baseAddress, point);

    internal void SetMockConnectionForTest(IntPtr hwnd, IntPtr memHandle, IntPtr memBase)
    {
        _hwnd = hwnd;
        _memHandle = memHandle;
        _memBase = memBase;
        _memPid = 12345;
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

    // ------------------------------------------------------------------ 觸發路徑

    /// <summary>
    /// 按下一顆作弊按鈕。通道可用就走通道，否則退回送鍵；兩條都不可用就明說原因，
    /// 不做任何靜默失敗（ISSUE-068 之前正是靜默失敗讓使用者以為修改器壞了）。
    /// </summary>
    private async Task TriggerCheatAsync(CheatSelection selection, Cheat cheatDef,
                                         uint? virtualKey, bool needsCursor)
    {
        if (_channelReady)
        {
            await RunCheatViaChannelAsync(selection, cheatDef, needsCursor);
            return;
        }

        if (virtualKey is { } vk)
        {
            if (needsCursor) await SpawnAtViewCentreAsync(vk);
            else SendKey(vk);
            return;
        }

        SetStatus(Strings.Get("Gui_Panel_NoChannelNoKey"), ok: false);
    }

    /// <summary>
    /// 把作弊腳本原文送進遊戲執行。
    ///
    /// 生成類作弊需要一個地圖座標：先把游標移到畫面中央，讓引擎自己算出該點的座標，
    /// 讀回來之後游標立刻歸位，最後把那組座標當**字面值**寫進腳本
    /// （<see cref="Cheats.BuildRuntimeScript"/>）。走這條路就不必再把座標寫回遊戲記憶體，
    /// 修改器對執行中遊戲的記憶體存取因此只剩讀取。
    /// </summary>
    private async Task RunCheatViaChannelAsync(CheatSelection selection, Cheat cheatDef, bool needsCursor)
    {
        if (_isSpawning) return;

        (int X, int Y)? point = null;
        if (needsCursor)
        {
            var sampled = await SampleViewCentrePointAsync();
            if (sampled is { } p)
            {
                point = (p.X, p.Y);
                _spawnPoint.Text = Strings.Get("Gui_Panel_SpawnPointAt", p.X, p.Y);
            }
            // 取樣失敗就照原樣送出 MousePtm()：引擎快取裡可能還是使用者剛才停留的點，
            // 猜一個座標寫進去反而更糟（AGENTS.md §2「對不上就拒絕，絕不猜測」）。
        }

        var parameters = Cheats.ResolveParameters(selection.Id, selection.Parameters, _selections);
        string script = Cheats.BuildRuntimeScript(cheatDef, _playerExpression, parameters, point);

        ScriptRunOutcome outcome = await RunScriptAsync(script);
        if (IsDisposed) return;

        SetStatus(ScriptChannel.Describe(outcome), outcome.Ran);

        if (outcome.Status is ScriptStatus.ChannelDisabled or ScriptStatus.Rejected)
        {
            // 通道對面已經不在了；下一次 tick 會重新探測並在必要時退回送鍵。
            _channelReady = false;
        }
    }

    /// <summary>
    /// 送出一段腳本。管線往返最壞情況是好幾秒，所以一律在執行緒集區上等，
    /// 不讓 WinForms 的訊息幫浦停下來（同 <see cref="RefreshChannel"/> 的理由）。
    /// </summary>
    private Task<ScriptRunOutcome> RunScriptAsync(string script)
    {
        if (RunScriptSeam is not null) return Task.FromResult(RunScriptSeam(script));

        ScriptChannel? channel = _channel;
        if (channel is null)
        {
            return Task.FromResult(new ScriptRunOutcome(ScriptStatus.ChannelDisabled, "no client"));
        }

        return Task.Run(() =>
        {
            var result = channel.Run(script);
            return result.IsOk
                ? result.Value
                : new ScriptRunOutcome(ScriptStatus.ChannelDisabled, result.ErrorMessage ?? string.Empty);
        });
    }

    /// <summary>
    /// 把游標暫時移到遊戲畫面中央、讓引擎算出該點的地圖座標、讀回來後立刻歸位。
    /// 這是 <see cref="SpawnAtViewCentreAsync"/> 取樣段落的通道版本：只讀不寫。
    /// </summary>
    private async Task<GameMemory.MapPoint?> SampleViewCentrePointAsync()
    {
        if (IsDisposed || _hwnd == IntPtr.Zero) { RefreshConnection(); return null; }
        if (!TryGetWindowRect(_hwnd, out Rectangle rect) || rect.Width <= 0) return null;

        _isSpawning = true;
        CancellationTokenSource? operationCts = null;
        try
        {
            EnsureMemory();
            if (_memHandle == IntPtr.Zero) return null;

            operationCts = new CancellationTokenSource();
            _spawnCts = operationCts;

            var centre = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            _savedCursor = GetCursorPosition();
            SetCursorPosition(centre);
            _cursorMoved = true;

            var sampled = await SampleMapPointAsync(operationCts.Token);
            operationCts.Token.ThrowIfCancellationRequested();
            return sampled;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch
        {
            try { operationCts?.Cancel(); } catch (ObjectDisposedException) { }
            return null;
        }
        finally
        {
            RestoreCursorIfNeeded();
            if (ReferenceEquals(_spawnCts, operationCts)) _spawnCts = null;
            operationCts?.Dispose();
            _isSpawning = false;
        }
    }

    private void SetStatus(string text, bool ok)
    {
        _status.Text = text;
        _status.ForeColor = ok ? Color.FromArgb(22, 163, 74) : Color.FromArgb(190, 18, 60);
    }

    /// <summary>
    /// 在遊戲畫面中央生成（非阻塞非同步版本，ISSUE-059）。
    ///
    /// 不自己換算螢幕像素到地圖座標（那要重建引擎的相機轉換），而是讓遊戲自己算：
    /// 把游標暫時移到畫面中央，引擎處理滑鼠移動時就會把該點的地圖座標寫進
    /// MousePtm 讀的那個快取（見 GameMemory 的說明），非同步取樣後游標立刻歸位。
    ///
    /// 歸位本身會再產生一次滑鼠移動、把快取蓋掉，所以歸位之後要把取樣到的座標
    /// 寫回去釘住，然後才送鍵。沒有記憶體路徑或全程讀不到座標則走退路：讓游標在中央多停
    /// CursorHoldMs 再歸位，靠時間差讓腳本先讀到。
    /// </summary>
    internal async Task<bool> SpawnAtViewCentreAsync(uint virtualKey)
    {
        if (_isSpawning) return false;
        _isSpawning = true;
        CancellationTokenSource? operationCts = null;

        try
        {
            if (IsDisposed || _hwnd == IntPtr.Zero)
            {
                RefreshConnection();
                return false;
            }

            if (!TryGetWindowRect(_hwnd, out Rectangle rect) || rect.Width <= 0)
            {
                SendKey(virtualKey);
                return false;
            }

            EnsureMemory();

            operationCts = new CancellationTokenSource();
            _spawnCts = operationCts;
            var ct = operationCts.Token;

            var centre = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            _savedCursor = GetCursorPosition();
            SetCursorPosition(centre);
            _cursorMoved = true;

            GameMemory.MapPoint? sampled = await SampleMapPointAsync(ct);
            ct.ThrowIfCancellationRequested();
            if (sampled is { } point)
            {
                RestoreCursorIfNeeded();
                TryWriteMousePoint(_memHandle, _memBase, point);
                SendKey(virtualKey);
                _spawnPoint.Text = Strings.Get("Gui_Panel_SpawnPointAt", point.X, point.Y);
                return true;
            }

            // 退路：讀不到座標就把游標留在中央送鍵，稍後再歸位。
            SendKey(virtualKey);
            try
            {
                await DelayAsync(CursorHoldMs, ct);
            }
            catch (OperationCanceledException)
            {
                // 視窗關閉或 Dispose 時中斷停留
            }
            RestoreCursorIfNeeded();
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch
        {
            try { operationCts?.Cancel(); } catch (ObjectDisposedException) { }
            return false;
        }
        finally
        {
            RestoreCursorIfNeeded();
            if (ReferenceEquals(_spawnCts, operationCts))
                _spawnCts = null;
            operationCts?.Dispose();
            _isSpawning = false;
        }
    }

    /// <summary>
    /// 等引擎把游標所在點換算好。連續兩次讀到相同的值才算穩定——移動當下讀到的
    /// 可能還是舊值。逾時時沿用最後一筆成功讀值；全程讀不到才回傳 null 走退路。
    /// 以非阻塞 Task.Delay 輪詢，不凍結 WinForms 訊息幫浦。
    /// </summary>
    private async Task<GameMemory.MapPoint?> SampleMapPointAsync(CancellationToken cancellationToken)
    {
        if (_memHandle == IntPtr.Zero) return null;

        GameMemory.MapPoint? previous = null;
        for (int waited = 0; waited <= SampleTimeoutMs; waited += SampleStepMs)
        {
            await DelayAsync(SampleStepMs, cancellationToken);
            if (_memHandle == IntPtr.Zero) return null;
            if (!TryReadMousePoint(_memHandle, _memBase, out var current)) continue;
            if (previous is { } p && p == current) return current;
            previous = current;
        }
        return previous;
    }

    /// <summary>依目前的遊戲行程建立／維持記憶體連線；遊戲換了行程就重新連。</summary>
    private void EnsureMemory()
    {
        if (_memHandle != IntPtr.Zero && ReadMousePointSeam is not null) return;
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
        IntPtr previousHwnd = _hwnd;
        IntPtr currentHwnd = GameWindow.Find();
        uint currentPid = currentHwnd == IntPtr.Zero ? 0 : GameWindow.GetProcessId(currentHwnd);
        bool connectionChanged = _isSpawning
            && (currentHwnd == IntPtr.Zero
                || currentHwnd != previousHwnd
                || (_memPid != 0 && currentPid != _memPid));

        _hwnd = currentHwnd;
        bool ok = _hwnd != IntPtr.Zero;
        if (connectionChanged)
        {
            CancelActiveSpawn();
        }
        foreach (var b in _cheatButtons) b.Enabled = ok;
        _speed.Enabled = ok;
        _speedApply.Enabled = ok;

        if (_hasCursorCheats) { EnsureMemory(); UpdateSpawnPointLabel(); }

        RefreshChannel(currentPid);

        if (!ok)
        {
            _status.Text = Strings.Get("Gui_Panel_GameNotFound");
            _status.ForeColor = Color.FromArgb(100, 116, 139);
            return;
        }

        // 三態：找到遊戲但沒有通道（只能送鍵）／找到遊戲且通道就緒（全部作弊可用）。
        _status.Text = Strings.Get(_channelReady
            ? "Gui_Panel_GameConnectedScript"
            : "Gui_Panel_GameConnectedKeys");
        _status.ForeColor = _channelReady
            ? Color.FromArgb(22, 163, 74)
            : Color.FromArgb(217, 119, 6);
    }

    /// <summary>
    /// 依目前的遊戲行程建立／維持腳本通道。
    ///
    /// <para><b>絕對不能在這裡同步等具名管線。</b></para>
    ///
    /// 這個方法由每秒一次的 UI 計時器呼叫。管線往返最壞情況要好幾秒，同步等下去就是
    /// 凍住整個訊息幫浦——ISSUE-059 已經因為同樣的錯誤修過一次。探測一律丟到執行緒集區，
    /// 結果再 marshal 回 UI 執行緒。
    /// </summary>
    private void RefreshChannel(uint currentPid)
    {
        if (RunScriptSeam is not null) { _channelReady = true; return; }

        if (currentPid == 0)
        {
            _channel = null;
            _channelPid = 0;
            _channelReady = false;
            return;
        }

        if (_channel is null || currentPid != _channelPid)
        {
            _channelReady = false;
            _channel = null;
            _channelPid = currentPid;

            var opened = ScriptChannelSession.TryOpen(currentPid);
            if (opened.IsError) return;
            _channel = opened.Value;
        }

        // 已經就緒就不必再探；沒就緒才每秒重試一次，而且同時間只允許一個探測在飛。
        if (_channelReady || _probing || _channel is null) return;
        BeginProbe(_channel, currentPid);
    }

    private void BeginProbe(ScriptChannel channel, uint pid)
    {
        _probing = true;
        // Probe 只確認「連得上、驗證過、抽取點會動」，不在乎有沒有對局——
        // 主選單時通道是好的，只是還沒有東西可以改。
        _ = Task.Run(() => channel.Probe()).ContinueWith(t =>
        {
            bool ready = t.Status == TaskStatus.RanToCompletion && t.Result;
            void Apply()
            {
                _probing = false;
                // 探測期間使用者可能已經換了一場遊戲；結果就過期了，直接丟掉。
                if (pid == _channelPid) _channelReady = ready;
            }

            if (IsDisposed || !IsHandleCreated) { _probing = false; return; }
            try
            {
                if (InvokeRequired) BeginInvoke(Apply);
                else Apply();
            }
            catch (ObjectDisposedException) { _probing = false; }
            catch (InvalidOperationException) { _probing = false; }
        }, TaskScheduler.Default);
    }

    private void SendKey(uint virtualKey)
    {
        if (_hwnd == IntPtr.Zero) { RefreshConnection(); return; }
        if (!PostKey(_hwnd, virtualKey))
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
