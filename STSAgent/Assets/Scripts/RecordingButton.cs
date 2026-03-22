using UnityEngine;
using UnityEngine.UI;
using System;
using System.Threading.Tasks;

public class RecordingButton : MonoBehaviour
{
    [SerializeField] private WebSocketClient webSocketClient;  // WebSocketClientへの参照
    [SerializeField] private Image buttonImage;               // ボタンの画像コンポーネント
    
    private bool isRecording = false;
    private DateTime lastMessageTime;
    
    // ボタンの色の定義
    private readonly Color recordingColor = Color.green;      // 録音中の色
    private readonly Color stopColor = Color.white;           // 停止中の色
    
    // タイムアウトの設定
    private const float TIMEOUT_MINUTES = 1f;
    
    private void Start()
    {
        // ボタンコンポーネントの取得と設定
        var button = GetComponent<Button>();
        if (button == null)
        {
            Debug.LogError("Buttonコンポーネントが見つかりません");
            return;
        }
        // OnClickイベントにOnButtonClick関数を登録
        button.onClick.AddListener(OnButtonClick);

        if (webSocketClient == null)
        {
            Debug.LogError("WebSocketClientが設定されていません");
            return;
        }
        
        if (buttonImage == null)
        {
            buttonImage = GetComponent<Image>();
            if (buttonImage == null)
            {
                Debug.LogError("Imageコンポーネントが見つかりません");
                return;
            }
        }
        
        // 初期状態は停止中
        SetButtonColor(false);
        lastMessageTime = DateTime.Now;
    }
    
    private void Update()
    {
        SetButtonColor(GlobalVariables.on_recording);
        if (isRecording)
        {
            CheckTimeout();
        }
    }
    
    // ボタンがクリックされたときの処理
    public async void OnButtonClick()
    {
        if (!isRecording)
        {
            // 録音開始
            Debug.Log("録音開始");
            await StartRecording();
        }
        else
        {
            // 録音停止
            Debug.Log("録音停止");
            await StopRecording();
        }
    }
    
    // 録音開始処理
    private async Task StartRecording()
    {
        await webSocketClient.StartRecording();
        isRecording = true;
        lastMessageTime = DateTime.Now;
    }
    
    // 録音停止処理
    private async Task StopRecording()
    {
        await webSocketClient.StopRecording();
        isRecording = false;
    }
    
    // メッセージ受信時の処理（WebSocketClientから呼び出される）
    public void OnMessageReceived()
    {
        lastMessageTime = DateTime.Now;
    }
    
    // タイムアウトチェック
    private void CheckTimeout()
    {
        if (DateTime.Now - lastMessageTime > TimeSpan.FromMinutes(TIMEOUT_MINUTES))
        {
            Debug.Log("タイムアウトにより録音を停止します");
            StopRecording().ConfigureAwait(false);  // 非同期処理のエラーを防ぐためConfigureAwait(false)を使用
        }
    }
    
    // ボタンの色を設定
    private void SetButtonColor(bool isRecording)
    {
        buttonImage.color = isRecording ? recordingColor : stopColor;
    }
}
