# FolderShift

Aplikasi desktop Windows untuk memindahkan folder besar dari drive sistem (C) ke drive lain tanpa merusak aplikasi yang memakainya. Isi folder disalin ke drive tujuan beserta permission-nya, lalu folder asli diganti dengan **junction** atau **symbolic link**. Aplikasi tetap membuka path lamanya seperti biasa.

## Unduh

Ambil versi terbaru di halaman [Releases](../../releases/latest):

| File | Isi |
|---|---|
| `FolderShift-vX.Y.Z.exe` | Aplikasinya langsung, satu file. Versi `-alpha`/`-beta`/`-rc` ditandai pre-release |
| `FolderShift-vX.Y.Z-win.zip` | Aplikasi + konfigurasi + lisensi font |
| `SHA256SUMS` | Checksum untuk mengecek unduhan |

Syarat: Windows 10 atau 11 dengan .NET Framework 4.8 (sudah bawaan Windows). Aplikasi selalu meminta hak Administrator lewat UAC.

## Cara kerja

1. **Pilih folder.** Pilih folder sumber, drive tujuan, dan metode tautan. Aplikasi menampilkan preview path, perintah yang akan dijalankan, dan cek awal: ruang cukup, hak Administrator, aplikasi yang masih memakai folder, dan folder sistem yang tidak boleh dipindah.
2. **Salin.** `robocopy /E /COPYALL /DCOPY:DAT /B /XJ` menyalin isi, ACL, owner, atribut, dan timestamp. Bisa dijeda atau dibatalkan. Kalau dibatalkan atau gagal, salinan di tujuan dihapus dan folder asli tidak berubah.
3. **Tinjau.** Setiap file sumber dicocokkan dengan tujuan (ukuran, waktu ubah, atribut, atau hash SHA-256 kalau diatur). Permission folder induk dibandingkan, dan bisa disamakan dengan sumber kalau berbeda.
4. **Ganti.** Folder asli diganti nama jadi `.bak`, tautan dibuat dengan `mklink /J` atau `mklink /D`, lalu tiga file acak dibaca lewat tautan. Kalau salah satu langkah gagal, semuanya dikembalikan.
5. **Selesai.** Cadangan `.bak` dihapus setelah kamu konfirmasi, atau dikembalikan ke lokasi asal.

**Riwayat** mencatat semua folder yang pernah dipindah, mengecek kesehatan tautannya, dan bisa mengembalikan folder ke lokasi asal kapan saja. **Pengaturan** menyimpan folder induk tujuan, metode bawaan, kebijakan cadangan, dan tingkat verifikasi.

Data aplikasi ada di `%ProgramData%\FolderShift`: `settings.json`, `history.json`, dan `logs\` (satu log per pemindahan, disimpan 90 hari).

### Junction atau symbolic link?

| | Junction | Symbolic link |
|---|---|---|
| Target | Folder saja | File dan folder |
| Lokasi target | Drive lokal | Lokal dan jaringan (UNC) |
| Hak Admin untuk membuat | Tidak perlu | Perlu, atau Developer Mode |
| Kompatibilitas aplikasi lama | Sangat baik | Kadang bermasalah |

Untuk memindah dari C ke drive lokal lain, junction adalah pilihan bawaan.

## Build

```powershell
dotnet test tests/FolderShift.Tests -c Release
dotnet build src/FolderShift -c Release
```

Hasilnya `src/FolderShift/bin/Release/net48/FolderShift.exe`. Butuh .NET SDK 8 di Windows. Proyek memakai WPF di .NET Framework 4.8 supaya aplikasi jalan tanpa memasang runtime tambahan.

| Path | Isi |
|---|---|
| `src/FolderShift/Core/` | Logika: robocopy, verifikasi, ACL, tautan, riwayat, pengaturan |
| `src/FolderShift/ViewModels/` | State dan perintah tiap layar |
| `src/FolderShift/Views/`, `MainWindow.xaml` | Tampilan XAML |
| `src/FolderShift/Themes/Theme.xaml` | Token warna, tipografi, dan komponen dari design system Alphanusa Account |
| `tests/FolderShift.Tests/` | Test untuk parser robocopy, aturan keamanan path, dan format angka |

## Rilis

Rilis berbasis tag dan dijalankan manual, sama seperti repo Alphanusa lain. Merge ke `master` tidak merilis apa pun.

1. Buka Actions → **Release** → Run workflow.
2. Pilih `level` (`-`, `patch`, `minor`, `major`) dan `maturity` (`-`, `alpha`, `beta`, `rc`). `dry_run` menyala secara default: baca rencananya dulu di ringkasan run.
3. Jalankan ulang dengan `dry_run` mati. Workflow menjalankan test, memotong tag `vX.Y.Z`, build exe di Windows, lalu menerbitkan GitHub Release.

Dari laptop: `scripts/release.sh minor` (rencana saja) atau `scripts/release.sh minor --push`. Kebijakan lengkap, kombinasi level × kematangan, dan hotfix ada di [docs/RILIS.md](docs/RILIS.md).

Pull request dan push ke branch selain `master` menjalankan `.github/workflows/ci.yml` (test + build, exe diunggah sebagai artifact).

## Untuk developer

Repo ini memakai Vibe Code Starter Pack di `.claude/`. Aturannya ada di `CLAUDE.md`, penjelasan lengkapnya di [docs/STARTER-PACK.md](docs/STARTER-PACK.md), dan versi tiap sumber di `SOURCES.md`.

Peta repo (opsional) memakai graphify:

```bash
pipx install graphifyy==0.9.90
```

## Lisensi font

IBM Plex Sans dan IBM Plex Mono dipakai di bawah SIL Open Font License 1.1. Teksnya ada di `src/FolderShift/Fonts/OFL.txt`.
