using UnityEngine;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using System;
using System.Text;
using System.Net.Http;
using System.Net.Http.Headers;

public class QuQu : MonoBehaviour
{
    [Header("コンポーネント設定")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private SkinnedMeshRenderer faceMR;
    [SerializeField] private Animator animator;
    [SerializeField] private Telop telop;

    [Header("にじボイス設定")]
    [SerializeField] private string apiKey = "";  // にじボイスのAPIキー
    [SerializeField] private string voiceId = "8c08fd5b-b3eb-4294-b102-a1da00f09c72";  // にじボイスのキャラクターID
    [SerializeField] private float speedScale = 0.9f;
    [SerializeField] private float emotionalLevel = 0.1f;
    [SerializeField] private float soundDuration = 0.1f;
    private const string API_BASE_URL = "https://api.nijivoice.com/api/platform/v1";

    [Header("テロップ設定")]
    [SerializeField] private float characterInterval = 0.1f;
    [SerializeField] private float displayDuration = 2f;

    protected List<ReceiveMessageFormat> AgentQueue => GlobalVariables.AgentQueue;

    private void Start()
    {
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }

        if (telop == null)
        {
            telop = FindObjectOfType<Telop>();
            if (telop == null)
            {
                Debug.LogError("Telop component not found in the scene!");
            }
        }
    }

    private void Update()
    {
        // メッセージキューの処理
        if (AgentQueue.Count > 0 && GlobalVariables.VoiceState == 0)
        {
            GlobalVariables.VoiceState = 1; // 音声合成中
            var message = AgentQueue[0];
            AgentQueue.RemoveAt(0);
            HandleAction(message.action);
            Text2VoiceAsync(
                message.content,
                message.emotion,
                message.action
            ).Forget();
        }
    }

    // HTTPクライアントの静的インスタンス
    private static readonly HttpClient client = new HttpClient();

    [System.Serializable]
    private class VoiceRequest
    {
        public string format = "wav";
        public string script;
        public string speed;
        public string emotionalLevel;
        public string soundDuration;
    }

    [System.Serializable]
    private class GeneratedVoice
    {
        public string audioFileUrl;
        public string audioFileDownloadUrl;
        public int duration;
        public int remainingCredits;
    }

    [System.Serializable]
    private class VoiceResponse
    {
        public GeneratedVoice generatedVoice;
    }

