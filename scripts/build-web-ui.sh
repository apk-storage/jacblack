#!/usr/bin/env bash
# Собирает интерфейс (webui/) в свежий wwwroot/ перед публикацией .NET.
# wwwroot целиком порождаемый — в git его нет (см. .gitignore).
# Во время работы туда же пишется trackers.txt (TrackersCron), его сохраняем.
#
# Прежний интерфейс лежал в web/: Vue с PWA, двумя языками, редактором
# настроек и веткой развёртывания на Cloudflare. Заменён на webui/ — тот же
# Vue, но без всего перечисленного. Проверка на sw.js убрана вместе с PWA.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
WEB="$ROOT/webui"
WWW="$ROOT/wwwroot"
DIST="$WEB/dist"
OPENAPI_SRC="$WEB/public/openapi.yaml"

if [[ ! -d "$WEB" ]]; then
  echo "error: webui/ не найден по пути $WEB" >&2
  exit 1
fi

if [[ ! -f "$OPENAPI_SRC" ]]; then
  echo "error: нет $OPENAPI_SRC — описание API должно лежать в webui/public" >&2
  exit 1
fi

echo "==> Сборка интерфейса..."
cd "$WEB"
if [[ -f package-lock.json ]]; then
  npm ci
else
  npm install
fi
npm run build

if [[ ! -f "$DIST/index.html" ]]; then
  echo "error: после сборки нет $DIST/index.html" >&2
  exit 1
fi
if [[ ! -f "$DIST/openapi.yaml" ]]; then
  echo "error: после сборки нет $DIST/openapi.yaml — ожидалась копия из public/" >&2
  exit 1
fi

# Пути к файлам сборки должны быть корневыми: приложение живёт в корне сайта.
# Если сюда попадёт сборка с базовым путём (её делают для временной выкладки),
# страница откроется белой — браузер пойдёт за файлами не туда. Один раз уже
# наступали, поэтому проверка стоит здесь, а не в голове.
if ! grep -q 'src="/assets/' "$DIST/index.html"; then
  echo "error: в index.html нет корневых путей — похоже, сборка сделана с базовым путём" >&2
  exit 1
fi

# trackers.txt пишется во время работы, пересборка не должна его терять
TRACKERS_BAK=""
if [[ -f "$WWW/trackers.txt" ]]; then
  TRACKERS_BAK="$(mktemp)"
  cp "$WWW/trackers.txt" "$TRACKERS_BAK"
fi

echo "==> Пересоздаём wwwroot из webui/dist..."
rm -rf "$WWW"
mkdir -p "$WWW"
cp -a "$DIST"/. "$WWW"/

if [[ -n "$TRACKERS_BAK" ]]; then
  cp "$TRACKERS_BAK" "$WWW/trackers.txt"
  rm -f "$TRACKERS_BAK"
fi

if [[ ! -f "$WWW/index.html" || ! -f "$WWW/openapi.yaml" ]]; then
  echo "error: wwwroot собран не полностью" >&2
  exit 1
fi

ASSET_COUNT="$(find "$WWW/assets" -type f | wc -l | tr -d ' ')"
echo "==> wwwroot готов (файлов в assets: $ASSET_COUNT)"
