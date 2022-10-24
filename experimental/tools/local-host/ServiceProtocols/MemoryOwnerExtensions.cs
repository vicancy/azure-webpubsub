using System;
using System.Buffers;

namespace Microsoft.Azure.SignalR
{
    public static class MemoryOwnerExtensions
    {
        public static IMemoryOwner<byte> CreateMemoryOwner(this MemoryBufferWriter writer)
        {
            var owner = ExactSizeMemoryPool.Shared.Rent((int)writer.Length);
            writer.CopyTo(owner.Memory.Span);
            return owner;
        }
    }
}
