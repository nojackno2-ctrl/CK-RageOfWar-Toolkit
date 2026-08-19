using System.Text;

namespace CKToolkit.Core.Common;

/// <summary>
/// PE 節區結構描述。
/// </summary>
public sealed class PeSection
{
    public string Name { get; set; } = string.Empty;
    public uint VirtualSize { get; set; }
    public uint VirtualAddress { get; set; }
    public uint SizeOfRawData { get; set; }
    public uint PointerToRawData { get; set; }
    public uint PointerToRelocations { get; set; }
    public uint PointerToLinenumbers { get; set; }
    public ushort NumberOfRelocations { get; set; }
    public ushort NumberOfLinenumbers { get; set; }
    public uint Characteristics { get; set; }

    public int HeaderOffset { get; internal set; }

    public override string ToString() =>
        $"{Name}: RVA 0x{VirtualAddress:X8}, VSize 0x{VirtualSize:X8}, RawOff 0x{PointerToRawData:X8}, RawSize 0x{SizeOfRawData:X8}";
}

/// <summary>
/// 32 位元 (PE32) 與 64 位元 (PE32+) PE 執行檔讀寫器。
/// 支援 DOS/NT 檔頭解析、節區表查表、RVA/VA 與檔案位移互轉、Characteristics 標誌 (LAA) 讀寫、
/// 以及動態附加節區（供 Phase 2 HD 解析度 .ckhr 節區使用）。
///
/// 移植自前身 C++ patches.cpp，保留所有記憶體位址與逆向分析註解。
/// </summary>
public sealed class PeFile
{
    public const ushort ImageFileLargeAddressAware = 0x0020;
    public const uint ImageScnCntUninitializedData = 0x00000080;
    public const uint ImageScnMemRead = 0x40000000;
    public const uint ImageScnMemWrite = 0x80000000;

    private byte[] _data;

    public int NtHeaderOffset { get; private set; }
    public int FileHeaderOffset { get; private set; }
    public int OptionalHeaderOffset { get; private set; }
    public int SectionTableOffset { get; private set; }
    public int SizeOfImageOffset { get; private set; }

    public bool Is64Bit { get; private set; }
    public ulong ImageBase { get; private set; }
    public uint SectionAlignment { get; private set; }
    public uint FileAlignment { get; private set; }
    public uint SizeOfImage { get; private set; }
    public uint SizeOfHeaders { get; private set; }
    public ushort NumberOfSections { get; private set; }

    private readonly List<PeSection> _sections = new();
    public IReadOnlyList<PeSection> Sections => _sections;

    private PeFile(byte[] data)
    {
        _data = data;
        Parse();
    }

    public static PeFile Parse(byte[] peBytes) => new(peBytes.ToArray());

    public static PeFile Load(string path) => new(File.ReadAllBytes(path));

    public byte[] ToBytes() => _data.ToArray();

    public void Save(string path) => File.WriteAllBytes(path, _data);

    public static uint AlignUp(uint value, uint alignment)
    {
        if (alignment == 0) return value;
        return (value + alignment - 1) / alignment * alignment;
    }

