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

        return Execute(args, stdout, stderr);
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
        bool isJson = false;
        string? gameDirOverride = null;
        string? configPathOverride = null;
        var commands = new List<string>();

        // 解析全域旗標與指令
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg == "--json")
            {
                isJson = true;
            }
            else if (arg == "--game" && i + 1 < args.Length)
            {
                gameDirOverride = args[++i];
            }
            else if (arg == "--config" && i + 1 < args.Length)
            {
                configPathOverride = args[++i];
            }
            else
            {
                commands.Add(arg);
            }
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

            default:
                return HandleUnknown(primaryCmd, isJson, stdout, stderr);
        }
    }

    private static int HandleHelp(bool isJson, TextWriter stdout)
    {
        string helpText = Strings.Get("Cli_HelpText");
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "help",
                Data = new { help = helpText }
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(helpText);
        }
        return ExitCodes.Success;
    }

    private static int HandleVersion(bool isJson, TextWriter stdout)
    {
        string versionStr = "1.0.0";
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "version",
                Data = new { version = versionStr, toolkit = "CKToolkit" }
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Cli_Version", versionStr));
        }
        return ExitCodes.Success;
    }

    /// <summary>
    /// 處理 status 狀態查詢。此路徑為嚴格唯讀，絕不建立備份目錄、絕不抓取檔案、絕不寫入設定。
    /// </summary>
    private static int HandleStatus(string? gameOverride, string? configOverride, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        var config = ToolkitConfig.Load(configOverride);
        string? gameDir = GamePaths.FindGameDir(gameOverride, config.GameDir);

        if (gameDir is null || !GamePaths.IsGameDir(gameDir))
        {
            string err = Strings.Get("Error_GameNotFound");
            if (isJson)
            {
                var envelope = new JsonEnvelope
                {
                    Ok = false,
                    Command = "status",
                    Errors = [err]
                };
                stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
            }
            else
            {
                stderr.WriteLine(err);
            }
            return ExitCodes.GameNotFound;
        }

        // 純唯讀查詢：使用 BackupManager 唯讀 API，絕不呼叫 ReadPristine 或 EnsureBackup
        var backupMgr = new BackupManager();
        var filesStatus = new Dictionary<string, object>();
        var warnings = new List<string>(config.MigrationsApplied);

        bool anyCoverageIncomplete = false;

        foreach (GameFile f in Enum.GetValues<GameFile>())
        {
            string fileName = BackupManager.GetFileName(f);
            bool hasBackup = backupMgr.HasBackup(f);
            var state = backupMgr.GetFilePristineState(gameDir, f);

            if (!backupMgr.IsCoverageComplete(f))
            {
                anyCoverageIncomplete = true;
            }

            string stateString = state switch
            {
                PristineState.Pristine => "pristine",
                PristineState.Patched => "patched",
                _ => "unknown"
            };

            string statusDisplay = state switch
            {
                PristineState.Pristine => Strings.Get("Status_Pristine"),
                PristineState.Patched => Strings.Get("Status_Patched"),
                _ => Strings.Get("Status_Unknown")
            };

            bool? isPristine = state switch
            {
                PristineState.Pristine => true,
                PristineState.Patched => false,
                _ => null
            };

            filesStatus[fileName] = new
            {
                hasBackup,
                pristineState = stateString,
                isPristine,
                status = statusDisplay
            };
        }

        if (anyCoverageIncomplete)
        {
            warnings.Add(Strings.Get("Warning_DetectionIncomplete"));
        }

        var data = new
        {
            gameDir,
            gameRunning = GamePaths.IsGameRunning(),
            configVersion = config.Version,
            uiLanguage = config.UiLanguage,
            files = filesStatus
        };

        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = true,
                Command = "status",
                Data = data,
                Warnings = warnings
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stdout.WriteLine(Strings.Get("Status_GameDir", gameDir));
            foreach (var (fn, info) in filesStatus)
            {
                stdout.WriteLine($"  - {fn}: {JsonSerializer.Serialize(info, JsonEnvelopeOptions)}");
            }
            if (warnings.Count > 0)
            {
                stdout.WriteLine("\n警告 / Warnings:");
                foreach (string w in warnings)
                {
                    stdout.WriteLine($"  ! {w}");
                }
            }
        }

        return ExitCodes.Success;
    }

    private static int HandleUnknown(string command, bool isJson, TextWriter stdout, TextWriter stderr)
    {
        string errMsg = Strings.Get("Error_InvalidArgs", $"未知的指令或參數 '{command}'");
        if (isJson)
        {
            var envelope = new JsonEnvelope
            {
                Ok = false,
                Command = command,
                Errors = [errMsg]
            };
            stdout.WriteLine(JsonSerializer.Serialize(envelope, JsonEnvelopeOptions));
        }
        else
        {
            stderr.WriteLine(errMsg);
        }
        return ExitCodes.InvalidArgs;
    }
}
