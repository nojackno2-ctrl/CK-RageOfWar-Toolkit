using System.Text;

namespace CKToolkit.Core.Common;

/// <summary>
/// HMMSYS PackFile（《Celtic Kings》專用之 .pak / .bfhp 封裝檔）讀寫器。
///
/// 格式規格：
///   0x00  magic "HMMSYS PackFile\n\x1a"，補零到 0x20
///   0x20  u32 fileCount
///   0x24  u32 dirSize            目錄位元組數，自 0x28 起算
///   0x28  目錄區，每筆條目：
///           u8  nameLen          完整檔名長度
///           u8  prefixLen        與前一筆檔名共用的前綴長度
///           u8  suffix[nameLen - prefixLen]
///           u32 offset           payload 絕對檔案位移
///           u32 size             payload 位元組長度
///         u32 mtime[fileCount]   每檔一筆 DOS 日期時間戳
///         payload 資料區，依目錄順序連續排列，無對齊、無間隙
///
/// 起始實作來自修改器之 HmmPak.cs（已對遊戲全部 6 個 HMMSYS pak 含 136 MB assets.pak
/// 通過逐位元組往返驗證）。
/// </summary>
public sealed class HmmPak
{
    /// <summary>pak 內的檔名一律以 latin-1 存放。</summary>
    public static readonly Encoding PakEncoding = Encoding.Latin1;

    private static ReadOnlySpan<byte> Magic => "HMMSYS PackFile\n\x1a"u8;

    public const int HeaderSize = 0x28;

    /// <summary>原版 data.pak 多數項目的時間戳：2004-01-23 12:46:32。</summary>
    public const uint DefaultMTime = 0x303765D0;

    private readonly List<PakEntry> _entries;
    private readonly Dictionary<string, byte[]> _blobs;

    private HmmPak(List<PakEntry> entries, Dictionary<string, byte[]> blobs)
    {
        _entries = entries;
        _blobs = blobs;
    }

