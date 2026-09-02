using System.Drawing;
using System.Runtime.InteropServices;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 遊戲視窗偵測與 Win32 按鍵訊息代送機制。
///
/// 引擎只認 20 個硬編按鍵 id，派送時只比對虛擬鍵碼、完全不看 Ctrl/Shift/Alt
/// （見 Core/Trainer/KeyMap.cs）。筆電沒有小鍵盤又不想用 F1~F12 時幾乎無鍵可綁
/// （ISSUES.md ISSUE-053 / ISSUE-054）。解法是：由工具用 Win32 訊息把已綁定的鍵碼
/// 代送給遊戲視窗，使用者改成點面板按鈕。
///
/// 已於 2026-08-31 實機驗證通過：
/// - Celtic kings.exe 匯入表無 DirectInput、無 GetAsyncKeyState，只有
///   PeekMessageA／RegisterClassA／DefWindowProcA／SetCapture／GetKeyState，
///   輸入是古典訊息式。
/// - 對原廠綁定 Mul（極速切換，VK 0x6A）送
///   PostMessageW(WM_KEYDOWN, 0x6A, 0x00370001) + PostMessageW(WM_KEYUP, 0x6A, 0xC0370001)，
///   兩次皆回傳 1、GetLastError=0，遊戲速度確實切換。
/// - 送出當下遊戲並非前景視窗，引擎照樣處理。所以面板不需要 SetForegroundWindow、
///   不需要搶焦點、不需要注入 DLL。
/// - 目標視窗：類別 "OSWndClass"、標題 "Celtic"、client 1280x960。
///   同一個 pid 另有兩個輔助視窗 "MSCTFIME UI" 與 "IME"，client 都是 0x0，必須排除。
///
/// 參考實作在 tools/trainer/postmessage_probe.py。
/// </summary>
internal static partial class GameWindow
{
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP   = 0x0101;
    private const uint MAPVK_VK_TO_VSC = 0;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const string GameExeName = "celtic kings.exe";

    /// <summary>
    /// 延伸鍵集合（lParam bit24）。
    /// 小鍵盤數字 0x60~0x69 與 Add/Sub/Mul 不是延伸鍵，只有小鍵盤除號 0x6F 是。
    /// </summary>
    private static readonly HashSet<uint> ExtendedKeys =
        [0x6F, 0x2D, 0x2E, 0x24, 0x23, 0x21, 0x22, 0x25, 0x26, 0x27, 0x28, 0x90, 0xA3, 0xA5];

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool EnumWindows(delegate* unmanaged<IntPtr, IntPtr, int> lpEnumFunc, IntPtr lParam);

    [LibraryImport("user32.dll")]
    private static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetClientRect(IntPtr hwnd, out RECT lpRect);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [LibraryImport("user32.dll", EntryPoint = "MapVirtualKeyW")]
    private static partial uint MapVirtualKey(uint uCode, uint uMapType);

    [LibraryImport("user32.dll", EntryPoint = "PostMessageW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(IntPtr hProcess, uint dwFlags, char* lpExeName, ref uint lpdwSize);

    private struct CandidateWindow
    {
        public IntPtr Hwnd;
        public long Area;
    }

    [ThreadStatic]
    private static List<CandidateWindow>? s_candidates;

    private static string? GetProcessImageName(uint pid)
    {
        IntPtr hProcess = OpenProcess(ProcessQueryLimitedInformation, false, pid);
        if (hProcess == IntPtr.Zero) return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            uint size = (uint)buffer.Length;
            unsafe
            {
                fixed (char* ptr = buffer)
                {
                    if (QueryFullProcessImageName(hProcess, 0, ptr, ref size))
                    {
                        return new string(ptr, 0, (int)size);
                    }
                }
            }
            return null;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    [UnmanagedCallersOnly]
    private static int EnumWindowsCallback(IntPtr hwnd, IntPtr lParam)
    {
        if (!IsWindowVisible(hwnd)) return 1;

        GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return 1;

        string? image = GetProcessImageName(pid);
        if (image is null) return 1;

        string fileName = Path.GetFileName(image);
        if (!string.Equals(fileName, GameExeName, StringComparison.OrdinalIgnoreCase))
            return 1;

        if (!GetClientRect(hwnd, out RECT clientRect)) return 1;
        int width = clientRect.Right - clientRect.Left;
        int height = clientRect.Bottom - clientRect.Top;
        if (width <= 0 || height <= 0) return 1;

        long area = (long)width * height;
        s_candidates?.Add(new CandidateWindow { Hwnd = hwnd, Area = area });
        return 1;
    }

    /// <summary>找出遊戲主視窗；找不到回傳 IntPtr.Zero。</summary>
    public static unsafe IntPtr Find()
    {
        s_candidates ??= [];
        s_candidates.Clear();

        EnumWindows(&EnumWindowsCallback, IntPtr.Zero);

        if (s_candidates.Count == 0) return IntPtr.Zero;

        IntPtr bestHwnd = IntPtr.Zero;
        long maxArea = -1;
        foreach (var candidate in s_candidates)
        {
            if (candidate.Area > maxArea)
            {
                maxArea = candidate.Area;
                bestHwnd = candidate.Hwnd;
            }
        }
        return bestHwnd;
    }

    /// <summary>取得視窗所屬的行程 ID；失敗回傳 0。面板要用它開啟行程讀寫座標快取。</summary>
    public static uint GetProcessId(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return 0;
        GetWindowThreadProcessId(hwnd, out uint pid);
        return pid;
    }

    /// <summary>組出 WM_KEYDOWN/WM_KEYUP 的 lParam。抽成獨立方法是為了讓 SelfTest 能驗位元編碼。</summary>
    public static int BuildLParam(uint scanCode, bool extended, bool keyUp)
    {
        uint value = 1u | ((scanCode & 0xFFu) << 16) | (extended ? (1u << 24) : 0u);
        if (keyUp)
        {
            value |= (1u << 30) | (1u << 31);
        }
        return unchecked((int)value);
    }

    /// <summary>把一顆鍵代送給遊戲。回傳兩個 PostMessage 是否都成功。</summary>
    public static bool PostKey(IntPtr hwnd, uint virtualKey)
    {
        if (hwnd == IntPtr.Zero) return false;
        uint scan = MapVirtualKey(virtualKey, MAPVK_VK_TO_VSC);
        bool extended = ExtendedKeys.Contains(virtualKey);
        int downLParam = BuildLParam(scan, extended, false);
        int upLParam = BuildLParam(scan, extended, true);
        bool okDown = PostMessage(hwnd, WM_KEYDOWN, (IntPtr)virtualKey, (IntPtr)downLParam);
        bool okUp = PostMessage(hwnd, WM_KEYUP, (IntPtr)virtualKey, (IntPtr)upLParam);
        return okDown && okUp;
    }

    /// <summary>嘗試取得視窗的螢幕矩形範圍。</summary>
    public static bool TryGetWindowRect(IntPtr hwnd, out Rectangle rect)
    {
        if (hwnd != IntPtr.Zero && GetWindowRect(hwnd, out RECT r))
        {
            rect = new Rectangle(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top);
            return true;
        }
        rect = Rectangle.Empty;
        return false;
    }
}
