#!/usr/bin/env bash
# release.sh — cut a semver release tag for FolderShift.
#
# Computes the next version from the tags that are ANCESTORS of --ref, gates it,
# and pushes it. It deliberately does NOT build: the tag is the only thing that
# reaches users, and the build hangs off the tag in
# .github/workflows/release.yml.
#
#   scripts/release.sh [level] [maturity] [options]
#   scripts/release.sh --validate <version>
#
#     level     -  | major | minor | patch      (default: -)
#     maturity  -  | alpha | beta  | rc         (default: -)
#
#     -  in level     = do not bump; continue or promote an open pre-release
#     -  in maturity  = final release, no pre-release suffix
#
# Dry run is the DEFAULT. Nothing is created until you pass --push.
#
#   scripts/release.sh minor beta          # plan v0.3.0-beta.1, create nothing
#   scripts/release.sh minor beta --push   # actually cut it
#   scripts/release.sh - -   --push        # promote the open pre-release
#
# See docs/RILIS.md for the release policy this implements.
set -euo pipefail

# ── Config ───────────────────────────────────────────────────────────────────
REMOTE="${RELEASE_REMOTE:-origin}"
TAG_PREFIX="v"        # single artifact -> plain vX.Y.Z. A monorepo would set
                      # this to e.g. "backend/v" and get backend/v1.2.3.
ARTIFACT="FolderShift"
DEFAULT_REF="master"

# P4: maturity is exactly these three. semver_key() depends on their
# ALPHABETICAL order matching their meaning, and on none exceeding 5 chars.
MATURITIES="alpha beta rc"

# House rule (P3/P4/P5) — stricter than semver.org. This is the only shape this
# script is allowed to produce: suffix always hyphen-led, always numbered, and
# only the three maturities. "hotfix" can never appear.
HOUSE_RE="^${TAG_PREFIX}(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-(alpha|beta|rc)\.(0|[1-9][0-9]*))?$"

# P2 — the semver.org "Suggested regular expression", translated from PCRE to
# POSIX ERE ( (?: -> ( , \d -> [0-9] ). Applied LAST, and to the
# PREFIX-STRIPPED string: the official regex has no "v", so handing it "v1.2.3"
# turns the gate into a silent no-op. It is necessary but NOT sufficient — it
# happily accepts "1.2.3-beta" (unnumbered, breaks P3) and "1.2.3-hotfix"
# (breaks P5), which is why HOUSE_RE runs first.
SEMVER_RE='^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(-((0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*)(\.(0|[1-9][0-9]*|[0-9]*[a-zA-Z-][0-9a-zA-Z-]*))*))?(\+([0-9a-zA-Z-]+(\.[0-9a-zA-Z-]+)*))?$'
# ─────────────────────────────────────────────────────────────────────────────

