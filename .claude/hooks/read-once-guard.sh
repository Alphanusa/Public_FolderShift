#!/usr/bin/env bash
# PreToolUse (Read): jembatan ke read-once (.claude/read-once-hooks/hook.sh).
#
# read-once mencatat bacaan per session_id. Subagent memakai session_id yang
# sama dengan sesi utama, padahal context-nya terpisah. Tanpa wrapper ini,
# subagent ditolak membaca file yang cuma pernah dibaca sesi utama. Di sini
# catatan dipisah per subagent dengan menempelkan agent_id ke session_id.
#
# read-once butuh jq. Tanpa jq, hook ini diam dan Read berjalan normal.

command -v jq >/dev/null 2>&1 || exit 0

input=$(cat)
input=$(printf '%s' "$input" | jq -c '
  if (.agent_id // "") != "" then .session_id = "\(.session_id)-\(.agent_id)" else . end
') || exit 0

printf '%s' "$input" | bash "$(dirname "$0")/../read-once-hooks/hook.sh"
