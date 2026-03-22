# プロジェクト概要（projectBrief.md）

## プロジェクト名
Unity連携型STS（Speech to Speech）エージェント バックエンド

## 目的
Unityアプリケーションと連携し、ユーザーの音声・テキスト入力をリアルタイムで処理し、感情やツール制御情報を含む構造化応答を返すエージェントのバックエンドを実装する。OpenAI Realtime APIを活用し、自然な対話体験と柔軟な拡張性を両立することを目指す。

## プロジェクトの特徴
- Unityからのリクエストを受け、音声＋テキストプロンプトをOpenAI Realtime APIへ送信。また、過去の会話履歴もテキスト形式で渡す。この時、前回の応答が完了した時点から、一定時間以上経過している場合は会話履歴を削除する。
- OpenAI Realtime API の **Function Calling 機能** を活用し、応答内容 (`reply`)、次の行動 (`action`: Nothing, Think, WebSearch)、感情 (`emotion`) を含む構造化応答 (`TalkFormat`) を生成。
- `action` が `Think` または `WebSearch` の場合、**AssistModel (GPT-4.1 mini)** による追加処理（思考・検索など）を実行。
- AssistModel の結果を基に、**TextTalkModel** が最終的な応答 (`reply`)、次の行動 (`action`)、感情 (`emotion`) を再度生成する **2段階処理** を実施。
- すべての応答 (`TalkFormat`) はFastAPIのWebSocket経由でUnity側にJSON形式で返却。
- AssistModelはLangChainのReAct Agentを利用して作成する（`src/old/ai_tuber.py`を参照）。
- 全体のフロー管理にはLangChainおよびLangGraphは使用しないので注意

## 要件・ゴール
- FastAPIによるWebSocketエンドポイント (`/ws/start-recording`) の提供。
- Unityからの `start_recording` 指示に基づき、Python (`sounddevice`) でマイク音声を取得し、OpenAI Realtime API へストリーミング送信。
- Realtime API の **Function Calling** を利用して、初期の構造化応答 (`TalkFormat`) を示す **関数引数** を受信。
- 受信した引数を `TalkFormat` オブジェクトにパース。
- `TalkFormat.action` が `Nothing` の場合は、その `TalkFormat` をUnityへ送信。
- `TalkFormat.action` が `Think` または `WebSearch` の場合は、**AssistModel** を呼び出して追加処理を実行し、その結果を基に **TextTalkModel** で最終的な `TalkFormat` を生成してUnityへ送信（**2段階処理**）。
- 実装はシンプルかつ拡張性を意識する。
- 旧実装（`src/old/`）は参考用。

## 主な実装ファイル
- `src/api.py` ... FastAPIエンドポイント
- `src/agent.py` ... エージェント処理
- `src/prompt.py` ... プロンプト定義
- `src/old/` ... 旧実装（参考用）
