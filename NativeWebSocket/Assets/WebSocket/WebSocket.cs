using System;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEngine;
namespace NativeWebSocket
{

#if !UNITY_WEBGL || UNITY_EDITOR
  public class WebSocket : IWebSocket
  {
    public event WebSocketOpenEventHandler OnOpen;
    public event WebSocketMessageEventHandler OnMessage;
    public event WebSocketErrorEventHandler OnError;
    public event WebSocketCloseEventHandler OnClose;

    private Uri uri;
    private Dictionary<string, string> headers;
    private List<string> subprotocols;
    private ClientWebSocket m_Socket = new ClientWebSocket();

    private CancellationTokenSource m_TokenSource;
    private CancellationToken m_CancellationToken;

    private readonly object Lock = new object();

    private bool isSending = false;
    private List<ArraySegment<byte>> sendBytesQueue = new List<ArraySegment<byte>>();
    private List<ArraySegment<byte>> sendTextQueue = new List<ArraySegment<byte>>();

    public WebSocket(string url, Dictionary<string, string> headers = null)
    {
      uri = new Uri(url);

      if (headers == null)
      {
        this.headers = new Dictionary<string, string>();
      }
      else
      {
        this.headers = headers;
      }

      subprotocols = new List<string>();

      string protocol = uri.Scheme;
      if (!protocol.Equals("ws") && !protocol.Equals("wss"))
        throw new ArgumentException("Unsupported protocol: " + protocol);
    }

    public WebSocket(string url, string subprotocol, Dictionary<string, string> headers = null)
    {
      uri = new Uri(url);

      if (headers == null)
      {
        this.headers = new Dictionary<string, string>();
      }
      else
      {
        this.headers = headers;
      }

      subprotocols = new List<string> { subprotocol };

      string protocol = uri.Scheme;
      if (!protocol.Equals("ws") && !protocol.Equals("wss"))
        throw new ArgumentException("Unsupported protocol: " + protocol);
    }

    public WebSocket(string url, List<string> subprotocols, Dictionary<string, string> headers = null)
    {
      uri = new Uri(url);

      if (headers == null)
      {
        this.headers = new Dictionary<string, string>();
      }
      else
      {
        this.headers = headers;
      }

      this.subprotocols = subprotocols;

      string protocol = uri.Scheme;
      if (!protocol.Equals("ws") && !protocol.Equals("wss"))
        throw new ArgumentException("Unsupported protocol: " + protocol);
    }

    public void CancelConnection()
    {
      m_TokenSource?.Cancel();
      m_TokenSource = null;
    }

    public async Task Connect()
    {
      try
      {
        m_TokenSource = new CancellationTokenSource();
        m_CancellationToken = m_TokenSource.Token;

        m_Socket = new ClientWebSocket();

        foreach (var header in headers)
        {
          m_Socket.Options.SetRequestHeader(header.Key, header.Value);
        }

        foreach (string subprotocol in subprotocols)
        {
          m_Socket.Options.AddSubProtocol(subprotocol);
        }

        await m_Socket.ConnectAsync(uri, m_CancellationToken);
        await new WaitForUpdate();
        if (!m_CancellationToken.IsCancellationRequested)
        {
          OnOpen?.Invoke();
          _ = Receive();
        }
      }
      catch (Exception ex)
      {
        Debug.Log("received connection error");
        OnError?.Invoke(ex.Message);
        OnClose?.Invoke(WebSocketCloseCode.Abnormal);
      }
    }

    public WebSocketState State
    {
      get
      {
        switch (m_Socket.State)
        {
          case System.Net.WebSockets.WebSocketState.Connecting:
            return WebSocketState.Connecting;

          case System.Net.WebSockets.WebSocketState.Open:
            return WebSocketState.Open;

          case System.Net.WebSockets.WebSocketState.CloseSent:
          case System.Net.WebSockets.WebSocketState.CloseReceived:
            return WebSocketState.Closing;

          case System.Net.WebSockets.WebSocketState.Closed:
            return WebSocketState.Closed;

          default:
            return WebSocketState.Closed;
        }
      }
    }

    public Task Send(byte[] bytes)
    {
      // return m_Socket.SendAsync(buffer, WebSocketMessageType.Binary, true, CancellationToken.None);
      return SendMessage(sendBytesQueue, WebSocketMessageType.Binary, new ArraySegment<byte>(bytes));
    }

    public Task SendText(string message)
    {
      var encoded = Encoding.UTF8.GetBytes(message);

      // m_Socket.SendAsync(buffer, WebSocketMessageType.Text, true, CancellationToken.None);
      return SendMessage(sendTextQueue, WebSocketMessageType.Text, new ArraySegment<byte>(encoded, 0, encoded.Length));
    }