    private void Parse()
    {
        if (_data.Length < 0x40)
            throw new InvalidDataException("檔案過小，非有效 PE 格式");

        // DOS Header 檢查
        if (_data[0] != (byte)'M' || _data[1] != (byte)'Z')
            throw new InvalidDataException("DOS 檔頭標記錯誤 (缺少 MZ)");

        uint lfanew = BitConverter.ToUInt32(_data, 0x3C);
        if (lfanew + 24 > _data.Length)
            throw new InvalidDataException("NT 檔頭位移超出檔案長度");

        NtHeaderOffset = (int)lfanew;

        // PE 標頭簽章 "PE\0\0"
        if (_data[NtHeaderOffset] != (byte)'P' ||
            _data[NtHeaderOffset + 1] != (byte)'E' ||
            _data[NtHeaderOffset + 2] != 0 ||
            _data[NtHeaderOffset + 3] != 0)
        {
            throw new InvalidDataException("PE 簽章錯誤 (缺少 PE\\0\\0)");
        }

        FileHeaderOffset = NtHeaderOffset + 4;
        NumberOfSections = BitConverter.ToUInt16(_data, FileHeaderOffset + 2);
        ushort sizeOfOptionalHeader = BitConverter.ToUInt16(_data, FileHeaderOffset + 16);

        OptionalHeaderOffset = FileHeaderOffset + 20;
        ushort optMagic = BitConverter.ToUInt16(_data, OptionalHeaderOffset);

        if (optMagic == 0x010B)
        {
            // 32-bit PE (PE32) - 如 Celtic kings.exe
            Is64Bit = false;
            ImageBase = BitConverter.ToUInt32(_data, OptionalHeaderOffset + 28);
            SectionAlignment = BitConverter.ToUInt32(_data, OptionalHeaderOffset + 32);
            FileAlignment = BitConverter.ToUInt32(_data, OptionalHeaderOffset + 36);
            SizeOfImageOffset = OptionalHeaderOffset + 56;
            SizeOfImage = BitConverter.ToUInt32(_data, SizeOfImageOffset);
            SizeOfHeaders = BitConverter.ToUInt32(_data, OptionalHeaderOffset + 60);
        }
        else if (optMagic == 0x020B)
        {
            // 64-bit PE (PE32+) - 如 Celtic kings Launcher.exe
            Is64Bit = true;
            ImageBase = BitConverter.ToUInt64(_data, OptionalHeaderOffset + 24);
            SectionAlignment = BitConverter.ToUInt32(_data, OptionalHeaderOffset + 32);
            FileAlignment = BitConverter.ToUInt32(_data, OptionalHeaderOffset + 36);
            SizeOfImageOffset = OptionalHeaderOffset + 56;
            SizeOfImage = BitConverter.ToUInt32(_data, SizeOfImageOffset);
            SizeOfHeaders = BitConverter.ToUInt32(_data, OptionalHeaderOffset + 60);
        }
        else
        {
            throw new InvalidDataException($"未知的 Optional Header Magic: 0x{optMagic:X4}");
        }

        SectionTableOffset = OptionalHeaderOffset + sizeOfOptionalHeader;

        if (SectionAlignment == 0 || SectionTableOffset + NumberOfSections * 40 > _data.Length)
            throw new InvalidDataException("PE 節區表格式不合法或超出檔案長度");

        _sections.Clear();
        for (int i = 0; i < NumberOfSections; i++)
        {
            int h = SectionTableOffset + i * 40;
            string name = Encoding.ASCII.GetString(_data, h, 8).TrimEnd('\0');

            _sections.Add(new PeSection
            {
                Name = name,
                VirtualSize = BitConverter.ToUInt32(_data, h + 8),
                VirtualAddress = BitConverter.ToUInt32(_data, h + 12),
                SizeOfRawData = BitConverter.ToUInt32(_data, h + 16),
                PointerToRawData = BitConverter.ToUInt32(_data, h + 20),
                PointerToRelocations = BitConverter.ToUInt32(_data, h + 24),
                PointerToLinenumbers = BitConverter.ToUInt32(_data, h + 28),
                NumberOfRelocations = BitConverter.ToUInt16(_data, h + 32),
                NumberOfLinenumbers = BitConverter.ToUInt16(_data, h + 34),
                Characteristics = BitConverter.ToUInt32(_data, h + 36),
                HeaderOffset = h
            });
        }
    }

    // --- Characteristics (LAA) 讀寫 ---

    public ushort Characteristics
    {
        get => BitConverter.ToUInt16(_data, FileHeaderOffset + 18);
        set => BitConverter.TryWriteBytes(_data.AsSpan(FileHeaderOffset + 18, 2), value);
    }

    /// <summary>
    /// LargeAddressAware 特徵旗標（允許 32 位元程序取得 4GB 虛擬位址空間）。
    /// </summary>
    public bool LargeAddressAware
    {
        get => (Characteristics & ImageFileLargeAddressAware) != 0;
        set
        {
            ushort ch = Characteristics;
            if (value)
                ch |= ImageFileLargeAddressAware;
            else
                ch = (ushort)(ch & ~ImageFileLargeAddressAware);
            Characteristics = ch;
        }
    }

    // --- 位址換算 ---

