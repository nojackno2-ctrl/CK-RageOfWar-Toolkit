using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace CKToolkit.Core.Common;

/// <summary>
/// 保留原始註解、鍵值順序、空格與換行格式（CRLF / LF）的 INI 讀寫器。
/// 支援全域（頂層無節區）與指定節區（如 [Language]、[Resolutions]）之鍵值讀寫與清單附加。
/// </summary>
public sealed class IniFile
{
    private enum LineType
    {
        CommentOrEmpty,
        SectionHeader,
        KeyValue
    }

    private sealed class IniLine
    {
        public LineType Type { get; set; }
        public string RawText { get; set; } = string.Empty;
        public string Section { get; set; } = string.Empty; // 節區名稱，頂層為 ""
        public string? Key { get; set; }
        public string? Value { get; set; }
        public string LineEnding { get; set; } = "\r\n";
        public string KeyPrefix { get; set; } = string.Empty;
        public string EqualsSeparator { get; set; } = " = ";
    }

    private readonly List<IniLine> _lines = new();
    private string _defaultLineEnding = "\r\n";

    public static IniFile FromText(string text)
    {
        var ini = new IniFile();
        ini.Parse(text);
        return ini;
    }

    public static IniFile Load(string path, Encoding? encoding = null)
    {
        byte[] bytes = File.ReadAllBytes(path);
        string text = (encoding ?? Encoding.UTF8).GetString(bytes);
        return FromText(text);
    }

    public string ToText()
    {
        var sb = new StringBuilder();
        foreach (var line in _lines)
        {
            sb.Append(line.RawText);
        }
        return sb.ToString();
    }

    public void Save(string path, Encoding? encoding = null)
    {
        byte[] bytes = (encoding ?? Encoding.UTF8).GetBytes(ToText());
        File.WriteAllBytes(path, bytes);
    }

    private void Parse(string text)
    {
        _lines.Clear();
        if (string.IsNullOrEmpty(text)) return;

        // 偵測預設換行符
        _defaultLineEnding = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

        string currentSection = string.Empty;
        var span = text.AsSpan();
        int pos = 0;
        while (pos < span.Length)
        {
            int nextLf = span[pos..].IndexOf('\n');
            int lineLenWithEnding;
            string lineEnding;

            if (nextLf == -1)
            {
                lineLenWithEnding = span.Length - pos;
                lineEnding = string.Empty;
            }
            else
            {
                lineLenWithEnding = nextLf + 1;
                var lineWithEnding = span.Slice(pos, lineLenWithEnding);
                lineEnding = lineWithEnding.EndsWith("\r\n") ? "\r\n" : "\n";
            }

            var fullLineSpan = span.Slice(pos, lineLenWithEnding);
            string fullLine = fullLineSpan.ToString();
            pos += lineLenWithEnding;

            var contentWithoutEnding = fullLineSpan.TrimEnd("\r\n");
            var trimmed = contentWithoutEnding.Trim();

            if (trimmed.Length >= 2 && trimmed[0] == '[' && trimmed[^1] == ']')
            {
                string secName = trimmed[1..^1].Trim().ToString();
                currentSection = secName;
                _lines.Add(new IniLine
                {
                    Type = LineType.SectionHeader,
                    RawText = fullLine,
                    Section = currentSection,
                    LineEnding = lineEnding.Length > 0 ? lineEnding : _defaultLineEnding
                });
            }
            else if (trimmed.Length == 0 || trimmed[0] == ';' || trimmed[0] == '#')
            {
                _lines.Add(new IniLine
                {
                    Type = LineType.CommentOrEmpty,
                    RawText = fullLine,
                    Section = currentSection,
                    LineEnding = lineEnding.Length > 0 ? lineEnding : _defaultLineEnding
                });
            }
            else
            {
                int eqIndex = contentWithoutEnding.IndexOf('=');
                if (eqIndex >= 0)
                {
                    var rawKeySpan = contentWithoutEnding[..eqIndex];
                    var rawValueSpan = contentWithoutEnding[(eqIndex + 1)..];

                    string key = rawKeySpan.Trim().ToString();
                    string value = rawValueSpan.Trim().ToString();

                    // 保留縮排與等號周圍格式
                    int keyLeadingLen = rawKeySpan.Length - rawKeySpan.TrimStart().Length;
                    string keyPrefix = rawKeySpan[..keyLeadingLen].ToString();

                    int keyTrailingLen = rawKeySpan.Length - rawKeySpan.TrimEnd().Length;
                    int valLeadingLen = rawValueSpan.Length - rawValueSpan.TrimStart().Length;
                    string equalsSeparator = new string(' ', keyTrailingLen) + "=" + new string(' ', valLeadingLen);

                    _lines.Add(new IniLine
                    {
                        Type = LineType.KeyValue,
                        RawText = fullLine,
                        Section = currentSection,
                        Key = key,
                        Value = value,
                        LineEnding = lineEnding.Length > 0 ? lineEnding : _defaultLineEnding,
                        KeyPrefix = keyPrefix,
                        EqualsSeparator = equalsSeparator.Length > 0 ? equalsSeparator : "="
                    });
                }
                else
                {
                    _lines.Add(new IniLine
                    {
                        Type = LineType.CommentOrEmpty,
                        RawText = fullLine,
                        Section = currentSection,
                        LineEnding = lineEnding.Length > 0 ? lineEnding : _defaultLineEnding
                    });
                }
            }
        }
    }