PROG=${0##*/}
warn() { printf '%s: %s\n' "$PROG" "$*" >&2; }
die()  { local c=$1; shift; printf '%s: error: %s\n' "$PROG" "$*" >&2; exit "$c"; }

usage() { sed -n '2,28p' "$0" | sed 's/^# \{0,1\}//'; }

# ── Semver comparator ────────────────────────────────────────────────────────
# A decorated fixed-width key, compared byte-lexically under LC_ALL=C.
#
# Why not `sort -V`: GNU sort orders v0.3.0 BELOW v0.3.0-alpha.1 — the exact
# inverse of semver, where a pre-release ranks below its own final. Picking a
# base with `sort -V | tail -1` would therefore choose an -rc over the release
# it precedes. `git tag --sort=-v:refname` has the same defect unless
# versionsort.suffix is configured, which is repo-local git config a fresh
# laptop clone will not have.
#
#   field 1-3  zero-padded major/minor/patch  -> 0.9.0 sorts below 0.10.0
#   field 4    1 for a final, 0 for a pre-release -> a final outranks its own
#              pre-releases (P5: v1.2.3-anything is BELOW v1.2.3)
#   field 5    maturity label padded to 5 with spaces. 0x20 sorts below every
#              letter, so alpha < beta < rc falls out of P4's alphabetical rule.
#   field 6    zero-padded pre-release counter -> beta.2 below beta.10
semver_key() {
  local v=${1#"$TAG_PREFIX"} core pre lbl num MA MI PA rest
  core=${v%%-*}; pre=""
  case "$v" in *-*) pre=${v#*-} ;; esac
  MA=${core%%.*}; rest=${core#*.}; MI=${rest%%.*}; PA=${rest#*.}
  if [ -z "$pre" ]; then
    printf '%010d%010d%010d1%-5s%010d' "$MA" "$MI" "$PA" "" 0
  else
    lbl=${pre%%.*}; num=${pre##*.}
    printf '%010d%010d%010d0%-5s%010d' "$MA" "$MI" "$PA" "$lbl" "$num"
  fi
}

# Reads tags on stdin, prints the highest. Empty in -> empty out.
highest_of() {
  local t
  while read -r t; do
    [ -n "$t" ] || continue
    printf '%s\t%s\n' "$(semver_key "$t")" "$t"
  done | LC_ALL=C sort | tail -n1 | cut -f2
}

is_final()      { [[ "$1" =~ ^${TAG_PREFIX}(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$ ]]; }
is_prerelease() { [[ "$1" =~ ^${TAG_PREFIX}(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)-(alpha|beta|rc)\.(0|[1-9][0-9]*)$ ]]; }
rank()          { case "$1" in alpha) echo 1 ;; beta) echo 2 ;; rc) echo 3 ;; *) echo 0 ;; esac; }

bump() {  # bump <vX.Y.Z> <level> -> vX'.Y'.Z'
  local v=${1#"$TAG_PREFIX"} MA MI PA rest
  MA=${v%%.*}; rest=${v#*.}; MI=${rest%%.*}; PA=${rest#*.}
  case "$2" in
    major) printf '%s%d.0.0'   "$TAG_PREFIX" $((MA + 1)) ;;
    minor) printf '%s%d.%d.0'  "$TAG_PREFIX" "$MA" $((MI + 1)) ;;
    patch) printf '%s%d.%d.%d' "$TAG_PREFIX" "$MA" "$MI" $((PA + 1)) ;;
  esac
}

# ── P2 gate ──────────────────────────────────────────────────────────────────
gate() {
  local t=$1
  [[ "$t" =~ $HOUSE_RE ]] \
    || die 1 "house rule: '$t' is not ${TAG_PREFIX}X.Y.Z[-(alpha|beta|rc).N]"
  printf '%s' "${t#"$TAG_PREFIX"}" | grep -Eq "$SEMVER_RE" \
    || die 1 "semver.org gate rejected '${t#"$TAG_PREFIX"}'"
}

# ── Arguments ────────────────────────────────────────────────────────────────
LEVEL="-"; MATURITY="-"; REF=""; PUSH=0; NO_FETCH=0; MSG=""; VALIDATE=""
FORCE_NEW_LINE=0; POSITIONAL=0

positional() {
  case $POSITIONAL in
    0) LEVEL=$1 ;;
    1) MATURITY=$1 ;;
    *) die 2 "unexpected argument: $1" ;;
  esac
  POSITIONAL=$((POSITIONAL + 1))
}

while [ $# -gt 0 ]; do
  case "$1" in
    --level)     LEVEL=${2:?--level needs a value}; shift 2 ;;
    --maturity)  MATURITY=${2:?--maturity needs a value}; shift 2 ;;
    --ref)       REF=${2:?--ref needs a value}; shift 2 ;;
    --remote)    REMOTE=${2:?--remote needs a value}; shift 2 ;;
    --message|-m) MSG=${2:?--message needs a value}; shift 2 ;;
    --validate)  VALIDATE=${2:?--validate needs a value}; shift 2 ;;
    --push)      PUSH=1; shift ;;
    --dry-run)   PUSH=0; shift ;;          # default; accepted for explicitness
    --no-fetch)  NO_FETCH=1; shift ;;
    --force-new-line) FORCE_NEW_LINE=1; shift ;;
    -h|--help)   usage; exit 0 ;;
    # A bare "-" is a VALUE, not a flag: it is the playbook's "no bump" level
    # and "final" maturity. It must be matched before the -* catch-all, or
    # `release.sh - -` dies as an unknown option.
    -)           positional "$1"; shift ;;
    -*)          die 2 "unknown option: $1" ;;
    # Anything else is positional. Values are NOT whitelisted here on purpose:
    # validation below owns the error messages, so `release.sh patch hotfix`
    # reaches the P5 explanation instead of a flat "unexpected argument".
    *)           positional "$1"; shift ;;
  esac
