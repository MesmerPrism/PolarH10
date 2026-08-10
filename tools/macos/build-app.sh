#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"
MACOS_ROOT="$REPO_ROOT/macos"
VERSION="${1:-0.0.0}"
ARTIFACT_ROOT="${2:-$REPO_ROOT/artifacts/macos}"
BUILD_ROOT="$ARTIFACT_ROOT/build"
APP_ROOT="$ARTIFACT_ROOT/PolarH10.app"
CONTENTS_ROOT="$APP_ROOT/Contents"
EXECUTABLE_ROOT="$CONTENTS_ROOT/MacOS"
RESOURCES_ROOT="$CONTENTS_ROOT/Resources"
ARCHIVE_PATH="$ARTIFACT_ROOT/PolarH10-macOS-universal.zip"

if [[ ! "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]]; then
  echo "Version must look like 0.1.0, got: $VERSION" >&2
  exit 2
fi

for command_name in swift lipo codesign ditto sips iconutil; do
  if ! command -v "$command_name" >/dev/null 2>&1; then
    echo "Required macOS build tool is missing: $command_name" >&2
    exit 3
  fi
done

rm -rf "$BUILD_ROOT" "$APP_ROOT" "$ARCHIVE_PATH"
mkdir -p "$BUILD_ROOT" "$EXECUTABLE_ROOT" "$RESOURCES_ROOT"

build_architecture() {
  local architecture="$1"
  local scratch_path="$BUILD_ROOT/$architecture"
  swift build \
    --package-path "$MACOS_ROOT" \
    --configuration release \
    --arch "$architecture" \
    --scratch-path "$scratch_path"
  swift build \
    --package-path "$MACOS_ROOT" \
    --configuration release \
    --arch "$architecture" \
    --scratch-path "$scratch_path" \
    --show-bin-path
}

ARM64_BIN_ROOT="$(build_architecture arm64 | tail -n 1)"
X86_64_BIN_ROOT="$(build_architecture x86_64 | tail -n 1)"

lipo -create \
  "$ARM64_BIN_ROOT/PolarH10Mac" \
  "$X86_64_BIN_ROOT/PolarH10Mac" \
  -output "$EXECUTABLE_ROOT/PolarH10Mac"
chmod 755 "$EXECUTABLE_ROOT/PolarH10Mac"

cp "$MACOS_ROOT/Info.plist" "$CONTENTS_ROOT/Info.plist"
printf 'APPL????' > "$CONTENTS_ROOT/PkgInfo"
/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString $VERSION" "$CONTENTS_ROOT/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion $VERSION" "$CONTENTS_ROOT/Info.plist"

ICON_SOURCE="$REPO_ROOT/docs/assets/polarh10-stripe-mark.png"
ICONSET_ROOT="$BUILD_ROOT/AppIcon.iconset"
mkdir -p "$ICONSET_ROOT"
sips -z 16 16 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_16x16.png" >/dev/null
sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_16x16@2x.png" >/dev/null
sips -z 32 32 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_32x32.png" >/dev/null
sips -z 64 64 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_32x32@2x.png" >/dev/null
sips -z 128 128 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_128x128.png" >/dev/null
sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_128x128@2x.png" >/dev/null
sips -z 256 256 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_256x256.png" >/dev/null
sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_256x256@2x.png" >/dev/null
sips -z 512 512 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_512x512.png" >/dev/null
sips -z 1024 1024 "$ICON_SOURCE" --out "$ICONSET_ROOT/icon_512x512@2x.png" >/dev/null
iconutil -c icns "$ICONSET_ROOT" -o "$RESOURCES_ROOT/AppIcon.icns"

SIGNING_IDENTITY="${MACOS_SIGNING_IDENTITY:--}"
codesign --force --deep --options runtime --sign "$SIGNING_IDENTITY" "$APP_ROOT"
codesign --verify --deep --strict --verbose=2 "$APP_ROOT"
lipo -archs "$EXECUTABLE_ROOT/PolarH10Mac"

ditto -c -k --sequesterRsrc --keepParent "$APP_ROOT" "$ARCHIVE_PATH"
echo "Built $APP_ROOT"
echo "Archived $ARCHIVE_PATH"
