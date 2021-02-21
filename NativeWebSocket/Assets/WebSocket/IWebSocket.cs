namespace NativeWebSocket
{
  public delegate void WebSocketOpenEventHandler();
  public delegate void WebSocketMessageEventHandler(byte[] data);
  public delegate void WebSocketErrorEventHandler(string errorMsg);
  public delegate void WebSocketCloseEventHandler(WebSocketCloseCode closeCode);
  public interface IWebSocket
  {
    event WebSocketOpenEventHandler OnOpen;
    event WebSocketMessageEventHandler OnMessage;
    event WebSocketErrorEventHandler OnError;
    event WebSocketCloseEventHandler OnClose;

    WebSocketState State { get; }
  }
}