done

# --validate: gate an existing version string and stop. Used by the workflow so
# a hand-pushed tag still meets P2 without duplicating the regexes.
if [ -n "$VALIDATE" ]; then
  gate "$VALIDATE"
  printf '%s: %s passes the house rule and the semver.org gate\n' "$PROG" "$VALIDATE" >&2
  exit 0
fi

case "$LEVEL" in -|major|minor|patch) ;; *) die 2 "level must be one of: - major minor patch (got '$LEVEL')" ;; esac
case "$MATURITY" in
  -|alpha|beta|rc) ;;
  hotfix) die 2 "P5: there is no 'hotfix' maturity. A pre-release ranks BELOW its
       plain version, so ${TAG_PREFIX}1.2.3-hotfix.1 would ship *before*
       ${TAG_PREFIX}1.2.3 and read as a downgrade. Branch from the production
       tag and cut a patch instead:  $PROG patch --ref hotfix/... --push" ;;
  *) die 2 "P4: maturity must be one of: - $MATURITIES (got '$MATURITY')" ;;
esac

git rev-parse --git-dir >/dev/null 2>&1 || die 2 "not inside a git repository"

# ── Guards ───────────────────────────────────────────────────────────────────
# P12: nothing may be written inside the worktree. A stray file here means an
# earlier step wrote where it should have used $RUNNER_TEMP, and tagging it
# would ship a tree that does not match the tag.
require_clean_worktree() {
  git update-index -q --refresh || true
  git diff-index --quiet HEAD -- \
    || die 2 "worktree has uncommitted changes; refusing to tag"
  [ -z "$(git ls-files --others --exclude-standard)" ] \
    || die 2 "worktree has untracked files (P12: never write inside the worktree)"
}

# Without this a fresh clone sees NO tags at all — `git tag --merged` returns
# empty, the base falls back to 0.0.0, and the script cheerfully re-proposes a
# version that already exists on the remote. Silent and plausible-looking.
fetch_tags() {
  if [ "$NO_FETCH" = 1 ]; then
    warn "--no-fetch: computing from LOCAL tags only. This is how you ship the wrong version."
    return
  fi
  git fetch --tags --force --prune --prune-tags "$REMOTE" --quiet \
    || die 2 "tag fetch from $REMOTE failed; refusing to compute a version from a stale tag set"
}

require_clean_worktree
fetch_tags

# ── Resolve the ref being released ───────────────────────────────────────────
[ -n "$REF" ] || REF=$DEFAULT_REF
if   SHA=$(git rev-parse -q --verify "${REF}^{commit}" 2>/dev/null); then :
elif SHA=$(git rev-parse -q --verify "${REMOTE}/${REF}^{commit}" 2>/dev/null); then :
else die 2 "cannot resolve ref '$REF' (tried '$REF' and '$REMOTE/$REF')"
fi
SHORT=$(git rev-parse --short "$SHA")

# ── P6 + P7: base = highest FINAL tag that is an ancestor of the ref ─────────
# --merged is what makes the hotfix lane work: branch off v1.4.2 while dev is
# already at v1.5.0 and this sees v1.4.2 only, so patch gives v1.4.3. A
# repo-wide "highest tag" would give v1.5.1 and ship unrelated work as a hotfix.
# Filtering to is_final makes P7 structural: a pre-release can never be a base.
ANCESTORS=$(git tag --list --merged "$SHA" "${TAG_PREFIX}*" || true)

finals=""
while read -r t; do
  [ -n "$t" ] && is_final "$t" && finals="${finals}${t}"$'\n'
done <<< "$ANCESTORS"

BASE_SYNTHETIC=0
BASE=$(printf '%s' "$finals" | highest_of)
if [ -z "$BASE" ]; then BASE="${TAG_PREFIX}0.0.0"; BASE_SYNTHETIC=1; fi

