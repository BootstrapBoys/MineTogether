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
    # Thunderstore packages built on Windows carry backslash separators; unzip reports that as a
    # warning and exits 1, which would abort the job even though the files are there.
    unzip -q -o "$WORK/deps/$dep.zip" -d "$WORK/deps/$dep" || true
  fi
done

# BepInEx core: locate it by the assemblies we need rather than by the archive's folder layout,
# which differs between packages and may use backslash separators.
HARMONY="$(find "$WORK/deps" -type f -name '0Harmony.dll' | head -1)"
[ -n "$HARMONY" ] || { echo "MISSING 0Harmony.dll in fetched dependencies" >&2; exit 1; }
BEPINEX_CORE="$(dirname "$HARMONY")"
echo "BepInEx core: ${BEPINEX_CORE#$WORK/}"
cp "$BEPINEX_CORE/"*.dll "$ROOT/refs/game/BepInEx/core/"

# Jotunn: match on a name ending in Jotunn.dll. A trailing wildcard would also match
# Jotunn.dll.mdb, the Mono debug symbols, which copies cleanly and then fails to compile.
JOTUNN="$(find "$WORK/deps" -type f -iname '*jotunn.dll' | head -1)"
[ -n "$JOTUNN" ] || { echo "MISSING Jotunn.dll in fetched dependencies" >&2; exit 1; }
cp "$JOTUNN" "$ROOT/refs/mods/Jotunn.dll"

# Conditional Config Sync: the API assembly only, not its bootstrap plugin.
CCS="$(find "$WORK/deps" -type f -iname 'ConditionalConfigSync.dll' | head -1)"
[ -n "$CCS" ] || { echo "MISSING ConditionalConfigSync.dll in fetched dependencies" >&2; exit 1; }
cp "$CCS" "$ROOT/refs/mods/ConditionalConfigSync.dll"

# ---------------------------------------------------------------- verify
say "verify"
MISSING=0
for required in \
  "$ROOT/refs/game/valheim_Data/Managed/assembly_valheim.dll" \
  "$ROOT/refs/game/valheim_Data/Managed/UnityEngine.dll" \
  "$ROOT/refs/game/valheim_Data/Managed/UnityEngine.CoreModule.dll" \
  "$ROOT/refs/game/BepInEx/core/BepInEx.dll" \
  "$ROOT/refs/game/BepInEx/core/0Harmony.dll" \
  "$ROOT/refs/mods/Jotunn.dll" \
  "$ROOT/refs/mods/ConditionalConfigSync.dll"; do
  if [ ! -f "$required" ]; then
    echo "MISSING ${required#$ROOT/}" >&2
    MISSING=1
  elif [ "$(head -c2 "$required")" != "MZ" ]; then
    echo "NOT AN ASSEMBLY ${required#$ROOT/} (no PE header)" >&2
    MISSING=1
  else
    echo "ok   ${required#$ROOT/}"
  fi
done
[ "$MISSING" -eq 0 ] || exit 1

echo
echo "references ready under refs/"
