using System.Buffers.Binary;
using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using CKToolkit.Core.Common;
using CKToolkit.I18n;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 引擎回報的腳本執行結果。數值是跨行程契約，必須與 <c>src/CKPerf/ckperf.h</c> 的
/// <c>ScriptStatus</c> 完全一致，不得重新編號。
/// </summary>
public enum ScriptStatus
{
    /// <summary>編譯成功並同步執行完畢。</summary>
    Ok = 0,

    /// <summary>latent 腳本，已交給引擎的腳本 VM 排程器。</summary>
    Scheduled = 1,

    /// <summary>引擎的腳本編譯器拒絕了這段原始碼。</summary>
    CompileError = 2,

    /// <summary>沒有進行中的對局，什麼都沒執行。</summary>
    NotInGame = 3,

    /// <summary>通道未啟用（簽章不符、自測失敗，或根本沒要求）。</summary>
    ChannelDisabled = 4,

    /// <summary>上一段腳本還沒跑完。</summary>
    Busy = 5,

    /// <summary>引擎沒有繪製新的一幀，抽取點沒被呼叫。</summary>
    TimedOut = 6,

    /// <summary>請求格式錯誤或驗證失敗。</summary>
    Rejected = 7,

    /// <summary>引擎在執行途中發生例外，已被攔截。</summary>
    Faulted = 8,
}

/// <summary>引擎回報的一次執行結果。<paramref name="Detail"/> 是原生端的英文短句，僅供記錄。</summary>
public readonly record struct ScriptRunOutcome(ScriptStatus Status, string Detail)
{
    /// <summary>腳本確實被引擎接受並執行（同步或排程）。</summary>
    public bool Ran => Status is ScriptStatus.Ok or ScriptStatus.Scheduled;
}

/// <summary>
/// 執行期腳本通道的受管端（ISSUE-068）。
///
/// <para><b>為什麼需要它</b></para>
///
/// 引擎只認 20 個硬編的 scdebug 按鍵代號，其中 9 個被遊戲本身用掉、5 個被原版 scdebug
/// 綁走，只剩 4 個自由鍵；小鍵盤模式雖然把 F1–F12 解放成 13 個，但那 13 個對映到的是
/// 筆電沒有的實體小鍵盤。18 個作弊塞不進去，塞不進去的就被靜默停用，面板連按鈕都不會
/// 出現——這正是使用者實測看到的「根本沒鍵可按」。
///
/// 按鍵其實只是「把一段字串交給引擎腳本編譯器」的其中一種方式。<c>ckperf.dll</c> 在遊戲
/// 行程內重現了 scdebug 派送的尾段（見 <c>src/CKPerf/script.cpp</c>），於是按鍵整個從
/// 這條路徑上消失了。這個類別就是對那條通道的客戶端。
///
/// <para><b>紀律</b></para>
///
/// 比照 <see cref="GameMemory"/>：預期內的失敗一律回傳 <see cref="Result{T}"/>，不丟例外；
/// 錯誤訊息全部走 <see cref="Strings"/>。連不上、逾時、驗證失敗都是正常結果，呼叫端
/// 應該退回送鍵模式或直接顯示原因，不應該讓使用者看到堆疊。
/// </summary>
public sealed class ScriptChannel : IDisposable
{
    // 與 src/CKPerf/script.cpp 的 kMagic / kVersion / kScriptTokenChars / kMaxScriptBytes 對應。
    private const uint Magic = 0x43534B43;   // 'CKSC'
    private const uint Version = 1;
    internal const int TokenChars = 32;
    internal const int MaxScriptBytes = 16 * 1024;

    private const int RequestHeaderBytes = 4 + 4 + TokenChars + 4 + 4;   // 48
    private const int ResponseHeaderBytes = 4 + 4 + 4 + 4;               // 16

    /// <summary>原生端最多等 5 秒；客戶端多留一點餘裕才不會把「引擎慢」誤判成「管線壞」。</summary>
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(8);

    private readonly uint _pid;
    private readonly string _token;

    private ScriptChannel(uint pid, string token)
    {
        _pid = pid;
        _token = token;
    }

    /// <summary>這個行程的通道管線名稱（不含 <c>\\.\pipe\</c> 前綴）。</summary>
    public static string PipeNameFor(uint pid) => $"ckperf-script-{pid}";

