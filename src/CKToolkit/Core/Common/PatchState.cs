using System.Text;
using CKToolkit.Core.Perf;
using CKToolkit.I18n;

namespace CKToolkit.Core.Common;

/// <summary>
/// 遊戲核心五大目標檔案列舉。
/// </summary>
public enum GameFile
{
    Exe,
    Launcher,
    DataPak,
    LocalPak,
    VxSettings
}

/// <summary>
/// 檔案修補狀態種類 (SPEC.md §3)。
/// </summary>
public enum FileStateKind
{
    Vanilla,
    PatchedByUs,
    Unrecognised
}

/// <summary>
/// 檔案修補狀態資訊 (SPEC.md §3)。
/// </summary>
public sealed class FileState
{
    public FileStateKind Kind { get; }
    public IReadOnlyList<string> AppliedPatches { get; }

    public bool IsVanilla => Kind == FileStateKind.Vanilla;
    public bool IsPatched => Kind == FileStateKind.PatchedByUs;
    public bool IsUnrecognised => Kind == FileStateKind.Unrecognised;

    public FileState(FileStateKind kind, IEnumerable<string>? appliedPatches = null)
    {
        Kind = kind;
        AppliedPatches = appliedPatches?.ToList() ?? [];
    }

    public static FileState Vanilla() => new(FileStateKind.Vanilla);
    public static FileState PatchedByUs(IEnumerable<string> patches) => new(FileStateKind.PatchedByUs, patches);
    public static FileState Unrecognised() => new(FileStateKind.Unrecognised);

    public override string ToString() => Kind switch
    {
        FileStateKind.Vanilla => "Vanilla",
        FileStateKind.PatchedByUs => $"PatchedByUs([{string.Join(", ", AppliedPatches)}])",
        _ => "Unrecognised"
    };
}

/// <summary>
/// 檔案狀態偵測與精確正規化反轉層 (SPEC.md §3, AGENTS.md §2.1-2.3)。
///
/// 關鍵責任：
///   1. 依據檔案位元組自身判定修補狀態 (Vanilla | PatchedByUs | Unrecognised)。
///   2. 精確反轉所有本工具套用之修改，將檔案正規化回逐位元組原廠原版 (Vanilla)。
///   3. 無法辨識 (Unrecognised) 嚴格拒絕操作並提示使用者執行 Steam 驗證檔案完整性。
/// </summary>
public static class PatchState
{
    private static readonly Encoding IniEncoding;