# ── The open pre-release ─────────────────────────────────────────────────────
# The highest pre-release ancestor whose core outranks BASE and whose plain tag
# does not exist yet. BASE is by construction the highest final ancestor, so
# "core above BASE" means that line has not shipped its final on this lane.
OPEN=""
pres=""
while read -r t; do
  [ -n "$t" ] || continue
  is_prerelease "$t" || continue
  core=${t%%-*}
  [ "$core" = "$BASE" ] && continue
  [ "$(printf '%s\n%s\n' "$core" "$BASE" | highest_of)" = "$BASE" ] && continue
  git rev-parse -q --verify "refs/tags/$core" >/dev/null 2>&1 && continue
  pres="${pres}${t}"$'\n'
done <<< "$ANCESTORS"
[ -n "$(printf '%s' "$pres" | tr -d '[:space:]')" ] && OPEN=$(printf '%s' "$pres" | highest_of)

OPEN_CORE=""; OPEN_LBL=""; OPEN_NUM=0
if [ -n "$OPEN" ]; then
  OPEN_CORE=${OPEN%%-*}
  OPEN_LBL=${OPEN#*-}; OPEN_LBL=${OPEN_LBL%%.*}
  OPEN_NUM=${OPEN##*.}
fi

# Highest existing counter for <core>-<label>.N, plus one. Scans every tag, not
# just ancestors, so a line opened on a sibling branch cannot be renumbered into
# a collision. max+1 rather than count+1: counting breaks the moment a tag is
# deleted.
next_counter() {
  local core=$1 lbl=$2 t n max=0
  while read -r t; do
    [ -n "$t" ] || continue
    n=${t##*.}
    case "$n" in ''|*[!0-9]*) continue ;; esac
    [ "$n" -gt "$max" ] && max=$n
  done < <(git tag --list "${core}-${lbl}.*")
  echo $((max + 1))
}

# ── Decision table ───────────────────────────────────────────────────────────
case "$LEVEL:$MATURITY" in
  -:-)
    [ -n "$OPEN" ] || die 2 "no open pre-release on this lane, so there is nothing to promote.
       Give a level instead:  $PROG minor"
    NEXT=$OPEN_CORE                       # P7: the bump already happened when
    ACTION="promote $OPEN to final"       # the line opened. Do not bump again.
    ;;
  -:*)
    [ -n "$OPEN" ] || die 2 "no open pre-release on this lane, so there is nothing to continue.
       Open one:  $PROG minor $MATURITY"
    [ "$(rank "$MATURITY")" -lt "$(rank "$OPEN_LBL")" ] \
      && die 2 "maturity cannot go backwards: the open line is at '$OPEN_LBL', you asked for '$MATURITY'"
    NEXT="${OPEN_CORE}-${MATURITY}.$(next_counter "$OPEN_CORE" "$MATURITY")"
    if [ "$MATURITY" = "$OPEN_LBL" ]; then ACTION="continue the $OPEN_LBL line on $OPEN_CORE"
    else ACTION="graduate $OPEN_CORE from $OPEN_LBL to $MATURITY"; fi
    ;;
  *:-)
    NEXT=$(bump "$BASE" "$LEVEL")
    ACTION="$LEVEL release from $BASE"
    [ -n "$OPEN" ] && [ "$OPEN_CORE" = "$NEXT" ] && ACTION="$ACTION (closes the open $OPEN)"
    ;;
  *:*)
    NEXT=$(bump "$BASE" "$LEVEL")
    if [ -n "$OPEN" ] && [ "$OPEN_CORE" != "$NEXT" ] && [ "$FORCE_NEW_LINE" = 0 ]; then
      die 2 "$OPEN is still open on a different line ($OPEN_CORE).
       Continue it:      $PROG - $MATURITY
       Or fork anyway:   $PROG $LEVEL $MATURITY --force-new-line"
    fi
    NEXT="${NEXT}-${MATURITY}.$(next_counter "$NEXT" "$MATURITY")"
    ACTION="open a $MATURITY line with a $LEVEL bump from $BASE"
    ;;
esac

gate "$NEXT"                              # P2, last, on the fully formed tag

case "$NEXT" in *-*) PRERELEASE=true ;; *) PRERELEASE=false ;; esac

# ── Collision guards ─────────────────────────────────────────────────────────
git rev-parse -q --verify "refs/tags/$NEXT" >/dev/null 2>&1 \
  && die 2 "tag $NEXT already exists locally"