    public int FindSection(string name)
    {
        string target = name.Trim();
        for (int i = 0; i < _sections.Count; i++)
        {
            if (string.Equals(_sections[i].Name, target, StringComparison.OrdinalIgnoreCase))
                return i;
        }
        return -1;
    }

    public bool TryRvaToFileOffset(uint rva, out int fileOffset)
    {
        foreach (var s in _sections)
        {
            if (s.SizeOfRawData != 0 && rva >= s.VirtualAddress && rva < s.VirtualAddress + s.SizeOfRawData)
            {
                fileOffset = (int)(s.PointerToRawData + (rva - s.VirtualAddress));
                return fileOffset + 4 <= _data.Length;
            }
        }
        fileOffset = 0;
        return false;
    }

    public bool TryVaToFileOffset(ulong va, out int fileOffset)
    {
        if (va < ImageBase)
        {
            fileOffset = 0;
            return false;
        }
        uint rva = (uint)(va - ImageBase);
        return TryRvaToFileOffset(rva, out fileOffset);
    }

    public int VaToFileOffset(ulong va)
    {
        if (!TryVaToFileOffset(va, out int off))
            throw new ArgumentOutOfRangeException(nameof(va), $"VA 0x{va:X} 無法對應至任何實體檔案節區");
        return off;
    }

    public int RvaToFileOffset(uint rva)
    {
        if (!TryRvaToFileOffset(rva, out int off))
            throw new ArgumentOutOfRangeException(nameof(rva), $"RVA 0x{rva:X} 無法對應至任何實體檔案節區");
        return off;
    }

    // --- 位元組讀寫輔助 ---

    public uint ReadUInt32(int fileOffset) => BitConverter.ToUInt32(_data, fileOffset);

    public void WriteUInt32(int fileOffset, uint value) =>
        BitConverter.TryWriteBytes(_data.AsSpan(fileOffset, 4), value);

    public ushort ReadUInt16(int fileOffset) => BitConverter.ToUInt16(_data, fileOffset);

    public void WriteUInt16(int fileOffset, ushort value) =>
        BitConverter.TryWriteBytes(_data.AsSpan(fileOffset, 2), value);

    public byte[] ReadBytes(int fileOffset, int length) =>
        _data.AsSpan(fileOffset, length).ToArray();

    public void WriteBytes(int fileOffset, ReadOnlySpan<byte> bytes) =>
        bytes.CopyTo(_data.AsSpan(fileOffset, bytes.Length));

    public uint ReadUInt32AtVa(ulong va) => ReadUInt32(VaToFileOffset(va));

    public void WriteUInt32AtVa(ulong va, uint value) => WriteUInt32(VaToFileOffset(va), value);

    public byte[] ReadBytesAtVa(ulong va, int length) => ReadBytes(VaToFileOffset(va), length);

    public void WriteBytesAtVa(ulong va, ReadOnlySpan<byte> bytes) => WriteBytes(VaToFileOffset(va), bytes);

    // --- 節區附加 ---

