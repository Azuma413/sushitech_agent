# 進捗状況（progress.md）

## 現在動作しているもの
- メモリバンクのコアファイル
- `src/api.py`: 
    - UnityからのWebSocket接続 (`/ws/recording`) を受け付け。
    - `start_recording` メッセージで録音を開始。
    - `stop_recording` メッセージで録音を停止。
    - WebSocket切断時のエラーハンドリング。
- `src/agent.py`:
    - `TalkFormat` Pydanticモデル定義。
    - `ConversationHistory` クラスによる会話履歴管理。
    - `stream_audio_to_openai` 関数:
        - マイク音声のリアルタイム録音と Realtime API へのストリーミング送信。
        - Realtime API の Function Calling を利用した初期 `TalkFormat` 生成。
        - `action` が `Nothing` の場合は初期 `TalkFormat` を送信。
        - `action` が `Think`/`WebSearch` の場合、AssistModelとTextTalkModelによる2段階処理を実行。
        - 会話履歴を考慮した応答生成。
        - エラーハンドリングの強化。
- `src/prompt.py`:
    - AssistModel用とTextTalkModel用のプロンプトテンプレート定義。
    - `PydanticOutputParser` によるレスポンス構造の標準化。

## 今後の作業予定
- WebSocket接続エラー時のリカバリ機能の追加。
- 会話履歴の永続化機能の実装。
- ロギング機能の強化。

## 既知の課題
- WebSocket接続エラー時のリカバリ処理が不十分。
- 会話履歴は現在メモリ内のみで保持（永続化が必要）。
- エラーハンドリングのさらなる強化が必要。
