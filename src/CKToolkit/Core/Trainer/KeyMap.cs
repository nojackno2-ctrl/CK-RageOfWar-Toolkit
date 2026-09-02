using CKToolkit.Core.Common;
namespace CKToolkit.Core.Trainer;

/// <summary>
/// 把 scdebug 按鍵名稱重新對應到小鍵盤——這是唯一需要動到執行檔的功能。
///
/// 引擎解析 scdebug.xml 的 id 屬性時，是一長串 strcmp 比對，比中了就把對應的
/// 虛擬鍵碼寫進區域變數，之後派送只比對這個鍵碼（完全不看 Ctrl/Shift/Alt）。
/// 鍵碼是編譯進 .text 的立即數，每個名稱各一個：
///
///     005E685C  C7 44 24 1C 70 00 00 00   mov [esp+1C], 0x70   ; "F1"  -> VK_F1
///     ...
///     005E72BA  C7 84 24 80 00 00 00 2E…  mov [esp+80], 0x2E   ; "Del" -> VK_DELETE
///
/// 所以把 F1 那個 0x70 改成 0x61（VK_NUMPAD1），scdebug.xml 裡的
/// &lt;key id="F1"&gt; 就變成按小鍵盤 1 才觸發。改的是 4 個位元組的立即數，
/// 不動任何指令長度，完全可逆。
///
/// 這麼做的好處是把作弊全部搬離 F1／F5–F10（遊戲的說明、外交、快速存讀檔、
/// 主選單），衝突一次消失；小鍵盤本身遊戲完全沒用到。可用的鍵變成 15 顆：
/// 0–9、/、. 這 12 顆靠改寫 F1–F12 取得，+ － ＊ 本來就是 Add／Sub／Mul。
///
/// 位移是這一版執行檔（Steam 版）專屬的，所以每次套用前都會先驗證該位址上
/// 確實是預期的指令與原版鍵碼，對不上就拒絕修改，不會亂寫。
/// </summary>
public static class KeyMap
{
    /// <summary>mov dword [esp+1C], imm32——F1~F12 走這條路徑。</summary>
    private static readonly byte[] PrefixLocal = [0xC7, 0x44, 0x24, 0x1C];

    /// <summary>mov dword [esp+80], imm32——Pause 之後的那幾個走這條。</summary>
    private static readonly byte[] PrefixWide = [0xC7, 0x84, 0x24, 0x80, 0x00, 0x00, 0x00];

    /// <summary>
    /// 按鍵名稱 → 執行檔內鍵碼立即數的位置。
    ///
    /// ImmOffset 是檔案位移（這個執行檔的 .text 是 VA = 檔案位移 + 0x400000）。
    /// Numpad 為 null 代表這顆鍵不重新對應：Add／Sub／Mul 本來就是小鍵盤的
    /// ＋－＊，Pause／Del／Ins／Backspace／Tab 則維持原本的實體鍵。
    /// </summary>
    public static readonly IReadOnlyList<KeyBinding> All =
    [
        new("F1",  0x1E6860, PrefixLocal, 0x70, 0x61, "小鍵盤 1"),
        new("F2",  0x1E693D, PrefixLocal, 0x71, 0x62, "小鍵盤 2"),
        new("F3",  0x1E6A0A, PrefixLocal, 0x72, 0x63, "小鍵盤 3"),
        new("F4",  0x1E6AD7, PrefixLocal, 0x73, 0x64, "小鍵盤 4"),
        new("F5",  0x1E6BA4, PrefixLocal, 0x74, 0x65, "小鍵盤 5"),
        new("F6",  0x1E6C71, PrefixLocal, 0x75, 0x66, "小鍵盤 6"),
        new("F7",  0x1E6D3E, PrefixLocal, 0x76, 0x67, "小鍵盤 7"),
        new("F8",  0x1E6E0B, PrefixLocal, 0x77, 0x68, "小鍵盤 8"),
        new("F9",  0x1E6ED8, PrefixLocal, 0x78, 0x69, "小鍵盤 9"),
        new("F10", 0x1E6FA5, PrefixLocal, 0x79, 0x60, "小鍵盤 0"),
        new("F11", 0x1E7072, PrefixLocal, 0x7A, 0x6F, "小鍵盤 /"),
        new("F12", 0x1E713F, PrefixLocal, 0x7B, 0x6E, "小鍵盤 ."),

        // 以下維持原樣，列出來是為了讓驗證能一次檢查整張表對不對得上
        new("Pause",     0x1E71F9, PrefixWide, 0x13, null, "Pause"),
        new("Add",       0x1E722B, PrefixWide, 0x6B, null, "小鍵盤 +"),
        new("Sub",       0x1E725D, PrefixWide, 0x6D, null, "小鍵盤 -"),
        new("Mul",       0x1E728F, PrefixWide, 0x6A, null, "小鍵盤 *"),
        new("Del",       0x1E72C1, PrefixWide, 0x2E, null, "Del"),
        new("Ins",       0x1E72F3, PrefixWide, 0x2D, null, "Ins"),
        new("Backspace", 0x1E7354, PrefixWide, 0x08, null, "Backspace"),
        new("Tab",       0x1E7387, PrefixWide, 0x09, null, "Tab"),
    ];

