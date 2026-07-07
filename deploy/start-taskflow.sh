#!/usr/bin/env bash
set -euo pipefail

dotnet /app/worker/TaskFlow.AgentWorker.dll &
worker_pid="$!"

cleanup() {
  kill "$worker_pid" 2>/dev/null || true
}
trap cleanup EXIT INT TERM

dotnet /app/api/TaskFlow.Api.dll
