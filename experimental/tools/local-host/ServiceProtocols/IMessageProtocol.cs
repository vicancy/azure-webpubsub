using System.Buffers;

namespace Microsoft.Azure.SignalR;

#nullable enable
public interface IMessageProtocol<T>
{
    bool TryParse(ref ReadOnlySequence<byte> buffer, out T message);
    void Write(T message, IBufferWriter<byte> writer);
}
#nullable restore