    public static readonly IReadOnlyDictionary<string, KeyBinding> ById =
        All.ToDictionary(b => b.Key, StringComparer.Ordinal);

    /// <summary>這顆鍵在小鍵盤模式下會被改到別的實體鍵上嗎。</summary>
    public static bool IsRemapped(string key) =>
        ById.TryGetValue(key, out var binding) && binding.Numpad is not null;

    /// <summary>
    /// 這個 scdebug id 在目前模式下實際對應到哪一顆實體鍵的 Win32 虛擬鍵碼。
    /// 面板要代送按鍵時就是查這裡；id 不存在回傳 null。
    /// </summary>
    public static uint? VirtualKeyFor(string key, bool numpadKeys)
    {
        if (!ById.TryGetValue(key, out var binding)) return null;
        return numpadKeys && binding.Numpad is { } np ? np : binding.Vanilla;
    }

    /// <summary>介面上要顯示的鍵名。小鍵盤模式下顯示實際要按的那顆鍵。</summary>
    public static string Display(string key, bool numpadKeys)
    {
        if (!ById.TryGetValue(key, out var binding)) return key;
        if (numpadKeys) return binding.Label;
        return binding.Numpad is null && binding.Label != key
            ? $"{key}（{binding.Label}）"   // Add/Sub/Mul 本來就是小鍵盤的 ＋－＊
            : key;
    }

    /// <summary>
    /// 反查：目前這個模式下，實際按下某個實體鍵（Win32 虛擬鍵碼）對應到哪個
    /// scdebug id。用於「按鍵擷取」介面——使用者直接按鍵盤，不是從清單挑名字。
    /// 找不到就回傳 null（那顆實體鍵在目前模式下沒有對應的 scdebug id）。
    /// </summary>
    public static string? IdFromVirtualKey(int virtualKey, bool numpadKeys)
    {
        foreach (var b in All)
        {
            uint effective = numpadKeys && b.Numpad is { } np ? np : b.Vanilla;
            if (effective == (uint)virtualKey) return b.Key;
        }
        return null;
    }

