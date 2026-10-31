# Vibe Code Starter Pack

Folder `.claude/` siap pakai untuk Claude Code. Isinya membuat Claude:

- menjawab singkat dan langsung ke inti (**caveman**),
- menulis kode sesedikit mungkin dan memakai ulang yang sudah ada (**ponytail**),
- menyusun jawaban yang gampang ditindaklanjuti (**i-have-adhd**),
- memakai peta repo sebelum menjelajah kode (**graphify**),
- tidak membaca file yang sama dua kali (**read-once**).

Semua skill dan script sudah ditanam di repo ini. Tidak perlu install plugin. Ketiga skill pertama otomatis aktif di setiap sesi, dan Claude selalu menjawab dalam Bahasa Indonesia.

## Isi

| Path | Isi |
|---|---|
| `.claude/skills/` | 24 skill: caveman (+15 turunannya), ponytail (+5), i-have-adhd, graphify |
| `.claude/agents/` | 3 subagent cavecrew dari caveman |
| `.claude/hooks/` | Hook milik starter pack: `session-start.sh`, `read-once-guard.sh` |
| `.claude/caveman-hooks/`, `.claude/ponytail-hooks/`, `.claude/read-once-hooks/` | Hook bawaan tiap skill |
| `.claude/settings.json` | Pendaftaran semua hook + setting default |
| `.claude/licenses/` | Lisensi tiap sumber |
| `CLAUDE.md` | Aturan cadangan kalau hook gagal |
| `scripts/sync-skills`, `scripts/sources.txt` | Update skill dari repo aslinya |
| `SOURCES.md` | Versi tiap sumber yang sedang ditanam |

## Syarat di laptop

| Program | Dipakai untuk | Kalau tidak ada |
|---|---|---|
| Git (Windows: Git for Windows, sudah termasuk Git Bash) | Semua hook bash | Hook tidak jalan |
| Node.js | Hook bawaan caveman dan ponytail | Tetap aktif lewat `session-start.sh`, tapi tanpa fitur ganti mode dan statusline |
| jq | read-once | Penahan baca ulang mati diam-diam |
| Python 3.10+ dan pipx | graphify | Peta repo tidak tersedia |

## Pasang sekali per laptop

```bash
# graphify (versi harus sama dengan SOURCES.md)
pipx install graphifyy==0.9.90

# jq
brew install jq            # macOS (macOS 15 ke atas sudah punya)
winget install jqlang.jq   # Windows
sudo apt install jq        # Ubuntu/Debian
```

Belum punya pipx? macOS: `brew install pipx`. Ubuntu: `sudo apt install pipx`. Windows: `python -m pip install --user pipx` lalu `python -m pipx ensurepath`.

## Pasang ke project

**Project baru:** buat repo dari starter pack ini. Di GitHub, buka repo ini lalu klik **Use this template**. Kalau tombolnya belum ada, owner repo bisa menyalakannya di Settings → General → Template repository.

**Project yang sudah ada:** salin ke root project:

1. Folder `.claude/` dan `scripts/`, plus file `SOURCES.md`.
2. Isi `CLAUDE.md`. Kalau project sudah punya CLAUDE.md, tempel di bagian paling atas.
3. Isi `.gitignore`.

Kalau project sudah punya `.claude/settings.json`, gabungkan isinya. Jangan ditimpa.

Buka Claude Code di project itu, lalu setujui saat diminta mempercayai folder. Ketik `/hooks` untuk memastikan hook sudah terdaftar.

## Pemakaian sehari-hari

| Ketik | Hasil |
|---|---|
| `/caveman`, `/ultracave` | Mode singkat, atau mode paling singkat |
| `stop caveman` | Matikan caveman di sesi ini |
| `/ponytail lite`, `/ponytail full`, `/ponytail ultra` | Ganti tingkat "malas nulis kode" |
| `stop ponytail` | Matikan ponytail di sesi ini |
| `stop adhd mode` | Matikan i-have-adhd di sesi ini |
| `/caveman-commit` | Pesan commit singkat |
| `/caveman-review` | Review perubahan, satu baris per temuan |
| `/ponytail-review` | Review perubahan: logika, keamanan, test, dan baris yang tidak perlu |
| `/ponytail-audit` | Audit seluruh repo: bug, celah keamanan, kode tanpa test, bagian yang bisa dihapus |
| `/ponytail-debt` | Daftar jalan pintas (`shortcut:`) yang sengaja ditunda |
| `/caveman-stats` | Pemakaian token di sesi ini |
| `/graphify .` | Bangun peta repo |

