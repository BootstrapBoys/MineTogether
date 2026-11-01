#!/usr/bin/env bash
# Build Mine Together and drop the DLL in artifacts/debug for in-game testing.
# A debug build is not a release: no zip is produced here.
set -euo pipefail

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT="$(cd "$HERE/.." && pwd)"

export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
export PATH="$DOTNET_ROOT:$PATH"
export DOTNET_ROLL_FORWARD=LatestMajor

VALHEIM_DIR="${VALHEIM_DIR:-/home/hermes/projects/valheim/quartermaster_integrations/refs/game}"
BEPINEX_DIR="${BEPINEX_DIR:-$VALHEIM_DIR/BepInEx}"
export VALHEIM_DIR BEPINEX_DIR

VERSION="$(grep -oP 'public const string Version = "\K[^"]+' "$ROOT/src/vein_mining.cs")"

dotnet build "$ROOT/MineTogether.csproj" -c Release -v q

OUT="$ROOT/artifacts/debug"
mkdir -p "$OUT"
cp "$ROOT/bin/Release/MineTogether.dll" "$OUT/MineTogether.dll"
SHA="$(sha256sum "$OUT/MineTogether.dll" | cut -d' ' -f1)"
{
  printf 'name: Mine Together\n'
  printf 'version: %s\n' "$VERSION"
  printf 'built: %s\n' "$(date -u +%Y-%m-%dT%H:%M:%SZ)"
  printf 'sha256: %s\n' "$SHA"
} > "$OUT/build-info.txt"

rm -rf "$ROOT/bin" "$ROOT/obj"
echo "built $OUT/MineTogether.dll  version $VERSION  sha256 ${SHA:0:16}"
