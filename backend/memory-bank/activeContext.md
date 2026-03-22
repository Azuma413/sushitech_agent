# アクティブ文脈（activeContext.md）

## 現在の作業
会話履歴の永続化機能の実装および関連エラーハンドリングの強化。

## 最近の変更
- `src/agent.py`:
    - `stream_audio_to_openai` 関数に停止機能を実装。
    - 停止フラグによる録音制御を追加。
    - 停止時のクリーンアップ処理を実装。
    - Unityへの停止通知機能を追加。
- `src/api.py`:
    - WebSocketエンドポイントを `/ws/recording` に変更。
    - `stop_recording` メッセージ受信時の処理を実装。
    - 停止フラグ（`asyncio.Event`）による制御を追加。
- (以前の変更) `src/agent.py`:
    - `_create_react_agent` ヘルパー関数を追加し、LangChain の ReAct Agent (AssistModel) を実装。DuckDuckGoSearchRun をツールとして利用。
    - `_generate_final_response` 非同期ヘルパー関数を追加し、AssistModel の結果を受けて最終的な `TalkFormat` を生成する TextTalkModel を実装。PydanticOutputParser を利用。
    - `stream_audio_to_openai` 関数内のダミー処理を、上記ヘルパー関数呼び出しに置き換え。
- (以前の変更) `src/api.py`:
    - `/ws/start-recording` エンドポイントを更新:
        - `stream_audio_to_openai` を非同期タスクとして実行するように変更。
        - 環境変数からAPIキー/URLを読み込む処理を追加。
        - WebSocket切断時のタスクキャンセル処理を追加。

## 次のステップ
- 会話履歴の永続化機能の実装。
- WebSocket接続エラー時のリカバリ機能の実装。
- ロギング機能の強化。
- 進捗・設計ドキュメントの随時更新。
