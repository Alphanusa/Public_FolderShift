#!/usr/bin/env bash
# SessionStart: aturan starter pack yang wajib di setiap sesi.
# Semua yang dicetak ke stdout masuk ke context Claude. Selalu exit 0
# supaya sesi tidak pernah gagal mulai gara-gara hook ini.
#
# Pembagian kerja dengan hook lain:
#   caveman-hooks/  → menyuntik skill caveman (butuh Node)
#   ponytail-hooks/ → menyuntik skill ponytail level full (butuh Node)
#   file ini        → bahasa, i-have-adhd, aturan gabungan, graphify,
#                     dan cadangan caveman + ponytail kalau Node tidak ada

CLAUDE_DIR=$(cd "$(dirname "$0")/.." && pwd)
PROJECT=${CLAUDE_PROJECT_DIR:-$(dirname "$CLAUDE_DIR")}
SKILLS="$CLAUDE_DIR/skills"

# Isi SKILL.md tanpa frontmatter YAML.
skill_body() {
  [ -f "$SKILLS/$1/SKILL.md" ] || return 0
  awk 'NR == 1 && /^---[[:space:]]*$/ { fm = 1; next }
       fm && /^---[[:space:]]*$/ { fm = 0; next }
       !fm' "$SKILLS/$1/SKILL.md"
}

# Isi satu bagian "## <judul>" dari file markdown, maksimal 25 baris.
md_section() {
  awk -v want="$2" '
    /^## / { on = (index($0, want) == 4) }
    on && n < 25 { print; n++ }' "$1"
}

cat <<'EOF'
# Starter pack: aturan wajib sesi ini

1. **Bahasa:** selalu jawab dalam Bahasa Indonesia. Kode, perintah, path, nama fungsi, dan pesan error tetap apa adanya.
2. **Skill yang aktif sepanjang sesi:** caveman (mode `caveman`), ponytail (level `full`), dan i-have-adhd. Matikan hanya kalau user minta ("stop caveman", "stop ponytail", "stop adhd mode", atau "normal mode").
3. **Kalau caveman dan i-have-adhd bentrok:** struktur ikut i-have-adhd (aksi berikutnya di depan, langkah bernomor, status singkat, satu aksi konkret di akhir). Pilihan kata ikut caveman (padat, tanpa basa-basi).
4. **Read ditolak read-once:** isi file itu sudah ada di context dan belum berubah. Pakai yang sudah ada. Jangan membaca ulang lewat `cat`, `head`, atau tool lain. Kalau read-once memberi diff, terapkan diff itu ke versi yang sudah kamu pegang.
EOF

echo
skill_body i-have-adhd

# Tanpa Node, hook caveman dan ponytail diam. Suntik skill-nya dari sini.
if ! command -v node >/dev/null 2>&1; then
  echo
  echo "# caveman (aktif, mode caveman)"
  skill_body caveman
  echo
  echo "# ponytail (aktif, level full: abaikan baris lite dan ultra)"
  skill_body ponytail
fi

echo
echo "# graphify"
report="$PROJECT/graphify-out/GRAPH_REPORT.md"
if [ -f "$PROJECT/graphify-out/graph.json" ]; then
  cat <<'EOF'
Repo ini punya peta graphify di `graphify-out/`.
- Untuk pertanyaan soal kode, jalankan `graphify query "<pertanyaan>"` sebelum grep, search, atau membaca file sumber. Baca file mentah hanya untuk baris yang sudah ditunjuk graph.
- Relasi dua hal: `graphify path "<A>" "<B>"`. Satu konsep: `graphify explain "<konsep>"`.
- Baca `graphify-out/GRAPH_REPORT.md` utuh hanya untuk review arsitektur besar.
- Setelah mengubah kode, jalankan `graphify update .` (tanpa biaya token).
- Masukkan aturan ini ke prompt setiap subagent yang menjelajah kode.
EOF
  if [ -f "$report" ]; then
    echo
    echo "Ringkasan peta:"
    md_section "$report" "Summary"
    md_section "$report" "God Nodes"
  fi
elif command -v graphify >/dev/null 2>&1; then
  echo "Belum ada peta graphify di repo ini. Kalau repo besar (sekitar 100 file atau lebih) dan user minta jelajah kode, tawarkan sekali untuk menjalankan \`/graphify .\`."
else
  echo "graphify belum terpasang di laptop ini. Jangan menjalankan perintah \`graphify\`. Kalau user butuh peta repo, arahkan ke bagian install di README.md."
fi

exit 0
