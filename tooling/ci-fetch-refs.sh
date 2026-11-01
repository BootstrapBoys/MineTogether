#!/usr/bin/env bash
# Fetch the reference assemblies CI needs to compile and package the mod.
#
# Nothing here is redistributable, so none of it is committed: the game comes from Valve's public
# dedicated-server depot and the dependencies come from the Thunderstore CDN, both anonymous.
# Safe to re-run - every download is skipped when the file already exists.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"
WORK="${CI_WORK:-$ROOT/.ci}"
DEPOT_VERSION="DepotDownloader_3.4.0"

mkdir -p "$WORK" "$ROOT/refs/game/valheim_Data/Managed" "$ROOT/refs/game/BepInEx/core" "$ROOT/refs/mods"

say() { printf '\n=== %s\n' "$*"; }

# ---------------------------------------------------------------- game assemblies
say "game assemblies (app 896660, anonymous)"
if [ ! -x "$WORK/DepotDownloader" ]; then
  curl -sSL -o "$WORK/depot.zip" \
    "https://github.com/SteamRE/DepotDownloader/releases/download/${DEPOT_VERSION}/DepotDownloader-linux-x64.zip"
  unzip -q -o "$WORK/depot.zip" -d "$WORK/depot"
  cp "$WORK/depot/DepotDownloader" "$WORK/DepotDownloader"
  chmod +x "$WORK/DepotDownloader"
fi

if [ ! -d "$WORK/game/valheim_server_Data/Managed" ]; then
  # No -username: a null username is what triggers anonymous login.
  "$WORK/DepotDownloader" -app 896660 -dir "$WORK/game" -validate
fi
cp "$WORK/game/valheim_server_Data/Managed/"*.dll "$ROOT/refs/game/valheim_Data/Managed/"

# ---------------------------------------------------------------- dependencies
say "dependencies from resources/manifest.json"
mapfile -t DEPS < <(python3 -c "
import json
m = json.load(open('$ROOT/resources/manifest.json', encoding='utf-8-sig'))
print('\n'.join(m['dependencies']))
")

for dep in "${DEPS[@]}"; do
  echo "-- $dep"
  if [ ! -d "$WORK/deps/$dep" ]; then
    mkdir -p "$WORK/deps"
    curl -sSL -o "$WORK/deps/$dep.zip" "https://gcdn.thunderstore.io/live/repository/packages/${dep}.zip"
    unzip -q -o "$WORK/deps/$dep.zip" -d "$WORK/deps/$dep"
  fi
done

# BepInEx core: the package nests it, so find it rather than assuming a path.
BEPINEX_CORE="$(find "$WORK/deps" -type d -path '*BepInEx/core' | head -1)"
[ -n "$BEPINEX_CORE" ] || { echo "MISSING BepInEx core in fetched dependencies" >&2; exit 1; }
cp "$BEPINEX_CORE/"*.dll "$ROOT/refs/game/BepInEx/core/"

# Jotunn: a plain plugins/Jotunn.dll inside its package.
JOTUNN="$(find "$WORK/deps" -name Jotunn.dll -type f | head -1)"
[ -n "$JOTUNN" ] || { echo "MISSING Jotunn.dll in fetched dependencies" >&2; exit 1; }
cp "$JOTUNN" "$ROOT/refs/mods/Jotunn.dll"

# ---------------------------------------------------------------- verify
say "verify"
MISSING=0
for required in \
  "$ROOT/refs/game/valheim_Data/Managed/assembly_valheim.dll" \
  "$ROOT/refs/game/valheim_Data/Managed/UnityEngine.dll" \
  "$ROOT/refs/game/valheim_Data/Managed/UnityEngine.CoreModule.dll" \
  "$ROOT/refs/game/BepInEx/core/BepInEx.dll" \
  "$ROOT/refs/game/BepInEx/core/0Harmony.dll" \
  "$ROOT/refs/mods/Jotunn.dll"; do
  if [ -f "$required" ]; then
    echo "ok   ${required#$ROOT/}"
  else
    echo "MISSING ${required#$ROOT/}" >&2
    MISSING=1
  fi
done
[ "$MISSING" -eq 0 ] || exit 1

echo
echo "references ready under refs/"