    /// <summary>
    /// 確認執行檔就是我們認識的那一版：每個位址上都要是預期的指令，
    /// 而且鍵碼是原版值或我們自己改上去的值。不符就回傳說明，符合回傳 null。
    /// </summary>
    public static string? Verify(byte[] exe)
    {
        foreach (var b in All)
        {
            if (b.ImmOffset + 4 > exe.Length)
                return $"執行檔太小，{b.Key} 的鍵碼位置 0x{b.ImmOffset:X} 超出檔案範圍";

            int start = b.ImmOffset - b.Prefix.Length;
            for (int i = 0; i < b.Prefix.Length; i++)
                if (exe[start + i] != b.Prefix[i])
                    return $"執行檔內容與預期不符（{b.Key} 在 0x{start:X} 不是預期的指令）";

            uint value = BitConverter.ToUInt32(exe, b.ImmOffset);
            if (value != b.Vanilla && value != b.Numpad)
                return $"執行檔內容與預期不符（{b.Key} 的鍵碼是 0x{value:X}，"
                     + $"預期 0x{b.Vanilla:X}）";
        }
        return null;
    }

    /// <summary>
    /// 把按鍵表精確還原成原版鍵碼。
    ///
    /// 本工具不保留執行檔備份（AGENTS.md §2.1），所以還原不能靠「把備份寫回去」，
    /// 必須從被修改後的位元組自己反推。這裡可行是因為每個位址的原版鍵碼
    /// (<see cref="KeyBinding.Vanilla"/>) 就寫在程式碼裡。
    ///
    /// 注意前身專案的 <see cref="Apply"/> 在 numpadKeys 為 false 時只回傳一份複本——
    /// 那是因為它一律從原廠備份重建，那個前提在這裡不成立。
    /// </summary>
    public static byte[] Restore(byte[] exe)
    {
        byte[] restored = (byte[])exe.Clone();
        foreach (var b in All)
        {
            if (b.Numpad is null) continue;
            if (b.ImmOffset + 4 > restored.Length) continue;
            BitConverter.GetBytes(b.Vanilla).CopyTo(restored, b.ImmOffset);
        }
        return restored;
    }

    /// <summary>目前執行檔是不是已經被改成小鍵盤了。</summary>
    public static bool IsRemappedExe(byte[] exe) =>
        All.Any(b => b.Numpad is { } np
                  && b.ImmOffset + 4 <= exe.Length
                  && BitConverter.ToUInt32(exe, b.ImmOffset) == np);

    /// <summary>所有可重新對應的按鍵是否都處於小鍵盤模式。</summary>
    public static bool IsFullyRemappedExe(byte[] exe) =>
        All.Where(b => b.Numpad is not null)
           .All(b => b.ImmOffset + 4 <= exe.Length
                  && BitConverter.ToUInt32(exe, b.ImmOffset) == b.Numpad);

    /// <summary>
    /// 產生改好鍵碼的執行檔內容。來源必須是原版（會先驗證），
    /// numpadKeys 為 false 時原封不動回傳一份複本。
    /// </summary>
    public static byte[] Apply(byte[] original, bool numpadKeys)
    {
        if (Verify(original) is { } problem)
            throw new TrainerException(
                "無法改寫執行檔的按鍵表：" + problem + "\n"
                + "按鍵位址是這一版 Steam 執行檔專屬的，換版本或改過的執行檔都不能套用。\n"
                + "請用 Steam「驗證遊戲檔案完整性」取回原版執行檔後再試。");

        byte[] patched = (byte[])original.Clone();
        if (!numpadKeys) return patched;

        foreach (var b in All)
        {
            if (b.Numpad is not { } np) continue;
            BitConverter.GetBytes(np).CopyTo(patched, b.ImmOffset);
        }
        return patched;
    }
}

/// <param name="Key">scdebug.xml 裡寫的 id。</param>
/// <param name="ImmOffset">鍵碼立即數在執行檔中的位移。</param>
/// <param name="Prefix">立即數前面應該要有的指令位元組，用來確認版本對得上。</param>
/// <param name="Vanilla">原版鍵碼。</param>
/// <param name="Numpad">小鍵盤模式要改成的鍵碼；null 表示這顆不動。</param>
/// <param name="Label">介面上顯示的實體鍵名。</param>
public sealed record KeyBinding(
    string Key, int ImmOffset, byte[] Prefix, uint Vanilla, uint? Numpad, string Label);
