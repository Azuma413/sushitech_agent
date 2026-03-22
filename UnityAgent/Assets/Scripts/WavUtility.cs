using UnityEngine;
using System;

public static class WavUtility
{
    public static AudioClip ToAudioClip(byte[] wavData)
    {
        try
        {
            if (wavData == null || wavData.Length < 44)
            {
                Debug.LogError($"無効なWAVデータです: {(wavData == null ? "null" : $"length={wavData.Length}")}");
                return null;
            }

            // WAVヘッダーのパース
            // RIFFヘッダーの確認
            if (wavData[0] != 'R' || wavData[1] != 'I' || wavData[2] != 'F' || wavData[3] != 'F')
            {
                Debug.LogError("RIFFヘッダーが見つかりません");
                return null;
            }

            // WAVEフォーマットの確認
            if (wavData[8] != 'W' || wavData[9] != 'A' || wavData[10] != 'V' || wavData[11] != 'E')
            {
                Debug.LogError("WAVEフォーマットではありません");
                return null;
            }

            // fmt チャンクの確認
            if (wavData[12] != 'f' || wavData[13] != 'm' || wavData[14] != 't' || wavData[15] != ' ')
            {
                Debug.LogError("fmtチャンクが見つかりません");
                return null;
            }

            // フォーマット情報の読み取り
            int channels = BitConverter.ToInt16(wavData, 22);
            int sampleRate = BitConverter.ToInt32(wavData, 24);
            int bitsPerSample = BitConverter.ToInt16(wavData, 34);

            Debug.Log($"WAVフォーマット: チャンネル数={channels}, サンプリングレート={sampleRate}Hz, ビット深度={bitsPerSample}bit");

            // dataチャンクの検索
            int dataOffset = 44; // 標準的なオフセット
            while (dataOffset < wavData.Length - 8)
            {
                if (wavData[dataOffset] == 'd' && wavData[dataOffset + 1] == 'a' && 
                    wavData[dataOffset + 2] == 't' && wavData[dataOffset + 3] == 'a')
                {
                    break;
                }
                dataOffset++;
            }

            if (dataOffset >= wavData.Length - 8)
            {
                Debug.LogError("dataチャンクが見つかりません");
                return null;
            }

            // データサイズの取得
            int dataSize = BitConverter.ToInt32(wavData, dataOffset + 4);
            int dataStart = dataOffset + 8;

            if (dataStart + dataSize > wavData.Length)
            {
                Debug.LogError($"データサイズが不正です: {dataSize} bytes (残りデータ: {wavData.Length - dataStart} bytes)");
                return null;
            }

            // サンプル数の計算
            int samplesPerChannel = dataSize / (bitsPerSample / 8) / channels;

            // AudioClipを作成
            var audioClip = AudioClip.Create("voice", samplesPerChannel, channels, sampleRate, false);

            // 音声データをfloat配列に変換
            var audioData = new float[samplesPerChannel * channels];
            int bytesPerSample = bitsPerSample / 8;

            for (int i = 0; i < samplesPerChannel * channels; i++)
            {
                switch (bitsPerSample)
                {
                    case 32:
                        // 32bitの場合はfloat
                        float sample32 = BitConverter.ToSingle(wavData, dataStart + i * bytesPerSample);
                        audioData[i] = Mathf.Clamp(sample32, -1f, 1f);
                        break;

                    case 16:
                        // 16bitデータをfloatに変換 (-1.0f to 1.0f)
                        short sample16 = BitConverter.ToInt16(wavData, dataStart + i * bytesPerSample);
                        audioData[i] = sample16 / 32768f;
                        break;

                    case 8:
                        // 8bitの場合は unsigned
                        audioData[i] = (wavData[dataStart + i] - 128) / 128f;
                        break;

                    default:
                        Debug.LogError($"未対応のビット深度です: {bitsPerSample}bit");
                        return null;
                }
            }

            audioClip.SetData(audioData, 0);
            Debug.Log($"AudioClip作成成功: 長さ={audioClip.length}秒");
            return audioClip;
        }
        catch (Exception e)
        {
            Debug.LogError($"WAVデータの変換中にエラーが発生しました: {e.Message}\n{e.StackTrace}");
            return null;
        }
    }
}
