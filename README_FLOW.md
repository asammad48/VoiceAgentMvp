# Voice Agent MVP - Call Flow Testing Guide

This guide explains how to test the inbound and outbound call flows in the Voice Agent system.

## Prerequisites

1.  **Postgres Database**: Ensure Postgres is running and the connection string in `src/VoiceAgent.Host.Api/appsettings.json` is correct.
2.  **Asterisk ARI**: Ensure Asterisk is configured with the `aiapp` Stasis application and ARI credentials match `src/VoiceAgent.Host.Worker/appsettings.json`.
3.  **Providers**: API keys for Deepgram, ElevenLabs, and HuggingFace must be configured in `appsettings.json`.

---

## 1. Running the System

### Step 1: Start the API
The API handles the data model, campaign profiles, and LLM orchestration logic.
```bash
cd src/VoiceAgent.Host.Api
dotnet run
```
*Swagger will be available at `http://localhost:5000/swagger`.*

### Step 2: Start the Worker
The Worker connects to Asterisk, handles RTP media, and polls for outbound calls.
```bash
cd src/VoiceAgent.Host.Worker
dotnet run
```

---

## 2. Testing Outbound Flows

Outbound calls are campaign-driven. They use an "Intro Pitch" immediately upon answer.

### Using the Console App
1.  Run the console tester:
    ```bash
    cd src/VoiceAgent.Console
    dotnet run
    ```
2.  Follow the prompts:
    *   **Tenant ID**: Use a valid Tenant GUID (create one via Swagger `/v1/tenants` if needed).
    *   **Agent ID**: Use a valid Agent GUID.
    *   **Phone Number**: The SIP endpoint or number to dial (e.g., `6001`).
    *   **Campaign**: Choose `SOLAR`, `AUTOCARE`, `FE`, `ACA`, or `MEDICARE`.
3.  The console app will queue the call in the database.
4.  The **Worker** will claim the call, originate it via Asterisk, and start the conversation with the campaign's intro pitch.

### Manual Queuing (via Swagger)
Post to `/v1/calls/start` with `direction: 1` (Outbound) and a `status: 0` (New). The worker polling loop will pick it up.

---

## 3. Testing Inbound Flows

Inbound calls are routed to specific use-cases based on the extension dialed.

### Routing Logic
*   **Dial 2001**: Routed to **Doctor Appointment** flow.
*   **Dial 2002**: Routed to **Cab Booking Service** flow.

### How to test:
1.  Use a SIP phone (like Linphone or MicroSIP) registered to your Asterisk instance.
2.  Dial `2001` or `2002`.
3.  The Worker will detect the `StasisStart` event, see the extension, and automatically trigger the correct conversation flow.
4.  The agent will greet you with the intro pitch for that use-case (e.g., "Thank you for calling our clinic...").

---

## 4. Key Features to Verify

1.  **Intro Pitch**: Confirm the agent speaks first on outbound calls.
2.  **Barge-in**: Try speaking while the agent is talking (intro or reply). The agent should stop and listen.
3.  **Guardrails**: Ask for prices or government affiliation. The agent should use the "Safe Fallback" or redirect to a licensed agent.
4.  **DNC**: Say "Stop calling me" or "Remove me from your list". The call should end, and the number should be added to the `DoNotCall` table.
5.  **Data Capture**: Provide information (e.g., "My name is John and I want to book for tomorrow at 5 PM"). Verify the fields are captured in the `CallFields` table in Postgres.
6.  **Concurrency**: Attempt to queue more calls than `MaxConcurrentCalls` (default 10) to see the 429 safety block.
7.  **Timeout**: Stay silent for 30 seconds. The agent should hang up.

---

## 5. Troubleshooting
*   **No Audio**: Check `Media:WindowsListenIp` and `Media:WindowsListenPort` in the Worker config.
*   **Worker not picking up calls**: Ensure `Outbound:TenantId` in Worker `appsettings.json` matches the TenantId you used in the Console app.
*   **LLM Errors**: Check HuggingFace router logs or API tokens.
