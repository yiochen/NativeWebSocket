using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

using NativeWebSocket;

public class Connection : MonoBehaviour
{
  WebSocket websocket;
  public InputField inputField;

  // Start is called before the first frame update
  void Start()
  {
    websocket = new WebSocket("ws://localhost:8080");

    websocket.OnOpen += () =>
    {
      Debug.Log("Connection open!");
    };

    websocket.OnError += (e) =>
    {
      Debug.Log("Error! " + e);
    };

    websocket.OnClose += (e) =>
    {
      Debug.Log("Connection closed!");
    };

    websocket.OnMessage += (bytes) =>
    {
      // Reading a plain text message
      var message = System.Text.Encoding.UTF8.GetString(bytes);
      Debug.Log("Received OnMessage! (" + bytes.Length + " bytes) " + message);
    };


  }

  public void Connect()
  {
    _ = websocket.Connect();
  }

  public void Disconnect()
  {
    _ = websocket.Close();
  }

  void Update()
  {
#if !UNITY_WEBGL || UNITY_EDITOR
    websocket.DispatchMessageQueue();
#endif
  }

  public async void SendWebSocketMessage()
  {
    if (websocket.State == WebSocketState.Open)
    {
      // Sending plain text
      await websocket.SendText(inputField.text);
    }
    else
    {
      Debug.Log("Websocket state is not open");
    }
  }

  private async void OnApplicationQuit()
  {
    Debug.Log("Quiting application, closing websocket");
    await websocket.Close();
  }
}