    public bool HasSection(string section)
    {
        string sec = section.Trim();
        if (sec.Length == 0) return true; // 頂層始終存在
        return _lines.Any(l => l.Type == LineType.SectionHeader &&
                               string.Equals(l.Section, sec, StringComparison.OrdinalIgnoreCase));
    }

    public bool HasKey(string? section, string key)
    {
        string sec = (section ?? string.Empty).Trim();
        string k = key.Trim();
        return _lines.Any(l => l.Type == LineType.KeyValue &&
                               string.Equals(l.Section, sec, StringComparison.OrdinalIgnoreCase) &&
                               string.Equals(l.Key, k, StringComparison.OrdinalIgnoreCase));
    }

    public bool TryGetValue(string? section, string key, [NotNullWhen(true)] out string? value)
    {
        string sec = (section ?? string.Empty).Trim();
        string k = key.Trim();

        var match = _lines.LastOrDefault(l => l.Type == LineType.KeyValue &&
                                              string.Equals(l.Section, sec, StringComparison.OrdinalIgnoreCase) &&
                                              string.Equals(l.Key, k, StringComparison.OrdinalIgnoreCase));

        if (match is not null && match.Value is not null)
        {
            value = match.Value;
            return true;
        }

        value = null;
        return false;
    }

    public string? GetValue(string? section, string key)
    {
        TryGetValue(section, key, out string? val);
        return val;
    }

    public string GetValue(string? section, string key, string defaultValue)
    {
        return TryGetValue(section, key, out string? val) ? val : defaultValue;
    }

    public void SetValue(string? section, string key, string value)
    {
        string sec = (section ?? string.Empty).Trim();
        string k = key.Trim();

        var matches = _lines.Where(l => l.Type == LineType.KeyValue &&
                                        string.Equals(l.Section, sec, StringComparison.OrdinalIgnoreCase) &&
                                        string.Equals(l.Key, k, StringComparison.OrdinalIgnoreCase))
                            .ToList();

        if (matches.Count > 0)
        {
            var first = matches[0];
            first.Value = value;
            first.RawText = $"{first.KeyPrefix}{first.Key}{first.EqualsSeparator}{value}{first.LineEnding}";

            // 若有任何多餘的重複鍵值，予以清除
            for (int i = 1; i < matches.Count; i++)
            {
                _lines.Remove(matches[i]);
            }
            return;
        }

        // 鍵不存在，插入至指定節區尾端
        InsertKeyIntoSection(sec, k, value);
    }

    /// <summary>
    /// 附加至清單型節區（例如 [Resolutions]）。即使鍵名稱相同也會附加於節區尾端。
    /// </summary>
    public void AppendToListSection(string section, string key, string value)
    {
        string sec = section.Trim();
        InsertKeyIntoSection(sec, key.Trim(), value);
    }

