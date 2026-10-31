# CLAUDE.md

Project ini memakai Vibe Code Starter Pack. Aturan di bawah berlaku di setiap sesi.

## Wajib di setiap sesi

1. Jawab dalam Bahasa Indonesia. Kode, perintah, path, nama fungsi, dan pesan error tetap apa adanya.
2. Tiga skill selalu aktif: `caveman` (mode `caveman`), `ponytail` (level `full`), dan `i-have-adhd`. Hook SessionStart sudah menyuntikkan aturannya. Kalau aturan salah satu skill tidak ada di context, baca `.claude/skills/<nama>/SKILL.md` lalu ikuti.
3. Matikan skill hanya kalau user minta: "stop caveman", "stop ponytail", "stop adhd mode", atau "normal mode".
4. Kalau caveman dan i-have-adhd bentrok: struktur ikut i-have-adhd, pilihan kata ikut caveman.
5. Tulisan yang disimpan di luar chat (kode, komentar, commit, PR, dokumen) memakai bahasa normal yang lengkap, bukan gaya caveman.

## Membaca kode

- Kalau `graphify-out/graph.json` ada, jalankan `graphify query "<pertanyaan>"` sebelum grep atau membaca file sumber. Pakai `graphify path` untuk relasi dan `graphify explain` untuk satu konsep. Setelah mengubah kode, jalankan `graphify update .`.
- Kalau `graphify` belum terpasang, jangan menjalankannya. Arahkan user ke README.md.
- Kalau Read ditolak read-once, isi file itu sudah ada di context dan belum berubah. Pakai yang sudah ada. Jangan membaca ulang lewat `cat`, `head`, atau tool lain.

## Merawat starter pack

- Jangan mengedit file hasil sync. Daftarnya ada di `.claude/.vendored`.
- Untuk update skill: ubah versi di `scripts/sources.txt`, lalu jalankan `bash scripts/sync-skills`.
