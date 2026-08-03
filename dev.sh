#!/usr/bin/env bash
# Start backend + frontend together. Ctrl+C stops both.
#   ./dev.sh
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BACKEND_PORT=5099
FRONTEND_PORT=5173
# Loopback за замовчуванням. Щоб телефон у тій самій Wi-Fi дістав API:
#   BACKEND_HOST=0.0.0.0 ./dev.sh
# Тоді API видно всій локальній мережі — а локально в нього немає пароля, тож робити це
# варто вдома, а не в кав'ярні.
BACKEND_HOST="${BACKEND_HOST:-localhost}"

# Homebrew tools are not on PATH for GUI-launched apps.
export PATH="/opt/homebrew/bin:$PATH"

for cmd in dotnet npm; do
  command -v "$cmd" >/dev/null || { echo "!! '$cmd' не знайдено в PATH"; exit 1; }
done

free_port() {
  local port=$1
  local pids
  pids="$(lsof -ti:"$port" 2>/dev/null || true)"
  if [ -n "$pids" ]; then
    echo "→ звільняю порт $port"
    echo "$pids" | xargs kill -9 2>/dev/null || true
    sleep 1
  fi
}

cleanup() {
  echo ""
  echo "→ зупиняю…"
  # `dotnet run` and `npm` spawn children, so killing the direct PID is not enough —
  # free the ports as well to make sure nothing is left listening.
  [ -n "${BACK_PID:-}" ] && kill "$BACK_PID" 2>/dev/null || true
  [ -n "${FRONT_PID:-}" ] && kill "$FRONT_PID" 2>/dev/null || true
  sleep 1
  for port in "$BACKEND_PORT" "$FRONTEND_PORT"; do
    lsof -ti:"$port" 2>/dev/null | xargs kill -9 2>/dev/null || true
  done
  echo "✓ зупинено"
}
trap cleanup EXIT INT TERM

free_port "$BACKEND_PORT"
free_port "$FRONTEND_PORT"

echo "→ бекенд  http://$BACKEND_HOST:$BACKEND_PORT  (Scalar: /scalar)"
ASPNETCORE_ENVIRONMENT=Development \
ASPNETCORE_URLS="http://$BACKEND_HOST:$BACKEND_PORT" \
  dotnet run --project "$ROOT/backend/Api" --no-launch-profile &
BACK_PID=$!

# Wait for the API to answer before starting Vite, so the first page load has data.
for _ in $(seq 1 40); do
  curl -fsS "http://localhost:$BACKEND_PORT/" >/dev/null 2>&1 && break
  sleep 0.5
done

echo "→ фронтенд http://localhost:$FRONTEND_PORT"
( cd "$ROOT/frontend" && [ -d node_modules ] || (cd "$ROOT/frontend" && npm install) )
( cd "$ROOT/frontend" && npm run dev -- --host ) &
FRONT_PID=$!

echo ""
echo "✓ Обидва запущені. Відкрий http://localhost:$FRONTEND_PORT"
echo "  (--host увімкнено: доступно й з телефона по IP цього Mac у тій самій Wi-Fi)"
echo "  Ctrl+C зупиняє обидва."
echo ""

wait
