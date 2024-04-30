using System.Security.Cryptography;
using System.Text;
public static class BufferHelper
{
    public static void AssertWrite(bool success)
    {
        if (!success) throw new InvalidOperationException("Failed to write to buffer.");
    }
}

// Communication Protocol
public ref struct RouterHeader(Span<byte> buffer)
{
    public const int HeaderSize = 20;
    private readonly Span<byte> buffer = buffer;

    // Properties
    public readonly byte Version
    {
        get => buffer[0];
        set => buffer[0] = value;
    }

    public readonly byte Ttl
    {
        get => buffer[1];
        set => buffer[1] = value;
    }

    public readonly ushort TotalLength
    {
        get => BitConverter.ToUInt16(buffer.Slice(2, 2));
        set => BufferHelper.AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(2, 2), value));
    }

    public readonly uint SourceID
    {
        get => BitConverter.ToUInt32(buffer.Slice(4, 4));
        set => BufferHelper.AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(4, 4), value));
    }
    public readonly ushort SourcePort
    {
        get => BitConverter.ToUInt16(buffer.Slice(8, 2));
        set => BufferHelper.AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(8, 2), value));
    }
    public readonly ushort DestinationPort
    {
        get => BitConverter.ToUInt16(buffer.Slice(10, 2));
        set => BufferHelper.AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(10, 2), value));
    }
    public readonly uint DestinationID
    {
        get => BitConverter.ToUInt32(buffer.Slice(12, 4));
        set => BufferHelper.AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(12, 4), value));
    }
    public readonly ushort MessageID
    {
        get => BitConverter.ToUInt16(buffer.Slice(16, 2));
        set => BufferHelper.AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(16, 2), value));
    }
    public readonly ushort Hash
    {
        get => BitConverter.ToUInt16(buffer.Slice(18, 2));
        set => BufferHelper.AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(18, 2), value));
    }

    public readonly ushort CalculateHash()
    {
        if (buffer.Length < 18)
            throw new ArgumentException("Buffer must be at least 18 bytes long, this exception is unexpected, also it is impossible");
        
        var arr = buffer[..18];
        var c = SHA256.HashData(arr);
        var offset = c[0]%(c.Length-2);
        return BitConverter.ToUInt16(c,offset);
    }
    public readonly void ReverseAddress()
    {
        (SourceID,DestinationID) = (DestinationID,SourceID);
        (SourcePort,DestinationPort) = (DestinationPort,SourcePort);
    }
    
    public override readonly string ToString() => $"Version: {Version} | TTL: {Ttl}  Source: {SourceID}:{SourcePort}  Destination: {DestinationID}:{DestinationPort}  Message: {MessageID} | Hash: {Hash}";
}
public enum IMPHState : byte
{
    None = 0,
    Request = 1,
    Response = 2,
}
// Inter-router Massaging Protocol Header
public ref struct IMPH(Span<byte> buffer)
{
    public const int HeaderSize = 17;
    private readonly Span<byte> buffer = buffer;

    // Properties
    public readonly Span<byte> HeaderCode => buffer[..4];
    public readonly Span<byte> MessageType => buffer.Slice(4,4);
    public readonly long Date{
        get => BitConverter.ToInt64(buffer.Slice(8,8));
        set => AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(8, 8), value));
    }
    public readonly IMPHState State {get => (IMPHState)buffer[16];set {buffer[16] = (byte)(value);}}

    public readonly bool IsIMPH => HeaderCode[0] == 'I' && HeaderCode[1] == 'M' && HeaderCode[2] == 'P' && HeaderCode[3] == 'H';
    public readonly void Make(string type){
        
        HeaderCode[0] = (byte)'I';
        HeaderCode[1] = (byte)'M';
        HeaderCode[2] = (byte)'P';
        HeaderCode[3] = (byte)'H';
        var tp = type.AsSpan(0,Math.Min(type.Length,4)).ToString().ToUpper();
        for (int i = 0; i < 4; i++)
            MessageType[i] = i<tp.Length?((byte)tp[i]):(byte)0;
        Date = 0;
    }

    public override readonly string ToString()
    {
        if(!IsIMPH)
            return "Not a IMPH";
        string t = Encoding.ASCII.GetString(MessageType);
        return $"[IMPH|{t}]";
    }
    private static void AssertWrite(bool success)
    {
        if (!success)
        {
            throw new InvalidOperationException("Failed to write to buffer.");
        }
    }
}