git ls-remote --exit-code --tags "$REMOTE" "refs/tags/$NEXT" >/dev/null 2>&1 \
  && die 2 "tag $NEXT already exists on $REMOTE — release tags are immutable, cut the next one instead"

# ── Plan table ───────────────────────────────────────────────────────────────
base_note=""
[ "$BASE_SYNTHETIC" = 1 ] && base_note="   (synthetic — no final tag is an ancestor of $REF)"

{
  printf '\n  release plan\n'
  printf '  ─────────────────────────────────────────────────────────────────\n'
  printf '  %-18s %s\n' "artifact"   "$ARTIFACT"
  printf '  %-18s %s (%s)\n' "ref"   "$REF" "$SHORT"
  printf '  %-18s %s%s\n' "base"     "$BASE" "$base_note"
  printf '  %-18s %s\n' "open pre-release" "${OPEN:-none}"
  printf '  %-18s %s / %s\n' "level / maturity" "$LEVEL" "$MATURITY"
  printf '  %-18s %s\n' "action"     "$ACTION"
  printf '  %-18s %s\n' "next tag"   "$NEXT"
  printf '  %-18s %s\n' "pre-release" "$PRERELEASE"
  printf '  %-18s %s\n' "gates"      "house rule OK, semver.org OK"
  if [ "$PUSH" = 1 ]; then
    printf '  %-18s %s\n' "mode"     "PUSH — the tag will be created"
  else
    printf '  %-18s %s\n' "mode"     "DRY RUN — nothing will be created (add --push)"
  fi
  printf '  ─────────────────────────────────────────────────────────────────\n\n'
} >&2

# $GITHUB_STEP_SUMMARY lives under $RUNNER_TEMP, never inside the worktree, so
# writing it satisfies P12 for free. No temp file is created anywhere.
if [ -n "${GITHUB_STEP_SUMMARY:-}" ]; then
  {
    printf '### Release plan — `%s`\n\n' "$NEXT"
    printf '| | |\n|---|---|\n'
    printf '| artifact | `%s` |\n' "$ARTIFACT"
    printf '| ref | `%s` (`%s`) |\n' "$REF" "$SHORT"
    printf '| base | `%s`%s |\n' "$BASE" "$base_note"
    printf '| open pre-release | `%s` |\n' "${OPEN:-none}"
    printf '| level / maturity | `%s` / `%s` |\n' "$LEVEL" "$MATURITY"
    printf '| action | %s |\n' "$ACTION"
    printf '| **next tag** | **`%s`** |\n' "$NEXT"
    printf '| pre-release | `%s` |\n' "$PRERELEASE"
    if [ "$PUSH" = 1 ]; then printf '\n**Mode: PUSH** — the tag is being created.\n'
    else printf '\n**Mode: DRY RUN** — nothing was created. Re-run with `dry_run: false`.\n'; fi
  } >> "$GITHUB_STEP_SUMMARY"
fi

# ── Outputs ──────────────────────────────────────────────────────────────────
# `if ... fi`, never `[ ... ] && ...`: under `set -e` a trailing && whose test is
# false makes the function return non-zero, and if it is the last statement the
# whole script exits non-zero on a laptop for no reason.
emit() {
  if [ -n "${GITHUB_OUTPUT:-}" ]; then
    printf '%s=%s\n' "$1" "$2" >> "$GITHUB_OUTPUT"
  fi
}

finish() {
  emit version    "$NEXT"
  emit tag        "$NEXT"
  emit base       "$BASE"
  emit open       "$OPEN"
  emit action     "$ACTION"
  emit prerelease "$PRERELEASE"
  emit sha        "$SHA"
  emit pushed     "$1"
  printf '%s\n' "$NEXT"        # stdout is the version, and nothing else
}

if [ "$PUSH" != 1 ]; then
  finish false
  exit 0
fi

# ── P13: atomic push, and clean up the local tag if it fails ─────────────────
git tag -a "$NEXT" -m "${MSG:-$ARTIFACT $NEXT}" "$SHA"
if ! git push --atomic "$REMOTE" "refs/tags/$NEXT"; then
  git tag -d "$NEXT" >/dev/null
  die 2 "push of $NEXT failed — local tag deleted, nothing was released"
fi
warn "pushed $NEXT -> $REMOTE ($SHORT)"
finish true