    public static HmmPak CreateEmpty() =>
        new([], new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase));

    public IReadOnlyList<PakEntry> Entries => _entries;

    public int Count => _entries.Count;

    public static string Normalize(string name) =>
        name.Replace('/', '\\').ToUpperInvariant();

    // ---- 讀取與還原 --------------------------------------------------------

    public static HmmPak FromBytes(byte[] data)
    {
        if (data.Length < HeaderSize || !data.AsSpan(0, Magic.Length).SequenceEqual(Magic))
            throw new PakException("不是 HMMSYS PackFile：magic 不符");

        int count = (int)BitConverter.ToUInt32(data, 0x20);
        int dirSize = (int)BitConverter.ToUInt32(data, 0x24);
        int dirEnd = HeaderSize + dirSize;
        if (count < 0 || dirSize < 0 || dirEnd > data.Length)
            throw new PakException("目錄長度超出檔案大小");

        var entries = new List<PakEntry>(count);
        var blobs = new Dictionary<string, byte[]>(count, StringComparer.OrdinalIgnoreCase);

        int pos = HeaderSize;
        string prev = string.Empty;
        for (int i = 0; i < count; i++)
        {
            if (pos + 2 > dirEnd) throw new PakException("目錄提前結束");
            int nameLen = data[pos];
            int prefixLen = data[pos + 1];
            if (prefixLen > nameLen || prefixLen > prev.Length)
                throw new PakException($"目錄損毀：第 {i} 筆的前綴長度不合理");

            int suffixLen = nameLen - prefixLen;
            int suffixEnd = pos + 2 + suffixLen;
            if (suffixEnd + 8 > dirEnd) throw new PakException("目錄提前結束");

            string name = string.Concat(
                prev.AsSpan(0, prefixLen),
                PakEncoding.GetString(data, pos + 2, suffixLen));

            uint offset = BitConverter.ToUInt32(data, suffixEnd);
            uint size = BitConverter.ToUInt32(data, suffixEnd + 4);
            if (offset + (long)size > data.Length)
                throw new PakException($"項目 {name} 的資料超出檔案範圍");

            entries.Add(new PakEntry(name, size));
            blobs[name] = data.AsSpan((int)offset, (int)size).ToArray();

            prev = name;
            pos = suffixEnd + 8;
        }

        if (pos != dirEnd)
            throw new PakException($"目錄尾端不對齊：0x{pos:X} != 0x{dirEnd:X}");

        for (int i = 0; i < count; i++)
            entries[i].MTime = BitConverter.ToUInt32(data, dirEnd + i * 4);

        return new HmmPak(entries, blobs);
    }

    public static HmmPak Load(string path) => FromBytes(File.ReadAllBytes(path));

    public bool Contains(string name) => _blobs.ContainsKey(Normalize(name));

    public int IndexOf(string name)
    {
        string key = Normalize(name);
        for (int i = 0; i < _entries.Count; i++)
        {
            if (string.Equals(_entries[i].Name, key, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public byte[] Read(string name)
    {
        string key = Normalize(name);
        return _blobs.TryGetValue(key, out var blob)
            ? blob
            : throw new PakException($"pak 內沒有檔案：{name}");
    }

    public string ReadText(string name, Encoding? encoding = null) =>
        (encoding ?? PakEncoding).GetString(Read(name));

    public IEnumerable<string> Names() => _entries.Select(e => e.Name);

    // ---- 寫入與編輯 --------------------------------------------------------

    /// <summary>覆寫既有檔案；不存在則新增一筆（排在目錄最後）。</summary>
    public void Write(string name, byte[] blob, uint? mtime = null)
    {
        string key = Normalize(name);
        if (_blobs.ContainsKey(key))
        {
            _blobs[key] = blob;
            foreach (var e in _entries)
            {
                if (string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase))
                {
                    e.Size = (uint)blob.Length;
                    if (mtime.HasValue) e.MTime = mtime.Value;
                    break;
                }
            }
        }
        else
        {
            var entry = new PakEntry(key, (uint)blob.Length);
            if (mtime.HasValue) entry.MTime = mtime.Value;
            _entries.Add(entry);
            _blobs[key] = blob;
        }
    }

    public void WriteText(string name, string text, Encoding? encoding = null, uint? mtime = null) =>
        Write(name, (encoding ?? PakEncoding).GetBytes(text), mtime);

    public bool Remove(string name)
    {
        string key = Normalize(name);
        if (!_blobs.Remove(key)) return false;
        _entries.RemoveAll(e => string.Equals(e.Name, key, StringComparison.OrdinalIgnoreCase));
        return true;
    }

    // ---- 序列化 ------------------------------------------------------------

    public byte[] ToBytes()
    {
        // ★ 目錄必須依名稱排序後才序列化。
        //
        // HMMSYS 的目錄是排序的（原版 local.pak 924 個項目、data.pak 876 個項目都完全有序），
        // 而且採前綴壓縮——每個項目只存「與前一個名稱共用幾個字元」。引擎顯然依賴這個
        // 順序查表（極可能是二分搜尋）。
        //
        // 這條規則違反時的症狀非常有欺騙性：檔案內容完全正確、雜湊也對，但遊戲找不到它們。
        // 實際發生過：語言包安裝後 CHINESE\ 底下 297 個項目全部存在且內容正確，
        // vxSettings 也指向 chinese，但遊戲仍顯示英文——因為新項目被 append 在目錄尾端，
        // 排在 SCENARIOS\... 之後，查表全部落空。前身 Python 實作在 ckpatch.py:308
        // 用 sorted(files.items()) 排序後才建檔，所以它沒有這個問題。
        //
        // 排序規則：名稱已由 Normalize 統一為大寫加反斜線，用序數比較即與 Python 的
        // 預設字串排序一致。
        _entries.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        int dirSize = 0;
        string prev = string.Empty;
        foreach (var e in _entries)
        {
            dirSize += 2 + (e.Name.Length - CommonPrefix(prev, e.Name)) + 8;
            prev = e.Name;
        }

        int dataStart = HeaderSize + dirSize + 4 * _entries.Count;
        long total = dataStart;
        foreach (var e in _entries) total += _blobs[e.Name].Length;

        var output = new byte[total];
        var span = output.AsSpan();

        Magic.CopyTo(span);
        BitConverter.TryWriteBytes(span[0x20..], (uint)_entries.Count);
        BitConverter.TryWriteBytes(span[0x24..], (uint)dirSize);

        int pos = HeaderSize;
        uint offset = (uint)dataStart;
        prev = string.Empty;
        foreach (var e in _entries)
        {
            int prefixLen = CommonPrefix(prev, e.Name);
            var blob = _blobs[e.Name];

            output[pos++] = (byte)e.Name.Length;
            output[pos++] = (byte)prefixLen;
            pos += PakEncoding.GetBytes(e.Name, prefixLen, e.Name.Length - prefixLen, output, pos);

            BitConverter.TryWriteBytes(span[pos..], offset);
            BitConverter.TryWriteBytes(span[(pos + 4)..], (uint)blob.Length);
            pos += 8;

            offset += (uint)blob.Length;
            prev = e.Name;
        }

        foreach (var e in _entries)
        {
            BitConverter.TryWriteBytes(span[pos..], e.MTime);
            pos += 4;
        }

        if (pos != dataStart)
            throw new PakException($"目錄寫入長度不符：0x{pos:X} != 0x{dataStart:X}");

        foreach (var e in _entries)
        {
            var blob = _blobs[e.Name];
            blob.CopyTo(span[pos..]);
            pos += blob.Length;
        }

        return output;
    }

    public void Save(string path) => File.WriteAllBytes(path, ToBytes());

    private static int CommonPrefix(string a, string b)
    {
        int max = Math.Min(Math.Min(a.Length, b.Length), 255);
        int i = 0;
        while (i < max && a[i] == b[i]) i++;
        return i;
    }
}

public sealed class PakEntry(string name, uint size)
{
    public string Name { get; } = name;
    public uint Size { get; internal set; } = size;
    public uint MTime { get; internal set; } = HmmPak.DefaultMTime;

    public override string ToString() => $"{Name} ({Size} bytes)";
}

public sealed class PakException(string message) : Exception(message);