    /// <summary>
    /// 產生一組新的通道權杖。每次注入都要換一組——它是「這個請求真的來自注入這個 DLL
    /// 的那一份 CKToolkit」的唯一憑據。長度固定 32 個十六進位字元，與原生端的
    /// <c>kScriptTokenChars</c> 一致。
    /// </summary>
    public static string NewToken() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(TokenChars / 2)).ToLowerInvariant();

    /// <summary>權杖是否符合原生端接受的形狀（32 個可列印 ASCII 字元）。</summary>
    public static bool IsValidToken(string? token) =>
        token is not null && token.Length == TokenChars && token.All(c => c is > (char)0x20 and < (char)0x7F);

    /// <summary>
    /// 建立客戶端。這裡不連線——通道是逐次請求連線的（原生端只開一個管線實體），
    /// 所以持有一個長連線只會擋住別人。<see cref="Probe"/> 才是確認對面在不在的方法。
    /// </summary>
    public static Result<ScriptChannel> Create(uint pid, string token)
    {
        if (pid == 0)
        {
            return Result<ScriptChannel>.Fail(Strings.Get("Error_ScriptChannelNoProcess"));
        }
        if (!IsValidToken(token))
        {
            return Result<ScriptChannel>.Fail(Strings.Get("Error_ScriptChannelBadToken"));
        }
        return Result<ScriptChannel>.Ok(new ScriptChannel(pid, token));
    }

    /// <summary>
    /// 送一段 VS 腳本進遊戲執行。腳本必須是單行、已經跳脫完畢的成品——這個方法不做任何
    /// 語法處理，原文會原封不動交給引擎自己的編譯器。
    /// </summary>
    public Result<ScriptRunOutcome> Run(string script, TimeSpan? timeout = null)
    {
        if (string.IsNullOrWhiteSpace(script))
        {
            return Result<ScriptRunOutcome>.Fail(Strings.Get("Error_ScriptChannelEmptyScript"));
        }

        // 與 SCDEBUG.XML 走同一種編碼，兩條路徑的位元組才會一致（見 TrainerInstaller
        // 對 ScDebugEncoding 的說明）。
        byte[] payload = new UTF8Encoding(false, false).GetBytes(script);
        if (payload.Length > MaxScriptBytes)
        {
            return Result<ScriptRunOutcome>.Fail(
                Strings.Get("Error_ScriptChannelTooLong", payload.Length, MaxScriptBytes));
        }

        TimeSpan wait = timeout ?? DefaultTimeout;

        try
        {
            using var pipe = new NamedPipeClientStream(
                ".", PipeNameFor(_pid), PipeDirection.InOut, PipeOptions.None);
            pipe.Connect((int)wait.TotalMilliseconds);

            pipe.Write(BuildRequest(payload));
            pipe.Flush();

            return Result<ScriptRunOutcome>.Ok(ReadResponse(pipe));
        }
        catch (TimeoutException)
        {
            return Result<ScriptRunOutcome>.Fail(Strings.Get("Error_ScriptChannelUnreachable"));
        }
        catch (IOException)
        {
            // 遊戲在請求途中結束是完全正常的事，不是錯誤現場。
            return Result<ScriptRunOutcome>.Fail(Strings.Get("Error_ScriptChannelUnreachable"));
        }
        catch (UnauthorizedAccessException)
        {
            return Result<ScriptRunOutcome>.Fail(Strings.Get("Error_ScriptChannelUnreachable"));
        }
        catch (ObjectDisposedException)
        {
            return Result<ScriptRunOutcome>.Fail(Strings.Get("Error_ScriptChannelUnreachable"));
        }
    }

    /// <summary>
    /// 通道現在通不通。用一段無副作用的探針腳本測試整條鏈路——連線、驗證、抽取、編譯——
    /// 但因為引擎沒有對局時原生端會回 <see cref="ScriptStatus.NotInGame"/>，這裡把
    /// 「連得上但不在對局中」也視為通道可用。
    /// </summary>
    public bool Probe()
    {
        var result = Run("int i; i = 1;", TimeSpan.FromSeconds(2));
        return result.IsOk;
    }

    private byte[] BuildRequest(byte[] payload)
    {
        byte[] buffer = new byte[RequestHeaderBytes + payload.Length];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(4, 4), Version);
        // 權杖固定 32 個 ASCII 位元組，沒有結尾 NUL——原生端也是逐字元比對 32 個。
        Encoding.ASCII.GetBytes(_token, span.Slice(8, TokenChars));
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(8 + TokenChars, 4), 0);   // flags, 保留
        BinaryPrimitives.WriteUInt32LittleEndian(span.Slice(12 + TokenChars, 4), (uint)payload.Length);
        payload.CopyTo(span[RequestHeaderBytes..]);

        return buffer;
    }

    private static ScriptRunOutcome ReadResponse(Stream pipe)
    {
        byte[] header = new byte[ResponseHeaderBytes];
        pipe.ReadExactly(header);

        uint magic = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(0, 4));
        uint version = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4, 4));
        if (magic != Magic || version != Version)
        {
            // 對不上就拒絕解讀，不猜測（AGENTS.md §2）。
            return new ScriptRunOutcome(ScriptStatus.Rejected, "protocol mismatch");
        }

        uint status = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8, 4));
        uint messageLength = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(12, 4));

        string detail = string.Empty;
        if (messageLength is > 0 and <= 4096)
        {
            byte[] body = new byte[messageLength];
            pipe.ReadExactly(body);
            detail = Encoding.UTF8.GetString(body);
        }

        ScriptStatus parsed = Enum.IsDefined(typeof(ScriptStatus), (int)status)
            ? (ScriptStatus)status
            : ScriptStatus.Rejected;

        return new ScriptRunOutcome(parsed, detail);
    }

    /// <summary>把引擎回報的狀態翻成目前 UI 語系的一句話。</summary>
    public static string Describe(ScriptRunOutcome outcome) => outcome.Status switch
    {
        ScriptStatus.Ok => Strings.Get("Trainer_ScriptStatus_Ok"),
        ScriptStatus.Scheduled => Strings.Get("Trainer_ScriptStatus_Scheduled"),
        ScriptStatus.CompileError => Strings.Get("Trainer_ScriptStatus_CompileError"),
        ScriptStatus.NotInGame => Strings.Get("Trainer_ScriptStatus_NotInGame"),
        ScriptStatus.ChannelDisabled => Strings.Get("Trainer_ScriptStatus_ChannelDisabled"),
        ScriptStatus.Busy => Strings.Get("Trainer_ScriptStatus_Busy"),
        ScriptStatus.TimedOut => Strings.Get("Trainer_ScriptStatus_TimedOut"),
        ScriptStatus.Faulted => Strings.Get("Trainer_ScriptStatus_Faulted"),
        _ => Strings.Get("Trainer_ScriptStatus_Rejected"),
    };

    public void Dispose()
    {
        // 沒有長連線可關；保留 IDisposable 是為了讓呼叫端的 using 寫法在日後
        // 通道改成持久連線時不必回頭改。
    }
}
