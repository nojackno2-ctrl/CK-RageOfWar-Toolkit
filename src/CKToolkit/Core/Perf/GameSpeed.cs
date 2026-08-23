using System.Runtime.InteropServices;

namespace CKToolkit.Core.Perf;

/// <summary>
/// 遊戲加速器 —— 讓「要跑很久才會出現」的問題（閃退、記憶體成長、AI 卡頓）
/// 在幾分鐘內就重現，不必真的坐在那裡等一小時。
///
/// 這裡不改遊戲一個位元組。用的是引擎自己就有的東西：
///
///   * 腳本函式 <c>SetSpeed(int)</c> / <c>GetSpeed()</c>（見 docs/VS腳本速查.md）。
///     引擎的原生速度基準是 1000，原版 scdebug.xml 綁的「極速切換」就是
///     <c>if (GetSpeed() != 10000) SetSpeed(10000); else SetSpeed(1000);</c>
///     ——也就是 10 倍速，這是原廠功能，不是修改。
///   * 原版 scdebug.xml 的按鍵綁定（見 Cheats.VanillaBindings）：
///     Add = 加速一級、Sub = 減速一級、Mul = 極速切換。
///   * 內建開發者主控台（見 docs/內建主控台.md）：出廠就是啟用的，
///     遊戲中按 Enter 叫出輸入列，輸入的內容會直接送進腳本編譯器執行。
///
/// 送鍵的方式是 <c>SendInput</c>，也就是走系統的真實輸入佇列，和使用者自己按下去
/// 完全同一條路徑：不管引擎是從視窗訊息、GetKeyState 還是 DirectInput 讀鍵盤都吃得到。
/// 代價是 SendInput 一律送給「目前有焦點的視窗」，所以送之前必須先把遊戲視窗帶到前景。
///
/// **帶不到前景就絕不送鍵。** 這是硬性規定：主控台方式會實際打字，
/// 要是焦點還在使用者的其他視窗上，那串 <c>SetSpeed(20000);</c> 就會被打進別人的文件裡。
///
/// 兩種方式的差別：
///   Hotkey  —— 只靠原版綁定，最安全，倍率受限於原版的預設值（極速 = 10 倍）。
///   Console —— 走主控台直接下 <c>SetSpeed(n);</c>，倍率任意，但會在畫面上留下輸入列痕跡。
///
/// 兩種方式都可逆：<see cref="Restore"/> 會把速度設回原生的 1000。
/// </summary>
public static partial class GameSpeed
{
    /// <summary>引擎的原生速度基準值。SetSpeed(1000) = 正常速度。</summary>
    public const int NormalSpeed = 1000;

    /// <summary>原版「極速切換」用的值，等於 10 倍速。</summary>
    public const int TurboSpeed = 10000;

    public enum Method
    {
        /// <summary>用原版 scdebug 的按鍵綁定（Add / Mul）。</summary>
        Hotkey,

        /// <summary>用內建主控台直接下 SetSpeed()，倍率任意。</summary>
        Console
    }

    public sealed record Outcome(bool Success, string Message);

    #region Win32

    private const int VkReturn = 0x0D;
    private const int VkMultiply = 0x6A;   // 小鍵盤 *  —— 原版「極速切換」
    private const int VkAdd = 0x6B;        // 小鍵盤 +  —— 原版「加速一級」

    private const uint InputKeyboard = 1;
    private const uint KeyEventKeyUp = 0x0002;
    private const uint KeyEventUnicode = 0x0004;

    private const int SwRestore = 9;

    /// <summary>
    /// 由工具啟動遊戲時，行程剛建立的那一刻視窗通常還沒出現（讀取資料、顯示啟動畫面），
    /// 這裡等視窗出現，而不是找一次沒找到就直接判失敗——不然「由工具啟動」模式的加速器
    /// 會在遊戲根本還沒開好視窗的時候就放棄，一次也沒送到鍵。
    /// </summary>
    private const int WindowWaitAttempts = 30;
    private const int WindowWaitIntervalMs = 500;

    /// <summary>KEYBDINPUT。x64 下是 2+2+4+4+(4 對齊)+8 = 24 bytes。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput
    {
        public ushort Vk;
        public ushort Scan;
        public uint Flags;
        public uint Time;
        public IntPtr ExtraInfo;
    }

    /// <summary>
    /// INPUT。x64 下 type 佔 4 bytes，union 因為含有 ULONG_PTR 而對齊到 8，
    /// 所以 union 從位移 8 開始，整個結構是 40 bytes。
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct Input
    {
        [FieldOffset(0)] public uint Type;
        [FieldOffset(8)] public KeyboardInput Keyboard;
    }

