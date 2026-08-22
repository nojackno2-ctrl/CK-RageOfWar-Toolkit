using System.Text.Json.Serialization;

namespace CKToolkit.Core.Common;

/// <summary>
/// 從 <c>Celtic kings.exe</c> 讀出的組建指紋。
/// </summary>
/// <param name="TimeDateStamp">PE COFF 標頭的編譯時間戳（1970 起算之秒數）。</param>
/// <param name="BuildTimeUtc">上述時間戳換算成的 UTC 時間。</param>
/// <param name="SizeOfImage">PE OptionalHeader 的 SizeOfImage。</param>
/// <param name="FileLength">exe 檔案總位元組數（已正規化回原版狀態後的長度）。</param>
/// <param name="IsKnown">是否與本工具驗證過的組建完全相符。</param>
public sealed record GameBuildInfo(
    uint TimeDateStamp,
    DateTime BuildTimeUtc,
    uint SizeOfImage,
    int FileLength,
    bool IsKnown)
{
    /// <summary>供 CLI --json 輸出使用的簡短描述，例如 "2004-02-19 17:17:37Z (0x4034EFB1)"。</summary>
    [JsonPropertyName("build")]
    public string Build => $"{BuildTimeUtc:yyyy-MM-dd HH:mm:ss}Z (0x{TimeDateStamp:X8})";
}

/// <summary>
/// 遊戲組建版本偵測。
///
/// 本工具的每一個位址與位移都是 Steam 版 <c>Celtic kings.exe</c> 專屬的
/// （<c>0x006BE340</c>、<c>0x0044F536</c>、<c>0x0047A020</c>…）。逆向工程期間就是靠
/// **PE 編譯時間戳 + 檔案大小** 來確認「這兩份 exe 是同一個組建」
/// （見 <c>docs/reverse-engineering-notes.md</c>，那裡記的是 UTC+8 當地時間 2004-02-20 01:17:37）。
///
/// 這裡把同一個判準做成執行期檢查：組建對不上就發出警告，但**不阻止修改**。
/// 之所以敢放行，是因為真正的安全網在更下層——每一個修補站點在寫入前都會比對
/// 原始指令位元組，對不上就拒絕（<c>PatchState</c> 的 Unrecognised 路徑）。
/// 版本檢查負責「早一步告訴使用者情況不對」，位元組檢查負責「絕不亂寫」。
/// </summary>
public static class GameVersion
{
    /// <summary>
    /// 已驗證組建的 PE 編譯時間戳：<c>0x4034EFB1</c> = 2004-02-19 17:17:37 UTC。
    /// 本專案所有記憶體位址都是對這個組建逆向出來的。
    ///
    /// 注意：<c>docs/reverse-engineering-notes.md</c> 記的是「2004-02-20 01:17:37」，
    /// 那是 UTC+8 的當地時間（差 28,800 秒），與這裡的 UTC 值是同一個組建。
    /// 這三個常數是 2026-08-22 用 <c>CKToolkit.exe status</c> 從實機安裝讀出來的，
    /// 讀的是正規化回原版狀態後的位元組。
    /// </summary>
    public const uint KnownTimeDateStamp = 0x4034EFB1;

    /// <summary>已驗證組建的 SizeOfImage。0 代表尚未記錄，比對時略過這一項。</summary>
    public const uint KnownSizeOfImage = 5_025_792;

    /// <summary>已驗證組建正規化後的檔案長度（位元組）。0 代表尚未記錄，比對時略過這一項。</summary>
    public const int KnownFileLength = 3_516_344;

    /// <summary>
    /// 從 exe 位元組讀出組建指紋。
    /// </summary>
    /// <param name="exeBytes">
    /// 建議傳入**正規化後**（已反轉本工具所有修補）的位元組。本工具的修補不會動到
    /// COFF 時間戳，但 HiRes 會附加 <c>.ckhr</c> 節區而改變 SizeOfImage 與檔案長度，
    /// 所以只有正規化後的數值拿來比對才有意義。
    /// </param>
    public static GameBuildInfo Detect(byte[] exeBytes)
    {
        uint stamp = 0;
        uint sizeOfImage = 0;

        try
        {
            var pe = PeFile.Parse(exeBytes);
            stamp = BitConverter.ToUInt32(exeBytes, pe.FileHeaderOffset + 4);
            sizeOfImage = pe.SizeOfImage;
        }
        catch
        {
            // 連 PE 標頭都解析不出來。這種情況下由呼叫端的位元組層檢查去擋，
            // 這裡只需要誠實回報「認不出來」。
        }

        bool known =
            stamp == KnownTimeDateStamp &&
            (KnownSizeOfImage == 0 || sizeOfImage == KnownSizeOfImage) &&
            (KnownFileLength == 0 || exeBytes.Length == KnownFileLength);

        return new GameBuildInfo(
            stamp,
            DateTimeOffset.FromUnixTimeSeconds(stamp).UtcDateTime,
            sizeOfImage,
            exeBytes.Length,
            known);
    }

    /// <summary>
    /// 若組建與已驗證版本不符，將警告加入清單。相符則什麼都不做。
    /// 這個函式**永遠不會**讓流程失敗——是警告，不是拒絕。
    /// </summary>
    public static void WarnIfUnknown(GameBuildInfo info, List<string>? warnings)
    {
        if (info.IsKnown || warnings is null) return;

        warnings.Add(I18n.Strings.Get(
            "Warning_UnknownGameBuild",
            info.Build,
            $"{DateTimeOffset.FromUnixTimeSeconds(KnownTimeDateStamp).UtcDateTime:yyyy-MM-dd HH:mm:ss}Z (0x{KnownTimeDateStamp:X8})"));
    }
}
