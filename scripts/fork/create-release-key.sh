#!/usr/bin/env bash
# Creates the fork's permanent Android release key, backs it up to Bitwarden and
# hands it to GitHub Actions. Run it once, yourself, in your own terminal: it
# never prints a secret, and nothing else should ever see them.
#
#   export BW_SESSION="$(bw unlock --raw)"   # or `bw login` first
#   scripts/fork/create-release-key.sh
#
# DRY_RUN=1 makes a throwaway key and stops before Bitwarden and GitHub.
# RESUME=1 makes no key: it takes the one already saved in Bitwarden, checks it
# against its recorded fingerprint, and only sets the GitHub secrets. Use it when
# a run saved the key to Bitwarden but stopped before GitHub, or to restore CI.
#
# The key is the fork's identity on every phone it is installed on: an update
# signed with any other key will not install. That is why this script refuses to
# make a new key when one already exists in Bitwarden or GitHub, and refuses to
# overwrite the keystore secret on GitHub.
set -euo pipefail

REPO="${REPO:-enchyrn/PKForge}"
ITEM_NAME="${ITEM_NAME:-PKForge Fork release key}"
ALIAS="pkforge-fork"
DNAME="CN=PKForge Fork, O=gabzidevs"
VALIDITY_DAYS=10000
KEYTOOL=(mise x java@temurin-21 -- keytool)
SHA256_FORMAT='^([0-9A-F]{2}:){31}[0-9A-F]{2}$'
P12_BEGIN='-----BEGIN PKCS12 BASE64-----'
P12_END='-----END PKCS12 BASE64-----'

die() { echo "error: $*" >&2; exit 1; }
dry_run() { [[ "${DRY_RUN:-0}" == 1 ]]; }
resume() { [[ "${RESUME:-0}" == 1 ]]; }

# The certificate SHA-256 of $1, opened with $PKF_PASSWORD, in keytool's AA:BB:... form.
fingerprint() {
  "${KEYTOOL[@]}" -list -v -storetype PKCS12 -keystore "$1" -storepass:env PKF_PASSWORD 2>/dev/null |
    awk '/SHA256:/ { print $2; exit }'
}

for tool in mise jq openssl base64; do command -v "$tool" >/dev/null || die "$tool is not installed"; done
if ! dry_run; then
  for tool in bw gh; do command -v "$tool" >/dev/null || die "$tool is not installed"; done
  if [[ "$(bw status | jq -r .status)" != unlocked ]]; then
    # shellcheck disable=SC2016 # the $(...) is an instruction for the user, not to expand here
    die 'Bitwarden is locked. Run: export BW_SESSION="$(bw unlock --raw)"'
  fi
  bw sync >/dev/null
  items="$(bw list items --search "$ITEM_NAME" | jq --arg n "$ITEM_NAME" '[.[] | select(.name == $n)]')"
  count="$(jq length <<<"$items")"
  if resume; then
    [[ "$count" == 1 ]] || die "RESUME needs exactly one Bitwarden item named \"$ITEM_NAME\"; found $count"
  else
    [[ "$count" == 0 ]] ||
      die "Bitwarden already has an item named \"$ITEM_NAME\". Not making a new key. To use that key, run with RESUME=1."
  fi
  secrets="$(gh secret list --repo "$REPO")" || die "gh could not list $REPO's secrets (run: gh auth status)"
  ! grep -qE '^PKFORGE_ANDROID_KEYSTORE_BASE64[[:space:]]' <<<"$secrets" ||
    die "$REPO already has PKFORGE_ANDROID_KEYSTORE_BASE64. Not replacing the release key."
fi

work="$(mktemp -d)"
cleanup() { find "$work" -type f -exec rm -P {} + 2>/dev/null || true; rm -rf "$work"; }
trap cleanup EXIT
keystore="$work/pkforge-fork-release.p12"

