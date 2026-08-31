# postmessage_probe.py — 「代按熱鍵」通道測試
#
# 驗證能不能用 Win32 訊息把《Celtic Kings: Rage of War》的 scdebug 熱鍵送進遊戲。
# 這是修改器面板（面板按鈕 -> 觸發作弊）的前置測試：如果 PostMessage(WM_KEYDOWN)
# 就能觸發作弊，面板完全不需要注入 DLL，也不需要使用者實際去按那顆鍵——筆電沒有
# 小鍵盤、或不想用 F1~F12 的問題就直接消失。
#
# 為什麼有理由相信行得通（Celtic kings.exe 匯入表實測）：
#
#     DirectInput / dinput      0 次
#     GetAsyncKeyState          0 次
#     PeekMessageA              1 次
#     RegisterClassA            1 次
#     DefWindowProcA            1 次
#     SetCapture / GetKeyState  各 1 次
#
# 完全沒有輪詢式輸入，就是一個古典的訊息迴圈 + WndProc。posted message 進得了
# 訊息佇列，理論上就會被 DispatchMessage 送到引擎手上。
#
# 已知的引擎側位址（逆向資產，不要精簡掉）：
#   .text VA 0x0047D380  鍵盤處理常式（虛擬函式，無直接呼叫者）：
#                          mov eax,[esp+4] / cmp eax,0x201
#                          mov eax,[esp+0x58] / cmp ax,0x0D  (VK_RETURN，叫出主控台)
#   .text VA 0x005E76A5  scdebug 派送：對 0x008AF108 的 map 以 16 位元鍵碼查表。
#
# 本檔屬於 tools/，是交叉驗證用的 oracle，不參與建置（AGENTS.md §3）。
#
# 注意：本檔會呼叫 SendInput / keybd_event 合成鍵盤輸入，防毒軟體（本機是
# Kaspersky）可能把它判成按鍵側錄／自動點擊工具而直接刪檔。若檔案憑空消失，
# 請到防毒的隔離區還原，並把本專案目錄加入信任區。
#
# 用法：
#     py tools/trainer/postmessage_probe.py --list
#     py tools/trainer/postmessage_probe.py --key numpad1
#     py tools/trainer/postmessage_probe.py --key numpad1 --method all --delay 5
#
# 注意 --key 要傳 scdebug **實際綁到的實體鍵**。小鍵盤模式下 "F1" 這個 id 已經被
# 改對應到小鍵盤 1（鍵碼 0x61），所以要傳 --key numpad1，不是 --key f1。

import argparse
import ctypes
import os
import sys
import time
from ctypes import wintypes

# ---------------------------------------------------------------- Win32 常數

WM_KEYDOWN = 0x0100
WM_KEYUP   = 0x0101
WM_CHAR    = 0x0102

MAPVK_VK_TO_VSC  = 0
MAPVK_VK_TO_CHAR = 2

KEYEVENTF_EXTENDEDKEY = 0x0001
KEYEVENTF_KEYUP       = 0x0002

INPUT_KEYBOARD = 1

PROCESS_QUERY_LIMITED_INFORMATION = 0x1000

# 延伸鍵（lParam bit24 = 1）。小鍵盤數字 0x60~0x69 與 Add/Sub/Mul **不是**延伸鍵，
# 只有小鍵盤的除號 0x6F 是；弄錯的話掃描碼對得上但引擎可能認成別顆鍵。
EXTENDED_KEYS = {
    0x6F,                        # VK_DIVIDE
    0x2D, 0x2E,                  # VK_INSERT, VK_DELETE
    0x24, 0x23,                  # VK_HOME, VK_END
    0x21, 0x22,                  # VK_PRIOR, VK_NEXT
    0x25, 0x26, 0x27, 0x28,      # 方向鍵
    0x90,                        # VK_NUMLOCK
    0xA3, 0xA5,                  # VK_RCONTROL, VK_RMENU
}

