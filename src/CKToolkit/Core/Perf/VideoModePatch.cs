using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Celtic kings.exe -> SetVideoMode Fix (Modern OS 16bpp Display Crash Fix)
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// The game engine's SetVideoMode at 0x006BE340 calls ChangeDisplaySettingsA with
/// dmBitsPerPel = 16. On Windows 10/11, WDDM display drivers reject this mode switch
/// (-1 / DISP_CHANGE_FAILED). When SetVideoMode fails, 0x006BFF90 returns 0xFFFF,
/// causing an unhandled null pointer dereference at 0x00657DCC (Crash on modern Windows).
/// Patching SetVideoMode at file offset 0x002BE340 to return SUCCESS (31 C0 C3 90 90 90)
/// allows the game to run smoothly using its native GDI SetDIBitsToDevice software
/// rasterizer without hardware 16bpp display mode failure.
///
/// A surgical alternative (NOP the specific enumeration-loop comparisons instead
/// of stubbing the whole function) was tried and reverted -- it let the OS
/// resolution genuinely switch, but that live switch corrupts video playback and
/// gets the game stuck at 1024x768 as soon as actual gameplay starts, almost
/// certainly because the engine's DirectDraw surfaces were never built to survive
/// a live mode change.
///
/// To play above 1024x768 without this patch trying to switch anything: set the
/// Windows desktop to the target resolution yourself before launching, then pick
/// the matching entry in-game.
/// </summary>
public static class VideoModePatch
{
    public const int Offset = 0x002BE340; // VA 0x006BE340
    public static readonly byte[] OrigBytes = [0x81, 0xEC, 0x38, 0x01, 0x00, 0x00];
    public static readonly byte[] PatchBytes = [0x31, 0xC0, 0xC3, 0x90, 0x90, 0x90];

    /// <summary>
    /// 檢查給定之 Exe 位元組是否已套用 SetVideoMode 相容性修補。
    /// </summary>
    public static bool IsApplied(byte[] exeBytes)
    {
        if (exeBytes.Length < Offset + 3) return false;
        return exeBytes[Offset] == 0x31 && exeBytes[Offset + 1] == 0xC0 && exeBytes[Offset + 2] == 0xC3;
    }

    /// <summary>
    /// 檢查給定之 Exe 位元組是否為原版 SetVideoMode 函式序言。
    /// </summary>
    public static bool IsOriginal(byte[] exeBytes)
    {
        if (exeBytes.Length < Offset + OrigBytes.Length) return false;
        return exeBytes.AsSpan(Offset, OrigBytes.Length).SequenceEqual(OrigBytes);
    }

    /// <summary>
    /// 套用或還原 SetVideoMode 相容性修補。
    /// </summary>
    public static void Apply(ref byte[] exeBytes, bool enable)
    {
        if (exeBytes.Length < Offset + PatchBytes.Length) return;

        if (enable)
        {
            PatchBytes.CopyTo(exeBytes.AsSpan(Offset, PatchBytes.Length));
        }
        else
        {
            OrigBytes.CopyTo(exeBytes.AsSpan(Offset, OrigBytes.Length));
        }
    }
}

/// <summary>
/// BackupManager 之 video_fix 修補特徵偵測器 (SPEC.md §3 / §5)。
/// </summary>
public sealed class VideoModeSignature : IPatchSignature
{
    public string PatchId => "video_fix";
    public GameFile AppliesTo => GameFile.Exe;
    public bool IsApplied(byte[] fileBytes) => VideoModePatch.IsApplied(fileBytes);
}
