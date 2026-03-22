import sys
import os
from pathlib import Path
import json

# srcディレクトリをPythonパスに追加
sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))

from dotenv import load_dotenv

# 環境変数を.envファイルから読み込み
load_dotenv()

import pytest
import asyncio
from fastapi.testclient import TestClient
from fastapi.websockets import WebSocketDisconnect
from src.api import app

# Unityから送信されるメッセージを保存するクラス
class UnityMessageLogger:
    def __init__(self):
        self.messages = []

    async def log_message(self, message):
        # messageは既にJSONオブジェクトとして受信済み
        self.messages.append(message)
        print(f"受信メッセージ: {message}")

# テストケース
@pytest.mark.asyncio
async def test_recording_flow():
    """録音の開始から停止までの一連のフローをテスト"""
    # Unityメッセージのロガーを準備
    unity_logger = UnityMessageLogger()

    with TestClient(app).websocket_connect("/ws/recording") as websocket:
        try:
            # 録音開始
            print("\n録音開始コマンド送信...")
            start_msg = {
                "type": "start_recording"
            }
            websocket.send_json(start_msg)
            
            # 録音タスクの開始を確認
            response = websocket.receive_json()
            assert response.get("action") == "Status" and response.get("content") == "Recording", "録音開始の応答が不正です"
            
            # 少し待機してOpenAIからの応答を受信（実際の音声入力とAPI応答を待つ）
            print("音声入力とOpenAI APIからの応答を待機中...")
            start_time = asyncio.get_event_loop().time()
            
            # メッセージの受信とログ記録
            while asyncio.get_event_loop().time() - start_time < 10:  # 10秒
                try:
                    # メッセージ受信にタイムアウトを設定
                    message = await asyncio.wait_for(
                        asyncio.to_thread(websocket.receive_json),
                        timeout=10.0
                    )
                    await unity_logger.log_message(message)
                except asyncio.TimeoutError:
                    print("メッセージ待機タイムアウト")
                    break
                except WebSocketDisconnect:
                    print("WebSocket切断")
                    break
                except Exception as e:
                    print(f"メッセージ受信エラー: {e}")
                    break
            # 録音停止
            print("録音停止コマンド送信...")
            stop_msg = {
                "type": "stop_recording"
            }
            websocket.send_json(stop_msg)
            try:
                response = await asyncio.wait_for(
                    asyncio.to_thread(websocket.receive_json),
                    timeout=2.0
                )
                assert response.get("action") == "Status" and response.get("content") == "Finished", "録音停止の応答が不正です"
            except asyncio.TimeoutError:
                print("録音停止応答のタイムアウト")
            
            # 残りのメッセージを受信（タイムアウト付き）
            end_time = asyncio.get_event_loop().time() + 3.0  # 3秒間だけ待機
            while asyncio.get_event_loop().time() < end_time:
                try:
                    message = await asyncio.wait_for(
                        asyncio.to_thread(websocket.receive_json),
                        timeout=1.0
                    )
                    await unity_logger.log_message(message)
                except asyncio.TimeoutError:
                    print("これ以上のメッセージはありません")
                    break
                except WebSocketDisconnect:
                    print("WebSocket切断")
                    break
                except Exception as e:
                    print(f"最終メッセージ受信エラー: {e}")
                    break
            
            # 受信したメッセージの表示
            print("\n受信したメッセージ一覧:")
            for i, msg in enumerate(unity_logger.messages):
                print(f"メッセージ {i + 1}: {msg}")
            
            print("テスト完了")
            
        except Exception as e:
            print(f"テスト実行中にエラーが発生しました: {e}")
            raise
