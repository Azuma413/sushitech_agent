# 技術文脈（techContext.md）

## 使用技術
- Python 3.11
- FastAPI（WebSocket/REST API サーバ）
- OpenAI Realtime API（音声・テキスト処理、Function Calling）
- AssistModel (GPT-4.1 mini)
- TextTalkModel (モデル未定)
- LangChain (AssistModelのReAct Agent実装に部分利用)
- sounddevice (マイク音声取得)
- WebSocket（Unity 連携）

## 開発環境
- poetry による依存管理（pyproject.toml）
- .env にAPIキー等の設定
- シンプルなディレクトリ構成（src/ 配下に主要モジュール）

## 技術的制約・方針
- 全体のフロー管理にはLangChain/LangGraphは使用しない
- 旧実装（src/old/）は参考用
- 拡張性・保守性を重視

## 主要依存パッケージ
- fastapi
- websockets
- openai
- langchain (AssistModel用: `langchain_openai.ChatOpenAI`, `langchain.agents.AgentExecutor`, `langchain.agents.create_react_agent`, `langchain_community.tools.ddg_search.DuckDuckGoSearchRun`, `langchain_core.prompts.PromptTemplate`)
- langchain (TextTalkModel用: `langchain_openai.ChatOpenAI`, `langchain_core.prompts.ChatPromptTemplate`, `langchain.output_parsers.PydanticOutputParser`)
- sounddevice
- python-dotenv
- その他必要最小限
