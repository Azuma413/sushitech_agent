"""
agent.py
音声とテキストを使用したリアルタイム対話エージェントの実装
"""

import asyncio
import base64
import json
import os
import struct
import sys
import traceback # トレースバック出力のため追加
from typing import Dict, List, Literal, Optional

import numpy as np
import sounddevice as sd
import websockets
from dotenv import load_dotenv
from langchain.agents import AgentExecutor, create_react_agent
from langchain.output_parsers import PydanticOutputParser
from langchain.schema.messages import AIMessage, HumanMessage, SystemMessage
from langchain_community.tools.ddg_search import DuckDuckGoSearchRun
from langchain_core.prompts import ChatPromptTemplate, MessagesPlaceholder, PromptTemplate
from langchain_openai import ChatOpenAI
from pydantic import BaseModel, Field

sys.path.append(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from src.prompt import ASSIST_MODEL_PROMPT, TALK_MODEL_PROMPT

# .env ファイルの読み込み
load_dotenv()

class TalkFormat(BaseModel):
    """対話エージェントの応答フォーマット"""
    reply: str = Field(..., description="視聴者に対する返答")
    action: Literal["Nothing", "Think", "WebSearch"] = Field(
        ..., description="次の行動．以下のいずれかから選択: Nothing, Think, WebSearch"
    )
    emotion: Literal[
        "normal", "happy", "angry", "sad", "surprised", "shy", "excited", "smug", "calm"
    ] = Field(..., description="現在の感情")

def float32_to_base64(float32_array: np.ndarray) -> str:
    """float32の音声データをbase64エンコードされたPCM形式に変換"""
    # 値を-1.0から1.0の範囲にクリップ
    clipped = np.clip(float32_array, -1.0, 1.0)
    # int16に変換 (-32768 to 32767)
    pcm16 = (clipped * 32767).astype(np.int16)
    # バイト列に変換
    pcm_bytes = pcm16.tobytes()
    # base64エンコード
    return base64.b64encode(pcm_bytes).decode('ascii')

class ConversationHistory:
    """会話履歴を管理するクラス"""
    def __init__(self):
        self.history: List[Dict[str, str]] = []

    def add_message(self, role: str, content: str):
        """メッセージを履歴に追加"""
        self.history.append({"role": role, "content": content})

    def get_messages(self, last_n: Optional[int] = None) -> List[Dict[str, str]]:
        """会話履歴を取得（オプションで最新のn件のみ）"""
        if last_n:
            return self.history[-last_n:]
        return self.history

# グローバルな会話履歴インスタンス
conversation_history = ConversationHistory()
output_parser = PydanticOutputParser(pydantic_object=TalkFormat)

def _create_react_agent() -> AgentExecutor:
    """ReAct Agent Executorを作成"""
    llm = ChatOpenAI(model="gpt-4.1-mini", temperature=0)
    tools = [DuckDuckGoSearchRun()]
    react_prompt = PromptTemplate.from_template(ASSIST_MODEL_PROMPT)
    agent = create_react_agent(llm, tools, react_prompt)
    return AgentExecutor(agent=agent, tools=tools, verbose=True)

async def _text_talk_model_response(assist_output: str) -> TalkFormat:
    """
    AssistModelの出力から会話履歴を考慮して最終的な応答を生成
    """
    llm = ChatOpenAI(model="gpt-4.1-mini", temperature=0).with_structured_output(TalkFormat)
    
    # 会話履歴の最新5件を取得してメッセージ形式に変換
    recent_history = conversation_history.get_messages(last_n=5)
    history_messages = [
        HumanMessage(content=msg["content"]) if msg["role"] == "user" 
        else AIMessage(content=msg["content"]) 
        for msg in recent_history
    ]
    
    prompt = ChatPromptTemplate.from_messages([
        SystemMessage(content=TALK_MODEL_PROMPT),
        MessagesPlaceholder(variable_name="history"),
        HumanMessage(content=assist_output)
    ])
    chain = prompt | llm | output_parser
    response = await chain.ainvoke({"history": history_messages})
    conversation_history.add_message("assistant", response.reply)
    return response

async def send_unity_message(unity_ws, content: str, action: str, emotion: str):
    """Unityにメッセージを送信"""
    message = json.dumps({
        "content": content,
        "action": action,
        "emotion": emotion,
    })
    await unity_ws.send_text(message)
    print(f"Unityへ送信: {message}")

async def handle_model_response(response_data: Dict, unity_ws, accumulated_arguments: str = "") -> None:
    """モデルからの応答を処理"""
    output_items = response_data.get("output", [])
    if not output_items or output_items[0].get("type") != "function_call":
        return

    function_call_info = output_items[0]
    if function_call_info.get("name") != "generate_structured_response":
        return

    final_arguments_str = accumulated_arguments or function_call_info.get("arguments", "{}")
    try:
        args_dict = json.loads(final_arguments_str)
        initial_response = TalkFormat(**args_dict)

        # ユーザーの発話を履歴に追加
        if response_data.get("text"):
            conversation_history.add_message("user", response_data["text"])

        # モデルの応答を履歴に追加
        conversation_history.add_message("assistant", initial_response.reply)

        # 応答をUnityに送信
        if unity_ws:
            await send_unity_message(
                unity_ws,
                content=initial_response.reply,
                action=initial_response.action,
                emotion=initial_response.emotion
            )

        # 追加アクションの処理
        current_response = initial_response
        while current_response.action != "Nothing":
            try:
                print(f"Action: {current_response.action} - 追加処理開始")
                agent_executor = _create_react_agent()
                assist_result = await agent_executor.ainvoke({"input": current_response.reply})
                assist_output = assist_result.get("output", "思考/検索結果なし")
                conversation_history.add_message("user", assist_output)

                next_response = await _text_talk_model_response(assist_output)
                if unity_ws:
                    await send_unity_message(
                        unity_ws,
                        content=next_response.reply,
                        action=next_response.action,
                        emotion=next_response.emotion
                    )
                current_response = next_response

            except Exception as e:
                print(f"追加処理中のエラー: {e}")
                if unity_ws:
                    await send_unity_message(
                        unity_ws=unity_ws,
                        content="すみません、処理中にエラーが発生しました。",
                        action="Nothing",
                        emotion="sad"
                    )
                break

    except Exception as e:
        print(f"応答処理中のエラー: {e}")

async def stream_audio_to_openai(
    api_url: str,
    api_key: str,
    sample_rate: int = 16000,
    chunk_duration: float = 0.04,
    unity_ws = None,
    stop_event = None
):
    """マイクから音声を録音し、OpenAI Realtime APIへストリーミング"""
    
    # セッション設定
    session_config = {
        # OpenAI Realtime API仕様に厳密に合わせる
        "modalities": ["text", "audio"],
        "instructions": TALK_MODEL_PROMPT,
        "tools": [{
            "type": "function",
            "name": "generate_structured_response",
            "description": "ユーザーの発話内容に基づいて構造化された応答を生成",
            "parameters": TalkFormat.model_json_schema()
        }],
        "tool_choice": "required",
        "input_audio_format": "pcm16",
        "output_audio_format": "pcm16",
        "turn_detection": {
            "type": "server_vad",
            "threshold": 0.5,
            "prefix_padding_ms": 500,
            "silence_duration_ms": 200
        }
    }

    # WebSocket接続とセッション管理
    ws = None
    audio_queue = asyncio.Queue()
    websocket_ready_event = asyncio.Event() # WebSocket準備完了イベント
    speech_active = False # speech_active と last_speech_time は現在未使用のため、削除も検討
    last_speech_time = None

    try:
        # WebSocket接続を確立
        ws = await websockets.connect(
            api_url,
            extra_headers={
                "Authorization": f"Bearer {api_key}",
                "OpenAI-Beta": "realtime=v1"
            }
        )
        print("WebSocket接続確立") # インデント修正

        # セッション初期化 (session.update)
        await ws.send(json.dumps({
            "type": "session.update",
            "session": session_config
        }))
        print("セッション更新リクエスト送信")

        # システムプロンプトの送信は session.created/updated 受信後に移動

        def audio_callback(indata, frames, time, status):
            if status:
                print(f"音声入力ステータス: {status}")
                return
            try:
                audio_queue.put_nowait(indata.copy())
            except asyncio.QueueFull:
                # キューがフルの場合、少し待ってリトライするか、古いデータを破棄するなどの戦略も考えられる
                print("音声キューがフル。古いデータが失われる可能性があります。")
            except Exception as e:
                print(f"audio_callback内でエラー発生: {e}")
                print(traceback.format_exc()) # トレースバックを出力

        # 音声送信タスク
        async def send_audio():
            # nonlocal speech_active, last_speech_time # 未使用のためコメントアウト
            try:
                # WebSocketが準備完了するまで待機
                print("音声送信タスク: WebSocket準備完了待機中...")
                await websocket_ready_event.wait()
                print("音声送信タスク: WebSocket準備完了、音声送信開始")

                # ループ条件に ws.open を追加
                while not stop_event.is_set() and ws and ws.open:
                    try:
                        # キューから音声チャンクを取得 (タイムアウトを短く設定)
                        audio_chunk = await asyncio.wait_for(audio_queue.get(), 0.1)

                        # 音声データをbase64エンコード
                        base64_audio = float32_to_base64(audio_chunk.flatten())

                        # 音声バッファにデータを追加
                        await ws.send(json.dumps({
                            "type": "input_audio_buffer.append",
                            "audio": base64_audio
                        }))
                        speech_active = True
                        last_speech_time = asyncio.get_event_loop().time()

                    except asyncio.TimeoutError:
                        # タイムアウト時のコミットロジックを削除 (VADに依存)
                        # 必要であれば、ここで無音状態が一定時間続いた場合の処理を追加可能
                        continue
                    except Exception as e:
                        print(f"音声送信エラー: {e}")
                        print(traceback.format_exc()) # トレースバックを出力
                        # WebSocketが閉じている場合はループを抜ける
                        if not (ws and ws.open):
                            break
                        await asyncio.sleep(0.01) # 短い待機

            finally:
                print("音声送信タスク終了")

        # 応答受信タスク
        async def receive_responses():
            accumulated_arguments = ""
            try:
                # ループ条件に ws.open を追加 (既に存在していたが明示)
                while not stop_event.is_set() and ws and ws.open:
                    try:
                        # タイムアウトを少し長くして安定性を試す (例: 1.0秒)
                        msg_str = await asyncio.wait_for(ws.recv(), 1.0)
                        msg = json.loads(msg_str)

                        # 各種イベントの処理
                        event_type = msg.get("type", "")
                        # print(f"受信イベント: {event_type}") # デバッグ用

                        if event_type == "session.created" or event_type == "session.updated":
                            print(f"セッション準備完了イベント受信: {event_type}")
                            # システムプロンプトを送信
                            print("システムプロンプト送信...")
                            await ws.send(json.dumps({
                                "type": "conversation.item.create",
                                "item": {
                                    "type": "message",
                                    "role": "system",
                                    "content": [{"type": "input_text", "text": TALK_MODEL_PROMPT}]
                                }
                            }))
                            websocket_ready_event.set() # WebSocket準備完了を通知

                        elif event_type == "input_audio_buffer.speech_started":
                            print("音声検出開始")

                        elif event_type == "input_audio_buffer.speech_stopped":
                            print("音声検出終了")

                        elif event_type == "input_audio_buffer.committed": # VADによるコミットを確認
                            print("音声入力コミット (VAD)")

                        elif event_type == "response.function_call_arguments.delta":
                            accumulated_arguments += msg.get("delta", "")

                        elif event_type == "response.done":
                            print("応答完了 (response.done)")
                            await handle_model_response(
                                msg.get("response", {}),
                                unity_ws,
                                accumulated_arguments
                            )
                            accumulated_arguments = "" # 次の応答のためにクリア
                            # response.done を受信しても接続は維持されるため、ループは継続する

                        # 他のイベントタイプも必要に応じてログ出力や処理を追加可能
                        # elif event_type == "response.text.delta":
                        #     print(f"テキスト差分: {msg.get('delta', '')}")
                        # elif event_type == "response.audio.delta":
                        #     print("音声差分受信")
                        # elif event_type == "response.audio_transcript.delta":
                        #     print(f"文字起こし差分: {msg.get('delta', '')}")

                    except asyncio.TimeoutError:
                        # タイムアウトは正常な動作の一部 (データがない場合)
                        continue
                    except Exception as e:
                        print(f"応答受信エラー: {e}")
                        print(traceback.format_exc()) # トレースバックを出力
                        if not (ws and ws.open):
                            break
                        await asyncio.sleep(0.1)
            finally:
                print("応答受信タスク終了")

        # 音声入力ストリームを開始
        with sd.InputStream(
            samplerate=sample_rate,
            channels=1,
            dtype=np.float32,
            callback=audio_callback,
            blocksize=int(sample_rate * chunk_duration)
        ):
            print("音声入力開始")
            if unity_ws:
                await send_unity_message(
                    unity_ws=unity_ws,
                    content="Recording",
                    action="Status",
                    emotion="normal"
                )

            # タスクを開始して監視
            send_task = asyncio.create_task(send_audio(), name="SendAudioTask")
            receive_task = asyncio.create_task(receive_responses(), name="ReceiveResponsesTask")
            tasks = {send_task, receive_task}

            # stop_eventが設定されるか、いずれかのタスクが終了するまで待機
            done, pending = await asyncio.wait(
                tasks | {asyncio.create_task(stop_event.wait(), name="StopEventWatcher")},
                return_when=asyncio.FIRST_COMPLETED
            )

            # どのタスクが完了したかを確認
            for task in done:
                task_name = task.get_name()
                if task_name == "StopEventWatcher":
                    print("stop_eventが設定されました。")
                else:
                    # send_task または receive_task が完了した場合
                    print(f"タスク '{task_name}' が予期せず完了しました。")
                    try:
                        # 例外が発生していればここで再発生させる
                        task.result()
                    except asyncio.CancelledError:
                        print(f"タスク '{task_name}' はキャンセルされました。")
                    except Exception as e:
                        print(f"タスク '{task_name}' で例外が発生しました: {e}")
                        print(traceback.format_exc())

            # stop_eventが設定されていない場合（つまり、タスクが早期終了した場合）
            if not stop_event.is_set():
                 print("タスクが早期終了したため、stop_eventを設定します。")
                 stop_event.set() # 他のタスクも停止させる

            # 残りのペンディング中のタスクをキャンセル
            print("残りのタスクをキャンセルします...")
            for task in pending:
                task.cancel()
                try:
                    await task
                except asyncio.CancelledError:
                    pass # キャンセルは想定通り
                except Exception as e:
                    # キャンセル中にさらにエラーが発生した場合
                    print(f"キャンセル中のタスク '{task.get_name()}' でエラー: {e}")
                    print(traceback.format_exc())
            print("タスクのクリーンアップ完了。")

    except websockets.exceptions.ConnectionClosedOK:
        print("WebSocket接続が正常に閉じられました (1000 OK)。")
        # 正常終了時は stop_event を設定しないようにする
        # if not stop_event.is_set():
        #     stop_event.set() # 正常終了でも停止させる場合
    except websockets.exceptions.ConnectionClosedError as e:
        print(f"WebSocket接続エラーで閉じられました: code={e.code}, reason={e.reason}")
        print(traceback.format_exc())
        if not stop_event.is_set():
            stop_event.set() # エラー発生時は停止させる
    except Exception as e:
        print(f"予期せぬストリーミングエラー: {e}")
        print(traceback.format_exc())
        if not stop_event.is_set():
            stop_event.set() # エラー発生時は停止させる
    finally:
        print("stream_audio_to_openai の finally ブロック開始")
        # stop_eventが設定された場合のみWebSocket接続を閉じるように修正
        if stop_event.is_set():
            if ws and ws.open:
                print("stop_eventによりWebSocket接続を閉じます。")
                try:
                    await ws.close(code=1000, reason="Client stopping")
                except Exception as close_err:
                    print(f"WebSocketクローズエラー: {close_err}")
            else:
                print("WebSocket接続は既に閉じられているか、存在しません。")

            # Unityへの終了通知も stop_event が設定された場合のみ送信
            if unity_ws:
                await send_unity_message(
                    unity_ws=unity_ws,
                    content="Finished",
                    action="Status",
                    emotion="normal"
                )
        else:
            # stop_eventが設定されていない場合 (例: 正常なConnectionClosedOKの後など) は接続を閉じない
            print("stop_eventが設定されていないため、WebSocket接続は閉じません。")