    static PatchState()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        IniEncoding = Encoding.GetEncoding(1252);
    }

    public static string GetFileName(GameFile file) => file switch
    {
        GameFile.Exe => GamePaths.ExeFileName,
        GameFile.Launcher => GamePaths.LauncherFileName,
        GameFile.DataPak => GamePaths.DataPakFileName,
        GameFile.LocalPak => GamePaths.LocalPakFileName,
        GameFile.VxSettings => GamePaths.VxSettingsFileName,
        _ => throw new ArgumentOutOfRangeException(nameof(file))
    };

    /// <summary>
    /// 依據檔案位元組檢查該檔案之修補狀態。
    /// </summary>
    public static FileState Inspect(GameFile file, byte[] liveBytes) => file switch
    {
        GameFile.Exe => InspectExe(liveBytes),
        GameFile.Launcher => InspectLauncher(liveBytes),
        GameFile.DataPak => InspectDataPak(liveBytes),
        GameFile.LocalPak => InspectLocalPak(liveBytes),
        GameFile.VxSettings => InspectVxSettings(liveBytes),
        _ => throw new ArgumentOutOfRangeException(nameof(file))
    };

    /// <summary>
    /// 將目標檔案位元組精確正規化反轉回 Vanilla 原廠狀態。
    /// 若檔案無法辨識則回傳失敗並要求 Steam 驗證。
    /// </summary>
    public static Result<byte[]> Normalise(GameFile file, byte[] liveBytes) => file switch
    {
        GameFile.Exe => NormaliseExe(liveBytes),
        GameFile.Launcher => NormaliseLauncher(liveBytes),
        GameFile.DataPak => NormaliseDataPak(liveBytes),
        GameFile.LocalPak => NormaliseLocalPak(liveBytes),
        GameFile.VxSettings => NormaliseVxSettings(liveBytes),
        _ => throw new ArgumentOutOfRangeException(nameof(file))
    };

    // ---- Exe 檢查與正規化 ---------------------------------------------------

    private static FileState InspectExe(byte[] bytes)
    {
        PeFile pe;
        try
        {
            pe = PeFile.Parse(bytes);
        }
        catch
        {
            return FileState.Unrecognised();
        }

        if (pe.Is64Bit || pe.ImageBase != 0x00400000)
        {
            return FileState.Unrecognised();
        }

        // 1. VideoMode 檢查
        bool vmApplied = VideoModePatch.IsApplied(bytes);
        bool vmOrig = VideoModePatch.IsOriginal(bytes);
        if (!vmApplied && !vmOrig) return FileState.Unrecognised();

        // 2. ResolutionWriteback 檢查
        bool rwApplied = ResolutionWriteback.IsApplied(bytes);
        bool rwOrig = ResolutionWriteback.IsOriginal(bytes);
        if (!rwApplied && !rwOrig) return FileState.Unrecognised();

        // 3. ZoomTables 檢查
        bool ztApplied = ZoomTables.IsApplied(pe);
        bool ztOrig = ZoomTables.IsOriginal(pe);
        if (!ztApplied && !ztOrig) return FileState.Unrecognised();

        // 4. LAA 檢查
        bool laaApplied = LargeAddressAware.IsApplied(bytes);

        var patches = new List<string>();
        if (laaApplied) patches.Add("laa");
        if (vmApplied) patches.Add("video_fix");
        if (ztApplied) patches.Add("hires_zoom");
        if (rwApplied) patches.Add("res_writeback");

        return patches.Count > 0 ? FileState.PatchedByUs(patches) : FileState.Vanilla();
    }

    private static Result<byte[]> NormaliseExe(byte[] liveBytes)
    {
        var state = InspectExe(liveBytes);
        if (state.IsUnrecognised)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_UnrecognisedFileNeedsSteamVerify", GamePaths.ExeFileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        if (state.IsVanilla)
        {
            return Result<byte[]>.Ok((byte[])liveBytes.Clone());
        }

        try
        {
            var pe = PeFile.Parse(liveBytes);

            // 1. 還原 LAA 旗標
            LargeAddressAware.Apply(pe, false);

            // 2. 還原 ZoomTables (.ckhr 節區移除與立即數/指令還原)
            ZoomTables.Apply(pe, false);

            byte[] exeBytes = pe.ToBytes();

            // 3. 還原 VideoMode 序言
            VideoModePatch.Apply(ref exeBytes, false);

            // 4. 還原 ResolutionWriteback 指令序列
            ResolutionWriteback.Apply(ref exeBytes, false);

            return Result<byte[]>.Ok(exeBytes);
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_GeneralFailure", $"Exe 正規化失敗：{ex.Message}"),
                ExitCodes.GeneralFailure);
        }
    }

    // ---- Launcher 檢查與正規化 ----------------------------------------------

    private static FileState InspectLauncher(byte[] bytes)
    {
        if (bytes.Length < 0x400) return FileState.Unrecognised();

        // 檢查 Launcher 模式表指紋特徵 (entries 1..3)
        if (!LauncherModeTable.CheckFingerprint(bytes))
        {
            return FileState.Unrecognised();
        }

        // 檢查 LauncherDisplay
        bool ldApplied = LauncherDisplay.IsApplied(bytes);
        bool ldOrig = LauncherDisplay.IsOriginal(bytes);
        if (!ldApplied && !ldOrig) return FileState.Unrecognised();

        // 檢查 LauncherModeTable
        bool mtApplied = LauncherModeTable.IsApplied(bytes);
        bool mtOrig = LauncherModeTable.IsOriginal(bytes);
        if (!mtApplied && !mtOrig) return FileState.Unrecognised();

        var patches = new List<string>();
        if (ldApplied) patches.Add("launcher_display");
        if (mtApplied) patches.Add("launcher_mode_table");

        return patches.Count > 0 ? FileState.PatchedByUs(patches) : FileState.Vanilla();
    }

    private static Result<byte[]> NormaliseLauncher(byte[] liveBytes)
    {
        var state = InspectLauncher(liveBytes);
        if (state.IsUnrecognised)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_UnrecognisedFileNeedsSteamVerify", GamePaths.LauncherFileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        if (state.IsVanilla)
        {
            return Result<byte[]>.Ok((byte[])liveBytes.Clone());
        }

        byte[] bytes = (byte[])liveBytes.Clone();
        LauncherDisplay.Apply(ref bytes, false);
        LauncherModeTable.Apply(ref bytes, false);

        return Result<byte[]>.Ok(bytes);
    }

    // ---- DataPak 檢查與正規化 -----------------------------------------------

    private static FileState InspectDataPak(byte[] bytes)
    {
        HmmPak pak;
        try
        {
            pak = HmmPak.FromBytes(bytes);
        }
        catch
        {
            return FileState.Unrecognised();
        }

        bool isOrig = Resolutions.IsOriginal(pak);
        bool isApp = Resolutions.IsCustomResolutionsApplied(pak);

        if (!isOrig && !isApp)
        {
            return FileState.Unrecognised();
        }

        var patches = new List<string>();
        if (isApp) patches.Add("resolutions_append");

        return patches.Count > 0 ? FileState.PatchedByUs(patches) : FileState.Vanilla();
    }

    private static Result<byte[]> NormaliseDataPak(byte[] liveBytes)
    {
        var state = InspectDataPak(liveBytes);
        if (state.IsUnrecognised)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_UnrecognisedFileNeedsSteamVerify", GamePaths.DataPakFileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        if (state.IsVanilla)
        {
            return Result<byte[]>.Ok((byte[])liveBytes.Clone());
        }

        try
        {
            var pak = HmmPak.FromBytes(liveBytes);
            Resolutions.RestoreStockResolutions(pak);
            return Result<byte[]>.Ok(pak.ToBytes());
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_GeneralFailure", $"data.pak 正規化失敗：{ex.Message}"),
                ExitCodes.GeneralFailure);
        }
    }

    // ---- LocalPak 檢查與正規化 ----------------------------------------------

    private static FileState InspectLocalPak(byte[] bytes)
    {
        try
        {
            _ = HmmPak.FromBytes(bytes);
            return FileState.Vanilla();
        }
        catch
        {
            return FileState.Unrecognised();
        }
    }

    private static Result<byte[]> NormaliseLocalPak(byte[] liveBytes)
    {
        var state = InspectLocalPak(liveBytes);
        if (state.IsUnrecognised)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_UnrecognisedFileNeedsSteamVerify", GamePaths.LocalPakFileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        return Result<byte[]>.Ok((byte[])liveBytes.Clone());
    }

    // ---- VxSettings 檢查與正規化 --------------------------------------------

    private static FileState InspectVxSettings(byte[] bytes)
    {
        IniFile ini;
        try
        {
            string text = IniEncoding.GetString(bytes);
            ini = IniFile.FromText(text);
        }
        catch
        {
            return FileState.Unrecognised();
        }

        if (!ini.HasSection("Options"))
        {
            return FileState.Unrecognised();
        }

        bool isCustom = VxSettingsPatch.IsCustom(ini);
        var patches = new List<string>();
        if (isCustom) patches.Add("vxsettings_custom");

        return patches.Count > 0 ? FileState.PatchedByUs(patches) : FileState.Vanilla();
    }

    private static Result<byte[]> NormaliseVxSettings(byte[] liveBytes)
    {
        var state = InspectVxSettings(liveBytes);
        if (state.IsUnrecognised)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_UnrecognisedFileNeedsSteamVerify", GamePaths.VxSettingsFileName),
                ExitCodes.BackupMissingNeedsSteamVerify);
        }

        if (state.IsVanilla)
        {
            return Result<byte[]>.Ok((byte[])liveBytes.Clone());
        }

        try
        {
            string text = IniEncoding.GetString(liveBytes);
            var ini = IniFile.FromText(text);
            VxSettingsPatch.Normalise(ini);
            return Result<byte[]>.Ok(IniEncoding.GetBytes(ini.ToText()));
        }
        catch (Exception ex)
        {
            return Result<byte[]>.Fail(
                Strings.Get("Error_GeneralFailure", $"vxSettings.ini 正規化失敗：{ex.Message}"),
                ExitCodes.GeneralFailure);
        }
    }
}
