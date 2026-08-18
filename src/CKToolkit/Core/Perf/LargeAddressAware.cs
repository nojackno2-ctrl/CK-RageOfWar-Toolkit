using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// Celtic kings.exe -> LARGE_ADDRESS_AWARE (2GB -> 4GB)
///
/// 移植自前身 C++ patches.cpp / patches.h，保留原始註解與逆向工程結論：
///
/// The game is a 32-bit PE without the flag, so it is capped at a 2 GB user
/// address space. Setting it raises the cap to 4 GB on 64-bit Windows. It loads
/// rle.mmp (170 MB of hicolor RLE sprites) plus assets.pak (136 MB) and does its
/// own caching, so long sessions on large maps are the realistic place to hit that
/// ceiling. Setting the flag raises the limit to 4 GB.
///
/// This makes nothing faster; it only removes a ceiling, and it invalidates the
/// file's Authenticode signature (Windows does not enforce signatures on ordinary applications).
/// </summary>
public static class LargeAddressAware
{
    public const ushort Flag = 0x0020; // IMAGE_FILE_LARGE_ADDRESS_AWARE

    /// <summary>
    /// 檢查給定之 PE 位元組是否已設定 LARGE_ADDRESS_AWARE 特徵旗標。
    /// </summary>
    public static bool IsApplied(byte[] exeBytes)
    {
        if (exeBytes.Length < 0x40) return false;
        if (exeBytes[0] != (byte)'M' || exeBytes[1] != (byte)'Z') return false;

        uint lfanew = BitConverter.ToUInt32(exeBytes, 0x3C);
        if (lfanew + 24 > exeBytes.Length) return false;
        if (exeBytes[lfanew] != (byte)'P' || exeBytes[lfanew + 1] != (byte)'E' ||
            exeBytes[lfanew + 2] != 0 || exeBytes[lfanew + 3] != 0)
        {
            return false;
        }

        int characteristicsOffset = (int)lfanew + 4 + 18;
        if (characteristicsOffset + 2 > exeBytes.Length) return false;

        ushort ch = BitConverter.ToUInt16(exeBytes, characteristicsOffset);
        return (ch & Flag) != 0;
    }

    /// <summary>
    /// 套用或移除 LARGE_ADDRESS_AWARE 旗標。
    /// </summary>
    public static void Apply(PeFile pe, bool enable)
    {
        pe.LargeAddressAware = enable;
    }

    /// <summary>
    /// 直接對 PE 位元組陣列套用或移除 LARGE_ADDRESS_AWARE 旗標。
    /// </summary>
    public static void Apply(ref byte[] exeBytes, bool enable)
    {
        if (exeBytes.Length < 0x40) return;
        uint lfanew = BitConverter.ToUInt32(exeBytes, 0x3C);
        if (lfanew + 24 > exeBytes.Length) return;

        int characteristicsOffset = (int)lfanew + 4 + 18;
        ushort ch = BitConverter.ToUInt16(exeBytes, characteristicsOffset);
        if (enable)
            ch |= Flag;
        else
            ch = (ushort)(ch & ~Flag);

        BitConverter.TryWriteBytes(exeBytes.AsSpan(characteristicsOffset, 2), ch);
    }
}
