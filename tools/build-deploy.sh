#!/bin/sh
# Сборка JacBlack на зеркале cub.black и доставка образа на awg.
#
# До 28.09.2026 сборочным хостом был домашний NUC — он умер. На самом awg
# собирать нельзя: там крутится прод, и на время сборки у него уже пропадал
# DNS (см. TASKS). Зеркало (169.58.210.166, 8 vCPU, 23 ГБ) почти простаивает.
#
# Что делает: git archive HEAD → зеркало → dotnet test в контейнере SDK →
# docker build → образ потоком через этот компьютер на awg (docker save | load)
# под тегом jacblack:<тег>. Боевой контейнер НЕ переключает — это отдельный,
# осознанный шаг (retag в jacblack:local и docker compose up -d на awg).
#
# Запуск из корня репозитория (Git Bash):
#     sh tools/build-deploy.sh <тег>            # например: sh tools/build-deploy.sh fix-kinozal
set -eu

TAG="${1:?укажи тег образа, например: sh tools/build-deploy.sh fix-x}"
MIRROR="root@169.58.210.166"
KEY_MIRROR="$HOME/.ssh/id_ed25519_cubmirror"
AWG="root@151.242.147.7"
KEY_AWG="$HOME/.ssh/id_ed25519_awg"
BUILD_DIR=/opt/jacblack-build
SHA=$(git rev-parse --short HEAD)

[ -z "$(git status --porcelain -- . ':!TASKS.md' ':!KNOWN_ISSUES.md' ':!DECISIONS.md')" ] || {
    echo "в рабочей копии незакоммиченные правки кода — собирается только HEAD, закоммить сначала"; exit 1; }

echo "== исходники $SHA → зеркало"
# autocrlf=false: иначе на Windows архив отдаёт CRLF, и generate-version.sh
# падает в сборке («$'\r': command not found»).
git -c core.autocrlf=false -c core.eol=lf archive --format=tar HEAD | ssh -i "$KEY_MIRROR" "$MIRROR" \
    "rm -rf $BUILD_DIR && mkdir -p $BUILD_DIR && tar -x -C $BUILD_DIR"

echo "== тесты"
ssh -i "$KEY_MIRROR" "$MIRROR" "docker run --rm -v $BUILD_DIR:/src -w /src \
    -v jacblack-nuget:/root/.nuget/packages mcr.microsoft.com/dotnet/sdk:10.0 \
    sh -c 'dotnet test tests/JacBlack.Tests -c Release --nologo -v q > /tmp/t.log 2>&1; rc=\$?; tail -5 /tmp/t.log; exit \$rc'"

echo "== образ"
ssh -i "$KEY_MIRROR" "$MIRROR" "cd $BUILD_DIR && nice docker build -q -t jacblack:$TAG \
    --label jacblack.git=$SHA . > /tmp/jb-build.log 2>&1; rc=\$?; tail -3 /tmp/jb-build.log; exit \$rc"

echo "== доставка на awg"
ssh -i "$KEY_MIRROR" "$MIRROR" "docker save jacblack:$TAG | gzip -1" \
    | ssh -i "$KEY_AWG" "$AWG" "gunzip | docker load"
ssh -i "$KEY_AWG" "$AWG" "docker image inspect jacblack:$TAG --format '{{index .Config.Labels \"jacblack.git\"}} {{.Id}}'"

echo "готово: на awg образ jacblack:$TAG (git $SHA). Выкат — вручную:"
echo "  docker tag jacblack:local jacblack:before-$TAG && docker tag jacblack:$TAG jacblack:local"
echo "  cd /opt/jacblack && docker compose up -d --force-recreate jacblack"
