using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using System.Net.WebSockets;

[Serializable]
public class SendTextFormat
{
    public string type;      // "start_recording" または "stop_recording"
    public string message;   // その他のメッセージ
}

public class WebSocketClient : MonoBehaviour
{
    [SerializeField] private RecordingButton recordingButton;  // RecordingButtonへの参照
    [SerializeField] private bool DEBUG = false;  // デバッグモードのフラグ
    private string serverUrl = "ws://localhost:8000/ws/recording";  // サーバーのURL
    private ClientWebSocket webSocket;
    private bool isConnecting = false;
    private bool isConnected = false;
    private bool isQuitting = false;
private bool Queue_is_empty = false;
private bool isRecording = false;

    // 録音制御メソッド
    public async Task StartRecording()
    {
        if (!isRecording)
        {
            isRecording = true;
            await SendText("start_recording", "start_recording");
            Debug.Log("録音開始リクエスト送信");
        }
    }

    public async Task StopRecording()
    {
        if (isRecording)
        {
            isRecording = false;
            await SendText("stop_recording", "stop_recording");
            Debug.Log("録音停止リクエスト送信");
        }
    }

    private void AddDebugMessages()
    {
        // エージェント用のデバッグメッセージ
        GlobalVariables.AgentQueue.Add(new ReceiveMessageFormat
        {
            content = "皆さん、今日も元気ですか？ 私は元気です！",
            action = "Nothing",
            emotion = "happy",
        });
    }

    async void Start()
    {
        if (DEBUG)
        {
            AddDebugMessages();
            Debug.Log("Debug mode: Added test messages to queues");
            return;
        }

        await ConnectToServer();
    }

    void Update()
    {
        int count = GlobalVariables.AgentQueue.Count;
        // Queueが空になったらFinishを送信
        if (count == 0 && !Queue_is_empty)
        {
            Queue_is_empty = true;
            // await SendText("Finish");
        }else if (count > 0)
        {
            Queue_is_empty = false;
        }
    }

    private async Task ConnectToServer()
    {
        if (isConnecting) return;
        isConnecting = true;
        webSocket = new ClientWebSocket();
        Uri serverUri = new Uri(serverUrl);
        try
        {
            await webSocket.ConnectAsync(serverUri, CancellationToken.None);
            Debug.Log("Connected to server");
            isConnected = true;
            StartReceiving();
        }
        catch (Exception e)
        {
            Debug.LogError($"WebSocket connection error: {e.Message}");
            Cleanup();
            await Task.Delay(5000); // Wait before retrying
            await ConnectToServer(); // Retry connection
        }
        finally
        {
            isConnecting = false;
        }
    }

    private void Cleanup()
    {
        if (webSocket != null)
        {
            try
            {
                if (webSocket.State == WebSocketState.Open)
                {
                    webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None).Wait();
                }
                webSocket.Dispose();
            }
            catch (Exception e)
            {
                Debug.LogError($"Error during cleanup: {e.Message}");
            }
            webSocket = null;
        }
        isConnected = false;
    }

    private async Task SendText(string message, string type = "")
    {
        if (webSocket == null || webSocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("Cannot send message - WebSocket is not connected");
            return;
        }
        {
            var messageObj = new SendTextFormat { 
                message = message,
                type = type
            };
            string jsonMessage = JsonUtility.ToJson(messageObj);
            byte[] buffer = Encoding.UTF8.GetBytes(jsonMessage);
            await webSocket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Text, true, CancellationToken.None);
            Debug.Log($"Message sent: {jsonMessage}");
        }
    }

    private async void StartReceiving()
    {
        if (webSocket == null || webSocket.State != WebSocketState.Open)
        {
            Debug.LogWarning("Cannot start receiving - WebSocket is not connected");
            return;
        }

        byte[] buffer = new byte[4096]; // Increased buffer size
        while (webSocket != null && webSocket.State == WebSocketState.Open)
        {
            try
            {
                WebSocketReceiveResult result = await webSocket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    CancellationToken.None);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    Debug.Log("Server requested connection close");
                    await webSocket.CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Closing",
                        CancellationToken.None);
                    break;
                }

                string jsonMessage = Encoding.UTF8.GetString(buffer, 0, result.Count);
                try
                {
                    var messageObj = JsonUtility.FromJson<ReceiveMessageFormat>(jsonMessage);
                    if (messageObj != null)
                    {
                        if (!string.IsNullOrEmpty(messageObj.content)){
                            if (messageObj.action == "Status"){
                                Debug.Log($"Status message received: {messageObj.content}");
                                if (messageObj.content == "Recording"){
                                    GlobalVariables.on_recording = true; // 録音中
                                }else if (messageObj.content == "Finished"){
                                    GlobalVariables.on_recording = false; // 録音終了
                                }
                            }else{
                                Debug.Log($"Message received: {messageObj.content}");
                                GlobalVariables.AgentQueue.Add(messageObj);
                                // メッセージ受信をRecordingButtonに通知
                                if (recordingButton != null)
                                {
                                    recordingButton.OnMessageReceived();
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Error parsing JSON message: {e.Message}");
                }
            }
            catch (WebSocketException e)
            {
                Debug.LogError($"WebSocket error: {e.Message}");
                await HandleDisconnection();
                break;
            }
            catch (Exception e)
            {
                Debug.LogError($"Error receiving message: {e.Message}");
                await HandleDisconnection();
                break;
            }
        }
    }

    private async Task HandleDisconnection()
    {
        Cleanup();
        if (!isQuitting)
        {
            Debug.Log("Attempting to reconnect...");
            await Task.Delay(3000); // Wait before reconnecting
            await ConnectToServer();
        }
        else
        {
            Debug.Log("Application is quitting - skipping reconnection");
        }
    }

    private void OnApplicationQuit()
    {
        Debug.Log("Application quitting - cleaning up WebSocket connection");
        isQuitting = true;
        Cleanup();
    }
}
