namespace CKToolkit.Core.Runtime;

/// <summary>
/// 分析器產物的資料夾配置。使用者選的是「儲存位置」，不是會被檔案
/// 直接灑滿的實際輸出目錄。
///
/// 固定結構：
///   &lt;選擇位置&gt;\CKToolkit 分析紀錄\yyyy-MM-dd\HH-mm-ss_mode\
///
/// 分類到「每場執行」就停：同一場的兩層 log、執行清單、崩潰報告、
/// dump 與 JSON 必須留在一起，否則分析時又得跨資料夾拼證據鏈。
/// </summary>
public static class DiagnosticOutputLayout
{
    public const string CollectionFolderName = "CKToolkit 分析紀錄";
    private const int MaxNativeOutputDirectoryLength = 200;

    /// <summary>取得選定位置下的固定分析紀錄根資料夾，不寫入磁碟。</summary>
    public static string GetCollectionDirectory(string selectedLocation)
    {
        if (string.IsNullOrWhiteSpace(selectedLocation))
            throw new ArgumentException("儲存位置不能是空的。", nameof(selectedLocation));

        string location = Path.GetFullPath(selectedLocation.Trim());
        string leaf = Path.GetFileName(Path.TrimEndingDirectorySeparator(location));
        return leaf.Equals(CollectionFolderName, StringComparison.OrdinalIgnoreCase)
            ? location
            : Path.Combine(location, CollectionFolderName);
    }

    /// <summary>建立並回傳固定分析紀錄根資料夾。</summary>
    public static string EnsureCollectionDirectory(string selectedLocation)
    {
        string root = GetCollectionDirectory(selectedLocation);
        Directory.CreateDirectory(root);
        return root;
    }

    /// <summary>
    /// 建立這一場獨立的輸出資料夾。同一秒開第二場時自動加上
    /// <c>_02</c>、<c>_03</c>，絕不重用舊場次資料夾。
    /// </summary>
    public static string CreateSessionDirectory(string selectedLocation, string mode, DateTime startedAt)
    {
        string root = EnsureCollectionDirectory(selectedLocation);
        string day = Path.Combine(root, startedAt.ToString("yyyy-MM-dd"));
        Directory.CreateDirectory(day);

        string safeMode = SanitizeMode(mode);
        string stem = $"{startedAt:HH-mm-ss}_{safeMode}";

        for (int number = 1; number <= 999; number++)
        {
            string name = number == 1 ? stem : $"{stem}_{number:00}";
            string candidate = Path.Combine(day, name);
            if (Directory.Exists(candidate) || File.Exists(candidate)) continue;
            if (candidate.Length > MaxNativeOutputDirectoryLength)
                throw new PathTooLongException(
                    $"分析輸出路徑過長（{candidate.Length} 個字元）；請選擇較上層的儲存位置。");

            Directory.CreateDirectory(candidate);
            return candidate;
        }

        throw new IOException($"同一秒內的分析資料夾已超過上限：{day}");
    }

    private static string SanitizeMode(string mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return "session";

        char[] invalid = Path.GetInvalidFileNameChars();
        char[] chars = mode.Trim().ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Array.IndexOf(invalid, chars[i]) >= 0 || char.IsWhiteSpace(chars[i])) chars[i] = '-';
        }

        string cleaned = new(chars);
        return string.IsNullOrWhiteSpace(cleaned) ? "session" : cleaned;
    }
}
