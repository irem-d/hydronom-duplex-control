# Hydronom Duplex Control — Web App + C# API + Python Sensor Simulator

**Date:** 2025-08-13

A minimal, working MVP of a duplex control & telemetry stack tailored to the Hydronom architecture.

**Components**
- `api/` — .NET 8 Minimal API + SignalR (WS) + dev JWT + JSONL logging
- `web/` — React + TypeScript + Vite + Tailwind operator UI
- `feeder/` — Python telemetry simulator (5–10 Hz), fault injection, basic waypoint follow
- `docs/` — JSON Schemas, flow diagrams (placeholders), test notes

## Quick Start

### 1) API
```bash
cd api
dotnet restore
dotnet run
# API base: http://localhost:5000
# Swagger:  http://localhost:5000/swagger
# WS Hub:   ws://localhost:5000/ws/telemetry
```

Issue a dev token (optional, for protected endpoints):
```bash
curl "http://localhost:5000/auth/dev-token?user=operator"
# -> { "token": "<jwt>" }
```

### 2) Web
```bash
cd web
npm install
npm run dev
# Web UI: http://localhost:5173
```
> Web expects `VITE_API_BASE` in `.env` (default: `http://localhost:5000`).

### 3) Feeder (Python)
```bash
cd feeder
python -m venv .venv && source .venv/bin/activate  # Windows: .venv\Scripts\activate
pip install -r requirements.txt
python simulate.py --hz 5 --vehicle hydronom-boat-01 --leak-after 30
```

## Scenarios
1) **Cable-Track Demo (Boat)** — create mission via Swagger or UI, start mission, switch MANUAL and back.
2) **Leak Alarm Reaction** — run feeder with `--leak-after 30`; UI shows alarm; optional auto-abort (Settings).
3) **Battery Drop** — run with `--low-battery 20`; UI shows Low Battery; suggests pause.

## Repo Layout
```
hydronom-duplex-control/
  api/
  web/
  feeder/
  docs/
```

## License
MIT (educational demo).
