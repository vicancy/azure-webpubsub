using System.Net.WebSockets;

namespace local_host
{
    public class WebSocketReceivedResult
    {
        public byte[] Buffer { get; }
        public WebSocketMessageType MessageType { get; }
        public WebSocketReceivedResult(byte[] buffer, WebSocketMessageType messageType)
        {
            Buffer = buffer;
            MessageType = messageType;
        }
    }
}