using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Celtic kings Launcher.exe -> hardcoded display-mode table (自動切換桌面解析度)
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// The other way to deal with the launcher: instead of suppressing the mode
/// change, retarget it. The table it matches against lives in .rdata at
/// VA 0x1400043B0 (file offset 0x2BB0) as four (width, height) int32 pairs:
///
///     [0] 1600 x 1200      [1] 1280 x 1024
///     [2] 1152 x  864      [3] 1024 x  768
///
/// The launcher picks the lowest index the display actually enumerates, so
/// rewriting entry 0 to the resolution the game renders at makes the launcher
/// set the desktop to it on start and restore the previous mode on exit. That
/// removes the black border you otherwise get when the game renders smaller than
/// the desktop, without any scaling anywhere.
///
/// ★ 互斥紀律：本功能與 LauncherDisplay 互斥。LauncherDisplay 把 ChangeDisplaySettingsA
/// 呼叫 NOP 掉，套用後模式表即為死碼。因此啟用其一必須停用另一。
/// </summary>
public static class LauncherModeTable
{
    public const int TableOffset = 0x2BB0; // VA 0x1400043B0 in .rdata (RVA 0x4000 -> file offset 0x2800)
    public static readonly int[] StockTable = [1600, 1200, 1280, 1024, 1152, 864, 1024, 768];

    /// <summary>
    /// 檢查 Launcher 模式表之後續第 1..3 筆指紋特徵是否符合預期版本。
    /// </summary>
    public static bool CheckFingerprint(byte[] launcherBytes)
    {
        if (launcherBytes.Length < TableOffset + StockTable.Length * 4) return false;

        for (int i = 2; i < 8; i++)
        {
            int val = BitConverter.ToInt32(launcherBytes, TableOffset + i * 4);
            if (val != StockTable[i]) return false;
        }

        return true;
    }

    /// <summary>
    /// 讀取當前模式表第 0 筆之寬與高。
    /// </summary>
    public static (int Width, int Height) ReadEntry0(byte[] launcherBytes)
    {
        if (launcherBytes.Length < TableOffset + 8) return (0, 0);
        int w = BitConverter.ToInt32(launcherBytes, TableOffset);
        int h = BitConverter.ToInt32(launcherBytes, TableOffset + 4);
        return (w, h);
    }

    /// <summary>
    /// 檢查給定之 Launcher 位元組是否已改寫模式表第 0 筆。
    /// </summary>
    public static bool IsApplied(byte[] launcherBytes)
    {
        if (!CheckFingerprint(launcherBytes)) return false;

        var (w, h) = ReadEntry0(launcherBytes);
        return w != StockTable[0] || h != StockTable[1];
    }

    /// <summary>
    /// 改寫或還原 Launcher 模式表第 0 筆。
    /// </summary>
    public static void Apply(ref byte[] launcherBytes, bool enable, int width = 1920, int height = 1080)
    {
        if (launcherBytes.Length < TableOffset + 8) return;

        int w = enable ? width : StockTable[0];
        int h = enable ? height : StockTable[1];

        BitConverter.TryWriteBytes(launcherBytes.AsSpan(TableOffset, 4), w);
        BitConverter.TryWriteBytes(launcherBytes.AsSpan(TableOffset + 4, 4), h);
    }
}

/// <summary>
/// BackupManager 之 launcher_mode_table 修補特徵偵測器 (SPEC.md §3 / §5)。
/// </summary>
public sealed class LauncherModeTableSignature : IPatchSignature
{
    public string PatchId => "launcher_mode_table";
    public GameFile AppliesTo => GameFile.Launcher;
    public bool IsApplied(byte[] fileBytes) => LauncherModeTable.IsApplied(fileBytes);
}