    [LibraryImport("user32.dll", SetLastError = true)]
    private static unsafe partial uint SendInput(uint count, Input* inputs, int size);

    [LibraryImport("user32.dll")]
    private static partial uint MapVirtualKeyW(uint code, uint mapType);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int cmdShow);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetForegroundWindow();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool BringWindowToTop(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetActiveWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial IntPtr SetFocus(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachThreadInput(uint attachTo, uint attachFrom, [MarshalAs(UnmanagedType.Bool)] bool attach);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetCurrentThreadId();

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> callback, IntPtr lParam);

    private static uint s_wantPid;
    private static IntPtr s_found;

    [UnmanagedCallersOnly]
    private static int EnumProc(IntPtr hwnd, IntPtr lParam)
    {
        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == s_wantPid && IsWindowVisible(hwnd))
        {
            s_found = hwnd;
            return 0;
        }
        return 1;
    }

    private static unsafe IntPtr FindWindowOf(uint pid)
    {
        s_wantPid = pid;
        s_found = IntPtr.Zero;
        EnumWindows(&EnumProc, IntPtr.Zero);
        return s_found;
    }

    #endregion

    /// <summary>把遊戲速度調成 <paramref name="multiplier"/> 倍。</summary>
    public static Outcome Apply(uint pid, int multiplier, Method method)
    {
        if (multiplier <= 1) return new Outcome(true, "加速器未啟用（倍率 1 倍）。");

        var (hwnd, error) = FocusGameWindow(pid);
        if (hwnd == IntPtr.Zero) return new Outcome(false, $"加速失敗：{error}");

        return method == Method.Console
            ? ApplyByConsole(hwnd, multiplier)
            : ApplyByHotkey(multiplier);
    }

    /// <summary>把速度設回原生的 1000。分析結束時呼叫，讓遊戲回到正常速度。</summary>
    public static Outcome Restore(uint pid, Method method)
    {
        var (hwnd, error) = FocusGameWindow(pid);
        if (hwnd == IntPtr.Zero) return new Outcome(false, $"還原速度失敗：{error}");

        if (method == Method.Console)
        {
            return RunConsole($"SetSpeed({NormalSpeed});")
                ? new Outcome(true, "已把遊戲速度設回正常。")
                : new Outcome(false, "還原速度失敗：主控台指令送不進去。");
        }

        // 原版的 Mul 是切換：現在是 10000 就切回 1000。
        Tap(VkMultiply);
        return new Outcome(true, "已送出極速切換鍵，遊戲速度應該回到正常。");
    }

    /// <summary>
    /// 找到遊戲視窗並把它帶到前景。回傳 (hwnd, 錯誤說明)；hwnd 是 Zero 就代表不能送鍵。
    /// </summary>
    private static (IntPtr Hwnd, string Error) FocusGameWindow(uint pid)
    {
        IntPtr hwnd = IntPtr.Zero;
        for (int i = 0; i < WindowWaitAttempts; i++)
        {
            hwnd = FindWindowOf(pid);
            if (hwnd != IntPtr.Zero) break;
            Thread.Sleep(WindowWaitIntervalMs);
        }

        if (hwnd == IntPtr.Zero)
        {
            double waitedSeconds = WindowWaitAttempts * WindowWaitIntervalMs / 1000.0;
            return (IntPtr.Zero, $"找不到遊戲視窗（等了 {waitedSeconds:0} 秒仍未出現，遊戲可能還在載入或啟動失敗）。");
        }

        if (!BringToForeground(hwnd))
            return (IntPtr.Zero, "遊戲視窗帶不到前景，為了不把按鍵送錯視窗而放棄。" +
                                 "（遊戲若以系統管理員身分執行，請用同樣權限開啟本工具。）");

        return (hwnd, string.Empty);
    }

    /// <summary>
    /// 把視窗搶到前景。
    ///
    /// Windows 只允許「目前的前景執行緒」變更前景視窗，直接呼叫 SetForegroundWindow
    /// 通常只會讓工作列閃一下。標準解法是先用 AttachThreadInput 把自己的輸入佇列
    /// 併進前景執行緒與目標執行緒，取得那個權利，做完再拆掉。
    /// </summary>
    private static bool BringToForeground(IntPtr hwnd)
    {
        if (!IsWindow(hwnd)) return false;
        if (IsIconic(hwnd)) ShowWindow(hwnd, SwRestore);
        if (GetForegroundWindow() == hwnd) return true;

        uint self = GetCurrentThreadId();
        uint target = GetWindowThreadProcessId(hwnd, out _);
        uint fore = GetWindowThreadProcessId(GetForegroundWindow(), out _);

        bool attachedFore = fore != 0 && fore != self && AttachThreadInput(self, fore, true);
        bool attachedTarget = target != 0 && target != self && AttachThreadInput(self, target, true);

        SetForegroundWindow(hwnd);
        BringWindowToTop(hwnd);
        SetActiveWindow(hwnd);
        SetFocus(hwnd);

        if (attachedTarget) AttachThreadInput(self, target, false);
        if (attachedFore) AttachThreadInput(self, fore, false);

        // 前景切換是非同步的，而且全螢幕遊戲切回來可能要重建畫面，所以給它一秒。
        for (int i = 0; i < 40 && GetForegroundWindow() != hwnd; i++) Thread.Sleep(25);
        return GetForegroundWindow() == hwnd;
    }

    /// <summary>
    /// 走原版按鍵綁定。10 倍以上直接用「極速切換」(Mul)；
    /// 低於 10 倍就按 Add 逐級加速（原版有 Speed1..Speed5 五級，按 4 次到頂）。
    /// </summary>
    private static Outcome ApplyByHotkey(int multiplier)
    {
        if (multiplier >= 10)
        {
            Tap(VkMultiply);
            return new Outcome(true, "已送出原版「極速切換」(小鍵盤 *)，遊戲速度約 10 倍。");
        }

        // Speed1..Speed5 的實際數值寫在 vxConst.ini，這裡不假設它們是多少，
        // 只按到最高一級；要精確倍率請改用主控台方式。
        for (int i = 0; i < 4; i++)
        {
            Tap(VkAdd);
            Thread.Sleep(80);
        }
        return new Outcome(true, "已送出 4 次原版「加速」(小鍵盤 +)，遊戲速度到達原版最高一級。");
    }

    /// <summary>走內建主控台，倍率任意。</summary>
    private static Outcome ApplyByConsole(IntPtr hwnd, int multiplier)
    {
        int speed = NormalSpeed * multiplier;
        return RunConsole($"SetSpeed({speed});")
            ? new Outcome(true, $"已透過內建主控台下 SetSpeed({speed})，遊戲速度約 {multiplier} 倍。")
            : new Outcome(false, "加速失敗：主控台指令送不進去（主控台被關掉了？）。");
    }

    /// <summary>
    /// 「Enter 開輸入列 → 打字 → Enter 執行」。
    ///
    /// 引擎的主控台觸發會檢查 Shift / Alt 沒有按著（Celtic kings.exe 0x47D39B 起），
    /// 所以送出前後都不能有殘留的修飾鍵——SendInput 只送這幾顆鍵，不碰修飾鍵，
    /// 使用者手上如果正按著 Shift 那是另一回事，但那也是他自己按的。
    ///
    /// 字元一律用 KEYEVENTF_UNICODE 送，直接產生 WM_CHAR，不必去處理
    /// 括號和數字需不需要 Shift、也不受鍵盤配置影響。
    /// </summary>
    private static bool RunConsole(string command)
    {
        if (!Tap(VkReturn)) return false;
        Thread.Sleep(300);

        foreach (char c in command)
        {
            if (!TypeChar(c)) return false;
            Thread.Sleep(20);
        }

        Thread.Sleep(150);
        return Tap(VkReturn);
    }

    /// <summary>送出一次完整的按下 / 放開；同時帶虛擬鍵碼與掃描碼，兩種讀法的引擎都吃得到。</summary>
    private static unsafe bool Tap(int vk)
    {
        ushort scan = (ushort)MapVirtualKeyW((uint)vk, 0);

        var inputs = stackalloc Input[2];
        inputs[0] = new Input
        {
            Type = InputKeyboard,
            Keyboard = new KeyboardInput { Vk = (ushort)vk, Scan = scan, Flags = 0 }
        };
        inputs[1] = new Input
        {
            Type = InputKeyboard,
            Keyboard = new KeyboardInput { Vk = (ushort)vk, Scan = scan, Flags = KeyEventKeyUp }
        };

        if (SendInput(1, &inputs[0], sizeof(Input)) != 1) return false;
        Thread.Sleep(40); // 按住一小段時間，太快的鍵有些引擎會漏掉
        return SendInput(1, &inputs[1], sizeof(Input)) == 1;
    }

    /// <summary>用 Unicode 事件送一個字元。</summary>
    private static unsafe bool TypeChar(char c)
    {
        var inputs = stackalloc Input[2];
        inputs[0] = new Input
        {
            Type = InputKeyboard,
            Keyboard = new KeyboardInput { Vk = 0, Scan = c, Flags = KeyEventUnicode }
        };
        inputs[1] = new Input
        {
            Type = InputKeyboard,
            Keyboard = new KeyboardInput { Vk = 0, Scan = c, Flags = KeyEventUnicode | KeyEventKeyUp }
        };

        return SendInput(2, &inputs[0], sizeof(Input)) == 2;
    }
}
