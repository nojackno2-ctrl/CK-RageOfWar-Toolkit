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
            allowLiberatiHeroArmy = config.GameSettings.AllowLiberatiHeroArmy,
            allowMuleHeroArmy = config.GameSettings.AllowMuleHeroArmy,
            wagonCapacity10k = config.GameSettings.WagonCapacity10k
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
            stdout.WriteLine($"  - 允許運糧馬編入英雄隊伍 (Allow Food Mules in Hero Armies): {(config.GameSettings.AllowMuleHeroArmy ? "on" : "off")}");
            stdout.WriteLine($"  - 運糧馬運載上限提升至 10,000 (Increase Mule Capacity to 10,000): {(config.GameSettings.WagonCapacity10k ? "on" : "off")}");
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
            string err = Strings.Get("Error_InvalidArgs", "settings set 必須提供至少一個設定選項 (--viking-army, --liberati-army, --mule-army 或 --wagon-10k)");
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

                case "--mule-army":
                    if (TryParseOnOff(val, out bool muleVal))
                    {
                        config.GameSettings.AllowMuleHeroArmy = muleVal;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--mule-army 值必須為 on 或 off，實際為 '{val}'");
                        return OutputError("settings set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;

                case "--wagon-10k":
                    if (TryParseOnOff(val, out bool wagon10kVal))
                    {
                        config.GameSettings.WagonCapacity10k = wagon10kVal;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--wagon-10k 值必須為 on 或 off，實際為 '{val}'");
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
            allowLiberatiHeroArmy = config.GameSettings.AllowLiberatiHeroArmy,
            allowMuleHeroArmy = config.GameSettings.AllowMuleHeroArmy,
            wagonCapacity10k = config.GameSettings.WagonCapacity10k
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
            stdout.WriteLine($"  - 允許運糧馬編入英雄隊伍: {(config.GameSettings.AllowMuleHeroArmy ? "on" : "off")}");
            stdout.WriteLine($"  - 運糧馬運載上限提升至 10,000: {(config.GameSettings.WagonCapacity10k ? "on" : "off")}");
            if (warnings.Count > 0)
            {
                stdout.WriteLine();
                foreach (string w in warnings) stdout.WriteLine($"警告: {w}");
            }
        }

        return ExitCodes.Success;
    }
}
