#!/usr/bin/env bash
set -eo pipefail

fail() { printf 'SkillSync: %s\n' "$*" >&2; exit 1; }
version=latest
install_dir="$HOME/.local/bin"
profile=
while [ "$#" -gt 0 ]; do
    case "$1" in
        --version|--install-dir|--profile)
            [ "$#" -ge 2 ] || fail "Missing value for $1"
            case "$1" in
                --version) version=$2 ;;
                --install-dir) install_dir=$2 ;;
                --profile) profile=$2 ;;
            esac
            shift 2 ;;
        *) fail "Unknown argument: $1" ;;
    esac
done
[[ "$version" == latest || "$version" =~ ^v[0-9]+\.[0-9]+\.[0-9]+(-[A-Za-z0-9]+([.-][A-Za-z0-9]+)*)?$ ]] ||
    fail 'Version must be latest or a tag such as v0.1.0-preview.4.'
command -v git >/dev/null 2>&1 || fail 'Git is required. Install Git, then run this installer again.'
for tool in curl tar mktemp install sed grep; do
    command -v "$tool" >/dev/null 2>&1 || fail "Required command is missing: $tool"
done
case "$(uname -s)" in
    Linux) platform=linux ;;
    Darwin) platform=osx ;;
    *) fail 'Supported systems: Linux with glibc and macOS.' ;;
esac
case "$(uname -m)" in
    x86_64|amd64) architecture=x64 ;;
    aarch64|arm64) architecture=arm64 ;;
    *) fail 'Supported architectures: x64 and arm64.' ;;
esac
if [ -z "$profile" ]; then
    case "$(basename "$SHELL")" in
        zsh) profile="$HOME/.zshrc"; [ -z "$ZDOTDIR" ] || profile="$ZDOTDIR/.zshrc" ;;
        bash)
            profile="$HOME/.bashrc"
            [ "$platform" != osx ] || profile="$HOME/.bash_profile" ;;
        *) fail 'Pass --profile /absolute/path/to/a/bash-or-zsh-profile for your shell.' ;;
    esac
fi
[[ "$install_dir" == /* && "$install_dir" != *:* && ! "$install_dir" =~ [[:cntrl:]] ]] ||
    fail 'Install directory must be an absolute path without colons or control characters.'
[[ "$profile" == /* && ! "$profile" =~ [[:cntrl:]] ]] ||
    fail 'Profile must be an absolute path without control characters.'
[ ! -e "$profile" ] || [ -f "$profile" ] || fail 'Profile must be a regular file.'
base_url=$SKILLSYNC_RELEASE_BASE_URL
[ -n "$base_url" ] || base_url=https://github.com/Walfhand/SkillSync/releases
case "$base_url" in
    https://*) protocols='=https' ;;
    http://127.0.0.1:*|http://localhost:*) protocols='=http,https' ;;
    *) fail 'Release URL must use HTTPS (HTTP is allowed only for a local test mirror).' ;;
esac
release_path="download/$version"
[ "$version" != latest ] || release_path=latest/download
asset="skillsync-$platform-$architecture.tar.gz"
stage=$(mktemp -d)
candidate=
trap 'rm -rf "$stage"; [ -z "$candidate" ] || rm -f "$candidate"' EXIT
download() {
    curl --fail --silent --show-error --location --connect-timeout 15 --max-time 120 \
        --proto "$protocols" --proto-redir "$protocols" "$1" --output "$2"
}
download "$base_url/$release_path/$asset" "$stage/archive.tar.gz"
download "$base_url/$release_path/$asset.sha256" "$stage/checksum"
read -r expected checksum_name < "$stage/checksum"
[[ "$expected" =~ ^[a-f0-9]{64}$ && "$checksum_name" == "$asset" ]] || fail 'Invalid SHA-256 checksum file.'
if command -v sha256sum >/dev/null 2>&1; then
    actual=$(sha256sum "$stage/archive.tar.gz")
elif command -v shasum >/dev/null 2>&1; then
    actual=$(shasum -a 256 "$stage/archive.tar.gz")
else
    fail 'SHA-256 verification requires sha256sum or shasum.'
fi
read -r actual ignored <<< "$actual"
[ "$actual" == "$expected" ] || fail 'SHA-256 mismatch; the existing installation was kept.'
# Stream only the expected entry so archive paths never choose a destination.
tar -xOzf "$stage/archive.tar.gz" -- skillsync > "$stage/skillsync"
[ -s "$stage/skillsync" ] || fail 'Archive does not contain the skillsync executable.'
mkdir -p "$install_dir" "$(dirname "$profile")"
candidate=$(mktemp "$install_dir/.skillsync.XXXXXXXX")
install -m 755 "$stage/skillsync" "$candidate"
quoted_dir=$(printf "'%s'" "$(printf '%s' "$install_dir" | sed "s/'/'\\\\''/g")")
path_line=$(printf 'case ":$PATH:" in *:%s:*) ;; *) export PATH=%s:"$PATH" ;; esac' "$quoted_dir" "$quoted_dir")
path_added=false
if ! grep -Fqx "$path_line" "$profile" 2>/dev/null; then
    printf '\n# SkillSync\n%s\n' "$path_line" >> "$profile"
    path_added=true
elif grep -F -B1 -x "$path_line" "$profile" | grep -Fqx '# SkillSync'; then
    path_added=true
fi
if command -v sha256sum >/dev/null 2>&1; then
    profile_hash=$(printf '%s' "$profile" | sha256sum)
else
    profile_hash=$(printf '%s' "$profile" | shasum -a 256)
fi
read -r profile_hash ignored <<< "$profile_hash"
receipt="$install_dir/.skillsync-path-$profile_hash.json"
json_string() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }
if [ ! -e "$receipt" ]; then
    printf '{"directory":"%s","profile":"%s","pathLine":"%s","pathAdded":%s}\n' \
        "$(json_string "$install_dir")" "$(json_string "$profile")" "$(json_string "$path_line")" "$path_added" > "$receipt"
fi
mv -f "$candidate" "$install_dir/skillsync"
candidate=
printf 'Installed SkillSync %s at %s/skillsync\nOpen a new terminal, or source %s.\n' "$version" "$install_dir" "$profile"
