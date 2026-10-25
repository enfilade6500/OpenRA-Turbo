#!/bin/bash
# Builds the Turbo downloads on Linux from a build of the three assemblies and OpenRA's official
# release-20250330 downloads (fetched once into <outdir>/upstream, or put there by hand):
#
#   OpenRA-Turbo-<ver>-release-20250330-patch.zip           the files to copy over an existing install (any OS)
#   OpenRA-Turbo-<ver>-release-20250330-x64-winportable.zip  the whole game for Windows: unzip and run
#   OpenRA-Turbo-<ver>-<Mod>-x86_64.AppImage                 the whole game for Linux, one per mod
#   SHA256SUMS.txt
#
# Each whole-game download is the official one with Turbo's files laid over it, nothing else changed; the
# AppImages even keep OpenRA's own AppImage runtime. Usage, from the checkout root after building:
#
#   turbo/package.sh <dir holding OpenRA.Game.dll OpenRA.Mods.Common.dll OpenRA.Mods.Cnc.dll> [outdir]
#
# Needs curl, unzip, zip, and appimagetool (https://github.com/AppImage/appimagetool; the AppImage itself, put in
# <outdir>/upstream or on PATH). package.cmd is the Windows counterpart for the patch zip alone.
set -euo pipefail

TAG=release-20250330
BIN=$(readlink -f "${1:?directory holding the built assemblies}")
OUT=$(readlink -f "${2:-turbo-dist}")
SRC=$(readlink -f "$(dirname "$0")/..")
UP="$OUT/upstream"
VER=$(sed -n 's/.*Version = "\([^"]*\)".*/\1/p' "$SRC/OpenRA.Game/Turbo.cs")
[ -n "$VER" ] || { echo "no Version in OpenRA.Game/Turbo.cs" >&2; exit 1; }
NAME="OpenRA-Turbo-$VER-$TAG"
RELEASES=https://github.com/OpenRA/OpenRA/releases/download/$TAG

# SHA-256 of OpenRA's official downloads, as published on its release page; a fetched file must match.
declare -A OFFICIAL=(
	["OpenRA-$TAG-x64-winportable.zip"]=880d46098dbdafaabf26a1c2411f23d60abe0a5c78f5b9eaf23c1e45ce6cb6fc
	["OpenRA-Red-Alert-x86_64.AppImage"]=3cb71c7428188c8526dd8916993fc16fa17ba5f28a35968a8267f3db9f2186fe
	["OpenRA-Tiberian-Dawn-x86_64.AppImage"]=b3d202d1bf701be5989c41c256d19bd2b1788694df01e343ade865cf1190706b
	["OpenRA-Dune-2000-x86_64.AppImage"]=7585986bf0cd488debaec3fadf853c1901003eff945dbad5de95b0b6bd781a67
)

for f in OpenRA.Game.dll OpenRA.Mods.Common.dll OpenRA.Mods.Cnc.dll; do
	[ -f "$BIN/$f" ] || { echo "missing $BIN/$f" >&2; exit 1; }
done

mkdir -p "$OUT" "$UP"
fetch() {
	[ -f "$UP/$1" ] || { echo "Fetching $1"; curl -sSL -o "$UP/$1" "$RELEASES/$1"; }
	echo "${OFFICIAL[$1]}  $UP/$1" | sha256sum -c --quiet || { echo "$1 is not the official file" >&2; exit 1; }
}

