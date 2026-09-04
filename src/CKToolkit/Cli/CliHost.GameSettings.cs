using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

public static partial class CliHost
{
    private static int HandleGameSettingsGet(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        var data = new
        {
            allowVikingLordHeroArmy = config.GameSettings.AllowVikingLordHeroArmy,
            allowLiberatiHeroArmy = config.GameSettings.AllowLiberatiHeroArmy
        };

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "settings get",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("遊戲設定 (Game Settings):");
            stdout.WriteLine($"  - 允許維京領主編入英雄隊伍 (Allow Viking Lords in Hero Armies): {(config.GameSettings.AllowVikingLordHeroArmy ? "on" : "off")}");
            stdout.WriteLine($"  - 允許自由鬥士編入英雄隊伍 (Allow Liberati in Hero Armies): {(config.GameSettings.AllowLiberatiHeroArmy ? "on" : "off")}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine();
                foreach (string w in warnings) stdout.WriteLine($"警告: {w}");
            }
        }
        return ExitCodes.Success;
    }

    private static int HandleGameSettingsSet(List<string> options, string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        if (options.Count == 0)
        {
            string err = Strings.Get("Error_InvalidArgs", "settings set 必須提供至少一個設定選項 (--viking-army 或 --liberati-army)");
            return OutputError("settings set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        if (config.LoadError is not null)
            return RejectCorruptConfig("settings set", config, isJson, stdout, stderr);
        if (!string.IsNullOrWhiteSpace(gameOverride))
        {
            config.GameDir = gameOverride;
        }

        var warnings = new List<string>(config.MigrationsApplied);
        if (config.LoadError is not null) warnings.Insert(0, config.LoadError);

        for (int i = 0; i < options.Count; i++)
        {
            string token = options[i];
            string flag;
            string val;

            if (token.StartsWith("--", StringComparison.Ordinal))
            {
                int eqIdx = token.IndexOf('=');
                if (eqIdx > 0)
                {
                    flag = token[..eqIdx].ToLowerInvariant();
                    val = token[(eqIdx + 1)..];
                }
                else if (i + 1 < options.Count)
                {
                    flag = token.ToLowerInvariant();
                    val = options[++i];
                }
                else
                {
                    string err = Strings.Get("Error_InvalidArgs", $"缺少選項值 '{token}'");
                    return OutputError("settings set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                }
            }
            else
            {
                string err = Strings.Get("Error_InvalidArgs", $"無效的語法 '{token}'");
                return OutputError("settings set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }

            switch (flag)
            {
                case "--viking-army":
                    if (TryParseOnOff(val, out bool vikingVal))
                    {
                        config.GameSettings.AllowVikingLordHeroArmy = vikingVal;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--viking-army 值必須為 on 或 off，實際為 '{val}'");
                        return OutputError("settings set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;

                case "--liberati-army":
                    if (TryParseOnOff(val, out bool liberatiVal))
                    {
                        config.GameSettings.AllowLiberatiHeroArmy = liberatiVal;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--liberati-army 值必須為 on 或 off，實際為 '{val}'");
                        return OutputError("settings set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;

                default:
                    return OutputError("settings set", Strings.Get("Error_UnknownOption", flag), ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }
        }

        try
        {
            config.Save(configOverride);
        }
        catch (Exception ex)
        {
            string err = Strings.Get("Error_ConfigSaveFailed", ex.Message);
            return OutputError("settings set", err, ExitCodes.GeneralFailure, isJson, stdout, stderr, warnings);
        }

        var data = new
        {
            allowVikingLordHeroArmy = config.GameSettings.AllowVikingLordHeroArmy,
            allowLiberatiHeroArmy = config.GameSettings.AllowLiberatiHeroArmy
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "settings set",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("遊戲設定已更新：");
            stdout.WriteLine($"  - 允許維京領主編入英雄隊伍: {(config.GameSettings.AllowVikingLordHeroArmy ? "on" : "off")}");
            stdout.WriteLine($"  - 允許自由鬥士編入英雄隊伍: {(config.GameSettings.AllowLiberatiHeroArmy ? "on" : "off")}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine();
                foreach (string w in warnings) stdout.WriteLine($"警告: {w}");
            }
        }

        return ExitCodes.Success;
    }
}
