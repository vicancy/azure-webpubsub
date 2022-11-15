using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;

using static Microsoft.Azure.SignalR.HttpTunnel.TunnelMessageProtocol;

namespace Microsoft.Azure.SignalR.HttpTunnel
{
    // payloads inside WebSocket connection:
    // 1. ackId
    // 2. http request message: method, host, headers, content
    // 3. http response message: headers, content

    public class TunnelMessageProtocol : IMessageProtocol<TunnelMessage>
    {
        public static readonly TunnelMessageProtocol Instance = new TunnelMessageProtocol();

        public enum TunnelMessageType
        {
            HttpRequest = 0,
            HttpResponse = 1,
        }

        public abstract class TunnelMessage
        {
            public abstract TunnelMessageType Type { get; }
        }

        public class RequestMessage : TunnelMessage
        {
            public override TunnelMessageType Type => TunnelMessageType.HttpRequest;
            public int AckId { get; set; }

            public string HttpMethod { get; set; }

            public string Url { get; set; }

            public Dictionary<string, string[]> Headers { get; set; }

            public ReadOnlyMemory<byte> Content { get; set; }

            public bool GlobalRouting { get; set; }
        }

        public class ResponseMessage : TunnelMessage
        {
            public override TunnelMessageType Type => TunnelMessageType.HttpResponse;
            public int AckId { get; set; }
            public int StatusCode { get; set; }

            public Dictionary<string, string[]> Headers { get; set; }

            public ReadOnlyMemory<byte> Content { get; set; }

            public bool GlobalRouting { get; set; }
        }

        public bool TryParseResponseMessage(ReadOnlyMemory<byte> data, out ResponseMessage message)
        {
            var buffer = new ReadOnlySequence<byte>(data);
            if (TryParse(ref buffer, out var tm) && tm is ResponseMessage rm)
            {
                message = rm;
                return true;
            }

            message = null;
            return false;
        }

        public bool TryParseRequestMessage(ReadOnlyMemory<byte> data, out RequestMessage message)
        {
            var buffer = new ReadOnlySequence<byte>(data);
            if (TryParse(ref buffer, out var tm) && tm is RequestMessage rm)
            {
                message = rm;
                return true;
            }

            message = null;
            return false;
        }

        public bool TryParse(ref ReadOnlySequence<byte> buffer, out TunnelMessage message)
        {
            if (buffer.Length <= 4)
            {
                message = null;
                return false;
            }

            var length = ReadLength(ref buffer);
            if (buffer.Length < length + 4)
            {
                message = null;
                return false;
            }

            var content = buffer.Slice(4, length).AsStream();
            buffer = buffer.Slice(length + 4);
            try
            {
                message = Parse(content, false);
                return message != null;
            }
            catch (Exception)
            {
                message = null;
                return false;
            }

            int ReadLength(ref ReadOnlySequence<byte> buffer)
            {
                if (buffer.FirstSpan.Length > 4)
                {
                    return BitConverter.ToInt32(buffer.FirstSpan[0..4]);
                }
                else
                {
                    Span<byte> copy = stackalloc byte[4];
                    buffer.Slice(0, 4).CopyTo(copy);
                    return BitConverter.ToInt32(copy);
                }
            }
        }

        public static TunnelMessage Parse(Stream stream) =>
            Parse(stream, false);

        private static TunnelMessage Parse(Stream stream, bool throwOnError)
        {
            var reader = MessagePackReader.Create(stream)
                .Array()
                .Int(out var type);
            switch ((TunnelMessageType)type)
            {
                case TunnelMessageType.HttpRequest:
                    {
                        reader.Int(out var ackId)
                            .Boolean(out var global)
                            .Text(out var method)
                            .Text(out var host)
                            .Item("headers").Map().ToDict<string[]>((r, b) => r.StringArray(out b.Value), out var headers)
                            .Bytes(out var body)
                            .EndArray()
                            .Terminate();
                        return new RequestMessage
                        {
                            AckId = ackId,
                            GlobalRouting = global,
                            HttpMethod = method,
                            Url = host,
                            Headers = headers,
                            Content = body
                        };
                    }
                case TunnelMessageType.HttpResponse:
                    {
                        reader.Int(out var ackId)
                            .Boolean(out var global)
                            .Int(out var code)
                            .Item("headers").Map().ToDict<string[]>((r, b) => r.StringArray(out b.Value), out var headers)
                            .Bytes(out var body)
                            .EndArray()
                            .Terminate();
                        return new ResponseMessage
                        {
                            AckId = ackId,
                            GlobalRouting = global,
                            StatusCode = code,
                            Headers = headers,
                            Content = body
                        };
                    }
                default:
                    if (throwOnError)
                    {
                        throw new NotSupportedException($"Message with type:{type} is not supported");
                    }
                    return null;
            }
        }

        public void Write(TunnelMessage message, IBufferWriter<byte> writer)
        {
            var m = writer.GetMemory(4);
            writer.Advance(4);
            var stream = writer.AsStream();
            Write(message, stream);
            BitConverter.TryWriteBytes(m.Span, (int)stream.Length);
        }

        private static void Write(TunnelMessage message, Stream writer)
        {
            switch (message.Type)
            {
                case TunnelMessageType.HttpRequest:
                    WriteRequestMessage(writer, (RequestMessage)message);
                    break;
                case TunnelMessageType.HttpResponse:
                    WriteResponseMessage(writer, (ResponseMessage)message);
                    break;
                default:
                    throw new ArgumentException($"Unknown message type: {message.Type}.", nameof(message));
            }
        }

        private static void WriteRequestMessage(Stream writer, RequestMessage message)
        {
            MessagePackWriter.Create(writer)
                .Array(7)
                .Int((int)TunnelMessageType.HttpRequest)
                .Int(message.AckId)
                .Boolean(message.GlobalRouting)
                .Text(message.HttpMethod)
                .Text(message.Url)
                .Item().Map(message.Headers, (w, v) => w.StringArray(v))
                .Bytes(message.Content)
                .EndArray()
                .Terminate();
        }

        private static void WriteResponseMessage(Stream writer, ResponseMessage message)
        {
            MessagePackWriter.Create(writer)
                .Array(6)
                .Int((int)TunnelMessageType.HttpResponse)
                .Int(message.AckId)
                .Boolean(message.GlobalRouting)
                .Int(message.StatusCode)
                .Item().Map(message.Headers, (w, v) => w.StringArray(v))
                .Bytes(message.Content)
                .EndArray()
                .Terminate();
        }
    }
}
