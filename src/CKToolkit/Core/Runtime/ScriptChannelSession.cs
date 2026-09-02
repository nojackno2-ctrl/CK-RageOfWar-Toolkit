using CKToolkit.Core.Common;

namespace CKToolkit.Core.Runtime;

/// <summary>
/// 記住「這一次注入用的是哪個行程、哪組權杖」。
///
/// 通道的權杖必須由注入端產生、由客戶端使用，而這兩件事發生在不同的畫面上——
/// 遊戲是從主視窗的「啟動遊戲」注入的，面板卻是修改器頁開的。中間需要一個
/// 行程內的交接點，就是這裡。
///
/// 刻意只活在記憶體：權杖不寫設定檔、不寫記錄檔、不進環境變數以外的任何地方
/// （環境變數是注入本來就要用的通道），工具一關就沒了。
/// </summary>
public static class ScriptChannelSession
{
    private static readonly object Gate = new();
    private static string _token = string.Empty;
    private static uint _pid;

    /// <summary>目前這一場的遊戲行程；沒有就是 0。</summary>
    public static uint ProcessId
    {
        get { lock (Gate) return _pid; }
    }

    /// <summary>目前這一場的權杖；沒有就是空字串。</summary>
    public static string Token
    {
        get { lock (Gate) return _token; }
    }

    /// <summary>
    /// 開一場新的：產生新權杖並清掉舊行程編號。注入之前呼叫，回傳的權杖要放進
    /// <see cref="DiagnosticsOptions.ScriptToken"/>。
    /// </summary>
    public static string Begin()
    {
        lock (Gate)
        {
            _token = ScriptChannel.NewToken();
            _pid = 0;
            return _token;
        }
    }

    /// <summary>注入成功後把行程編號補上。</summary>
    public static void Attached(uint pid)
    {
        lock (Gate) _pid = pid;
    }

    /// <summary>遊戲結束或使用者關掉面板時清乾淨。</summary>
    public static void End()
    {
        lock (Gate)
        {
            _token = string.Empty;
            _pid = 0;
        }
    }

    /// <summary>
    /// 針對指定行程取得一個可用的客戶端。行程編號對不上目前這一場（例如使用者自己
    /// 從 Steam 又開了一個），就回傳失敗——絕不拿舊權杖去試新行程。
    /// </summary>
    public static Result<ScriptChannel> TryOpen(uint pid)
    {
        string token;
        uint known;
        lock (Gate)
        {
            token = _token;
            known = _pid;
        }

        if (known == 0 || known != pid || !ScriptChannel.IsValidToken(token))
        {
            return Result<ScriptChannel>.Fail(I18n.Strings.Get("Error_ScriptChannelNoSession"));
        }

        return ScriptChannel.Create(pid, token);
    }
}
