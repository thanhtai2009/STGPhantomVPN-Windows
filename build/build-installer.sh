#!/usr/bin/env bash
# Builds the Windows installer (Inno Setup 6) from a staged app folder, on Linux through Wine.
#
#   build/build-installer.sh <app-dir> [out-dir]   compile installer/STGPhantomVPN.iss for <app-dir>
#   build/build-installer.sh                       run the full build: build/build.sh --installer
#
# <app-dir> is the folder build.sh packs into the portable zip. Inno Setup 6.7.4 is installed on first
# use into a private Wine prefix from the official GitHub release, pinned by SHA-256 (the same file
# jrsoftware.org/isdl.php links to). Needs wine with 32-bit support (Debian: wine, wine64, wine32:i386).
#
#   INNO_WINEPREFIX=/path   Wine prefix for Inno Setup (default ~/.cache/stgphantomvpn/wine-inno)
#   SOURCE_DATE_EPOCH=n     file times stored in the installer (build.sh sets it from RELEASE_DATE)
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
if [[ $# -eq 0 ]]; then exec "$ROOT/build/build.sh" --installer; fi

APP_DIR="$(cd "$1" && pwd)"
OUT_DIR="${2:-${OUT_DIR:-$ROOT/out}}"
VERSION="${VERSION:-$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/STGPhantomVPN/STGPhantomVPN.csproj" | head -1)}"
CACHE="${CACHE_DIR:-$HOME/.cache/stgphantomvpn}"

INNO_VERSION="6.7.4"
INNO_URL="https://github.com/jrsoftware/issrc/releases/download/is-6_7_4/innosetup-6.7.4.exe"
INNO_SHA=a549929319f9120716677c685535ca4671e4b937b5fdc87e2585f32d8eb30e8c
# Unofficial translations from jrsoftware.org/files/istrans, taken from the is-6_7_4 tag
# (https://github.com/jrsoftware/issrc/tree/is-6_7_4/Files/Languages/Unofficial). There is no Khmer file.
declare -A ISL_SHA=(
  [Vietnamese.isl]=826bc509b612d96ccfec89b81956640a3b56b547d194404e5ff95ae1a85babf0
  [ChineseSimplified.isl]=7d544b9bb1d142cfa11f2e5d3cc8abe2e55f8e066c5124e3772675aa236e1278
)

export WINEPREFIX="${INNO_WINEPREFIX:-$CACHE/wine-inno}"
export WINEDEBUG=-all
export WINEDLLOVERRIDES="mscoree,mshtml="
ISCC="$WINEPREFIX/drive_c/InnoSetup6/ISCC.exe"
MARKER="$WINEPREFIX/drive_c/InnoSetup6/stg-installed-from.txt"

command -v wine >/dev/null || { echo "wine is required" >&2; exit 1; }

# xvfb-run gives the Inno Setup installer a display on headless machines; ISCC itself is a console tool.
run_wine() {
  if [[ -z "${DISPLAY:-}" ]] && command -v xvfb-run >/dev/null; then xvfb-run -a wine "$@"; else wine "$@"; fi
}

if [[ ! -f "$ISCC" ]] || [[ "$(cat "$MARKER" 2>/dev/null)" != "$INNO_SHA" ]]; then
  echo "== Installing Inno Setup $INNO_VERSION into $WINEPREFIX"
  mkdir -p "$CACHE/inno"
  setup="$CACHE/inno/innosetup-$INNO_VERSION.exe"
  if [[ ! -f "$setup" ]] || ! echo "$INNO_SHA  $setup" | sha256sum -c --quiet - 2>/dev/null; then
    curl --proto '=https' --tlsv1.2 -fsSL "$INNO_URL" -o "$setup.part"
    mv "$setup.part" "$setup"
  fi
  echo "$INNO_SHA  $setup" | sha256sum -c --quiet - || { echo "SHA-256 mismatch for $INNO_URL" >&2; rm -f "$setup"; exit 1; }
  run_wine "$setup" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CURRENTUSER /NOICONS '/DIR=C:\InnoSetup6'
  wineserver -w
  [[ -f "$ISCC" ]] || { echo "Inno Setup install failed" >&2; exit 1; }
  echo "$INNO_SHA" > "$MARKER"
fi

for f in "${!ISL_SHA[@]}"; do
  echo "${ISL_SHA[$f]}  $ROOT/installer/Languages/$f" | sha256sum -c --quiet - || { echo "Unexpected change in installer/Languages/$f" >&2; exit 1; }
done

if [[ -n "${SOURCE_DATE_EPOCH:-}" ]]; then
  find "$APP_DIR" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +
fi

mkdir -p "$OUT_DIR"
NAME="STGPhantomVPN-Setup-${VERSION}"
rm -f "$OUT_DIR/$NAME.exe" "$OUT_DIR/$NAME.exe.sha256"
echo "== Compiling $NAME.exe with Inno Setup $INNO_VERSION"
wine "$ISCC" /Q \
  "/DAppVersion=$VERSION" \
  "/DAppDir=$(winepath -w "$APP_DIR")" "/DOutputDir=$(winepath -w "$OUT_DIR")" \
  "$(winepath -w "$ROOT/installer/STGPhantomVPN.iss")"
[[ -f "$OUT_DIR/$NAME.exe" ]] || { echo "ISCC produced no $NAME.exe" >&2; exit 1; }
( cd "$OUT_DIR" && sha256sum "$NAME.exe" > "$NAME.exe.sha256" )
ls -l "$OUT_DIR/$NAME.exe"
cat "$OUT_DIR/$NAME.exe.sha256"
