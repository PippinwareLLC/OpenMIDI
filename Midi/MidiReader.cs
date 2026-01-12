using System.Text;

namespace OpenMIDI.Midi;

internal sealed class MidiReader
{
    private readonly BinaryReader _reader;

    public MidiReader(Stream stream)
    {
        _reader = new BinaryReader(stream, Encoding.ASCII, leaveOpen: true);
    }

    public long Position => _reader.BaseStream.Position;

    public string ReadChunkId()
    {
        byte[] data = _reader.ReadBytes(4);
        if (data.Length < 4)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading chunk id.");
        }

        return Encoding.ASCII.GetString(data);
    }

    public ushort ReadUInt16BE()
    {
        byte[] data = _reader.ReadBytes(2);
        if (data.Length < 2)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading UInt16.");
        }

        return (ushort)((data[0] << 8) | data[1]);
    }

    public uint ReadUInt32BE()
    {
        byte[] data = _reader.ReadBytes(4);
        if (data.Length < 4)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading UInt32.");
        }

        return (uint)((data[0] << 24) | (data[1] << 16) | (data[2] << 8) | data[3]);
    }

    public byte ReadByte()
    {
        try
        {
            return _reader.ReadByte();
        }
        catch (EndOfStreamException)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading byte.");
        }
    }

    public byte[] ReadBytes(int length)
    {
        byte[] data = _reader.ReadBytes(length);
        if (data.Length < length)
        {
            throw new EndOfStreamException("Unexpected end of stream while reading bytes.");
        }

        return data;
    }

    public int ReadVariableLength()
    {
        int value = 0;
        for (int i = 0; i < 4; i++)
        {
            byte current = ReadByte();
            value = (value << 7) | (current & 0x7F);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }

        throw new FormatException("Variable length quantity exceeds 4 bytes.");
    }

    public void Skip(long length)
    {
        _reader.BaseStream.Seek(length, SeekOrigin.Current);
    }
}
