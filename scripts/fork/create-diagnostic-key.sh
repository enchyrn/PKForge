#!/usr/bin/env bash
# Creates the stable key that CI signs diagnostic builds (io.github.gabzidevs.pkforge.debug)
# with, and hands it to GitHub Actions. With it, each new diagnostic APK installs over the
# last one instead of being refused for a changed signature.
#
#   scripts/fork/create-diagnostic-key.sh
#
# Unlike the release key, this one guards nothing worth backing up: if it is lost, make a
# new one with FORCE=1 and reinstall the diagnostic app once. Like Android's own debug key,
# its passwords are "android" (the workflow passes them as-is); the keystore is the secret.
# It never prints the keystore, and refuses to replace an existing one unless FORCE=1.
set -euo pipefail

REPO="${REPO:-enchyrn/PKForge}"
ALIAS="pkforge-fork-diagnostic"
DNAME="CN=PKForge Fork Diagnostics, O=gabzidevs"
VALIDITY_DAYS=10000
KEYTOOL=(mise x java@temurin-21 -- keytool)

die() { echo "error: $*" >&2; exit 1; }

for tool in mise gh base64; do command -v "$tool" >/dev/null || die "$tool is not installed"; done
secrets="$(gh secret list --repo "$REPO")" || die "gh could not list $REPO's secrets (run: gh auth status)"
if grep -qE '^PKFORGE_DIAGNOSTIC_KEYSTORE_BASE64[[:space:]]' <<<"$secrets" && [[ "${FORCE:-0}" != 1 ]]; then
  die "$REPO already has PKFORGE_DIAGNOSTIC_KEYSTORE_BASE64. Run with FORCE=1 to replace it (then reinstall the diagnostic app once)."
fi

work="$(mktemp -d)"
cleanup() { find "$work" -type f -exec rm -P {} + 2>/dev/null || true; rm -rf "$work"; }
trap cleanup EXIT
keystore="$work/pkforge-fork-diagnostic.p12"

"${KEYTOOL[@]}" -genkeypair -storetype PKCS12 -keystore "$keystore" -alias "$ALIAS" \
  -keyalg RSA -keysize 2048 -validity "$VALIDITY_DAYS" -dname "$DNAME" \
  -storepass android -keypass android >/dev/null 2>&1 || die "keytool could not create the key"
sha256="$("${KEYTOOL[@]}" -list -v -storetype PKCS12 -keystore "$keystore" -storepass android 2>/dev/null |
  awk '/SHA256:/ { print $2; exit }')"
[[ "$sha256" =~ ^([0-9A-F]{2}:){31}[0-9A-F]{2}$ ]] || die "could not read the certificate's SHA-256"

base64 -i "$keystore" | tr -d '\n' | gh secret set PKFORGE_DIAGNOSTIC_KEYSTORE_BASE64 --repo "$REPO"
gh variable set PKFORGE_DIAGNOSTIC_CERT_SHA256 --repo "$REPO" --body "$sha256"
echo "Set PKFORGE_DIAGNOSTIC_KEYSTORE_BASE64 and PKFORGE_DIAGNOSTIC_CERT_SHA256 on $REPO"
echo "Diagnostic certificate SHA-256: $sha256"