# scdebug 那 20 個 id 在兩種模式下實際對應的實體鍵，加上常用別名。
KEY_NAMES = {
    "numpad0": 0x60, "numpad1": 0x61, "numpad2": 0x62, "numpad3": 0x63,
    "numpad4": 0x64, "numpad5": 0x65, "numpad6": 0x66, "numpad7": 0x67,
    "numpad8": 0x68, "numpad9": 0x69,
    "mul": 0x6A, "add": 0x6B, "sub": 0x6D, "decimal": 0x6E, "div": 0x6F,
    "f1": 0x70, "f2": 0x71, "f3": 0x72, "f4": 0x73, "f5": 0x74, "f6": 0x75,
    "f7": 0x76, "f8": 0x77, "f9": 0x78, "f10": 0x79, "f11": 0x7A, "f12": 0x7B,
    "pause": 0x13, "del": 0x2E, "delete": 0x2E, "ins": 0x2D, "insert": 0x2D,
    "backspace": 0x08, "tab": 0x09, "enter": 0x0D, "home": 0x24, "end": 0x23,
}

# ---------------------------------------------------------------- ctypes 宣告

user32   = ctypes.WinDLL("user32",   use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

ULONG_PTR = ctypes.c_ulonglong if ctypes.sizeof(ctypes.c_void_p) == 8 else ctypes.c_ulong

WNDENUMPROC = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)

class KEYBDINPUT(ctypes.Structure):
    _fields_ = [("wVk", wintypes.WORD), ("wScan", wintypes.WORD),
                ("dwFlags", wintypes.DWORD), ("time", wintypes.DWORD),
                ("dwExtraInfo", ULONG_PTR)]

class _INPUTUNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("padding", ctypes.c_byte * 24)]

class INPUT(ctypes.Structure):
    _anonymous_ = ("u",)
    _fields_ = [("type", wintypes.DWORD), ("u", _INPUTUNION)]

user32.EnumWindows.argtypes = [WNDENUMPROC, wintypes.LPARAM]
user32.EnumWindows.restype = wintypes.BOOL
user32.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
user32.GetWindowThreadProcessId.restype = wintypes.DWORD
user32.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetClassNameW.restype = ctypes.c_int
user32.GetWindowTextW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
user32.GetWindowTextW.restype = ctypes.c_int
user32.GetWindowRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.GetClientRect.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.RECT)]
user32.IsWindowVisible.argtypes = [wintypes.HWND]
user32.GetForegroundWindow.restype = wintypes.HWND
user32.SetForegroundWindow.argtypes = [wintypes.HWND]
user32.MapVirtualKeyW.argtypes = [wintypes.UINT, wintypes.UINT]
user32.MapVirtualKeyW.restype = wintypes.UINT
user32.VkKeyScanW.argtypes = [wintypes.WCHAR]
user32.VkKeyScanW.restype = ctypes.c_short
user32.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.PostMessageW.restype = wintypes.BOOL
user32.SendMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
user32.SendMessageW.restype = wintypes.LPARAM
user32.SendInput.argtypes = [wintypes.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
user32.SendInput.restype = wintypes.UINT
user32.keybd_event.argtypes = [wintypes.BYTE, wintypes.BYTE, wintypes.DWORD, ULONG_PTR]
kernel32.OpenProcess.argtypes = [wintypes.DWORD, wintypes.BOOL, wintypes.DWORD]
kernel32.OpenProcess.restype = wintypes.HANDLE
kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
kernel32.QueryFullProcessImageNameW.argtypes = [
    wintypes.HANDLE, wintypes.DWORD, wintypes.LPWSTR, ctypes.POINTER(wintypes.DWORD)]
kernel32.QueryFullProcessImageNameW.restype = wintypes.BOOL

# ---------------------------------------------------------------- 尋找遊戲視窗

GAME_EXE = "celtic kings.exe"


def _process_image(pid):
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not handle:
        return None
    try:
        size = wintypes.DWORD(32768)
        buf = ctypes.create_unicode_buffer(size.value)
        if kernel32.QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size)):
            return buf.value
        return None
    finally:
        kernel32.CloseHandle(handle)


