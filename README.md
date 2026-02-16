# VoiceAgent MVP — Multi-tenant + Campaign prompts + Postgres + Swagger + (optional) Asterisk/WSL

This ZIP includes:
- Swagger API to test **campaign outbound scripts** (FE / ACA / Medicare) with a bounded LLM
- **Multi-tenant** storage in PostgreSQL (sell to multiple agents/clients)
- Lead/Call management + statuses (NotInterested, CallbackScheduled, Dnc, etc.)
- Original Asterisk WSL configs + Worker for later telephony integration

## 0) Start PostgreSQL (recommended)
From repo root:
```powershell
docker compose -f infra/docker-compose.postgres.yml up -d
```

Default connection string is in:
`src/VoiceAgent.Host.Api/appsettings.json`

## 1) Run Swagger API (NO Asterisk needed)
```powershell
cd src/VoiceAgent.Host.Api
dotnet run
```
Open:
http://localhost:5000/swagger

### Multi-tenant header
For all endpoints except `/v1/tenants`, set header:
`X-Tenant-Id: <tenant-guid>`

## 2) Quick test flow (Swagger)
1) POST `/v1/tenants`  -> get `tenantId`
2) Set header `X-Tenant-Id`
3) POST `/v1/agents`   -> create agent with `DisplayName` (your agent name)
4) POST `/v1/leads`    -> create lead with `CampaignCode` = FE / ACA / MEDICARE
5) POST `/v1/calls/start` -> create call record
6) POST `/v1/calls/{callId}/next`
   - send `Transcript`: what the lead said (text)
   - (optional) send `Fields`: known info so far
   - returns bounded JSON with `say`, `intent`, `fields`, `next_step`
7) If lead says "not interested":
   POST `/v1/calls/{callId}/status` with Status `NotInterested`

Campaign prompts live here:
`src/VoiceAgent.Host.Api/CampaignProfiles.json`

## 3) Campaign bounding (important)
The model is forced to output strict JSON.
We also block banned phrases and unknown intents via `ResponseGuard`.

## 4) Asterisk in WSL2 (optional later)
WSL configs:
`wsl/asterisk-config/`

Worker:
`src/VoiceAgent.Host.Worker`

> For WSL2 RTP: set `Media:WindowsListenIp` to your Windows LAN IP (NOT localhost) and open UDP port.

## 5) Local-only (no API keys) later
Keep providers behind interfaces:
- ILlmProvider -> swap to Ollama/vLLM/llama.cpp server
- STT/TTS -> swap to local whisper/piper

The API already has `Providers` section in appsettings (placeholder). Wire DI switching when you're ready.
