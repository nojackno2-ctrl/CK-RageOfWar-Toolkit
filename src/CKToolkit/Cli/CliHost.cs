using System.Runtime.InteropServices;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Cli;

/// <summary>
/// CLI 回傳之 JSON 封套結構 (SPEC.md §10)。
/// </summary>
public sealed class JsonEnvelope
{
    [JsonPropertyName("ok")]
    public bool Ok { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; } = string.Empty;

    [JsonPropertyName("data")]
    public object? Data { get; set; }

    [JsonPropertyName("warnings")]
    public List<string> Warnings { get; set; } = [];

    [JsonPropertyName("errors")]
    public List<string> Errors { get; set; } = [];

    [JsonPropertyName("error")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
}

/// <summary>
/// CLI 命令列處理主機 (SPEC.md §10)。
/// 供 AI 代理程式非互動式驅動，支援結構化 JSON 輸出封套與標準結束代碼。
/// 確保輸出永遠為無 BOM 之 UTF-8 編碼。
/// </summary>
public static partial class CliHost
{
    private const int AttachParentProcess = -1;

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AttachConsole(int dwProcessId);

    [LibraryImport("kernel32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllocConsole();

    private static readonly JsonSerializerOptions JsonEnvelopeOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions JsonLineOptions = new(JsonEnvelopeOptions)
    {
        WriteIndented = false
    };

    /// <summary>
    /// CLI 主進入點。設定無 BOM UTF-8 編碼並執行指令。
    /// </summary>
    public static int Run(string[] args)
    {
        var utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        Console.OutputEncoding = utf8NoBom;
        Console.InputEncoding = utf8NoBom;

        EnsureConsole();

        Console.OutputEncoding = utf8NoBom;
        Console.InputEncoding = utf8NoBom;

        using var stdout = new StreamWriter(Console.OpenStandardOutput(), utf8NoBom) { AutoFlush = true };
        using var stderr = new StreamWriter(Console.OpenStandardError(), utf8NoBom) { AutoFlush = true };
        Console.SetOut(stdout);
        Console.SetError(stderr);

        try
        {
            return Execute(args, stdout, stderr);
        }
        catch (Exception ex)
        {
            return OutputUnhandled(args, ex, stdout, stderr);
        }
    }

    private static void EnsureConsole()
    {
        if (!AttachConsole(AttachParentProcess))
        {
            AllocConsole();
        }
    }

    /// <summary>
    /// 執行 CLI 指令並輸出至指定 TextWriter（便於單元測試與自我測試）。
    /// </summary>
    public static int Execute(string[] args, TextWriter stdout, TextWriter stderr)
    {
        try
        {
            return ExecuteCore(args, stdout, stderr);
        }
        catch (Exception ex)
        {
            return OutputUnhandled(args, ex, stdout, stderr);
        }
    }

    private static int ExecuteCore(string[] args, TextWriter stdout, TextWriter stderr)
    {
        // 先掃一遍 --json：全域解析本身也可能失敗，那些錯誤同樣必須以 JSON 回報。
        // 舊寫法在迴圈裡才設定 isJson，一旦 --json 被 --game 吃掉就整個退回純文字（ISSUE-065）。
        bool isJson = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
        string? gameDirOverride = null;
        string? configPathOverride = null;
        var commands = new List<string>();

        // 解析全域旗標與指令 tokens。
        // 無法辨識的 token 仍然往下傳給子指令（--help、perf set --resolution 都靠這條路），
        // 真正的把關在各指令自己的選項白名單，以及下方 RejectExtraArgs。
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.Equals("--json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (arg.Equals("--game", StringComparison.OrdinalIgnoreCase)
                || arg.Equals("--config", StringComparison.OrdinalIgnoreCase))
            {
                // 取值前先確認下一個 token 存在且不是另一個選項；否則會靜默吃掉 --json
                // 之類的旗標，讓代理程式拿到跑在別的目標上、又不是 JSON 的結果（ISSUE-065）。
                if (i + 1 >= args.Length || IsOptionToken(args[i + 1]))
                {
                    return OutputError(arg, Strings.Get("Error_OptionRequiresValue", arg),
                        ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }

                if (arg.Equals("--game", StringComparison.OrdinalIgnoreCase))
                    gameDirOverride = args[++i];
                else
                    configPathOverride = args[++i];
                continue;
            }

            commands.Add(arg);
        }

        // 不吃任何選項的指令：多餘的 token 幾乎都是打錯的選項名稱，必須明確拒絕（ISSUE-065）。
        int consumed = NoOptionCommandTokenCount(commands);
        if (consumed > 0
            && RejectExtraArgs(string.Join(' ', commands.Take(consumed)), commands.Skip(consumed),
                   isJson, stdout, stderr, out int rejectExit))
        {
            return rejectExit;
        }

        string primaryCmd = commands.Count > 0 ? commands[0].ToLowerInvariant() : "help";

        switch (primaryCmd)
        {
            case "--help" or "-h" or "help":
                return HandleHelp(isJson, stdout);

            case "--version" or "-v" or "version":
                return HandleVersion(isJson, stdout);

            case "status":
                return HandleStatus(gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "apply":
                return HandleApply(gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "restore":
                return HandleRestore(commands.Skip(1).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "verify":
                return HandleVerify(gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "perf":
                if (commands.Count < 2)
                {
                    string err = Strings.Get("Error_PerfSubcommandRequired");
                    return OutputError("perf", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
                string subCmd = commands[1].ToLowerInvariant();
                if (subCmd == "get")
                {
                    return HandlePerfGet(gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (subCmd == "set")
                {
                    return HandlePerfSet(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                return OutputError("perf", Strings.Get("Error_InvalidArgs", $"未知的 perf 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            case "lang":
                if (commands.Count < 2)
                {
                    string err = Strings.Get("Error_LangSubcommandRequired");
                    return OutputError("lang", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
                string langSubCmd = commands[1].ToLowerInvariant();
                if (langSubCmd == "list")
                {
                    return HandleLangList(gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "install")
                {
                    return HandleLangInstall(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "uninstall")
                {
                    return HandleLangUninstall(gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "import")
                {
                    return HandleLangImport(commands.Skip(2).ToList(), configPathOverride, isJson, stdout, stderr);
                }
                if (langSubCmd == "export-template")
                {
                    return HandleLangExportTemplate(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                }
                return OutputError("lang", Strings.Get("Error_InvalidArgs", $"未知的 lang 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            case "trainer":
                if (commands.Count < 2)
                    return OutputError("trainer", Strings.Get("Error_TrainerSubcommandRequired"), ExitCodes.InvalidArgs, isJson, stdout, stderr);
                string trainerSubCmd = commands[1].ToLowerInvariant();
                if (trainerSubCmd == "list-cheats")
                    return HandleTrainerListCheats(isJson, stdout);
                if (trainerSubCmd == "list-tweaks")
                    return HandleTrainerListTweaks(isJson, stdout);
                if (trainerSubCmd == "set")
                    return HandleTrainerSet(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                if (trainerSubCmd == "apply")
                    return HandleApply(gameDirOverride, configPathOverride, isJson, stdout, stderr, "trainer apply");
                if (trainerSubCmd == "exec")
                    return HandleTrainerExec(commands.Skip(2).ToList(), configPathOverride, isJson, stdout, stderr);
                return OutputError("trainer", Strings.Get("Error_InvalidArgs", $"未知的 trainer 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            case "settings" or "gamesettings":
                if (commands.Count < 2)
                {
                    string err = Strings.Get("Error_SettingsSubcommandRequired");
                    return OutputError("settings", err, ExitCodes.InvalidArgs, isJson, stdout, stderr);
                }
                string settingsSubCmd = commands[1].ToLowerInvariant();
                if (settingsSubCmd == "get")
                    return HandleGameSettingsGet(gameDirOverride, configPathOverride, isJson, stdout, stderr);
                if (settingsSubCmd == "set")
                    return HandleGameSettingsSet(commands.Skip(2).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);
                return OutputError("settings", Strings.Get("Error_InvalidArgs", $"未知的 settings 子指令 '{commands[1]}'"), ExitCodes.InvalidArgs, isJson, stdout, stderr);

            case "save":
                return HandleSave(commands.Skip(1).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "profile":
                return HandleProfile(commands.Skip(1).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);

            case "run":
                return HandleRun(commands.Skip(1).ToList(), gameDirOverride, configPathOverride, isJson, stdout, stderr);

            default:
                return HandleUnknown(primaryCmd, isJson, stdout, stderr);
        }
    }


    /// <summary>看起來像選項的 token。遊戲目錄與設定檔路徑都不會以 `--` 開頭。</summary>
    private static bool IsOptionToken(string token) =>
        token.StartsWith("--", StringComparison.Ordinal);

    /// <summary>
    /// 這條指令路徑本身佔用幾個 token；回傳 0 代表該指令有自己的選項白名單，
    /// 多餘 token 交給它自己驗證（例如 perf set、save、run）。
    /// </summary>
    private static int NoOptionCommandTokenCount(IReadOnlyList<string> commands)
    {
        if (commands.Count == 0) return 0;
        string first = commands[0].ToLowerInvariant();
        string? second = commands.Count > 1 ? commands[1].ToLowerInvariant() : null;

        return (first, second) switch
        {
            ("--help" or "-h" or "help", _) => 1,
            ("--version" or "-v" or "version", _) => 1,
            ("status" or "apply" or "verify", _) => 1,
            ("perf", "get") => 2,
            ("lang", "list" or "uninstall") => 2,
            ("trainer", "list-cheats" or "list-tweaks" or "apply") => 2,
            _ => 0,
        };
    }

    /// <summary>
    /// 多餘 token 一律以 InvalidArgs 拒絕。靜默忽略會讓 AI 代理程式拿到看似成功、
    /// 實際卻跑在別的目標上的結果（ISSUE-065）。回傳 true 代表已輸出錯誤。
    /// </summary>
    private static bool RejectExtraArgs(string command, IEnumerable<string> extras,
                                        bool isJson, TextWriter stdout, TextWriter stderr,
                                        out int exitCode)
    {
        string? unknown = extras.FirstOrDefault();
        if (unknown is null)
        {
            exitCode = ExitCodes.Success;
            return false;
        }

        exitCode = OutputError(command, Strings.Get("Error_UnknownOption", unknown),
            ExitCodes.InvalidArgs, isJson, stdout, stderr);
        return true;
    }

    private static int HandleUnknown(string command, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string errMsg = Strings.Get("Error_InvalidArgs", $"未知的指令或參數 '{command}'");
        return OutputError(command, errMsg, ExitCodes.InvalidArgs, isJson, stdout, stderr);
    }

    private static bool TryParseOnOff(string s, out bool value)
    {
        if (s.Equals("on", StringComparison.OrdinalIgnoreCase) || s == "1" || s.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            value = true;
            return true;
        }
        if (s.Equals("off", StringComparison.OrdinalIgnoreCase) || s == "0" || s.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            value = false;
            return true;
        }
        value = false;
        return false;
    }

    private static int OutputError(string command, string message, int exitCode, bool isJson, TextWriter stdout, TextWriter stderr, List<string>? warnings = null)
    {
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = false,
                Command = command,
                Warnings = warnings ?? [],
                Errors = [message],
                Error = message
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stderr.WriteLine(message);
            if (warnings is { Count: > 0 })
            {
                stderr.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings) stderr.WriteLine($"  ! {w}");
            }
        }
        return exitCode;
    }

    public static int ReportUnhandled(string[] args, Exception exception) =>
        OutputUnhandled(args, exception, Console.Out, Console.Error);

    private static int OutputUnhandled(string[] args, Exception exception, TextWriter stdout, TextWriter stderr)
    {
        bool isJson = args.Any(a => a.Equals("--json", StringComparison.OrdinalIgnoreCase));
        string command = args.FirstOrDefault(a => !a.StartsWith("--", StringComparison.Ordinal)) ?? "cli";
        string message = exception.Message;

        if (isJson)
        {
            stdout.WriteLine(JsonSerializer.Serialize(new JsonEnvelope
            {
                Ok = false,
                Command = command,
                Error = message,
                Errors = [message]
            }, JsonEnvelopeOptions));
        }
        else
        {
            stderr.WriteLine(message);
        }

        return ExitCodes.GeneralFailure;
    }

    private static int RejectCorruptConfig(
        string command,
        ToolkitConfig config,
        bool isJson,
        TextWriter stdout,
        TextWriter stderr)
    {
        return OutputError(
            command,
            config.LoadError ?? Strings.Get("Error_InvalidConfig"),
            ExitCodes.InvalidArgs,
            isJson,
            stdout,
            stderr);
    }
}