def find_game_windows():
    """列舉所有屬於 Celtic kings.exe 的最上層視窗。"""
    found = []

    def callback(hwnd, _lparam):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        image = _process_image(pid.value)
        if image and os.path.basename(image).lower() == GAME_EXE:
            cls = ctypes.create_unicode_buffer(256)
            user32.GetClassNameW(hwnd, cls, 256)
            title = ctypes.create_unicode_buffer(512)
            user32.GetWindowTextW(hwnd, title, 512)
            wr, cr = wintypes.RECT(), wintypes.RECT()
            user32.GetWindowRect(hwnd, ctypes.byref(wr))
            user32.GetClientRect(hwnd, ctypes.byref(cr))
            found.append({
                "hwnd": hwnd, "pid": pid.value, "class": cls.value,
                "title": title.value, "visible": bool(user32.IsWindowVisible(hwnd)),
                "window": (wr.left, wr.top, wr.right - wr.left, wr.bottom - wr.top),
                "client": (cr.right, cr.bottom),
            })
        return True

    user32.EnumWindows(WNDENUMPROC(callback), 0)
    return found


def describe(windows):
    foreground = user32.GetForegroundWindow()
    for w in windows:
        cw, ch = w["client"]
        print(f"  HWND 0x{w['hwnd']:08X}  pid {w['pid']}")
        print(f"    類別       {w['class']!r}")
        print(f"    標題       {w['title']!r}")
        print(f"    可見       {'是' if w['visible'] else '否'}"
              f"   前景視窗 {'是' if w['hwnd'] == foreground else '否'}")
        print(f"    視窗矩形   x={w['window'][0]} y={w['window'][1]} "
              f"{w['window'][2]}x{w['window'][3]}")
        print(f"    Client     {cw}x{ch}")


def pick(windows):
    """優先取可見且 client 區域非空的；有多個就取面積最大的。"""
    usable = [w for w in windows if w["visible"] and w["client"][0] > 0 and w["client"][1] > 0]
    pool = usable or windows
    return max(pool, key=lambda w: w["client"][0] * w["client"][1])

# ---------------------------------------------------------------- 鍵名解析


def resolve_key(text):
    key = text.strip().lower()
    if key in KEY_NAMES:
        return KEY_NAMES[key], None
    if key.startswith("0x"):
        try:
            vk = int(key, 16)
        except ValueError:
            return None, f"無法解析十六進位鍵碼 '{text}'"
        if not 0 < vk <= 0xFF:
            return None, f"鍵碼超出範圍 '{text}'"
        return vk, None
    if len(text.strip()) == 1:
        res = user32.VkKeyScanW(text.strip())
        if res == -1:
            return None, f"目前鍵盤配置打不出字元 '{text}'"
        return res & 0xFF, None
    return None, f"無法辨識的鍵名 '{text}'"

# ---------------------------------------------------------------- 五種送法


def send(hwnd, vk, method):
    scan = user32.MapVirtualKeyW(vk, MAPVK_VK_TO_VSC)
    ext = 1 if vk in EXTENDED_KEYS else 0
    down = 1 | (scan << 16) | (ext << 24)
    up = down | (1 << 30) | (1 << 31)

    print(f"  鍵碼 0x{vk:02X}  掃描碼 0x{scan:02X}  extended={ext}  "
          f"lParam down=0x{down:08X} up=0x{up:08X}")
    print(f"  送出當下遊戲是前景視窗：{'是' if user32.GetForegroundWindow() == hwnd else '否'}")

    if method == "post":
        ctypes.set_last_error(0)
        rd = user32.PostMessageW(hwnd, WM_KEYDOWN, vk, down)
        ed = ctypes.get_last_error()
        time.sleep(0.03)
        ctypes.set_last_error(0)
        ru = user32.PostMessageW(hwnd, WM_KEYUP, vk, up)
        eu = ctypes.get_last_error()
        print(f"  PostMessageW WM_KEYDOWN -> {rd} (GetLastError={ed})")
        print(f"  PostMessageW WM_KEYUP   -> {ru} (GetLastError={eu})")

    elif method == "send":
        rd = user32.SendMessageW(hwnd, WM_KEYDOWN, vk, down)
        time.sleep(0.03)
        ru = user32.SendMessageW(hwnd, WM_KEYUP, vk, up)
        print(f"  SendMessageW WM_KEYDOWN -> {rd}")
        print(f"  SendMessageW WM_KEYUP   -> {ru}")

    elif method == "char":
        if 0x60 <= vk <= 0x69:
            code = ord("0") + (vk - 0x60)
        else:
            code = user32.MapVirtualKeyW(vk, MAPVK_VK_TO_CHAR) & 0xFFFF
        if not code:
            print("  [跳過] 這顆鍵沒有對應字元，WM_CHAR 無從送起")
            return
        r = user32.PostMessageW(hwnd, WM_CHAR, code, down)
        print(f"  PostMessageW WM_CHAR ({code!r} = {chr(code)!r}) -> {r}")

    elif method in ("sendinput", "keybd"):
        # 這兩種是「真實輸入」對照組：走系統的輸入佇列，任何靠訊息讀鍵盤的程式
        # 都一定收得到。如果連它們都沒反應，代表問題不在 PostMessage，而是
        # 那顆鍵根本沒綁到作弊、或修改器沒套用。兩者都需要遊戲在前景。
        user32.SetForegroundWindow(hwnd)
        time.sleep(0.2)
        flags = KEYEVENTF_EXTENDEDKEY if ext else 0
        if method == "sendinput":
            def make(f):
                return INPUT(type=INPUT_KEYBOARD,
                             ki=KEYBDINPUT(wVk=vk, wScan=scan, dwFlags=f, time=0, dwExtraInfo=0))
            batch = (INPUT * 2)(make(flags), make(flags | KEYEVENTF_KEYUP))
            n = user32.SendInput(2, batch, ctypes.sizeof(INPUT))
            print(f"  SendInput -> 送出 {n}/2 筆")
        else:
            user32.keybd_event(vk, scan, flags, 0)
            time.sleep(0.03)
            user32.keybd_event(vk, scan, flags | KEYEVENTF_KEYUP, 0)
            print("  keybd_event down/up 已送出")