# Turbo's files, laid over a game directory. Mod files go only where the mod exists (an AppImage holds one mod).
overlay() {
	local dir=$1
	for f in OpenRA.Game OpenRA.Mods.Common OpenRA.Mods.Cnc; do
		cp "$BIN/$f.dll" "$dir/"
		if [ -f "$BIN/$f.pdb" ]; then cp "$BIN/$f.pdb" "$dir/"; else rm -f "$dir/$f.pdb"; fi
	done
	for f in mods/common/chrome/settings.yaml mods/common/chrome/settings-display.yaml \
		mods/common/chrome/settings-advanced.yaml mods/common/chrome/settings-turbo.yaml \
		mods/common/fluent/turbo.ftl mods/ra/mod.yaml mods/cnc/mod.yaml mods/cnc/chrome/settings.yaml \
		mods/d2k/mod.yaml mods/ts/mod.yaml; do
		[ -d "$dir/$(dirname "$f")" ] && cp "$SRC/$f" "$dir/$f"
	done
	return 0
}

WORK=$(mktemp -d)
trap 'rm -rf "$WORK"' EXIT
rm -f "$OUT/$NAME"*.zip "$OUT/OpenRA-Turbo-$VER-"*.AppImage "$OUT/SHA256SUMS.txt"

# 1. The patch zip: the same layout package.cmd makes, OpenRA/ holding the files as they go into an install.
echo "Patch zip"
mkdir -p "$WORK/patch/$NAME/OpenRA"/mods/{common/chrome,common/fluent,ra,cnc/chrome,d2k,ts}
overlay "$WORK/patch/$NAME/OpenRA"
cp "$SRC/README.md" "$SRC/TURBO.md" "$WORK/patch/$NAME/"
(cd "$WORK/patch" && zip -q -r -9 "$OUT/$NAME-patch.zip" "$NAME")

# 2. Windows portable: the official zip with the overlay, same flat layout (Explorer's "Extract All" makes the folder).
echo "Windows portable zip"
fetch "OpenRA-$TAG-x64-winportable.zip"
mkdir "$WORK/win" && unzip -q "$UP/OpenRA-$TAG-x64-winportable.zip" -d "$WORK/win"
overlay "$WORK/win"
cp "$SRC/README.md" "$SRC/TURBO.md" "$WORK/win/"
(cd "$WORK/win" && zip -q -r -9 "$OUT/$NAME-x64-winportable.zip" .)

# 3. Linux AppImages: the official image unpacked, the overlay applied, repacked with the official image's own runtime.
APPIMAGETOOL=$(command -v appimagetool || ls "$UP"/appimagetool-*.AppImage 2>/dev/null | head -1 || true)
if [ -z "$APPIMAGETOOL" ]; then
	echo "appimagetool not found; skipping the AppImages" >&2
else
	chmod +x "$APPIMAGETOOL"
	mkdir "$WORK/tool" && (cd "$WORK/tool" && "$APPIMAGETOOL" --appimage-extract >/dev/null)
	for mod in Red-Alert Tiberian-Dawn Dune-2000; do
		echo "AppImage: $mod"
		fetch "OpenRA-$mod-x86_64.AppImage"
		chmod +x "$UP/OpenRA-$mod-x86_64.AppImage"
		rm -rf "$WORK/app" && mkdir "$WORK/app" && cd "$WORK/app"
		"$UP/OpenRA-$mod-x86_64.AppImage" --appimage-extract >/dev/null
		head -c "$("$UP/OpenRA-$mod-x86_64.AppImage" --appimage-offset)" "$UP/OpenRA-$mod-x86_64.AppImage" > runtime
		overlay squashfs-root/usr/lib/openra
		sed -i 's/^Name=OpenRA - /Name=OpenRA Turbo - /' squashfs-root/*.desktop squashfs-root/usr/share/applications/*.desktop
		ARCH=x86_64 "$WORK/tool/squashfs-root/AppRun" --no-appstream --runtime-file runtime \
			squashfs-root "$OUT/OpenRA-Turbo-$VER-$mod-x86_64.AppImage" >/dev/null
		cd "$OUT"
	done
fi

cd "$OUT"
sha256sum "$NAME"*.zip OpenRA-Turbo-$VER-*.AppImage 2>/dev/null > SHA256SUMS.txt
cat SHA256SUMS.txt
echo "Wrote to $OUT"
