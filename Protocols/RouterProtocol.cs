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
    public readonly Span<byte> Signature => buffer.Slice(20, 16);
    public const int HeaderSize = 36;
    public readonly ushort CalculateHash()
    {
        Signature.Clear();
        var arr = buffer[..18];
        var c = SHA256.HashData(arr);
        var offset = c[0]%(c.Length-2);
        return BitConverter.ToUInt16(c,offset);
    }
    public override readonly string ToString()
    {
        return $"Version: {Version} | TTL: {Ttl}  Source: {SourceID}:{SourcePort}  Destination: {DestinationID}:{DestinationPort}  Message: {MessageID} | Hash: {Hash}";
    }
    public readonly void ReverseAddress()
    {
        (SourceID,DestinationID) = (DestinationID,SourceID);
        (SourcePort,DestinationPort) = (DestinationPort,SourcePort);
    }
}
// Interrouter Massaging Protocol Header
public ref struct IMPH(Span<byte> buffer)
{
    private readonly Span<byte> buffer = buffer;
    // Properties
    public readonly Span<byte> Code => buffer[..4];
    public readonly Span<byte> Type => buffer.Slice(4,4);
    public readonly long Date{
        get => BitConverter.ToInt64(buffer.Slice(8,8));
        set => AssertWrite(BitConverter.TryWriteBytes(buffer.Slice(8, 8), value));
    }
    public readonly bool Request {get => buffer[16]!=0;set {buffer[16] = (byte)(value?1:0);}}
    public const int HeaderSize = 17;
    public readonly bool IsIMPH => buffer[0] == 'I' && buffer[1] == 'M' && buffer[2] == 'P' && buffer[3] == 'H';
    public readonly void Make(string type){
        
        Code[0] = (byte)'I';
        Code[1] = (byte)'M';
        Code[2] = (byte)'P';
        Code[3] = (byte)'H';
        var tp = type.AsSpan(0,Math.Min(type.Length,4)).ToString().ToUpper();
        for (int i = 0; i < 4; i++)
            Type[i] = i<tp.Length?((byte)tp[i]):(byte)0;
        Date = 0;
    }

    public override readonly string ToString()
    {
        if(!IsIMPH)
            return "Not a IMPH";
        string t = Encoding.ASCII.GetString(Type);
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
