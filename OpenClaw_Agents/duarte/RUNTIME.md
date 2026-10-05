# Duarte OpenClaw Runtime

## Agent

- Agent ID: duarte
- Agent route: openclaw/duarte
- Backend model: ollama-npc-qwen3/qwen3:4b-instruct

## Model configuration

- Context window: 16384
- Maximum output tokens: 400
- Thinking: false
- Tool support: false

## Local services

- Ollama: http://127.0.0.1:11434
- OpenClaw Gateway: http://127.0.0.1:18789
- Unity Bridge: http://127.0.0.1:5055

## Decision path

Unity -> Flask Bridge -> OpenClaw Gateway -> Duarte agent -> Qwen3 -> validated decision -> Unity

## Security

Do not store API keys, authentication profiles, sessions, gateway tokens or the global OpenClaw configuration in this directory.
