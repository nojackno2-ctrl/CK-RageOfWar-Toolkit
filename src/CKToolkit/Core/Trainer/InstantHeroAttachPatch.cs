using CKToolkit.Core.Common;

namespace CKToolkit.Core.Trainer;

/// <summary>
/// Celtic kings.exe -> CVXUnit::AttachTo 遠距／全圖編入英雄隊伍修補 (ISSUE-077)
///
/// 原始引擎在 0x0050BC60 (CVXUnit::AttachTo) 執行實體編入前：
///   0050BCEC: push edi                       ; unit
///   0050BCED: mov ecx, ebx                   ; hero
///   0050BCEF: call 0x4f4120                  ; InRange: effective_dist &lt;= hero-&gt;sight
///   0050BCF4: test eax, eax
///   0050BCF6: 0F 84 25 01 00 00              ; je 0x50be21 (若超出英雄視野則拒絕編入)
///
/// 英雄視野定義於 HERO.SC.XML 的 sight 屬性（預設 600）。
/// 將 0x0050BCF6 的 6 位元組條件跳轉指令替換為 NOP (90 90 90 90 90 90)，
/// 解除 600 像素附著距離硬限制，使部隊在任何距離都能透過右鍵點擊英雄立刻被吸納入隊。
///
/// 本修補完全保留英雄帶兵數容量檢查 (cmp edx, eax; je 0x50be21)，隊伍滿員時依然正確拒絕。
/// 支援 100% 逐位元組精確反轉回 Steam 原廠指令。
/// </summary>
public static class InstantHeroAttachPatch
{
    public const int Offset = 0x0010BCF6; // VA 0x0050BCF6
    public static readonly byte[] OrigBytes = [0x0F, 0x84, 0x25, 0x01, 0x00, 0x00];
    public static readonly byte[] PatchBytes = [0x90, 0x90, 0x90, 0x90, 0x90, 0x90];

    /// <summary>
    /// 檢查給定之 Exe 位元組是否已套用瞬時編入英雄修補 (NOP 序列)。
    /// </summary>
    public static bool IsApplied(byte[] exeBytes)
    {
        if (exeBytes.Length < Offset + PatchBytes.Length) return false;
        return exeBytes.AsSpan(Offset, PatchBytes.Length).SequenceEqual(PatchBytes);
    }

    /// <summary>
    /// 檢查給定之 Exe 位元組是否為原版 AttachTo 視野範圍跳轉指令。
    /// </summary>
    public static bool IsOriginal(byte[] exeBytes)
    {
        if (exeBytes.Length < Offset + OrigBytes.Length) return false;
        return exeBytes.AsSpan(Offset, OrigBytes.Length).SequenceEqual(OrigBytes);
    }

    /// <summary>
    /// 套用或還原瞬時編入英雄修補。
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
