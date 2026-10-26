#!/usr/bin/env bash
# Publish a plugin release: build committed HEAD, zip the DLL, GitHub release, manifest entry.
# Usage: ./release.sh <version> "<changelog>"   e.g. ./release.sh 1.0.1.0 "Fix X. Add Y."
set -euo pipefail

VERSION="${1:?version required, e.g. 1.0.1.0}"
CHANGELOG="${2:?changelog required}"
cd "$(dirname "$0")"
TARGET_ABI="10.11.0.0"  # first release only; later entries copy the previous one

NAME=$(sed -n 's:.*<AssemblyName>\(.*\)</AssemblyName>.*:\1:p' *.csproj)
SLUG=$(echo "$NAME" | tr '[:upper:]' '[:lower:]')
REPO="trecoool/$SLUG"
TAG="v$VERSION"
ZIP="${SLUG}_${VERSION}.zip"

git fetch -q origin --tags
[ "$(git rev-parse HEAD)" = "$(git rev-parse origin/master)" ] || { echo "HEAD must match origin/master (push first)"; exit 1; }
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && { echo "$TAG already exists"; exit 1; }

# Build from a clean checkout of HEAD so uncommitted work never ships
WORK=$(mktemp -d)
trap 'git worktree remove --force "$WORK/src" 2>/dev/null; rm -rf "$WORK"' EXIT
git worktree add -q --detach "$WORK/src" HEAD
dotnet build "$WORK/src" -c Release -p:Version="$VERSION" -nologo -v q
(cd "$WORK/src/bin/Release/net9.0" && python -I -c "import sys,zipfile; zipfile.ZipFile(sys.argv[1],'w',zipfile.ZIP_DEFLATED).write(sys.argv[2])" "$WORK/$ZIP" "$NAME.dll")
CHECKSUM=$(md5sum "$WORK/$ZIP" | cut -d' ' -f1)

git tag "$TAG"
git push -q origin "$TAG"
gh release create "$TAG" "$WORK/$ZIP" -R "$REPO" --title "$TAG" --notes "$CHANGELOG"

python -I - "$VERSION" "$CHANGELOG" "$CHECKSUM" "https://github.com/$REPO/releases/download/$TAG/$ZIP" "$TARGET_ABI" <<'EOF'
import json, sys, datetime
version, changelog, checksum, url, default_abi = sys.argv[1:6]
with open('manifest.json', encoding='utf-8') as f:
    manifest = json.load(f)
versions = manifest[0]['versions']
versions.insert(0, {
    'version': version,
    'changelog': changelog,
    'targetAbi': versions[0]['targetAbi'] if versions else default_abi,
    'sourceUrl': url,
    'checksum': checksum,
    'timestamp': datetime.datetime.now(datetime.timezone.utc).strftime('%Y-%m-%dT%H:%M:%SZ'),
})
with open('manifest.json', 'w', encoding='utf-8', newline='\n') as f:
    json.dump(manifest, f, indent=2, ensure_ascii=False)
    f.write('\n')
EOF

git add manifest.json
git commit -q -m "Release $TAG"
git push -q origin master
echo "Released $TAG ($ZIP, md5 $CHECKSUM)"