    private void InsertKeyIntoSection(string section, string key, string value)
    {
        string separator = "=";
        var sampleLine = _lines.FirstOrDefault(l => l.Type == LineType.KeyValue && string.Equals(l.Section, section, StringComparison.OrdinalIgnoreCase))
                      ?? _lines.FirstOrDefault(l => l.Type == LineType.KeyValue);
        if (sampleLine is not null)
        {
            separator = sampleLine.EqualsSeparator;
        }

        if (section.Length == 0)
        {
            // 頂層無節區：插入在頂層最後一個 KeyValue 之後（若無則在第一個 SectionHeader 之前或文件尾端）
            int firstHeaderIndex = _lines.FindIndex(l => l.Type == LineType.SectionHeader);
            int topLevelEnd = firstHeaderIndex >= 0 ? firstHeaderIndex : _lines.Count;

            int lastKeyValIndex = -1;
            for (int i = topLevelEnd - 1; i >= 0; i--)
            {
                if (_lines[i].Type == LineType.KeyValue)
                {
                    lastKeyValIndex = i;
                    break;
                }
            }

            int insertIndex = lastKeyValIndex >= 0 ? lastKeyValIndex + 1 : topLevelEnd;
            string lineEnding = sampleLine?.LineEnding ?? _defaultLineEnding;

            var newLine = new IniLine
            {
                Type = LineType.KeyValue,
                Section = string.Empty,
                Key = key,
                Value = value,
                LineEnding = lineEnding,
                KeyPrefix = sampleLine?.KeyPrefix ?? string.Empty,
                EqualsSeparator = separator,
                RawText = $"{sampleLine?.KeyPrefix ?? string.Empty}{key}{separator}{value}{lineEnding}"
            };

            _lines.Insert(insertIndex, newLine);
            return;
        }

        int headerIndex = _lines.FindIndex(l => l.Type == LineType.SectionHeader &&
                                               string.Equals(l.Section, section, StringComparison.OrdinalIgnoreCase));

        if (headerIndex >= 0)
        {
            // 找到該節區的結束位置（下一個 SectionHeader 之前）
            int nextHeaderIndex = _lines.FindIndex(headerIndex + 1, l => l.Type == LineType.SectionHeader);
            int sectionEnd = nextHeaderIndex >= 0 ? nextHeaderIndex : _lines.Count;

            // 尋找此節區內最後一個 KeyValue 行的位置（在空白行與註解之前插入）
            int lastKeyValIndex = -1;
            for (int i = sectionEnd - 1; i > headerIndex; i--)
            {
                if (_lines[i].Type == LineType.KeyValue)
                {
                    lastKeyValIndex = i;
                    break;
                }
            }

            int insertPos = lastKeyValIndex >= 0 ? lastKeyValIndex + 1 : headerIndex + 1;
            string lineEnding = sampleLine?.LineEnding ?? _defaultLineEnding;

            var newLine = new IniLine
            {
                Type = LineType.KeyValue,
                Section = section,
                Key = key,
                Value = value,
                LineEnding = lineEnding,
                KeyPrefix = sampleLine?.KeyPrefix ?? string.Empty,
                EqualsSeparator = separator,
                RawText = $"{sampleLine?.KeyPrefix ?? string.Empty}{key}{separator}{value}{lineEnding}"
            };

            _lines.Insert(insertPos, newLine);
        }
        else
        {
            // 節區不存在，建立新節區並附加於檔案尾端
            if (_lines.Count > 0 && !_lines[^1].RawText.EndsWith('\n'))
            {
                _lines[^1].RawText += _defaultLineEnding;
            }

            _lines.Add(new IniLine
            {
                Type = LineType.SectionHeader,
                Section = section,
                RawText = $"[{section}]{_defaultLineEnding}",
                LineEnding = _defaultLineEnding
            });

            _lines.Add(new IniLine
            {
                Type = LineType.KeyValue,
                Section = section,
                Key = key,
                Value = value,
                LineEnding = _defaultLineEnding,
                KeyPrefix = string.Empty,
                EqualsSeparator = separator,
                RawText = $"{key}{separator}{value}{_defaultLineEnding}"
            });
        }
    }

    public void RemoveKey(string? section, string key)
    {
        string sec = (section ?? string.Empty).Trim();
        string k = key.Trim();
        _lines.RemoveAll(l => l.Type == LineType.KeyValue &&
                              string.Equals(l.Section, sec, StringComparison.OrdinalIgnoreCase) &&
                              string.Equals(l.Key, k, StringComparison.OrdinalIgnoreCase));
    }

    public void RemoveSection(string section)
    {
        string sec = section.Trim();
        if (sec.Length == 0) return;
        _lines.RemoveAll(l => string.Equals(l.Section, sec, StringComparison.OrdinalIgnoreCase));
    }

    public IReadOnlyList<string> GetSectionNames()
    {
        return _lines
            .Where(l => l.Type == LineType.SectionHeader)
            .Select(l => l.Section)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public IReadOnlyList<KeyValuePair<string, string>> GetSectionEntries(string? section)
    {
        string sec = (section ?? string.Empty).Trim();
        return _lines
            .Where(l => l.Type == LineType.KeyValue &&
                        string.Equals(l.Section, sec, StringComparison.OrdinalIgnoreCase) &&
                        l.Key is not null && l.Value is not null)
            .Select(l => new KeyValuePair<string, string>(l.Key!, l.Value!))
            .ToList();
    }
}
