using CKToolkit.Core.Common;

namespace CKToolkit.Core.Perf;

/// <summary>
/// 效能與相容性修補模組 (SPEC.md §4 / §5)。
///
/// 負責協調整合：
///   - Exe: LAA -> SetVideoMode -> HiRes ZoomMap -> ResolutionWriteback
///   - Launcher: DisplaySuppress 互斥 ModeTable
///   - data.pak: [Resolutions] 附加
///   - vxSettings.ini: 動畫開關與 Resolution 索引查表
/// </summary>
public sealed class PerfModule : IPatchModule
{
    public string ModuleId => "Perf";
    public int Order => 100;

    /// <summary>
    /// 對 Celtic kings.exe 依序套用 LAA、VideoMode、HiRes ZoomMap 與 ResolutionWriteback。
    /// </summary>
    public void ApplyExe(ref byte[] exeBytes, ToolkitConfig config)
    {
        var pe = PeFile.Parse(exeBytes);

        // 1. LargeAddressAware (2GB -> 4GB)
        LargeAddressAware.Apply(pe, config.Perf.Laa);

        // 2. VideoMode Patch (0x006BE340 -> xor eax, eax; ret)
        byte[] peData = pe.ToBytes();
        VideoModePatch.Apply(ref peData, config.Perf.VideoFix);
        pe = PeFile.Parse(peData);

        // 3. HiRes ZoomMap Tables (.ckhr 節區搬遷)
        bool hiresEnabled = config.Perf.Hires >= 1600;
        uint maxDim = hiresEnabled ? (uint)config.Perf.Hires : 1600u;
        ZoomTables.Apply(pe, hiresEnabled, maxDim);

        // 4. Resolution Writeback Suppression (0x00658FAB -> 21x NOP)
        peData = pe.ToBytes();
        ResolutionWriteback.Apply(ref peData, config.Perf.KeepRes);

        exeBytes = peData;
    }

    /// <summary>
    /// 對 Celtic kings Launcher.exe 套用顯示模式設定。
    /// 嚴格執行 LauncherDisplay (抑制) 與 LauncherModeTable (自動切換) 之互斥。
    /// </summary>
    public void ApplyLauncher(ref byte[] launcherBytes, ToolkitConfig config)
    {
        string mode = config.Perf.DesktopMode.Trim();

        if (mode.Equals("suppress", StringComparison.OrdinalIgnoreCase))
        {
            // 啟用完全抑制，停用模式表改寫
            LauncherDisplay.Apply(ref launcherBytes, enable: true);
            LauncherModeTable.Apply(ref launcherBytes, enable: false);
        }
        else if (mode.Equals("autoSwitch", StringComparison.OrdinalIgnoreCase))
        {
            // 啟用模式表自動切換，停用抑制
            LauncherDisplay.Apply(ref launcherBytes, enable: false);

            var (width, height) = ParseDimensions(config.Perf.Resolution, 1920, 1080);
            LauncherModeTable.Apply(ref launcherBytes, enable: true, width, height);
        }
        else
        {
            // 原版模式：兩者皆停用
            LauncherDisplay.Apply(ref launcherBytes, enable: false);
            LauncherModeTable.Apply(ref launcherBytes, enable: false);
        }
    }

    /// <summary>
    /// 對 data.pak 內的 VXCONST.INI 附加自訂解析度清單。
    /// 嚴格確保 [Resolutions] 不包含寬度大於當前 ZoomMap 表格容量之條目。
    /// </summary>
    public void ApplyDataPak(HmmPak pak, ToolkitConfig config)
    {
        int zoomMapCapacity = config.Perf.Hires >= 1600 ? config.Perf.Hires : 1600;

        // 確保 [Resolutions] 不包含寬度大於當前 ZoomMap 容量之條目
        Resolutions.EnforceCapacity(pak, zoomMapCapacity);

        if (config.Perf.AddRes is null || config.Perf.AddRes.Count == 0) return;

        var wanted = new List<(int Width, int Height)>();
        foreach (string resStr in config.Perf.AddRes)
        {
            var (w, h) = ParseDimensions(resStr, 0, 0);
            if (w > 0 && h > 0 && w <= zoomMapCapacity)
            {
                wanted.Add((w, h));
            }
        }

        if (wanted.Count > 0)
        {
            Resolutions.AppendResolutions(pak, wanted, zoomMapCapacity);
        }
    }

    /// <summary>
    /// local.pak 不受 Perf 模組修改。
    /// </summary>
    public void ApplyLocalPak(HmmPak pak, ToolkitConfig config)
    {
        // No-op for Perf module
    }

    /// <summary>
    /// 對 vxSettings.ini 套用動畫開關與 Resolution 索引。
    /// </summary>
    public void ApplyVxSettings(IniFile ini, ToolkitConfig config, IReadOnlyList<string>? availableResolutions, List<string>? warnings = null)
    {
        VxSettingsPatch.Apply(ini, config, availableResolutions, warnings);
    }

    public static (int Width, int Height) ParseDimensions(string text, int defaultW, int defaultH)
    {
        if (string.IsNullOrWhiteSpace(text)) return (defaultW, defaultH);

        string[] parts = text.ToLowerInvariant().Split('x');
        if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
        {
            return (w, h);
        }

        return (defaultW, defaultH);
    }
}