if resume && ! dry_run; then
  item="$(jq '.[0]' <<<"$items")"
  item_id="$(jq -r .id <<<"$item")"
  # A Login item: username = key alias, password = keystore password (PKCS12 uses it for the key too).
  ALIAS="$(jq -r '.login.username // empty' <<<"$item")"
  PKF_PASSWORD="$(jq -r '.login.password // empty' <<<"$item")"
  sha256="$(jq -r '[.fields[]? | select(.name == "cert_sha256") | .value][0] // empty' <<<"$item")"
  [[ -n "$ALIAS" && -n "$PKF_PASSWORD" && -n "$sha256" ]] ||
    die "the Bitwarden item is missing its username (key alias), password or cert_sha256 field"
  # The keystore lives in the note (works on every plan) and, with Premium, as an attachment too.
  attachment_id="$(jq -r '[.attachments[]? | select(.fileName | endswith(".p12")) | .id][0] // empty' <<<"$item")"
  if [[ -n "$attachment_id" ]]; then
    bw get attachment "$attachment_id" --itemid "$item_id" --output "$keystore" >/dev/null || die "could not download the keystore attachment"
  else
    jq -r '.notes // ""' <<<"$item" | awk "/^$P12_BEGIN\$/{p=1; next} /^$P12_END\$/{p=0} p" | tr -d '\n' |
      base64 -D >"$keystore" 2>/dev/null || true
    [[ -s "$keystore" ]] ||
      die "the Bitwarden item holds no keystore (no .p12 attachment, nothing in its note). No GitHub secrets exist yet, so nothing was signed with it: delete the item in Bitwarden and run this script again without RESUME."
  fi
  export PKF_PASSWORD
  [[ "$(fingerprint "$keystore")" == "$sha256" ]] ||
    die "the attached keystore doesn't open with the saved password or doesn't match cert_sha256"
  echo "Using the key saved in Bitwarden: \"$ITEM_NAME\" ($item_id)"
else
  # PKCS12 keeps one password for the store and the key; the workflow passes it as both.
  PKF_PASSWORD="$(openssl rand -base64 33 | tr -d '\n/+=')"
  export PKF_PASSWORD

  "${KEYTOOL[@]}" -genkeypair -storetype PKCS12 -keystore "$keystore" -alias "$ALIAS" \
    -keyalg RSA -keysize 4096 -validity "$VALIDITY_DAYS" -dname "$DNAME" \
    -storepass:env PKF_PASSWORD -keypass:env PKF_PASSWORD >/dev/null 2>&1 ||
    die "keytool could not create the key"
  sha256="$(fingerprint "$keystore")"
fi
[[ "$sha256" =~ $SHA256_FORMAT ]] || die "could not read the certificate's SHA-256"

if dry_run; then
  echo "dry run: made a throwaway key (alias $ALIAS), certificate SHA-256 $sha256"
  echo "dry run: would save it to Bitwarden as \"$ITEM_NAME\" and set 4 secrets + 1 variable on $REPO"
  exit 0
fi

if ! resume; then
  # Bitwarden first: if anything after this fails, the key is already backed up.
  # The keystore goes in the note itself, base64 between markers, because attachments need
  # Bitwarden Premium; it is attached as a file as well when the account allows it.
  P12_B64="$(base64 -i "$keystore" | fold -w 76)"
  export P12_B64
  item_id="$(bw get template item | ITEM_NAME="$ITEM_NAME" ALIAS="$ALIAS" SHA="$sha256" REPO="$REPO" \
    BEGIN="$P12_BEGIN" END="$P12_END" jq '
    .type = 1 | .secureNote = null | .name = env.ITEM_NAME |
    .login = { username: env.ALIAS, password: env.PKF_PASSWORD, uris: [ { uri: "https://github.com/\(env.REPO)", match: null } ], totp: null } |
    .notes = "Android release key for the PKForge fork (\(env.REPO)). The keystore (PKCS12; username = key alias, password = keystore and key password) is the base64 block below; decode it with `base64 -D`. Losing it means future updates cannot install over existing copies.\n\n\(env.BEGIN)\n\(env.P12_B64)\n\(env.END)" |
    .fields = [ { name: "cert_sha256", value: env.SHA, type: 0 } ]' | bw encode | bw create item | jq -r .id)"
  [[ -n "$item_id" && "$item_id" != null ]] || die "Bitwarden did not create the item"
  if bw create attachment --file "$keystore" --itemid "$item_id" >/dev/null 2>&1; then
    echo "Saved to Bitwarden: \"$ITEM_NAME\" ($item_id), keystore in the note and attached"
  else
    echo "Saved to Bitwarden: \"$ITEM_NAME\" ($item_id), keystore in the note (no attachment: needs Premium)"
  fi
fi

base64 -i "$keystore" | tr -d '\n' | gh secret set PKFORGE_ANDROID_KEYSTORE_BASE64 --repo "$REPO"
printf '%s' "$PKF_PASSWORD" | gh secret set PKFORGE_ANDROID_KEYSTORE_PASSWORD --repo "$REPO"
printf '%s' "$ALIAS" | gh secret set PKFORGE_ANDROID_KEY_ALIAS --repo "$REPO"
printf '%s' "$PKF_PASSWORD" | gh secret set PKFORGE_ANDROID_KEY_PASSWORD --repo "$REPO"
gh variable set PKFORGE_RELEASE_CERT_SHA256 --repo "$REPO" --body "$sha256"
echo "Set 4 secrets and PKFORGE_RELEASE_CERT_SHA256 on $REPO"
echo "Certificate SHA-256: $sha256"
