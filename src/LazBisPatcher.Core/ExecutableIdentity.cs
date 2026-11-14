using System.Buffers.Binary;
using System.Reflection.PortableExecutable;

namespace LazBisPatcher.Core;

internal static class ExecutableIdentity
{
    // Read the native FileVersion resource even when cross-publishing on Linux.
    internal static Version Read(string path)
    {
        Files.Guard(path);
        using var input = File.OpenRead(path);
        using var pe = new PEReader(input);
        var headers = pe.PEHeaders;
        if (headers.CoffHeader.Machine != Machine.Amd64 || headers.PEHeader is not { Magic: PEMagic.PE32Plus } header)
            throw new InvalidDataException("Expected a Windows x64 patcher executable.");
        var resources = header.ResourceTableDirectory;
        if (resources.Size is < 16 or > 4_194_304) throw InvalidVersion();
        var section = pe.GetSectionData(resources.RelativeVirtualAddress);
        // Some apphosts round the resource directory length up beyond its file-backed data.
        var table = section.GetContent(0, Math.Min(resources.Size, section.Length)).AsSpan();
        var type = Find(table, 0, 16, directory: true); // RT_VERSION
        var name = Find(table, type, 1, directory: true);
        var leaf = Find(table, name, null, directory: false);
        var address = checked((int)Word(table, leaf));
        var size = checked((int)Word(table, leaf + 4));
        if (size is < 92 or > 65536) throw InvalidVersion();
        var block = pe.GetSectionData(address).GetContent(0, size).AsSpan();
        const int fixedOffset = 40;
        if (Half(block, 0) != size || Half(block, 2) != 52 ||
            !block.Slice(6, 32).SequenceEqual(System.Text.Encoding.Unicode.GetBytes("VS_VERSION_INFO\0")) ||
            Word(block, fixedOffset) != 0xFEEF04BD || Word(block, fixedOffset + 4) != 0x10000)
            throw InvalidVersion();
        uint ms = Word(block, fixedOffset + 8), ls = Word(block, fixedOffset + 12);
        return new((int)(ms >> 16), (int)(ms & 65535), (int)(ls >> 16), (int)(ls & 65535));
    }

    private static int Find(ReadOnlySpan<byte> table, int offset, uint? id, bool directory)
    {
        int count = Half(table, offset + 12) + Half(table, offset + 14);
        if (count is < 1 or > 256) throw InvalidVersion();
        int found = -1;
        for (int i = 0; i < count; i++)
        {
            int entry = offset + 16 + 8 * i;
            uint key = Word(table, entry);
            if (id is not null && key != id) continue;
            uint value = Word(table, entry + 4);
            if (found >= 0 || ((value & 0x80000000) != 0) != directory) throw InvalidVersion();
            found = (int)(value & 0x7FFFFFFF);
        }
        return found >= 0 ? found : throw InvalidVersion();
    }

    private static ushort Half(ReadOnlySpan<byte> value, int offset) => offset >= 0 && offset <= value.Length - 2
        ? BinaryPrimitives.ReadUInt16LittleEndian(value[offset..]) : throw InvalidVersion();
    private static uint Word(ReadOnlySpan<byte> value, int offset) => offset >= 0 && offset <= value.Length - 4
        ? BinaryPrimitives.ReadUInt32LittleEndian(value[offset..]) : throw InvalidVersion();
    private static InvalidDataException InvalidVersion() => new("The patcher executable has invalid version metadata.");
}
