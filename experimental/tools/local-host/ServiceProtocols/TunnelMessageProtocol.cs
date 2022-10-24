using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MessagePack;

using Microsoft.Azure.SignalR;

namespace local_host.ServiceProtocols
{

    // payloads inside WebSocket connection:
    // 1. ackId
    // 2. http request message: method, host, headers, content
    // 3. http response message: headers, content
    public class TunnelMessageProtocol
    {
        public static readonly TunnelMessageProtocol Instance = new TunnelMessageProtocol();

        public const byte HttpRequestMessageType = 0;
        public const byte HttpResponseMessageType = 1;
        public const byte OnDemandConnectionMessageType = 5;

        public abstract class TunnelMessage
        {
            public int Type { get; set; }
        }

        public class OnDemandConnectionMessage : TunnelMessage
        {
            public string[] Targets { get; set; }
        }

        public class RequestMessage : TunnelMessage
        {
            public int AckId { get; set; }

            public string HttpMethod { get; set; }

            public string Host { get; set; }

            public Dictionary<string, string[]> Headers { get; set; }

            public byte[] Content { get; set; }

            public HttpRequestMessage ToRequest()
            {
                var request = new HttpRequestMessage()
                {
                    Method = new HttpMethod(HttpMethod),
                    RequestUri = new Uri(Host),
                    Content = new ByteArrayContent(Content),
                };
                foreach(var header in Headers)
                {
                    request.Headers.Add(header.Key, header.Value);
                }
            }

            public static async Task<RequestMessage> FromHttpRequestMessage(HttpRequestMessage http, int ackId)
            {
                return new RequestMessage
                {
                    AckId = ackId,
                    HttpMethod = http.Method.Method,
                    Host = http.RequestUri?.ToString(),
                    Headers = http.Headers.ToDictionary(s => s.Key, s => s.Value.ToArray()),
                    Content = http.Content == null ? Array.Empty<byte>() : await http.Content.ReadAsByteArrayAsync(),
                };
            }
        }

        public class ResponseMessage : TunnelMessage
        {
            public int AckId { get; set; }
            public int StatusCode { get; set; }

            public Dictionary<string, string[]> Headers { get; set; }

            public byte[] Content { get; set; }

            public HttpResponseMessage ToResponse()
            {

            }

            public static async Task<ResponseMessage> FromHttpResponseMessage(HttpResponseMessage http, int ackId)
            {
                return new ResponseMessage
                {
                    AckId = ackId,
                    StatusCode = (int)http.StatusCode,
                    Headers = http.Headers.ToDictionary(s => s.Key, s => s.Value.ToArray()),
                    Content = http.Content == null ? Array.Empty<byte>() : await http.Content.ReadAsByteArrayAsync(),
                };
            }
        }

        public bool TryParseMessage(byte[] input, out TunnelMessage message)
        {
            message = null;
            try
            {
                var typeId = ParseMeta(input, out var offset);
                if (typeId == HttpRequestMessageType)
                {
                    message = ParseHttpRequestMessage(input, ref offset);
                }
                else if (typeId == HttpResponseMessageType)
                {
                    message = ParseHttpResponseMessage(input, ref offset);
                }
                else if (typeId == OnDemandConnectionMessageType)
                {
                    message = ParseOnDemandConnectionMessage(input, ref offset);
                }
            }
            catch
            {
                // Ignore
            }

            return message != null;
        }

        public bool TryParseOnDemandConnectionMessage(byte[] input, out OnDemandConnectionMessage message)
        {
            message = null;
            try
            {
                var typeId = ParseMeta(input, out var offset);
                if (typeId == OnDemandConnectionMessageType)
                {
                    message = ParseOnDemandConnectionMessage(input, ref offset);
                }
            }
            catch
            {
                // Ignore
            }

            return message != null;
        }

        public IMemoryOwner<byte> WriteMessage(TunnelMessage message)
        {
            using var lease = MemoryBufferWriter.GetLocalLease();
            try
            {
                MessagePackBinary.WriteArrayHeader(lease.Writer, 2);

                switch (message)
                {
                    case RequestMessage request:
                        MessagePackBinary.WriteByte(lease.Writer, HttpRequestMessageType);
                        return WriteRequestMessage(lease.Writer, request);
                    case ResponseMessage response:
                        MessagePackBinary.WriteByte(lease.Writer, HttpResponseMessageType);
                        return WriteResponseMessage(lease.Writer, response);
                    case OnDemandConnectionMessage request:
                        MessagePackBinary.WriteByte(lease.Writer, OnDemandConnectionMessageType);
                        return WriteRequestConnectionMessage(lease.Writer, request);
                }
            }
            catch
            {
                // Ignored
            }

            return null;
        }

