#!/usr/bin/env bash
# Builds STG Phantom VPN on Linux and packages the Windows portable zip.
#
#   build/build.sh                     app only; OpenVPN is downloaded by the app on first use
#   build/build.sh --installer         also build the Inno Setup installer (build/build-installer.sh, needs wine)
#   build/build.sh --bundle-openvpn    also ship the official OpenVPN MSI in redist\ plus its GPL source in source\
#                                      (zip only; the installer never bundles them)
#   OUT_DIR=/path build/build.sh       change the output folder (default: <repo>/out)
#   PKG_SUFFIX=-reviewed build/build.sh  append a suffix to the zip name (keeps earlier zips intact)
#
# Needs the .NET SDK 8+ (net48 is built through EnableWindowsTargeting) and zip, curl, sha256sum.
# File times in the zip and installer are set to RELEASE_DATE (or SOURCE_DATE_EPOCH) so rebuilds of the
# same tree give identical outputs.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
# The version lives in one place: <Version> in src/STGPhantomVPN/STGPhantomVPN.csproj (the tests check that
# app.manifest, AppVersion.Current, the installer default, README and the notices agree with it).
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' "$ROOT/src/STGPhantomVPN/STGPhantomVPN.csproj" | head -1)"
[[ -n "$VERSION" ]] || { echo "No <Version> in STGPhantomVPN.csproj" >&2; exit 1; }
RELEASE_DATE="2026-11-05"
NAME="STGPhantomVPN-Windows-${VERSION}${PKG_SUFFIX:-}"
OUT_DIR="${OUT_DIR:-$ROOT/out}"
BUNDLE_OPENVPN=0
INSTALLER=0
for arg in "$@"; do
  case "$arg" in
    --bundle-openvpn) BUNDLE_OPENVPN=1 ;;
    --installer) INSTALLER=1 ;;
    *) echo "Unknown option: $arg" >&2; exit 2 ;;
  esac
done
export SOURCE_DATE_EPOCH="${SOURCE_DATE_EPOCH:-$(date -u -d "$RELEASE_DATE" +%s)}"

# Pinned official downloads; must match src/STGPhantomVPN/Core/PinnedDownloads.cs.
OVPN_BASE="https://build.openvpn.net/downloads/releases"
declare -A OVPN_MSI_SHA=(
  [amd64]=31809e97debe44c12be06ed892c2a43e55b67b31cb852827b7ec8f2d6c0b8dc6
  [arm64]=1c5e21a537fb920f251de9f842f2582d744901d45a1cc5f1e911eade87b2d63f
  [x86]=bbf4c4b76f6255ccb2fbec0203fe72e6c6269dfb7cb075436da69faff90c6101
)
OVPN_SRC_URL="https://swupdate.openvpn.org/community/releases/openvpn-2.6.23.tar.gz"
OVPN_SRC_SHA=4041c709162bec1325abf5aa8cf27a255cc477c634b15ee310411c701fc40a96
CACHE="${CACHE_DIR:-$HOME/.cache/stgphantomvpn}"

fetch_pinned() {   # url sha256 dest
  local url="$1" sha="$2" dest="$3"
  if [[ ! -f "$dest" ]] || ! echo "$sha  $dest" | sha256sum -c --quiet - 2>/dev/null; then
    curl --proto '=https' --tlsv1.2 -fsSL "$url" -o "$dest.part"
    mv "$dest.part" "$dest"
  fi
  echo "$sha  $dest" | sha256sum -c --quiet - || { echo "SHA-256 mismatch for $url" >&2; rm -f "$dest"; exit 1; }
}

export DOTNET_CLI_TELEMETRY_OPTOUT=1
command -v dotnet >/dev/null || export PATH="$HOME/.dotnet:$PATH"

echo "== Unit tests"
dotnet run -c Release --project "$ROOT/tests/STGPhantomVPN.Tests"

echo "== Build"
BIN="$ROOT/src/STGPhantomVPN/bin/Release/net48"
# A clean obj folder keeps the exe byte-identical between builds of the same tree.
rm -rf "$BIN" "$ROOT/src/STGPhantomVPN/obj"
dotnet build "$ROOT/src/STGPhantomVPN/STGPhantomVPN.csproj" -c Release -nologo -v q

STAGE="$(mktemp -d)"
PKG="$STAGE/$NAME"
mkdir -p "$PKG"
cp "$BIN"/STGPhantomVPN.exe "$BIN"/STGPhantomVPN.exe.config "$PKG/"
# No third-party DLLs since 1.0.10 (the Fluent theme is part of the exe); copy any that a future reference adds.
compgen -G "$BIN/*.dll" >/dev/null && cp "$BIN"/*.dll "$PKG/"
cp -r "$BIN/vi" "$BIN/zh-CN" "$BIN/km" "$BIN/Assets" "$PKG/"
cp -r "$ROOT/licenses" "$PKG/licenses"
cp "$ROOT/README.md" "$ROOT/README.vi.md" "$PKG/"

if [[ $BUNDLE_OPENVPN == 1 ]]; then
  echo "== Bundling OpenVPN MSI and GPL source"
  mkdir -p "$CACHE" "$PKG/redist" "$PKG/source"
  for arch in amd64 arm64 x86; do
    f="OpenVPN-2.6.23-I002-$arch.msi"
    fetch_pinned "$OVPN_BASE/$f" "${OVPN_MSI_SHA[$arch]}" "$CACHE/$f"
    cp "$CACHE/$f" "$PKG/redist/"
  done
  fetch_pinned "$OVPN_SRC_URL" "$OVPN_SRC_SHA" "$CACHE/openvpn-2.6.23.tar.gz"
  cp "$CACHE/openvpn-2.6.23.tar.gz" "$PKG/source/"
  NAME="${NAME}-with-openvpn"
  mv "$PKG" "$STAGE/$NAME"
  PKG="$STAGE/$NAME"
fi

find "$PKG" -exec touch -h -d "@$SOURCE_DATE_EPOCH" {} +

mkdir -p "$OUT_DIR"
ZIP="$OUT_DIR/$NAME.zip"
rm -f "$ZIP"
(cd "$STAGE" && find "$NAME" | LC_ALL=C sort | TZ=UTC zip -q -X -@ "$ZIP")

if [[ $INSTALLER == 1 ]]; then
  echo "== Installer"
  VERSION="$VERSION" "$ROOT/build/build-installer.sh" "$PKG" "$OUT_DIR"
fi
rm -rf "$STAGE"
( cd "$OUT_DIR" && sha256sum "$NAME.zip" > "$NAME.zip.sha256" )
echo "== Done"
ls -l "$ZIP"
cat "$ZIP.sha256"
