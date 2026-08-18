using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Celtic kings.exe -> stop clobbering vxSettings.ini's Resolution
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// The settings writer at `0x00658F90` saves every `[Options]` key back to
/// vxSettings.ini on shutdown. The `Resolution` key is written from a settings
/// struct member at `[edi+0x34]`:
///
///     0x00658FAB  8B 47 34           mov  eax, [edi+0x34]
///     0x00658FAE  50                 push eax
///     0x00658FAF  68 C8 53 74 00     push 0x7453C8   ; "Resolution"
///     0x00658FB4  68 4C 54 74 00     push 0x74544C   ; "Options"
///     0x00658FB9  8B CE              mov  ecx, esi
///     0x00658FBB  E8 C0 AC DB FF     call 0x00413C80 ; write key
///
/// By the time that runs the member is already 0, so **every** exit rewrites
/// `Resolution=0` and the next launch drops to 1024x768. Measured 2026-08-18:
/// this happens on a clean in-game Quit, not just on Alt+F4, so it is not an
/// abnormal-teardown artifact -- it defeats any resolution this tool sets.
///
/// The patch NOPs those 21 bytes, so the engine simply never writes that one
/// key. Every other `[Options]` key still saves normally. The cost is that
/// changing the resolution from the in-game options menu no longer persists;
/// CKToolkit's own picker becomes the way to set it, which is also the only way
/// that gets the 0-based index right.
/// </summary>
public static class ResolutionWriteback
{
    public const int Offset = 0x00258FAB; // VA 0x00658FAB - 0x00400000 (.text raw mapping)
    public const int Length = 21;

    public static readonly byte[] OrigBytes =
    [
        0x8B, 0x47, 0x34,             // mov  eax, [edi+0x34]
        0x50,                         // push eax
        0x68, 0xC8, 0x53, 0x74, 0x00, // push 0x7453C8  "Resolution"
        0x68, 0x4C, 0x54, 0x74, 0x00, // push 0x74544C  "Options"
        0x8B, 0xCE,                   // mov  ecx, esi
        0xE8, 0xC0, 0xAC, 0xDB, 0xFF  // call 0x00413C80
    ];

    /// <summary>
    /// 檢查給定之 Exe 位元組是否已抑制 Resolution 覆寫（21 位元組皆為 NOP 0x90）。
    /// </summary>
    public static bool IsApplied(byte[] exeBytes)
    {
        if (exeBytes.Length < Offset + Length) return false;
        for (int i = 0; i < Length; i++)
        {
            if (exeBytes[Offset + i] != 0x90) return false;
        }
        return true;
    }

    /// <summary>
    /// 檢查給定之 Exe 位元組是否為原版寫回指令序列。
    /// </summary>
    public static bool IsOriginal(byte[] exeBytes)
    {
        if (exeBytes.Length < Offset + Length) return false;
        return exeBytes.AsSpan(Offset, Length).SequenceEqual(OrigBytes);
    }

    /// <summary>
    /// 套用或還原抑制 Resolution 覆寫之修補。
    /// </summary>
    public static void Apply(ref byte[] exeBytes, bool suppress)
    {
        if (exeBytes.Length < Offset + Length) return;

        if (suppress)
        {
            exeBytes.AsSpan(Offset, Length).Fill(0x90);
        }
        else
        {
            OrigBytes.CopyTo(exeBytes.AsSpan(Offset, Length));
        }
    }
}

/// <summary>
/// BackupManager 之 res_writeback 修補特徵偵測器 (SPEC.md §3 / §5)。
/// </summary>
public sealed class ResolutionWritebackSignature : IPatchSignature
{
    public string PatchId => "res_writeback";
    public GameFile AppliesTo => GameFile.Exe;
    public bool IsApplied(byte[] fileBytes) => ResolutionWriteback.IsApplied(fileBytes);
}