Di sesi baru setelah `stop ...`, skill otomatis aktif lagi.

## graphify

Paling terasa manfaatnya di repo besar (sekitar 100 file atau lebih) yang dikerjakan berkali-kali. Di project kecil boleh dilewati.

1. Di Claude Code, ketik `/graphify .` sekali. Kode dipetakan lokal tanpa biaya token. Dokumen, PDF, dan gambar dibaca lewat Claude, jadi makan token.
2. Supaya peta ikut diperbarui tiap commit, jalankan `graphify hook install` di terminal, sekali per clone. Perintah ini juga menambah satu baris di `.gitattributes`, dan baris itu boleh ikut di-commit.
3. Mulai sesi berikutnya, Claude otomatis mendapat ringkasan peta. Claude juga diingatkan memakai `graphify query` sebelum grep atau membaca file.

Folder `graphify-out/` tidak di-commit. Tiap laptop membangun petanya sendiri, jadi tidak ada konflik merge. Lihat komentar di `.gitignore` kalau repo kalian banyak dokumen.

## Cara kerja

| Kapan | Hook | Fungsi |
|---|---|---|
| Sesi mulai, resume, `/clear`, dan setelah compact | `hooks/session-start.sh` | Bahasa Indonesia, i-have-adhd, aturan gabungan, ringkasan graphify. Tanpa Node, ikut menyuntik caveman dan ponytail |
| | `caveman-hooks/caveman-activate.js` | Menyuntik skill caveman |
| | `ponytail-hooks/ponytail-activate.js` | Menyuntik skill ponytail level full. Peta bawaannya dimatikan (`PONYTAIL_MAP=0`) karena sudah ada graphify |
| Subagent mulai | hook subagent caveman dan ponytail | Subagent ikut aturan yang sama |
| Setiap prompt | mode tracker caveman dan ponytail | Mencatat ganti mode seperti `/ultracave` atau `stop caveman` |
| Sebelum Read | `hooks/read-once-guard.sh` | Menolak baca ulang file yang belum berubah. Kalau file berubah, Claude hanya diberi diff-nya. Catatan baca dipisah per subagent |
| Sebelum Read, Glob, Grep, Bash | `graphify hook-guard` | Mengingatkan Claude memakai peta, kalau peta ada |
| Setelah compact | `read-once-hooks/compact.sh` | Mengosongkan catatan baca, karena isi file sudah hilang dari context |
| Sesi selesai | `caveman-stats.js --record` | Mencatat statistik token |

## Update skill

1. Ubah versi di `scripts/sources.txt`. Pakai tag, branch, atau commit SHA.
2. Jalankan `bash scripts/sync-skills`. Hanya butuh bash dan git.
3. Kalau versi graphify berubah, script otomatis menyamakan baris `pipx install` di README ini. Minta tim menjalankan `pipx install --force graphifyy==<versi baru>`.
4. Cek perubahan dengan `git diff`, lalu commit.

Script hanya menghapus dan menulis ulang path yang tercatat di `.claude/.vendored`, jadi file milik project sendiri di `.claude/` aman.

## Catatan

- **Statusline:** di sesi pertama, caveman dan ponytail masing-masing menawarkan badge statusline. Claude Code hanya bisa memakai satu statusline, jadi pilih salah satu atau tolak dua-duanya. Tawarannya tidak diulang.
- **Yang sengaja tidak ditanam:** skill caveman untuk Caveman Cloud (`caveman-setup`, `caveman-discover`, `caveman-learn`, `caveman-manage`, `caveman-optimize`, `caveman-evidence-review`). Semuanya butuh layanan berbayar, dan `caveman-setup` mengalihkan request LLM repo ke gateway mereka. Hook always-on bawaan i-have-adhd juga tidak dipakai, karena perannya sudah diambil `session-start.sh`.
- **Sesi headless** (`claude -p`, Agent SDK): caveman sengaja tidak aktif di sesi ini, sesuai aturan bawaannya.
- **Lisensi:** caveman dan graphify memakai Apache-2.0. ponytail, i-have-adhd, dan read-once memakai MIT. Teks lengkapnya ada di `.claude/licenses/`, rinciannya di `SOURCES.md`.
