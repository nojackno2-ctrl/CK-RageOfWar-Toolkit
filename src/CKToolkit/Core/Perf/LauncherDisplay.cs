using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Celtic kings Launcher.exe -> stop forcing the desktop resolution (Display suppression)
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// The launcher is a separate 2019-era x64 executable, so none of the game exe patches
/// touch it. Before starting the game it enumerates the display's modes, matches them
/// against a hardcoded 4-entry table at VA 0x1400043B0 (1600x1200, 1280x1024, 1152x864, 1024x768),
/// picks the lowest matching index -- i.e. the highest of those four the monitor supports --
/// and calls ChangeDisplaySettingsA to force the desktop to it. On a modern monitor that
/// reports 1600x1200, launching the game silently drops the desktop to 1600x1200 no matter
/// what resolution the game itself is set to render at.
///
/// Suppressing the mode change entirely is both smaller and closer to what we want,
/// since the game no longer changes modes either:
///
///   0x14000159B  74 37 (je)          -> EB 37 (jmp)  -- always skip the set
///   0x1400019F9  FF 15 C9 26 00 00   -> 6x 90        -- and skip the restore
///
/// With both suppressed the launcher never touches display settings, so the
/// desktop stays wherever the user put it and the game renders at its selected
/// resolution into a matching desktop.
///
/// ★ 互斥紀律：本功能與 LauncherModeTable 互斥。啟用本功能時必須關閉 LauncherModeTable。
/// </summary>
public static class LauncherDisplay
{
    public sealed record LauncherSite(uint Rva, byte[] Orig, byte[] Patch);

    public static readonly LauncherSite[] Sites =
    [
        // 0x14000159B: je 0x1400015D4 -> jmp 0x1400015D4 (skip ChangeDisplaySettings block)
        new(0x159B, [0x74, 0x37], [0xEB, 0x37]),

        // 0x1400019F9: call ChangeDisplaySettingsA(NULL, 0) -> nop (skip restore on exit)
        new(0x19F9, [0xFF, 0x15, 0xC9, 0x26, 0x00, 0x00], [0x90, 0x90, 0x90, 0x90, 0x90, 0x90])
    ];

    /// <summary>
    /// 計算 Launcher .text 區段之檔案位移 (RVA 0x1000 -> 檔案位移 0x400)。
    /// </summary>
    public static bool TryGetFileOffset(uint rva, int length, out int fileOffset)
    {
        if (rva < 0x1000)
        {
            fileOffset = 0;
            return false;
        }
        fileOffset = (int)(rva - 0x1000 + 0x400);
        return true;
    }

    /// <summary>
    /// 檢查給定之 Launcher 位元組是否已套用顯示模式切換抑制修補。
    /// </summary>
    public static bool IsApplied(byte[] launcherBytes)
    {
        if (!TryGetFileOffset(Sites[0].Rva, Sites[0].Patch.Length, out int off)) return false;
        if (launcherBytes.Length < off + Sites[0].Patch.Length) return false;

        return launcherBytes[off] == Sites[0].Patch[0] && launcherBytes[off + 1] == Sites[0].Patch[1];
    }

    /// <summary>
    /// 套用或還原顯示模式切換抑制修補。
    /// </summary>
    public static void Apply(ref byte[] launcherBytes, bool enable)
    {
        foreach (var site in Sites)
        {
            if (!TryGetFileOffset(site.Rva, site.Orig.Length, out int off)) continue;
            if (launcherBytes.Length < off + site.Orig.Length) continue;

            byte[] bytesToWrite = enable ? site.Patch : site.Orig;
            bytesToWrite.CopyTo(launcherBytes.AsSpan(off, bytesToWrite.Length));
        }
    }
}

/// <summary>
/// BackupManager 之 launcher_display 修補特徵偵測器 (SPEC.md §3 / §5)。
/// </summary>
public sealed class LauncherDisplaySignature : IPatchSignature
{
    public string PatchId => "launcher_display";
    public GameFile AppliesTo => GameFile.Launcher;
    public bool IsApplied(byte[] fileBytes) => LauncherDisplay.IsApplied(fileBytes);
}
