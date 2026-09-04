using System.Globalization;
using System.Text.Json;
using CKToolkit.Core.Common;
using CKToolkit.Core.Runtime;
using CKToolkit.Core.Trainer;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

public static partial class CliHost
{
    /// <summary>
    /// <c>trainer exec</c>：把一個作弊（或一段任意 VS 腳本）送進執行中的遊戲立刻執行。
    ///
    /// 這是 ISSUE-068 的執行期通道對 AI 代理程式開的門。<c>trainer apply</c> 改的是磁碟上的
    /// 檔案、下一次開遊戲才生效；<c>trainer exec</c> 改的是**現在正在跑的這一場**。
    ///
    /// 用法：
    /// <code>
    ///   CKToolkit.exe trainer exec --cheat gold_fill [--param count=100]... [--json]
    ///   CKToolkit.exe trainer exec --script "ExploreAll();" [--json]
    /// </code>
    ///
    /// 遊戲沒在跑、或這一場沒有腳本通道（例如工具重開過、或遊戲不是由本工具啟動的），
    /// 一律 fail-closed 回錯誤，不做任何猜測性的注入——注入是使用者的決定，不是 CLI 的。
    /// </summary>
    private static int HandleTrainerExec(
        List<string> options,
        string? configOverride,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        string? cheatId = null;
        string? rawScript = null;
        var parameters = new Dictionary<string, object>(StringComparer.Ordinal);

        for (int i = 0; i < options.Count; i++)
        {
            string token = options[i];
            if (!token.StartsWith("--", StringComparison.Ordinal))
            {
                return OutputError("trainer exec",
                    Strings.Get("Error_InvalidArgs", $"無效的語法 '{token}'"),
                    ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }

            string flag;
            string val;
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
                return OutputError("trainer exec",
                    Strings.Get("Error_InvalidArgs", $"缺少選項值 '{token}'"),
                    ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }

            switch (flag)
            {
                case "--cheat":
                    cheatId = val.Trim();
                    break;
                case "--script":
                    rawScript = val;
                    break;
                case "--param":
                {
                    int eq = val.IndexOf('=');
                    if (eq <= 0)
                    {
                        return OutputError("trainer exec",
                            Strings.Get("Error_InvalidArgs", $"--param 格式必須為 <name>=<value>，實際為 '{val}'"),
                            ExitCodes.InvalidArgs, isJson, stdout, stderr);
                    }
                    parameters[val[..eq].Trim()] = val[(eq + 1)..];
                    break;
                }
                default:
                    return OutputError("trainer exec",
                        Strings.Get("Error_UnknownOption", flag),
                        ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }
        }

        if ((cheatId is null) == (rawScript is null))
        {
            return OutputError("trainer exec",
                Strings.Get("Error_InvalidArgs", "trainer exec 必須指定 --cheat <id> 或 --script <VS>，且兩者只能擇一"),
                ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        string script;
        if (cheatId is not null)
        {
            if (!Cheats.ById.TryGetValue(cheatId, out var cheat))
            {
                return OutputError("trainer exec",
                    Strings.Get("Error_TrainerUnknownCheat", cheatId),
                    ExitCodes.InvalidArgs, isJson, stdout, stderr);
            }

            var config = ToolkitConfig.Load(configOverride);
            if (config.LoadError is not null)
                return RejectCorruptConfig("trainer exec", config, isJson, stdout, stderr);

            // 參數解析與 apply 走同一條規則，CLI 與 GUI 才不會對同一個作弊產生不同的腳本。
            var selections = config.Trainer.Cheats
                .Where(c => Cheats.ById.ContainsKey(c.Id))
                .Select(c => new CheatSelection
                {
                    Id = c.Id,
                    Key = c.Key,
                    Parameters = c.Parameters.ToDictionary(
                        p => p.Key, p => (object)p.Value, StringComparer.Ordinal),
                })
                .ToList();

            var own = selections.FirstOrDefault(s => s.Id == cheat.Id)?.Parameters;
            var resolved = Cheats.ResolveParameters(cheat.Id, own, selections);

            // 命令列上明確給的 --param 蓋過設定檔。
            if (parameters.Count > 0)
            {
                var merged = new Dictionary<string, object>(StringComparer.Ordinal);
                if (resolved is not null)
                    foreach (var (k, v) in resolved) merged[k] = v;
                foreach (var (k, v) in parameters) merged[k] = v;
                resolved = merged;
            }

            script = Cheats.BuildRuntimeScript(
                cheat,
                Cheats.PlayerExpression(config.Trainer.PlayerMode, config.Trainer.FixedPlayer),
                resolved);
        }
        else
        {
            script = Cheats.FlattenScript(rawScript!);
        }

        int pid = GameRunner.FindGameProcessId();
        if (pid == 0)
        {
            // 用本功能自己帶進來的鍵，不借用其他 issue 的字串，這條路徑才不會相依於
            // 別的分支是否已經合併。
            return OutputError("trainer exec", Strings.Get("Error_ScriptChannelNoProcess"),
                ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        var opened = ScriptChannelSession.TryOpen((uint)pid);
        if (opened.IsError || opened.Value is null)
        {
            return OutputError("trainer exec", opened.ErrorMessage ?? Strings.Get("Error_ScriptChannelNoSession"),
                ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }

        using var channel = opened.Value;
        var run = channel.Run(script);
        if (run.IsError)
        {
            return OutputError("trainer exec", run.ErrorMessage ?? Strings.Get("Error_ScriptChannelUnreachable"),
                ExitCodes.GeneralFailure, isJson, stdout, stderr);
        }
        ScriptRunOutcome outcome = run.Value;

        var data = new
        {
            processId = pid,
            cheat = cheatId,
            status = outcome.Status.ToString(),
            statusCode = (int)outcome.Status,
            ran = outcome.Ran,
            message = ScriptChannel.Describe(outcome),
            engineDetail = outcome.Detail,
            script,
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = outcome.Ran,
                Command = "trainer exec",
                Data = data,
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine($"pid {pid}: {ScriptChannel.Describe(outcome)} ({outcome.Status})");
        }

        // 引擎明確拒絕（腳本錯誤、不在對局中）也要有非零退出碼，AI 代理程式才判得出來。
        return outcome.Ran ? ExitCodes.Success : ExitCodes.GeneralFailure;
    }

    private static int HandleTrainerListCheats(bool isJson, TextWriter stdout)
    {
        var cheatList = Cheats.All.Select(c => new
        {
            id = c.Id,
            label = TrainerStrings.GetCheatName(c.Id, c.Name),
            name = TrainerStrings.GetCheatName(c.Id, c.Name),
            description = TrainerStrings.GetCheatDescription(c.Id, c.Description),
            defaultKey = c.DefaultKey,
            defaultKeyDisplay = KeyMap.Display(c.DefaultKey, numpadKeys: false),
            numpadKey = c.NumpadKey,
            numpadKeyDisplay = KeyMap.Display(c.NumpadKey, numpadKeys: true),
            defaultEnabled = c.DefaultEnabled,
            numpadDefaultEnabled = c.NumpadDefaultEnabled,
            parameters = c.Parameters.Select(p => new
            {
                name = p.Name,
                label = TrainerStrings.GetCheatParamLabel(c.Id, p),
                description = TrainerStrings.GetCheatParamLabel(c.Id, p),
                @default = p.Default,
                minimum = p.Minimum,
                maximum = p.Maximum,
                isText = p.IsText,
                isMulti = p.IsMulti,
                hidden = p.Hidden
            }).ToList()
        }).ToList();

        var data = new
        {
            totalCheats = cheatList.Count,
            cheats = cheatList
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "trainer list-cheats",
                Data = data
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine($"作弊項目清單 (Available Cheats - {Cheats.All.Count}):");
            foreach (var c in Cheats.All)
            {
                stdout.WriteLine($"  * {c.Id} - {TrainerStrings.GetCheatName(c.Id, c.Name)}");
                stdout.WriteLine($"      預設按鍵 (原版): {c.DefaultKey} ({KeyMap.Display(c.DefaultKey, false)}) [{(c.DefaultEnabled ? "預設開啟" : "預設關閉")}]");
                stdout.WriteLine($"      預設按鍵 (小鍵盤): {c.NumpadKey} ({KeyMap.Display(c.NumpadKey, true)}) [{(c.NumpadDefaultEnabled ? "預設開啟" : "預設關閉")}]");
                stdout.WriteLine($"      說明: {TrainerStrings.GetCheatDescription(c.Id, c.Description)}");
                if (c.Parameters.Count > 0)
                {
                    stdout.WriteLine("      參數 (Parameters):");
                    foreach (var p in c.Parameters)
                    {
                        string rangeStr = p.IsText ? "(文字選項)" : $"[{p.Minimum}..{p.Maximum}]";
                        stdout.WriteLine($"        - {p.Name}: {TrainerStrings.GetCheatParamLabel(c.Id, p)} (預設: {p.Default}, 範圍: {rangeStr})");
                    }
                }
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleTrainerListTweaks(bool isJson, TextWriter stdout)
    {
        var groups = Tweaks.Groups().Select(g => new
        {
            group = g.Group,
            label = TrainerStrings.GetGroupName(g.Group),
            name = TrainerStrings.GetGroupName(g.Group),
            tweaks = g.Items.Select(t => new
            {
                id = t.Id,
                group = t.Group,
                label = TrainerStrings.GetTweakName(t.Id, t.Label),
                name = TrainerStrings.GetTweakName(t.Id, t.Label),
                description = TrainerStrings.GetTweakDescription(t.Id, t.Description),
                @default = t.Default,
                minimum = t.Minimum,
                maximum = t.Maximum,
                isMultiplier = t.IsMultiplier,
                scopedSupported = ScopedTweakPatch.IsSupportedScopedTweakId(t.Id),
                scopes = ScopedTweakPatch.GetSupportedScopes(t.Id)
            }).ToList()
        }).ToList();

        var flatTweaks = Tweaks.All.Select(t => new
        {
            id = t.Id,
            group = t.Group,
            label = TrainerStrings.GetTweakName(t.Id, t.Label),
            name = TrainerStrings.GetTweakName(t.Id, t.Label),
            description = TrainerStrings.GetTweakDescription(t.Id, t.Description),
            @default = t.Default,
            minimum = t.Minimum,
            maximum = t.Maximum,
            isMultiplier = t.IsMultiplier,
            scopedSupported = ScopedTweakPatch.IsSupportedScopedTweakId(t.Id),
            scopes = ScopedTweakPatch.GetSupportedScopes(t.Id)
        }).ToList();

        var data = new
        {
            totalTweaks = flatTweaks.Count,
            groups,
            tweaks = flatTweaks
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "trainer list-tweaks",
                Data = data
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine("數值調整清單 (Available Tweaks):");
            foreach (var (grp, items) in Tweaks.Groups())
            {
                stdout.WriteLine($"\n[{TrainerStrings.GetGroupName(grp)}] ({items.Count} 項)");
                foreach (var t in items)
                {
                    string mulStr = t.IsMultiplier ? " [倍率 / Multiplier]" : "";
                    stdout.WriteLine($"  * {t.Id}{mulStr}: {TrainerStrings.GetTweakName(t.Id, t.Label)}");
                    stdout.WriteLine($"      預設: {t.Default} (範圍: [{t.Minimum}..{t.Maximum}])");
                    IReadOnlyList<string> scopes = ScopedTweakPatch.GetSupportedScopes(t.Id);
                    if (scopes.Count > 0)
                        stdout.WriteLine($"      分流 scope: {string.Join(", ", scopes)}");
                    stdout.WriteLine($"      說明: {TrainerStrings.GetTweakDescription(t.Id, t.Description)}");
                }
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleTrainerSet(
        List<string> options,
        string? gameOverride,
        string? configOverride,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        if (options.Count == 0)
        {
            string err = Strings.Get("Error_InvalidArgs", "trainer set 必須提供至少一個設定選項");
            return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
        }

        var config = ToolkitConfig.Load(configOverride);
        if (config.LoadError is not null)
            return RejectCorruptConfig("trainer set", config, isJson, stdout, stderr);
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

            if (token.StartsWith("--"))
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
                    return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                }
            }
            else
            {
                string err = Strings.Get("Error_InvalidArgs", $"無效的語法 '{token}'");
                return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }

            switch (flag)
            {
                case "--cheat":
                {
                    int eq = val.IndexOf('=');
                    if (eq <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--cheat 格式必須為 <id>=on|off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string cheatId = val[..eq].Trim();
                    string stateStr = val[(eq + 1)..].Trim();

                    if (!Cheats.ById.TryGetValue(cheatId, out var cheat))
                    {
                        string err = Strings.Get("Error_TrainerUnknownCheat", cheatId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!TryParseOnOff(stateStr, out bool cheatEnabled))
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--cheat 開關必須為 on 或 off，實際為 '{stateStr}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var cheatCfg = config.Trainer.Cheats.FirstOrDefault(c => c.Id.Equals(cheat.Id, StringComparison.OrdinalIgnoreCase));
                    if (cheatCfg is null)
                    {
                        cheatCfg = new CheatConfig
                        {
                            Id = cheat.Id,
                            Enabled = cheatEnabled,
                            Key = cheat.DefaultKeyFor(config.Trainer.NumpadKeys)
                        };
                        config.Trainer.Cheats.Add(cheatCfg);
                    }
                    else
                    {
                        cheatCfg.Enabled = cheatEnabled;
                    }
                    break;
                }

                case "--key":
                {
                    int eq = val.IndexOf('=');
                    if (eq <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--key 格式必須為 <id>=<KEY>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string cheatId = val[..eq].Trim();
                    string keyName = val[(eq + 1)..].Trim();

                    if (!Cheats.ById.TryGetValue(cheatId, out var cheat))
                    {
                        string err = Strings.Get("Error_TrainerUnknownCheat", cheatId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var binding = KeyMap.All.FirstOrDefault(b => b.Key.Equals(keyName, StringComparison.OrdinalIgnoreCase));
                    if (binding is null)
                    {
                        string err = Strings.Get("Error_TrainerInvalidKey", keyName);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var cheatCfg = config.Trainer.Cheats.FirstOrDefault(c => c.Id.Equals(cheat.Id, StringComparison.OrdinalIgnoreCase));
                    if (cheatCfg is null)
                    {
                        cheatCfg = new CheatConfig
                        {
                            Id = cheat.Id,
                            Enabled = cheat.DefaultEnabledFor(config.Trainer.NumpadKeys),
                            Key = binding.Key
                        };
                        config.Trainer.Cheats.Add(cheatCfg);
                    }
                    else
                    {
                        cheatCfg.Key = binding.Key;
                    }
                    break;
                }

                case "--param":
                {
                    int dotIdx = val.IndexOf('.');
                    if (dotIdx <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--param 格式必須為 <id>.<name>=<v>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string cheatId = val[..dotIdx].Trim();
                    string rest = val[(dotIdx + 1)..];
                    int eqIdx = rest.IndexOf('=');
                    if (eqIdx <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--param 格式必須為 <id>.<name>=<v>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string paramName = rest[..eqIdx].Trim();
                    string paramVal = rest[(eqIdx + 1)..].Trim();

                    if (!Cheats.ById.TryGetValue(cheatId, out var cheat))
                    {
                        string err = Strings.Get("Error_TrainerUnknownCheat", cheatId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    var paramDef = cheat.Parameters.FirstOrDefault(p => p.Name.Equals(paramName, StringComparison.OrdinalIgnoreCase));
                    if (paramDef is null)
                    {
                        string err = Strings.Get("Error_TrainerUnknownParam", cheat.Id, paramName);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!paramDef.IsText)
                    {
                        if (!decimal.TryParse(paramVal, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out decimal numVal))
                        {
                            string err = Strings.Get("Error_TrainerParamOutOfRange", cheat.Id, paramDef.Name, paramVal, paramDef.Minimum, paramDef.Maximum);
                            return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                        }
                        if ((paramDef.Minimum != 0 || paramDef.Maximum != 0) && (numVal < paramDef.Minimum || numVal > paramDef.Maximum))
                        {
                            string err = Strings.Get("Error_TrainerParamOutOfRange", cheat.Id, paramDef.Name, paramVal, paramDef.Minimum, paramDef.Maximum);
                            return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                        }
                    }
                    else if (paramDef.HasOptions)
                    {
                        var validOptions = paramDef.Options?.Select(o => o.Value).ToHashSet(StringComparer.OrdinalIgnoreCase)
                            ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        if (paramDef.IsMulti)
                        {
                            var parts = paramVal.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (parts.Length == 0)
                            {
                                string err = Strings.Get("Error_TrainerParamInvalidOption", cheat.Id, paramDef.Name, paramVal);
                                return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                            }
                            foreach (var p in parts)
                            {
                                if (!validOptions.Contains(p))
                                {
                                    string err = Strings.Get("Error_TrainerParamInvalidOption", cheat.Id, paramDef.Name, p);
                                    return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                                }
                            }
                        }
                        else
                        {
                            if (!validOptions.Contains(paramVal))
                            {
                                string err = Strings.Get("Error_TrainerParamInvalidOption", cheat.Id, paramDef.Name, paramVal);
                                return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                            }
                        }
                    }

                    var cheatCfg = config.Trainer.Cheats.FirstOrDefault(c => c.Id.Equals(cheat.Id, StringComparison.OrdinalIgnoreCase));
                    if (cheatCfg is null)
                    {
                        cheatCfg = new CheatConfig
                        {
                            Id = cheat.Id,
                            Enabled = cheat.DefaultEnabledFor(config.Trainer.NumpadKeys),
                            Key = cheat.DefaultKeyFor(config.Trainer.NumpadKeys)
                        };
                        config.Trainer.Cheats.Add(cheatCfg);
                    }
                    cheatCfg.Parameters[paramDef.Name] = paramVal;
                    break;
                }

                case "--tweak":
                {
                    int eq = val.IndexOf('=');
                    if (eq <= 0)
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--tweak 格式必須為 <id>=<value>，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    string tweakId = val[..eq].Trim();
                    string tweakValStr = val[(eq + 1)..].Trim();

                    if (Tweaks.Retired.Contains(tweakId))
                    {
                        string err = Strings.Get("Error_TrainerRetiredTweak", tweakId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!Tweaks.ById.TryGetValue(tweakId, out var tweak))
                    {
                        string err = Strings.Get("Error_TrainerUnknownTweak", tweakId);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!decimal.TryParse(tweakValStr, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out decimal decVal))
                    {
                        string err = Strings.Get("Error_TrainerTweakOutOfRange", tweak.Id, tweakValStr, tweak.Minimum, tweak.Maximum);
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    decimal clamped = Math.Clamp(decVal, tweak.Minimum, tweak.Maximum);
                    if (clamped != decVal)
                    {
                        warnings.Add(Strings.Get("Warning_TrainerTweakClamped", tweak.Id, decVal, clamped, tweak.Minimum, tweak.Maximum));
                    }

                    config.Trainer.Tweaks[tweak.Id] = clamped;
                    break;
                }

                case "--scoped-tweak":
                {
                    int dot = val.IndexOf('.');
                    int eq = val.IndexOf('=', dot + 1);
                    if (dot <= 0 || eq <= dot + 1)
                    {
                        string err = Strings.Get(
                            "Error_InvalidArgs",
                            $"--scoped-tweak 格式必須為 <id>.<scope>=<value>，實際為 '{val}'");
                        return OutputError(
                            "trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    string tweakId = val[..dot].Trim();
                    string scope = val[(dot + 1)..eq].Trim();
                    string tweakValStr = val[(eq + 1)..].Trim();

                    if (Tweaks.Retired.Contains(tweakId))
                    {
                        string err = Strings.Get("Error_TrainerRetiredTweak", tweakId);
                        return OutputError(
                            "trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    if (!Tweaks.ById.TryGetValue(tweakId, out var tweak))
                    {
                        string err = Strings.Get("Error_TrainerUnknownTweak", tweakId);
                        return OutputError(
                            "trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    if (!ScopedTweakPatch.IsSupportedScopedTweakId(tweak.Id))
                    {
                        string err = Strings.Get("Error_TrainerScopedTweakUnsupported", tweak.Id);
                        return OutputError(
                            "trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    if (!ScopedTweakPatch.GetSupportedScopes(tweak.Id).Contains(scope, StringComparer.Ordinal))
                    {
                        string err = Strings.Get("Error_TrainerScopedTweakUnknownScope", tweak.Id, scope);
                        return OutputError(
                            "trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    if (!decimal.TryParse(
                            tweakValStr,
                            NumberStyles.Float | NumberStyles.AllowThousands,
                            CultureInfo.InvariantCulture,
                            out decimal decVal))
                    {
                        string err = Strings.Get(
                            "Error_TrainerTweakOutOfRange",
                            $"{tweak.Id}.{scope}",
                            tweakValStr,
                            tweak.Minimum,
                            tweak.Maximum);
                        return OutputError(
                            "trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }

                    decimal clamped = Math.Clamp(decVal, tweak.Minimum, tweak.Maximum);
                    if (clamped != decVal)
                    {
                        warnings.Add(Strings.Get("Warning_TrainerTweakClamped", $"{tweak.Id}.{scope}", decVal, clamped, tweak.Minimum, tweak.Maximum));
                    }

                    if (!config.Trainer.ScopedTweaks.TryGetValue(
                            tweak.Id,
                            out Dictionary<string, decimal>? scopedValues))
                    {
                        scopedValues = new Dictionary<string, decimal>(StringComparer.Ordinal);
                        config.Trainer.ScopedTweaks[tweak.Id] = scopedValues;
                    }
                    scopedValues[scope] = clamped;
                    break;
                }

                case "--mode":
                {
                    if (val.Equals("both", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("patch", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("file", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("panel", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("window", StringComparison.OrdinalIgnoreCase) ||
                        val.Equals("inject", StringComparison.OrdinalIgnoreCase))
                    {
                        config.Trainer.Mode = val.ToLowerInvariant();
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--mode 必須為 both、patch 或 panel，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--numpad":
                {
                    if (TryParseOnOff(val, out bool numpad))
                    {
                        config.Trainer.NumpadKeys = numpad;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--numpad 必須為 on 或 off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--trainer" or "--enabled":
                {
                    if (TryParseOnOff(val, out bool enabled))
                    {
                        config.Trainer.Enabled = enabled;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--trainer 必須為 on 或 off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--player-mode":
                {
                    if (val.Equals("auto", StringComparison.OrdinalIgnoreCase) || val.Equals("fixed", StringComparison.OrdinalIgnoreCase))
                    {
                        config.Trainer.PlayerMode = val.ToLowerInvariant();
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--player-mode 必須為 auto 或 fixed，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--fixed-player":
                {
                    if (int.TryParse(val, out int fp) && fp >= 1 && fp <= 16)
                    {
                        config.Trainer.FixedPlayer = fp;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--fixed-player 必須為 1 到 16 之間的整數，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                case "--keep-vanilla":
                {
                    if (TryParseOnOff(val, out bool kv))
                    {
                        config.Trainer.KeepVanilla = kv;
                    }
                    else
                    {
                        string err = Strings.Get("Error_InvalidArgs", $"--keep-vanilla 必須為 on 或 off，實際為 '{val}'");
                        return OutputError("trainer set", err, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
                    }
                    break;
                }

                default:
                    return OutputError("trainer set", Strings.Get("Error_InvalidArgs", $"未知的設定選項 '{token}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }
        }

        // 與核心產生器共用同一套按鍵驗證；任何失敗都必須發生在 config.Save 之前。
        if (config.Trainer.SupportsFilePatch)
        {
            var enabledSelections = config.Trainer.Cheats
                .Where(c => c.Enabled && Cheats.ById.ContainsKey(c.Id))
                .Select(c => new CheatSelection
                {
                    Id = c.Id,
                    Key = c.Key,
                    Parameters = c.Parameters.ToDictionary(
                        p => p.Key, p => (object)p.Value, StringComparer.Ordinal)
                })
                .ToList();
            try
            {
                Cheats.ValidateBindings(
                    enabledSelections, config.Trainer.KeepVanilla, config.Trainer.NumpadKeys);
            }
            catch (InvalidOperationException ex)
            {
                return OutputError(
                    "trainer set", ex.Message, ExitCodes.InvalidArgs, isJson, stdout, stderr, warnings);
            }
        }

        // 當 trainer.enabled 為 false 時加入警告提醒
        if (!config.Trainer.Enabled)
        {
            warnings.Add(Strings.Get("Warning_TrainerNotEnabled"));
        }

        // 寫入設定檔（嚴格僅寫入 cktoolkit.json，絕對不碰遊戲檔案）
        config.Save(configOverride);

        var trainerData = new
        {
            enabled = config.Trainer.Enabled,
            numpadKeys = config.Trainer.NumpadKeys,
            playerMode = config.Trainer.PlayerMode,
            fixedPlayer = config.Trainer.FixedPlayer,
            keepVanilla = config.Trainer.KeepVanilla,
            cheats = config.Trainer.Cheats.Select(c => new
            {
                id = c.Id,
                enabled = c.Enabled,
                key = c.Key,
                parameters = c.Parameters
            }).ToList(),
            tweaks = config.Trainer.Tweaks,
            scopedTweaks = config.Trainer.ScopedTweaks
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "trainer set",
                Data = trainerData,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Trainer_Set_Success"));
            stdout.WriteLine("更新後的修改器設定 (Updated Trainer Settings):");
            stdout.WriteLine($"  - 修改器開關 (Enabled): {(config.Trainer.Enabled ? "on" : "off")}");
            stdout.WriteLine($"  - 按鍵模式 (NumpadKeys): {(config.Trainer.NumpadKeys ? "小鍵盤 (numpad)" : "原版 (original)")}");
            stdout.WriteLine($"  - 玩家目標 (PlayerMode): {config.Trainer.PlayerMode}{(config.Trainer.PlayerMode == "fixed" ? $" (玩家 #{config.Trainer.FixedPlayer})" : "")}");
            stdout.WriteLine($"  - 保留原版按鍵 (KeepVanilla): {(config.Trainer.KeepVanilla ? "on" : "off")}");
            stdout.WriteLine($"  - 已設定作弊項目數: {config.Trainer.Cheats.Count(c => c.Enabled)} 項啟用 / {config.Trainer.Cheats.Count} 項設定");
            foreach (var c in config.Trainer.Cheats.Where(c => c.Enabled))
            {
                string name = Cheats.ById.TryGetValue(c.Id, out var cheat) ? TrainerStrings.GetCheatName(c.Id, cheat.Name) : c.Id;
                stdout.WriteLine($"      * {c.Id} ({name}): [{(c.Enabled ? "on" : "off")}] key={c.Key}");
            }
            stdout.WriteLine($"  - 已設定調整項目 (Tweaks): {config.Trainer.Tweaks.Count} 項");
            foreach (var (k, v) in config.Trainer.Tweaks)
            {
                string name = Tweaks.ById.TryGetValue(k, out var tweak) ? TrainerStrings.GetTweakName(k, tweak.Label) : k;
                stdout.WriteLine($"      * {k} ({name}) = {v}");
            }
            int scopedCount = config.Trainer.ScopedTweaks.Sum(kv => kv.Value.Count);
            stdout.WriteLine($"  - 已設定分流調整 (ScopedTweaks): {scopedCount} 個 scope");
            foreach (var (id, values) in config.Trainer.ScopedTweaks)
            {
                string name = Tweaks.ById.TryGetValue(id, out var tweak) ? TrainerStrings.GetTweakName(id, tweak.Label) : id;
                foreach (var (scope, value) in values)
                    stdout.WriteLine($"      * {id}.{scope} ({name}) = {value}");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stdout.WriteLine($"  ! {w}");
            }
        }

        return ExitCodes.Success;
    }
}