    private async UniTask Text2VoiceAsync(string text, string emotion, string action)
    {
        try
        {
            if (string.IsNullOrEmpty(apiKey))
            {
                Debug.LogError("にじボイスのAPIキーが設定されていません");
                return;
            }

            if (action != "Think" && action != "WebSearch")
            {
                ApplyEmotion(emotion);
            }

            // リクエストの構築
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Post,
                RequestUri = new Uri($"{API_BASE_URL}/voice-actors/{voiceId}/generate-voice")
            };

            // ヘッダーの設定
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            request.Headers.Add("x-api-key", apiKey);

            // リクエストボディの構築
            var voiceRequest = new VoiceRequest
            {
                script = text,
                speed = speedScale.ToString(),
                emotionalLevel = emotionalLevel.ToString(),
                soundDuration = soundDuration.ToString()
            };

            var jsonContent = JsonUtility.ToJson(voiceRequest);
            request.Content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

            // 音声生成リクエストの送信
            GlobalVariables.VoiceState = 1; // 音声合成中

            using (var response = await client.SendAsync(request))
            {
                response.EnsureSuccessStatusCode();
                var body = await response.Content.ReadAsStringAsync();
                Debug.Log($"にじボイスAPIレスポンス: {body}");

                // レスポンスJSONのパース
                var voiceResponse = JsonUtility.FromJson<VoiceResponse>(body);
                if (voiceResponse?.generatedVoice?.audioFileDownloadUrl == null)
                {
                    throw new Exception("音声ファイルのダウンロードURLが取得できませんでした");
                }

                Debug.Log($"音声ファイルのダウンロードを開始します: {voiceResponse.generatedVoice.audioFileDownloadUrl}");

                // 音声ファイルのダウンロード
                using (var audioResponse = await client.GetAsync(voiceResponse.generatedVoice.audioFileDownloadUrl))
                {
                    audioResponse.EnsureSuccessStatusCode();
                    var audioData = await audioResponse.Content.ReadAsByteArrayAsync();
                    
                    try
                    {
                        // 音声データの取得と再生
                        GlobalVariables.VoiceState = 2; // 音声出力中
                        
                        // WAVデータの解析
                        Debug.Log($"音声データの長さ: {audioData.Length} bytes");
                        if (audioData.Length < 44)
                        {
                            throw new Exception($"不正なWAVデータです。データ長: {audioData.Length} bytes");
                        }

                        var audioClip = WavUtility.ToAudioClip(audioData);
                        // テロップ表示
                        if (telop != null)
                        {
                            Color textColor = Color.HSVToRGB(0.08f, 1.0f, 0.5f);
                            telop.Display(text, textColor, characterInterval, displayDuration).Forget();
                        }
                        if (audioClip != null)
                        {
                            Debug.Log($"AudioClipの設定: 長さ={audioClip.length}秒, チャンネル数={audioClip.channels}, 周波数={audioClip.frequency}Hz");
                            audioSource.clip = audioClip;
                            audioSource.volume = 1.0f;
                            audioSource.spatialBlend = 0f; // 2Dサウンドとして再生
                            audioSource.Play();
                            Debug.Log("音声の再生を開始しました");
                            
                            // 音声の長さだけ待機
                            await UniTask.WaitWhile(() => audioSource.isPlaying);
                        }
                        else
                        {
                            throw new Exception("音声データの変換に失敗しました");
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"音声データの処理中にエラーが発生しました: {e.Message}\nStackTrace: {e.StackTrace}");
                        throw;
                    }
                }
            }
        }
        catch (HttpRequestException e)
        {
            Debug.LogError($"にじボイスAPIリクエストエラー: {e.Message}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Text2Voiceエラー: {e.Message}");
        }
        finally
        {
            Debug.Log("音声出力が終了しました");
            ResetEmotion();
            GlobalVariables.VoiceState = 0;
        }
    }

    private void HandleAction(string action)
    {
        switch (action)
        {
            case "Think":
                animator.SetBool("QuQuIsThinking", true);
                animator.SetBool("QuQuIsSearching", false);
                break;
            case "WebSearch":
                animator.SetBool("QuQuIsSearching", true);
                animator.SetBool("QuQuIsThinking", false);
                break;
            case "Nothing":
                animator.SetBool("QuQuIsThinking", false);
                animator.SetBool("QuQuIsSearching", false);
                break;
        }
    }

    private void ApplyEmotion(string emotion)
    {
        switch (emotion)
        {
            case "normal":
                Debug.Log("QuQuEmotionIdx: normal");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.normal);
                break;
            case "happy":
                Debug.Log("QuQuEmotionIdx: happy");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.happy);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.warai, 100f);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.nikori, 100f);
                break;
            case "angry":
                Debug.Log("QuQuEmotionIdx: angry");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.angry);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.okori, 100f);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.niramu, 100f);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.high_light_off, 100f);
                break;
            case "sad":
                Debug.Log("QuQuEmotionIdx: sad");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.sad);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.komaru, 100f);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.mayu_sita, 60f);
                break;
            case "surprised":
                Debug.Log("QuQuEmotionIdx: surprised");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.surprised);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.bikkuri, 50f);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.hitomi_small, 40f);
                break;
            case "shy":
                Debug.Log("QuQuEmotionIdx: shy");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.shy);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.hohozome, 100f);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.komaru, 70f);
                break;
            case "excited":
                Debug.Log("QuQuEmotionIdx: excited");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.excited);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.star, 100f);
                break;
            case "smug":
                Debug.Log("QuQuEmotionIdx: smug");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.smug);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.okori, 50f);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.zitome, 80f);
                break;
            case "calm":
                Debug.Log("QuQuEmotionIdx: calm");
                animator.SetInteger("QuQuEmotionIdx", (int)Emotion.calm);
                faceMR.SetBlendShapeWeight((int)QuQuMorph.nagomi, 15f);
                break;
        }
    }

    private void ResetEmotion()
    {
        faceMR.SetBlendShapeWeight((int)QuQuMorph.warai, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.nikori, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.okori, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.niramu, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.high_light_off, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.komaru, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.mayu_sita, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.bikkuri, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.hitomi_small, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.hohozome, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.star, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.zitome, 0f);
        faceMR.SetBlendShapeWeight((int)QuQuMorph.nagomi, 0f);
        animator.SetTrigger("QuQuFinishTalk");
        Debug.Log("QuQuの発話が終了しました");
    }
}