    private async Task SendMessage(List<ArraySegment<byte>> queue, WebSocketMessageType messageType, ArraySegment<byte> buffer)
    {
      // Return control to the calling method immediately.
      // await Task.Yield();

      // Make sure we have data.
      if (buffer.Count == 0)
      {
        return;
      }

      // The state of the connection is contained in the context Items dictionary.
      bool sending;

      lock (Lock)
      {
        sending = isSending;

        // If not, we are now.
        if (!isSending)
        {
          isSending = true;
        }
      }

      if (!sending)
      {
        // Lock with a timeout, just in case.
        if (!Monitor.TryEnter(m_Socket, 1000))
        {
          // If we couldn't obtain exclusive access to the socket in one second, something is wrong.
          await m_Socket.CloseAsync(WebSocketCloseStatus.InternalServerError, string.Empty, m_CancellationToken);
          return;
        }

        try
        {
          // Send the message synchronously.
          var t = m_Socket.SendAsync(buffer, messageType, true, m_CancellationToken);
          t.Wait(m_CancellationToken);
        }
        finally
        {
          Monitor.Exit(m_Socket);
        }

        // Note that we've finished sending.
        lock (Lock)
        {
          isSending = false;
        }

        // Handle any queued messages.
        await HandleQueue(queue, messageType);
      }
      else
      {
        // Add the message to the queue.
        lock (Lock)
        {
          queue.Add(buffer);
        }
      }
    }

    private async Task HandleQueue(List<ArraySegment<byte>> queue, WebSocketMessageType messageType)
    {
      var buffer = new ArraySegment<byte>();
      lock (Lock)
      {
        // Check for an item in the queue.
        if (queue.Count > 0)
        {
          // Pull it off the top.
          buffer = queue[0];
          queue.RemoveAt(0);
        }
      }

      // Send that message.
      if (buffer.Count > 0)
      {
        await SendMessage(queue, messageType, buffer);
      }
    }

    private Mutex m_MessageListMutex = new Mutex();
    private List<byte[]> m_MessageList = new List<byte[]>();

    // simple dispatcher for queued messages.
    public void DispatchMessageQueue()
    {
      // lock mutex, copy queue content and clear the queue.
      m_MessageListMutex.WaitOne();
      List<byte[]> messageListCopy = new List<byte[]>();
      messageListCopy.AddRange(m_MessageList);
      m_MessageList.Clear();
      // release mutex to allow the websocket to add new messages
      m_MessageListMutex.ReleaseMutex();

      foreach (byte[] bytes in messageListCopy)
      {
        OnMessage?.Invoke(bytes);
      }
    }

    public async Task Receive()
    {
      WebSocketCloseCode closeCode = WebSocketCloseCode.Abnormal;
      await new WaitForBackgroundThread();

      ArraySegment<byte> buffer = new ArraySegment<byte>(new byte[8192]);
      try
      {
        while (m_Socket.State == System.Net.WebSockets.WebSocketState.Open)
        {
          if (m_CancellationToken.IsCancellationRequested)
          {
            break;
          }
          WebSocketReceiveResult result = null;
          using (var ms = new MemoryStream())
          {
            do
            {
              result = await m_Socket.ReceiveAsync(buffer, m_CancellationToken);
              ms.Write(buffer.Array, buffer.Offset, result.Count);
            }
            while (!result.EndOfMessage);

            ms.Seek(0, SeekOrigin.Begin);

            if (result.MessageType == WebSocketMessageType.Text)
            {
              m_MessageListMutex.WaitOne();
              m_MessageList.Add(ms.ToArray());
              m_MessageListMutex.ReleaseMutex();
            }
            else if (result.MessageType == WebSocketMessageType.Binary)
            {
              m_MessageListMutex.WaitOne();
              m_MessageList.Add(ms.ToArray());
              m_MessageListMutex.ReleaseMutex();
            }
            else if (result.MessageType == WebSocketMessageType.Close)
            {
              await Close();
              closeCode = WebSocketHelpers.ParseCloseCodeEnum((int)result.CloseStatus);
              break;
            }
          }
        }
      }
      catch (Exception e)
      {
        Debug.Log("Exception while waiting for messages: " + e.Message);
        m_TokenSource.Cancel();
        m_TokenSource = null;
      }
      finally
      {
        await new WaitForUpdate();
        OnClose?.Invoke(closeCode);
      }
    }

    public async Task Close()
    {
      if (State == WebSocketState.Open && !m_CancellationToken.IsCancellationRequested)
      {
        await m_Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, m_CancellationToken);
      }
      m_Socket?.Dispose();
      m_TokenSource?.Cancel();
      m_TokenSource = null;
    }
  }
#endif
}