    /// <summary>
    /// 附加新節區（如 Phase 2 的 .ckhr 掃描線緩衝節區）。
    /// </summary>
    /// <param name="name">節區名稱（最長 8 字元）</param>
    /// <param name="virtualSize">虛擬記憶體大小</param>
    /// <param name="characteristics">節區屬性旗標</param>
    /// <param name="rawData">實體附加資料（未初始化資料傳入 null）</param>
    /// <returns>新增之節區物件</returns>
    public PeSection AddSection(string name, uint virtualSize, uint characteristics, byte[]? rawData = null)
    {
        if (FindSection(name) >= 0)
            throw new InvalidOperationException($"節區 {name} 已存在");

        int newSectionIndex = _sections.Count;
        int newHeaderOffset = SectionTableOffset + newSectionIndex * 40;

        if (newHeaderOffset + 40 > SizeOfHeaders)
            throw new InvalidOperationException("PE 檔頭空間不足，無法新增節區");

        uint alignedVirtualSize = AlignUp(virtualSize, SectionAlignment);
        uint alignedRawSize = rawData != null ? AlignUp((uint)rawData.Length, FileAlignment) : 0;

        // 計算新節區的 VirtualAddress (RVA)
        uint maxRvaTop = 0;
        foreach (var s in _sections)
        {
            uint top = AlignUp(s.VirtualAddress + s.VirtualSize, SectionAlignment);
            if (top > maxRvaTop) maxRvaTop = top;
        }
        uint newRva = maxRvaTop;

        // 計算新節區的 PointerToRawData
        uint newRawPointer = 0;
        if (rawData != null && rawData.Length > 0)
        {
            uint maxRawEnd = 0;
            foreach (var s in _sections)
            {
                uint end = AlignUp(s.PointerToRawData + s.SizeOfRawData, FileAlignment);
                if (end > maxRawEnd) maxRawEnd = end;
            }
            newRawPointer = Math.Max(maxRawEnd, (uint)_data.Length);
            newRawPointer = AlignUp(newRawPointer, FileAlignment);
        }

        // 寫入 40 位元組的 Section Header
        byte[] nameBytes = new byte[8];
        Encoding.ASCII.GetBytes(name, 0, Math.Min(name.Length, 8), nameBytes, 0);

        Array.Copy(nameBytes, 0, _data, newHeaderOffset, 8);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 8, 4), alignedVirtualSize);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 12, 4), newRva);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 16, 4), alignedRawSize);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 20, 4), newRawPointer);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 24, 4), 0u);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 28, 4), 0u);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 32, 2), (ushort)0);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 34, 2), (ushort)0);
        BitConverter.TryWriteBytes(_data.AsSpan(newHeaderOffset + 36, 4), characteristics);

        // 更新 FileHeader 的 NumberOfSections
        ushort updatedSectionCount = (ushort)(NumberOfSections + 1);
        BitConverter.TryWriteBytes(_data.AsSpan(FileHeaderOffset + 2, 2), updatedSectionCount);

        // 更新 OptionalHeader 的 SizeOfImage
        uint newSizeOfImage = AlignUp(newRva + alignedVirtualSize, SectionAlignment);
        BitConverter.TryWriteBytes(_data.AsSpan(SizeOfImageOffset, 4), newSizeOfImage);

        // 若有實體資料，擴展 _data
        if (rawData != null && rawData.Length > 0)
        {
            int requiredLength = (int)(newRawPointer + alignedRawSize);
            if (_data.Length < requiredLength)
            {
                Array.Resize(ref _data, requiredLength);
            }
            Array.Copy(rawData, 0, _data, (int)newRawPointer, rawData.Length);
        }

        // 重新解析快取
        Parse();

        return _sections.First(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// 移除指定名稱之節區（供 HiRes .ckhr 節區還原使用）。
    /// </summary>
    public bool RemoveSection(string name)
    {
        int index = FindSection(name);
        if (index < 0) return false;

        // 將目標 Section Header (40 位元組) 清零
        int h = _sections[index].HeaderOffset;
        _data.AsSpan(h, 40).Clear();

        // 若不是最後一個節區，將後續節區標頭向前搬移 40 位元組
        if (index < _sections.Count - 1)
        {
            for (int i = index; i < _sections.Count - 1; i++)
            {
                int src = _sections[i + 1].HeaderOffset;
                int dst = SectionTableOffset + i * 40;
                Array.Copy(_data, src, _data, dst, 40);
            }
            int lastHeader = SectionTableOffset + (_sections.Count - 1) * 40;
            _data.AsSpan(lastHeader, 40).Clear();
        }

        // 更新 FileHeader 的 NumberOfSections
        ushort updatedSectionCount = (ushort)(NumberOfSections - 1);
        BitConverter.TryWriteBytes(_data.AsSpan(FileHeaderOffset + 2, 2), updatedSectionCount);

        // 計算剩餘節區之最高 RVA 邊界以重新計算 SizeOfImage
        uint maxRvaTop = 0;
        for (int i = 0; i < _sections.Count; i++)
        {
            if (i == index) continue;
            var s = _sections[i];
            uint top = AlignUp(s.VirtualAddress + s.VirtualSize, SectionAlignment);
            if (top > maxRvaTop) maxRvaTop = top;
        }
        BitConverter.TryWriteBytes(_data.AsSpan(SizeOfImageOffset, 4), maxRvaTop);

        // 重新解析快取
        Parse();
        return true;
    }
}