        private IMemoryOwner<byte> WriteRequestConnectionMessage(MemoryBufferWriter writer, OnDemandConnectionMessage message)
        {
            return MessagePackWriter.Create(writer)
                .Array(1)
                .Item().NullableStringArray(message.Targets)
                .EndArray()
                .Terminate()
                .CreateMemoryOwner();
        }

        private IMemoryOwner<byte> WriteRequestMessage(MemoryBufferWriter writer, RequestMessage message)
        {
            return MessagePackWriter.Create(writer)
                .Array(5)
                .Int(message.AckId)
                .Text(message.HttpMethod)
                .Text(message.Host)
                .Item().Map(message.Headers, (w, v) => w.StringArray(v))
                .Bytes(message.Content)
                .EndArray()
                .Terminate()
                .CreateMemoryOwner();
        }


        private IMemoryOwner<byte> WriteResponseMessage(MemoryBufferWriter writer, ResponseMessage message)
        {
            return MessagePackWriter.Create(writer)
                .Array(4)
                .Int(message.AckId)
                .Int(message.StatusCode)
                .Item().Map(message.Headers, (w, v) => w.StringArray(v))
                .Bytes(message.Content)
                .EndArray()
                .Terminate()
                .CreateMemoryOwner();
        }

        private static OnDemandConnectionMessage ParseOnDemandConnectionMessage(byte[] input, ref int offset)
        {
            _ = MessagePackBinary.ReadArrayHeader(input, offset, out var c);
            offset += c;
            var targetIds = MessagePackHelper.ReadNullableStringArray(input, ref offset, "targetIds");

            return new OnDemandConnectionMessage
            {
                Targets = targetIds
            };
        }

        private static RequestMessage ParseHttpRequestMessage(byte[] input, ref int offset)
        {
            _ = MessagePackBinary.ReadArrayHeader(input, offset, out var c);
            offset += c;
            var ackId = MessagePackHelper.ReadInt32(input, ref offset, "ackId");
            var method = MessagePackHelper.ReadString(input, ref offset, "method");
            var host = MessagePackHelper.ReadString(input, ref offset, "host");

            var headers = ReadHeaders(input, ref offset);

            var body = MessagePackHelper.ReadBytes(input, ref offset, "body");

            return new RequestMessage
            {
                AckId = ackId,
                HttpMethod = method,
                Host = host,
                Headers = headers,
                Content = body
            };
        }

        private static Dictionary<string, string[]> ReadHeaders(byte[] input, ref int offset)
        {
            var headerCount = MessagePackHelper.ReadMapLength(input, ref offset, "headers");

            if (headerCount > 0)
            {
                var headers = new Dictionary<string, string[]>((int)headerCount);
                for (var i = 0; i < headerCount; i++)
                {
                    var key = MessagePackHelper.ReadString(input, ref offset, $"headers[{i}].key");
                    var value = MessagePackHelper.ReadStringArray(input, ref offset, $"headers[{i}].value");
                    headers.Add(key, value);
                }
                return headers;
            }

            return null;
        }

        private static ResponseMessage ParseHttpResponseMessage(byte[] input, ref int offset)
        {
            _ = MessagePackBinary.ReadArrayHeader(input, offset, out var c);
            offset += c;
            var ackId = MessagePackHelper.ReadInt32(input, ref offset, "ackId");
            var code = MessagePackHelper.ReadInt32(input, ref offset, "code");
            var headers = ReadHeaders(input, ref offset);
            var body = MessagePackHelper.ReadBytes(input, ref offset, "body");

            return new ResponseMessage
            {
                AckId = ackId,
                StatusCode = code,
                Headers = headers,
                Content = body
            };
        }

        private byte ParseMeta(byte[] input, out int offset)
        {
            // We use an array to wrap our raw message.
            // In json format: [ <typeid>, <raw message> ].
            // Here raw message is another array.
            // So actual format is: [ <typeid>, [ <raw>, <messages>, <body> ]]
            // In message pack, an array with 2 element encoded as 0x92
            // Type id is a number between 0 to 127, and in message pack, it encoded as itself, i.e. 0x00~0x7f
            // See messagepack spec: https://github.com/msgpack/msgpack/blob/master/spec.md
            if (input.Length > 2 && input[0] == 0x92 && input[1] < 0x80)
            {
                // Ensure: second element is an array.
                if (input[2] >= 0x90 && input[2] <= 0x9f)
                {
                    offset = 2;
                    return input[1];
                }
            }

            offset = 0;
            return 0;
        }
    }
}
