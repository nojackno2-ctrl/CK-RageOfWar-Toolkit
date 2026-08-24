using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace CKToolkit.Core.Common;

/// <summary>
/// 遊戲目錄自動探測與五個核心目標檔案路徑管理。
/// 探測優先順序：
///   1. 明確覆寫參數 (--game)
///   2. 設定檔記憶路徑 (cktoolkit.json)
///   3. Steam 註冊表路徑 + steamapps/libraryfolders.vdf 解析的所有庫目錄
///   4. 常見安裝路徑與所有磁碟機猜測
///   5. 工具所在目錄與工作目錄
///
/// 判定標準：目錄必須同時包含 local.pak 與 Celtic kings.exe。
/// </summary>
public static partial class GamePaths
{
    public const string ExeFileName = "Celtic kings.exe";
    public const string LauncherFileName = "Celtic kings Launcher.exe";
    public const string DataPakFileName = "data.pak";
    public const string LocalPakFileName = "local.pak";
    public const string VxSettingsFileName = "vxSettings.ini";

    private static readonly string[] WellKnownSteamPaths =
    [
        @"C:\Program Files (x86)\Steam\steamapps\common\CK_RageOfWar",
        @"C:\Program Files\Steam\steamapps\common\CK_RageOfWar",
        @"D:\Steam\steamapps\common\CK_RageOfWar",
        @"D:\SteamLibrary\steamapps\common\CK_RageOfWar",
        @"E:\SteamLibrary\steamapps\common\CK_RageOfWar",
    ];

    [GeneratedRegex("\"path\"\\s*\"([^\"]+)\"")]
    private static partial Regex VdfLibraryRegex();

