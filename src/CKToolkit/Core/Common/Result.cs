namespace CKToolkit.Core.Common;

/// <summary>
/// CLI 規範定義的標準結束代碼 (SPEC.md §10)。
/// </summary>
public static class ExitCodes
{
    public const int Success = 0;
    public const int GeneralFailure = 1;
    public const int InvalidArgs = 2;
    public const int GameNotFound = 3;
    public const int BackupMissingNeedsSteamVerify = 4;
    public const int FileLocked = 5;
}

/// <summary>
/// 跨 GUI 與 CLI 共用的操作結果型別。
/// 預期內的失敗路徑（如遊戲目錄未找到、缺少備份、檔案佔用）均以 Result 回傳，不拋出例外。
/// </summary>
public class Result
{
    public bool Success { get; }
    public bool IsOk => Success;
    public bool IsError => !Success;
    public int ExitCode { get; }
    public string? ErrorMessage { get; }
    public IReadOnlyList<string> Warnings { get; }

    protected Result(bool success, int exitCode, string? errorMessage, IReadOnlyList<string>? warnings)
    {
        Success = success;
        ExitCode = exitCode;
        ErrorMessage = errorMessage;
        Warnings = warnings ?? Array.Empty<string>();
    }

    public static Result Ok(IReadOnlyList<string>? warnings = null) =>
        new(true, ExitCodes.Success, null, warnings);

    public static Result Fail(string message, int exitCode = ExitCodes.GeneralFailure, IReadOnlyList<string>? warnings = null) =>
        new(false, exitCode, message, warnings);

    public static Result<T> Ok<T>(T value, IReadOnlyList<string>? warnings = null) =>
        Result<T>.Ok(value, warnings);

    public static Result<T> Fail<T>(string message, int exitCode = ExitCodes.GeneralFailure, IReadOnlyList<string>? warnings = null) =>
        Result<T>.Fail(message, exitCode, warnings);
}

/// <summary>
/// 攜帶傳回值之操作結果型別。
/// </summary>
public sealed class Result<T> : Result
{
    public T? Value { get; }

    private Result(bool success, int exitCode, string? errorMessage, T? value, IReadOnlyList<string>? warnings)
        : base(success, exitCode, errorMessage, warnings)
    {
        Value = value;
    }

    public static Result<T> Ok(T value, IReadOnlyList<string>? warnings = null) =>
        new(true, ExitCodes.Success, null, value, warnings);

    public static new Result<T> Fail(string message, int exitCode = ExitCodes.GeneralFailure, IReadOnlyList<string>? warnings = null) =>
        new(false, exitCode, message, default, warnings);
}
