# api.py
# FastAPIによるWebSocketエンドポイント雛形

import asyncio # 追加
from fastapi import FastAPI, WebSocket, WebSocketDisconnect
from fastapi.responses import HTMLResponse
import os
import sys
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from src import agent

from dotenv import load_dotenv # 追加

# .env ファイルの読み込み
load_dotenv()

app = FastAPI()

# 環境変数の取得と存在チェック
OPENAI_API_KEY = os.getenv("OPENAI_API_KEY")
OPENAI_WS_URL = "wss://api.openai.com/v1/realtime?model=gpt-4o-mini-realtime-preview-2024-12-17"

if not OPENAI_API_KEY:
    print("エラー: 環境変数 OPENAI_API_KEY が設定されていません。")

@app.websocket("/ws/recording")
async def websocket_recording(websocket: WebSocket):
    await websocket.accept() # WebSocket接続を受け入れる
    recording_task = None
    stop_event = asyncio.Event()  # 停止フラグ用のEventオブジェクト
    try:
        while True:
            json_data = await websocket.receive_json()  # JSONとしてメッセージを受信
            msg_type = json_data.get('type', '')

            # メッセージの処理
            if msg_type == "start_recording":
                if recording_task is None:
                    print("録音開始リクエスト受信")
                    recording_task = asyncio.create_task(
                        agent.stream_audio_to_openai(
                            api_url=OPENAI_WS_URL,
                            api_key=OPENAI_API_KEY,
                            unity_ws=websocket,
                            stop_event=stop_event
                        )
                    )
                else:
                    print("録音タスクは既に実行中です")
            elif msg_type == "stop_recording":
                if recording_task is not None:
                    print("録音停止リクエスト受信")
                    stop_event.set()  # 停止フラグを設定
                else:
                    print("録音タスクは実行されていません")
            else:
                # 不明なメッセージタイプの場合は無視して接続を維持
                print(f"不明なメッセージを無視: {json_data}")
                continue  # 次のメッセージを待機

    except WebSocketDisconnect:
        print("WebSocket接続が切断されました")
        if recording_task and not recording_task.done():
            print("実行中の録音タスクをキャンセルします")
            recording_task.cancel()
            try:
                await recording_task # キャンセルが完了するのを待つ
            except asyncio.CancelledError:
                print("録音タスクは正常にキャンセルされました")
    except Exception as e:
        print(f"WebSocketエラー: {e}")
        if recording_task and not recording_task.done():
            recording_task.cancel() # エラー時もタスクをキャンセル
    finally:
        print("WebSocketハンドラ終了")