    /// <summary>
    /// 判定指定目錄是否為有效的《Celtic Kings》遊戲目錄。
    /// 必須同時存在 local.pak 與 Celtic kings.exe。
    /// </summary>
    public static bool IsGameDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            return File.Exists(Path.Combine(path, LocalPakFileName))
                && File.Exists(Path.Combine(path, ExeFileName));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 自動探測遊戲安裝目錄。
    /// </summary>
    /// <param name="explicitDir">命令列或使用者指定之明確路徑</param>
    /// <param name="rememberedDir">設定檔記錄之路徑</param>
    /// <returns>找到之遊戲完整路徑；找不到則回傳 null</returns>
    public static string? FindGameDir(string? explicitDir = null, string? rememberedDir = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitDir))
        {
            string candidate = explicitDir.Trim();
            if (!IsGameDir(candidate)) return null;

            try
            {
                return Path.GetFullPath(candidate);
            }
            catch
            {
                return candidate;
            }
        }

        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(rememberedDir))
            candidates.Add(rememberedDir.Trim());

        // 收集 Steam 註冊表與 libraryfolders.vdf
        foreach (string lib in CollectSteamLibraries())
        {
            candidates.Add(Path.Combine(lib, "steamapps", "common", "CK_RageOfWar"));
        }

        // 常見預設路徑
        candidates.AddRange(WellKnownSteamPaths);

        // 各磁碟機掃描
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady) continue;
                candidates.Add(Path.Combine(drive.Name, @"SteamLibrary\steamapps\common\CK_RageOfWar"));
                candidates.Add(Path.Combine(drive.Name, @"Steam\steamapps\common\CK_RageOfWar"));
                candidates.Add(Path.Combine(drive.Name, @"Games\Steam\steamapps\common\CK_RageOfWar"));
            }
        }
        catch
        {
            // 磁碟機列舉失敗時忽略
        }

        // 本機執行目錄
        candidates.Add(AppContext.BaseDirectory);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "CK_RageOfWar"));
        try
        {
            candidates.Add(Directory.GetCurrentDirectory());
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "CK_RageOfWar"));
        }
        catch
        {
            // 忽略
        }

        foreach (string candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (IsGameDir(candidate))
            {
                try
                {
                    return Path.GetFullPath(candidate);
                }
                catch
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 解析 Steam 安裝路徑與其 libraryfolders.vdf 中記錄的所有遊戲庫目錄。
    /// </summary>
    public static IEnumerable<string> CollectSteamLibraries()
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 讀取 Windows 註冊表
        string? steamPath = GetRegistryString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath")
            ?? GetRegistryString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
            ?? GetRegistryString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath");

        if (!string.IsNullOrWhiteSpace(steamPath))
        {
            libraries.Add(steamPath);
        }

        // 常見預設 Steam 目錄
        libraries.Add(@"C:\Program Files (x86)\Steam");
        libraries.Add(@"C:\Program Files\Steam");

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in libraries)
        {
            if (!Directory.Exists(root)) continue;
            result.Add(root);

            string vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) continue;

            try
            {
                string text = File.ReadAllText(vdfPath);
                foreach (Match m in VdfLibraryRegex().Matches(text))
                {
                    string lib = m.Groups[1].Value.Replace(@"\\", @"\");
                    if (Directory.Exists(lib))
                    {
                        result.Add(lib);
                    }
                }
            }
            catch
            {
                // vdf 讀取錯誤時跳過
            }
        }

        return result;
    }

    private static string? GetRegistryString(RegistryKey rootKey, string subKey, string valueName)
    {
        try
        {
            using var key = rootKey.OpenSubKey(subKey);
            if (key?.GetValue(valueName) is string str)
            {
                return str.Replace('/', '\\');
            }
        }
        catch
        {
            // 忽略註冊表權限或路徑錯誤
        }
        return null;
    }

    // --- 五個核心目標檔案路徑 ---

    public static string GetExePath(string gameDir) => Path.Combine(gameDir, ExeFileName);
    public static string GetLauncherPath(string gameDir) => Path.Combine(gameDir, LauncherFileName);
    public static string GetDataPakPath(string gameDir) => Path.Combine(gameDir, DataPakFileName);
    public static string GetLocalPakPath(string gameDir) => Path.Combine(gameDir, LocalPakFileName);
    public static string GetVxSettingsPath(string gameDir) => Path.Combine(gameDir, VxSettingsFileName);

    /// <summary>
    /// 檢查遊戲主程序或啟動器是否正在執行。
    /// 若指定 gameDir，則僅當執行中程序來自該目錄時回傳 true；若是測試用暫存目錄 (%TEMP%) 則忽略，避免測試誤報。
    /// </summary>
    public static bool IsGameRunning(string? gameDir = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(gameDir))
            {
                string fullTarget = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string tempPath = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (fullTarget.StartsWith(tempPath, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            var p1 = Process.GetProcessesByName("Celtic kings");
            var p2 = Process.GetProcessesByName("Celtic kings Launcher");
            var allProcesses = new List<Process>(p1.Length + p2.Length);
            allProcesses.AddRange(p1);
            allProcesses.AddRange(p2);

            try
            {
                if (allProcesses.Count == 0)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(gameDir))
                {
                    return true;
                }

                string targetDir = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                foreach (var p in allProcesses)
                {
                    try
                    {
                        string? procPath = null;
                        try
                        {
                            procPath = p.MainModule?.FileName;
                        }
                        catch
                        {
                            // 32 位元 / 64 位元跨架構或權限限制
                        }

                        if (string.IsNullOrEmpty(procPath))
                        {
                            // 無法查詢路徑時（例如權限限制），若目標不是 temp 目錄則保守判定為執行中
                            return true;
                        }

                        string? procDir = Path.GetDirectoryName(procPath);
                        if (!string.IsNullOrEmpty(procDir))
                        {
                            string fullProc = Path.GetFullPath(procDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                            if (string.Equals(targetDir, fullProc, StringComparison.OrdinalIgnoreCase))
                            {
                                return true;
                            }
                        }
                    }
                    catch
                    {
                        // 忽略個別程序查詢錯誤
                    }
                }
            }
            finally
            {
                foreach (var p in allProcesses)
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // 忽略
        }
        return false;
    }
}
