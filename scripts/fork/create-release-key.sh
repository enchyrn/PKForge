#!/usr/bin/env bash
# Creates the fork's permanent Android release key, backs it up to Bitwarden and
# hands it to GitHub Actions. Run it once, yourself, in your own terminal: it
# never prints a secret, and nothing else should ever see them.
#
#   export BW_SESSION="$(bw unlock --raw)"   # or `bw login` first
#   scripts/fork/create-release-key.sh
#
# DRY_RUN=1 makes a throwaway key and stops before Bitwarden and GitHub.
#
# The key is the fork's identity on every phone it is installed on: an update
# signed with any other key will not install. That is why this script refuses to
# run when a key already exists in Bitwarden or GitHub. Restore from the
# Bitwarden item instead of making a new one.
set -euo pipefail

REPO="${REPO:-gabzidevs/PKForge}"
ITEM_NAME="${ITEM_NAME:-PKForge Fork release key}"
ALIAS="pkforge-fork"
DNAME="CN=PKForge Fork, O=gabzidevs"
VALIDITY_DAYS=10000
KEYTOOL=(mise x java@temurin-21 -- keytool)

die() { echo "error: $*" >&2; exit 1; }

for tool in mise jq openssl base64; do command -v "$tool" >/dev/null || die "$tool is not installed"; done
if [[ "${DRY_RUN:-0}" != 1 ]]; then
  for tool in bw gh; do command -v "$tool" >/dev/null || die "$tool is not installed"; done
  if [[ "$(bw status | jq -r .status)" != unlocked ]]; then
    # shellcheck disable=SC2016 # the $(...) is an instruction for the user, not to expand here
    die 'Bitwarden is locked. Run: export BW_SESSION="$(bw unlock --raw)"'
  fi
  bw sync >/dev/null
  [[ "$(bw list items --search "$ITEM_NAME" | jq --arg n "$ITEM_NAME" '[.[] | select(.name == $n)] | length')" == 0 ]] ||
    die "Bitwarden already has an item named \"$ITEM_NAME\". Not replacing the release key."
  secrets="$(gh secret list --repo "$REPO")" || die "gh could not list $REPO's secrets (run: gh auth status)"
  ! grep -qE '^PKFORGE_ANDROID_KEYSTORE_BASE64[[:space:]]' <<<"$secrets" ||
    die "$REPO already has PKFORGE_ANDROID_KEYSTORE_BASE64. Not replacing the release key."
fi

work="$(mktemp -d)"
cleanup() { find "$work" -type f -exec rm -P {} + 2>/dev/null || true; rm -rf "$work"; }
trap cleanup EXIT
keystore="$work/pkforge-fork-release.p12"

# PKCS12 keeps one password for the store and the key; the workflow passes it as both.
PKF_PASSWORD="$(openssl rand -base64 33 | tr -d '\n/+=')"
export PKF_PASSWORD

"${KEYTOOL[@]}" -genkeypair -storetype PKCS12 -keystore "$keystore" -alias "$ALIAS" \
  -keyalg RSA -keysize 4096 -validity "$VALIDITY_DAYS" -dname "$DNAME" \
  -storepass:env PKF_PASSWORD -keypass:env PKF_PASSWORD >/dev/null 2>&1 ||
  die "keytool could not create the key"

sha256="$("${KEYTOOL[@]}" -list -v -storetype PKCS12 -keystore "$keystore" -storepass:env PKF_PASSWORD 2>/dev/null |
  awk '/SHA256:/ { print $2; exit }')"
[[ "$sha256" =~ ^([0-9A-F]{2}:){31}[0-9A-F]{2}$ ]] || die "could not read the certificate's SHA-256"

if [[ "${DRY_RUN:-0}" == 1 ]]; then
  echo "dry run: made a throwaway key (alias $ALIAS), certificate SHA-256 $sha256"
  echo "dry run: would save it to Bitwarden as \"$ITEM_NAME\" and set 4 secrets + 1 variable on $REPO"
  exit 0
fi

# Bitwarden first: if anything after this fails, the key is already backed up.
item_id="$(bw get template item | ITEM_NAME="$ITEM_NAME" ALIAS="$ALIAS" SHA="$sha256" REPO="$REPO" jq '
  .type = 2 | .secureNote = { type: 0 } | .login = null | .name = env.ITEM_NAME |
  .notes = "Android release key for the PKForge fork (\(env.REPO)). The attached .p12 is the keystore. Losing it means future updates cannot install over existing copies." |
  .fields = [
    { name: "alias", value: env.ALIAS, type: 0 },
    { name: "store_password", value: env.PKF_PASSWORD, type: 1 },
    { name: "key_password", value: env.PKF_PASSWORD, type: 1 },
    { name: "cert_sha256", value: env.SHA, type: 0 }
  ]' | bw encode | bw create item | jq -r .id)"
[[ -n "$item_id" && "$item_id" != null ]] || die "Bitwarden did not create the item"
bw create attachment --file "$keystore" --itemid "$item_id" >/dev/null || die "Bitwarden did not take the keystore attachment (item $item_id was created)"
echo "Saved to Bitwarden: \"$ITEM_NAME\" ($item_id), keystore attached"

base64 -i "$keystore" | tr -d '\n' | gh secret set PKFORGE_ANDROID_KEYSTORE_BASE64 --repo "$REPO"
printf '%s' "$PKF_PASSWORD" | gh secret set PKFORGE_ANDROID_KEYSTORE_PASSWORD --repo "$REPO"
printf '%s' "$ALIAS" | gh secret set PKFORGE_ANDROID_KEY_ALIAS --repo "$REPO"
printf '%s' "$PKF_PASSWORD" | gh secret set PKFORGE_ANDROID_KEY_PASSWORD --repo "$REPO"
gh variable set PKFORGE_RELEASE_CERT_SHA256 --repo "$REPO" --body "$sha256"
echo "Set 4 secrets and PKFORGE_RELEASE_CERT_SHA256 on $REPO"
echo "Certificate SHA-256: $sha256"
