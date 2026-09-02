using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.Core.Perf;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

public static partial class CliHost
{
    /// <summary>
    /// 處理 perf get 指令。讀取當前有效之效能修補設定。
    /// </summary>
    private static int HandlePerfGet(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        var perfData = new
        {
            laa = config.Perf.Laa,
            videoFix = config.Perf.VideoFix,
            keepRes = config.Perf.KeepRes,
            hires = config.Perf.Hires,
            resolution = config.Perf.Resolution,
            addRes = config.Perf.AddRes,
            desktopMode = config.Perf.DesktopMode,
            noObjectAnimations = config.Perf.NoObjectAnimations,
            noWaterAnimation = config.Perf.NoWaterAnimation
        };

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "perf get",
                Data = perfData,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("效能修補設定 (Performance Settings):");
            stdout.WriteLine($"  - LargeAddressAware (LAA): {(config.Perf.Laa ? "on" : "off")}");
            stdout.WriteLine($"  - VideoMode Fix (16bpp): {(config.Perf.VideoFix ? "on" : "off")}");
            stdout.WriteLine($"  - HiRes Zoom: {(config.Perf.Hires > 0 ? $"{config.Perf.Hires}" : "off")}");
            stdout.WriteLine($"  - Keep Resolution (res_writeback): {(config.Perf.KeepRes ? "on" : "off")}");
            stdout.WriteLine($"  - Desktop Mode: {config.Perf.DesktopMode}");
            stdout.WriteLine($"  - Resolution: {config.Perf.Resolution}");
            stdout.WriteLine($"  - Object Animations: {(!config.Perf.NoObjectAnimations ? "on" : "off")}");
            stdout.WriteLine($"  - Water Animation: {(!config.Perf.NoWaterAnimation ? "on" : "off")}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 perf set 指令。修改設定檔中之效能設定（嚴格不碰遊戲檔案，套用需執行 apply）。
    /// </summary>
    private static int HandlePerfSet(List<string> options, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        if (options.Count == 0)
        {
            string err = Strings.Get("Error_InvalidArgs", "perf set 必須提供至少一個設定選項");
            return OutputError("perf set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        if (config.LoadError is not null)
            return RejectCorruptConfig("perf set", config, isJson, stdout, stderr);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        for (int i = 0; i < options.Count; i++)
        {
            string opt = options[i];
            if (opt.StartsWith("--") && i + 1 < options.Count)
            {
                string flag = opt.ToLowerInvariant();
                string val = options[++i];

                switch (flag)
                {
                    case "--laa":
                        if (TryParseOnOff(val, out bool laa))
                        {
                            config.Perf.Laa = laa;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--laa 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--videofix":
                        if (TryParseOnOff(val, out bool vfix))
                        {
                            config.Perf.VideoFix = vfix;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--videofix 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--hires":
                        if (val.Equals("off", StringComparison.OrdinalIgnoreCase) || val == "0" || val.Equals("none", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Perf.Hires = 0;
                            config.Perf.AddRes.RemoveAll(r =>
                            {
                                var (rw, _) = PerfModule.ParseDimensions(r, 0, 0);
                                return rw > 1600;
                            });
                            var (curW, _) = PerfModule.ParseDimensions(config.Perf.Resolution, 0, 0);
                            if (curW > 1600)
                            {
                                string oldRes = config.Perf.Resolution;
                                config.Perf.Resolution = "1600x1200";
                                warnings.Add(Strings.Get("Warning_ResolutionExceedsCapacity", oldRes, 1600, "1600x1200", 3));
                            }
                        }
                        else
                        {
                            int w;
                            if (val.Contains('x', StringComparison.OrdinalIgnoreCase))
                            {
                                var parts = val.Split(['x', 'X'], StringSplitOptions.RemoveEmptyEntries);
                                if (parts.Length != 2 || !int.TryParse(parts[0], out w) || !int.TryParse(parts[1], out int h) || w <= 0 || h <= 0)
                                {
                                    return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--hires 格式必須為 <W>x<H> 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                                }
                            }
                            else if (!int.TryParse(val, out w) || w <= 0)
                            {
                                return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--hires 格式必須為 <W>x<H> 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                            }

                            // ZoomTables 本身能做到 16384，但 CVXVisible 的 32px 髒矩形網格只覆蓋到
                            // 4096 寬。容量開得比網格大只會讓使用者選到會塗抹又會閃退的解析度。
                            if (w < 1600 || w > CellGridPatch.MaxSurfaceWidth)
                            {
                                return OutputError("perf set", Strings.Get("Error_InvalidTableDimensions"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                            }

                            config.Perf.Hires = w;
                            config.Perf.AddRes.RemoveAll(r =>
                            {
                                var (rw, _) = PerfModule.ParseDimensions(r, 0, 0);
                                return rw > w;
                            });
                            var (curW, _) = PerfModule.ParseDimensions(config.Perf.Resolution, 0, 0);
                            if (curW > w)
                            {
                                string oldRes = config.Perf.Resolution;
                                string safeRes = "1600x1200";
                                int safeIdx = 3;
                                config.Perf.Resolution = safeRes;
                                warnings.Add(Strings.Get("Warning_ResolutionExceedsCapacity", oldRes, w, safeRes, safeIdx));
                            }
                        }
                        break;

                    case "--keepres":
                        if (TryParseOnOff(val, out bool keepRes))
                        {
                            config.Perf.KeepRes = keepRes;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--keepres 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--desktop":
                        if (val.Equals("suppress", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Perf.DesktopMode = "suppress";
                        }
                        else if (val.Equals("autoswitch", StringComparison.OrdinalIgnoreCase))
                        {
                            config.Perf.DesktopMode = "autoSwitch";
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--desktop 必須為 suppress 或 autoswitch，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--resolution":
                        var resParts = val.Split(['x', 'X'], StringSplitOptions.RemoveEmptyEntries);
                        if (resParts.Length != 2 || !int.TryParse(resParts[0], out int rw) || !int.TryParse(resParts[1], out int rh) || rw <= 0 || rh <= 0)
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--resolution 格式必須為 <寬>x<高>，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }

                        if (!CellGridPatch.IsSurfaceSupported(rw, rh))
                        {
                            return OutputError(
                                "perf set",
                                Strings.Get("Error_ResolutionExceedsGridCeiling", $"{rw}x{rh}",
                                    CellGridPatch.MaxSurfaceWidth, CellGridPatch.MaxSurfaceHeight),
                                ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }

                        string normRes = $"{rw}x{rh}";
                        config.Perf.Resolution = normRes;
                        if (rw > 1600)
                        {
                            if (!config.Perf.AddRes.Contains(normRes, StringComparer.OrdinalIgnoreCase))
                            {
                                config.Perf.AddRes.Add(normRes);
                            }
                        }
                        else
                        {
                            int curCapacity = config.Perf.Hires >= 1600 ? config.Perf.Hires : 1600;
                            config.Perf.AddRes.RemoveAll(r =>
                            {
                                var (w, _) = PerfModule.ParseDimensions(r, 0, 0);
                                return w > curCapacity;
                            });
                        }
                        break;

                    case "--anim-objects":
                        if (TryParseOnOff(val, out bool animObj))
                        {
                            config.Perf.NoObjectAnimations = !animObj;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--anim-objects 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    case "--anim-water":
                        if (TryParseOnOff(val, out bool animWater))
                        {
                            config.Perf.NoWaterAnimation = !animWater;
                        }
                        else
                        {
                            return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"--anim-water 必須為 on 或 off，實際為 '{val}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                        }
                        break;

                    default:
                        return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"未知的設定選項 '{opt}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
            }
            else
            {
                return OutputError("perf set", Strings.Get("Error_InvalidArgs", $"缺少選項值或無效的語法 '{opt}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }
        }

        // 儲存設定檔（純設定檔寫入，絕對不碰遊戲檔案）
        config.Save(configOverride);

        var perfData = new
        {
            laa = config.Perf.Laa,
            videoFix = config.Perf.VideoFix,
            keepRes = config.Perf.KeepRes,
            hires = config.Perf.Hires,
            resolution = config.Perf.Resolution,
            addRes = config.Perf.AddRes,
            desktopMode = config.Perf.DesktopMode,
            noObjectAnimations = config.Perf.NoObjectAnimations,
            noWaterAnimation = config.Perf.NoWaterAnimation
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "perf set",
                Data = perfData,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Perf_Set_Success"));
            stdout.WriteLine("更新後的效能修補設定 (Updated Performance Settings):");
            stdout.WriteLine($"  - LargeAddressAware (LAA): {(config.Perf.Laa ? "on" : "off")}");
            stdout.WriteLine($"  - VideoMode Fix (16bpp): {(config.Perf.VideoFix ? "on" : "off")}");
            stdout.WriteLine($"  - HiRes Zoom: {(config.Perf.Hires > 0 ? $"{config.Perf.Hires}" : "off")}");
            stdout.WriteLine($"  - Keep Resolution (res_writeback): {(config.Perf.KeepRes ? "on" : "off")}");
            stdout.WriteLine($"  - Desktop Mode: {config.Perf.DesktopMode}");
            stdout.WriteLine($"  - Resolution: {config.Perf.Resolution}");
            stdout.WriteLine($"  - Object Animations: {(!config.Perf.NoObjectAnimations ? "on" : "off")}");
            stdout.WriteLine($"  - Water Animation: {(!config.Perf.NoWaterAnimation ? "on" : "off")}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }
}
