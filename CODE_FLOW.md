# Voice Agent MVP - Technical Code Flow

This document details the step-by-step execution path for outbound and inbound calls, highlighting the specific projects, methods, and API endpoints involved.

---

## 1. Outbound Call Flow

The outbound flow starts from the human operator (Console) and moves through the API to the background Worker.

### Phase A: Queuing the Call
1.  **`VoiceAgent.Console`**: `Program.cs`
    *   Calls `POST /v1/leads` to ensure the lead exists.
    *   Calls `POST /v1/calls/start` with `Direction=Outbound` and `Status=New`.
2.  **`VoiceAgent.Host.Api`**: `Program.cs`
    *   Endpoint: `app.MapPost("/v1/calls/start", ...)`
    *   Logic: Inserts a `Call` record with `Status.New` into Postgres.

### Phase B: Polling and Origination
1.  **`VoiceAgent.Host.Worker`**: `Worker.cs`
    *   Method: `ExecuteAsync` (Background Loop)
    *   Every 2 seconds, calls `IVoiceAgentApiClient.ClaimNextCallAsync()`.
2.  **`VoiceAgent.Host.Api`**: `Program.cs`
    *   Endpoint: `app.MapPost("/v1/calls/claim", ...)`
    *   Logic: Atomically finds the next `New` call, checks the `DoNotCall` table, and updates status to `Started`.
3.  **`VoiceAgent.Host.Worker`**: `AsteriskAriTelephonyControl.cs`
    *   Method: `TriggerOutboundAsync(CallDto call)`
    *   Calls `AriClient.OriginateAsync(endpoint, ...)` which tells Asterisk to dial.
    *   Stores call metadata in `_channelToCall` indexed by the new Channel ID.

### Phase C: Call Connection
1.  **`Asterisk`**: Dials the endpoint and triggers a `StasisStart` event once answered.
2.  **`VoiceAgent.Host.Worker`**: `AsteriskAriTelephonyControl.cs`
    *   Method: `RunAsync` (Event Loop) receives `StasisStart`.
    *   Identifies the channel from `_channelToCall`.
    *   Calls `HandleCallAsync(channelId, call, ...)`
    *   Calls `AriClient.AnswerAsync()` and `AriClient.CreateExternalMediaAsync()`.
    *   Instantiates `ConversationOrchestrator` and calls `orch.RunAsync(call, ...)`.

---

## 2. Inbound Call Flow

The inbound flow starts when a user dials a recognized extension in Asterisk.

### Phase A: Detection
1.  **`User`**: Dials extension `2001` (Doctor) or `2002` (Cab) from a SIP phone.
2.  **`Asterisk`**: Triggers a `StasisStart` event for the `aiapp` application.
3.  **`VoiceAgent.Host.Worker`**: `AsteriskAriTelephonyControl.cs`
    *   Method: `RunAsync` (Event Loop) receives `StasisStart`.
    *   Detects `ev.Channel.Dialplan.Exten` (e.g., "2001").
    *   Maps extension to `InboundUseCaseCode` (e.g., `DOCTOR_APPT`).
    *   Synthesizes a `CallDto` for the inbound use-case.
    *   Calls `HandleCallAsync(id, call, ...)`.

### Phase B: Connection
1.  **`VoiceAgent.Host.Worker`**: `AsteriskAriTelephonyControl.cs`
    *   Method: `HandleCallAsync(...)`
    *   Same as outbound Phase C: Answers, bridges, creates ExternalMedia, and starts orchestration.
    *   Method: `ConversationOrchestrator.RunAsync(call, ...)`

---

## 3. Conversation Loop (Shared)

Once a call (Inbound or Outbound) is connected, the orchestration logic takes over.

### Phase A: Intro Pitch
1.  **`ConversationOrchestrator`**: `RunAsync()`
    *   Calls `PlayIntroAsync(intro, ...)` if an intro pitch is defined for the campaign.
    *   **`ElevenLabsTtsProvider`**: `SynthesizeMuLawAsync()` generates audio.
    *   **`RtpAudioTransport`**: `SendAsync()` streams audio to Asterisk.
    *   *Barge-in Logic*: Monitors `IVadDetector.IsSpeech()` and `ISttProvider.GetUpdatesAsync()` to cancel intro if the user starts speaking.

### Phase B: Interaction Loop
1.  **`ConversationOrchestrator`**: `HandleTranscriptsAsync()`
    *   **`DeepgramSttProvider`**: Streams audio to Deepgram; receives `TranscriptUpdate`.
    *   On `upd.IsFinal == true`:
        *   Calls `IVoiceAgentApiClient.GetNextActionAsync(..., userText, ...)`.
2.  **`VoiceAgent.Host.Api`**: `Program.cs`
    *   Endpoint: `app.MapPost("/v1/calls/{callId}/next", ...)`
    *   **`PromptBuilder`**: `BuildTurns()` constructs the system prompt based on campaign rules and known fields.
    *   **`HfRouterLlmProvider`**: `CompleteAsync()` calls the LLM (e.g., Qwen).
    *   **`ResponseGuard`**: `Enforce()` validates the JSON response and applies safety guardrails.
3.  **`ConversationOrchestrator`**:
    *   Receives `AgentActionDto`.
    *   Triggers TTS playback via `_tts.SynthesizeMuLawAsync()`.
    *   Updates call status or ends call based on intent (e.g., `dncl`, `end`).

---

## 4. Finalization and Cleanup

1.  **`ConversationOrchestrator`**: `RunAsync()` finishes.
2.  **`VoiceAgent.Host.Worker`**: `AsteriskAriTelephonyControl.cs`
    *   The `finally` block in the Task running `HandleCallAsync` triggers.
    *   Calls `AriClient.DeleteBridgeAsync()`.
    *   Calls `AriClient.HangupAsync()`.
3.  **`VoiceAgent.Host.Api`**: `POST /v1/calls/{id}/status` is called to set `EndedAt` and final disposition.
