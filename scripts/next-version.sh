#!/usr/bin/env bash
# Hitung versi rilis berikutnya dari conventional commits sejak tag v* terakhir.
#
# Cetak versi tanpa awalan "v" (misalnya 0.2.0), atau string kosong kalau
# tidak ada commit yang perlu dirilis.
#
#   type!: ...  atau "BREAKING CHANGE" di body  -> major (minor selama masih 0.x)
#   feat: ...                                   -> minor
#   fix: ... / perf: ...                        -> patch
#   lainnya (docs, chore, ci, refactor, test)   -> tidak rilis
set -euo pipefail

last=$(git describe --tags --abbrev=0 --match 'v[0-9]*' 2>/dev/null || true)
range=${last:+$last..}HEAD
base=${last#v}
base=${base:-0.0.0}
IFS=. read -r major minor patch <<<"$base"

subjects=$(git log --format=%s "$range")
bodies=$(git log --format=%b "$range")
scope='(\([^)]*\))?'

if grep -Eq "^[a-z]+$scope!:" <<<"$subjects" || grep -q 'BREAKING CHANGE' <<<"$bodies"; then
  bump=major
elif grep -Eq "^feat$scope:" <<<"$subjects"; then
  bump=minor
elif grep -Eq "^(fix|perf)$scope:" <<<"$subjects"; then
  bump=patch
else
  exit 0
fi

# Selama 0.x, perubahan yang memutus kompatibilitas cukup menaikkan minor.
if [ "$bump" = major ] && [ "$major" = 0 ]; then bump=minor; fi

case $bump in
  major) echo "$((major + 1)).0.0" ;;
  minor) echo "$major.$((minor + 1)).0" ;;
  patch) echo "$major.$minor.$((patch + 1))" ;;
esac