# ---------------------------------------------------------------- 主程式


def main():
    ap = argparse.ArgumentParser(
        description="測試能不能用 Win32 訊息代按 scdebug 熱鍵")
    ap.add_argument("--list", action="store_true", help="只列出遊戲視窗，不送訊息")
    ap.add_argument("--key", help="要送的實體鍵，例如 numpad1 / f4 / del / 0x61 / [")
    ap.add_argument("--method", default="post",
                    choices=["post", "send", "char", "sendinput", "keybd", "all"],
                    help="送法，預設 post")
    ap.add_argument("--delay", type=int, default=5, help="送出前倒數秒數，預設 5")
    ap.add_argument("--repeat", type=int, default=1, help="重複次數，預設 1")
    args = ap.parse_args()

    windows = find_game_windows()
    if not windows:
        print("找不到遊戲視窗。請先啟動 Celtic kings.exe 並進入一場遊戲。", file=sys.stderr)
        return 2

    print(f"找到 {len(windows)} 個遊戲視窗：")
    describe(windows)

    if args.list:
        return 0
    if not args.key:
        print("\n缺少 --key。範例：--key numpad1", file=sys.stderr)
        return 3

    vk, err = resolve_key(args.key)
    if err:
        print(err, file=sys.stderr)
        return 3

    target = pick(windows)
    print(f"\n目標視窗：HWND 0x{target['hwnd']:08X}")

    for remaining in range(args.delay, 0, -1):
        print(f"  {remaining} 秒後送出——請切回遊戲畫面")
        time.sleep(1)

    methods = ["post", "send", "char", "sendinput", "keybd"] if args.method == "all" \
        else [args.method]

    for method in methods:
        print(f"\n--- method = {method} ---")
        for i in range(args.repeat):
            if args.repeat > 1:
                print(f"  第 {i + 1}/{args.repeat} 次")
            send(target["hwnd"], vk, method)
            time.sleep(0.25)
        if len(methods) > 1:
            time.sleep(1.5)

    print("""
------------------------------------------------------------
請看遊戲畫面，回報：

  1. 有沒有出現作弊的捲動文字訊息？
  2. 有的話是哪一個 method 觸發的？（--method all 會依序試五種）

測試前請先確認：
  * 修改器已套用（trainer apply），該作弊已啟用；
  * --key 傳的是那個作弊**實際綁到的實體鍵**。小鍵盤模式下 scdebug 的
    "F1" 已被改對應到小鍵盤 1，所以要傳 --key numpad1 而不是 --key f1；
  * post / send / char 不需要遊戲在前景，sendinput / keybd 需要。
------------------------------------------------------------""")
    return 0


if __name__ == "__main__":
    sys.exit(main())
