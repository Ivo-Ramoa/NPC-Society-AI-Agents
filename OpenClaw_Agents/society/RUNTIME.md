# Society Scheduler Runtime

## Agent

- Agent ID: society
- Agent route: openclaw/society
- Backend model: ollama-npc-qwen3/qwen3:4b-instruct

## Model configuration

- Context window: 4096
- Maximum output tokens: 220
- Thinking: false
- Tool support: false

## Local services

- Ollama: http://127.0.0.1:11434
- OpenClaw Gateway: http://127.0.0.1:18789
- Unity Bridge: http://127.0.0.1:5055

## Decision path

Unity -> Flask Bridge -> OpenClaw Gateway -> Society Scheduler -> selected actor -> Unity manager -> NPC agent decision
