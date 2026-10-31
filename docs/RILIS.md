# Rilis FolderShift

Rilis di repo ini berbasis **tag**, mengikuti pola yang sama dengan repo
Alphanusa lain (lihat `PRODUCT_Surveilance_Cast/docs/RILIS.md`). Tag rilis
adalah satu-satunya hal yang menghasilkan file yang diunduh pengguna: merge ke
`master` tidak merilis apa pun.

---

## Lembar keputusan

```
Artefak            : FolderShift.exe (tunggal) — src/FolderShift
Bentuk tag         : vX.Y.Z   (pra-rilis: vX.Y.Z-{alpha|beta|rc}.N)
Tujuan artefak     : GitHub Releases: .exe, .zip (exe + config + lisensi font), SHA256SUMS
Registry           : tidak ada
Branch build       : tidak ada rilis per-merge; ci.yml hanya test + build untuk PR
Pemicu rilis       : formulir Actions, atau scripts/release.sh dari laptop
Ref bawaan         : master
MAJOR artinya      : data di %ProgramData%\FolderShift (history.json, settings.json)
                     tidak bisa dibaca versi lama/baru, atau syarat sistem naik
                     (misalnya bukan lagi .NET Framework 4.8 / Windows 10)
Versi awal         : v0.1.0
```

**Arti MAJOR di repo ini.** FolderShift adalah aplikasi desktop, jadi
definisinya ditulis sendiri:

- **MAJOR**: pengguna tidak bisa sekadar mengganti exe. Format `history.json` atau
  `settings.json` berubah tak-kompatibel, atau syarat sistemnya naik.
- **MINOR**: kemampuan baru yang kompatibel ke belakang.
- **PATCH**: perbaikan.

Selama versi masih `0.x`, perubahan tak-kompatibel boleh lewat MINOR, tapi wajib
disebut di catatan rilis.

**Kematangan hanya `alpha`, `beta`, `rc`.** Semver membandingkan identifier
berhuruf secara abjad, dan hanya ketiga kata itu yang urutan abjadnya sama dengan
urutan maknanya. Komparator di `scripts/release.sh` bergantung pada sifat itu.

**Tidak ada label `hotfix`.** Pra-rilis berperingkat **di bawah** versi
polosnya, jadi `v1.2.3-hotfix.1` resminya datang *sebelum* `v1.2.3`. Hotfix
adalah kenaikan patch; lihat di bawah.

---

## Merilis

### Lewat formulir Actions

Actions → **Release** → Run workflow:

| Input | Isi |
|---|---|
| `level` | `-`, `major`, `minor`, atau `patch` |
| `maturity` | `-`, `alpha`, `beta`, atau `rc` |
| `ref` | kosong = ref dari dropdown (biasanya `master`) |
| `dry_run` | **menyala secara default** |

Jalankan sekali dengan `dry_run` menyala, baca tabel rencana di ringkasan run,
lalu jalankan ulang tanpa centang. Urutannya: test di Windows, potong tag, build
exe dengan versi dari tag, lalu terbitkan GitHub Release. Tag pra-rilis
otomatis ditandai *pre-release*.

> Dropdown ref hanya memuat ref yang **sudah punya berkas workflow ini**. Branch
> hotfix yang bercabang dari tag lama tidak akan muncul di sana, jadi isi lewat
> input `ref`.

### Lewat CLI

```sh
scripts/release.sh minor beta          # rencana saja, tidak membuat apa pun
scripts/release.sh minor beta --push   # betulan
```

Dry-run adalah **default**. Skripnya sama persis dengan yang dijalankan
formulir. Tag yang didorong dari laptop memakai kredensial manusia, sehingga
memicu `on: push: tags` dan tetap membangun exe.

### Kombinasi level × kematangan

| `level` | `maturity` | Hasil dari basis `v0.2.0` |
|---|---|---|
| `minor` | `-` | `v0.3.0` |
| `minor` | `beta` | `v0.3.0-beta.1`: membuka jalur beta |
| `-` | `beta` | `v0.3.0-beta.2`: melanjutkan jalur yang terbuka |
| `-` | `rc` | `v0.3.0-rc.1`: menaikkan kematangan, nomor mulai dari 1 |
| `-` | `-` | `v0.3.0`: **promosi**, tidak naik lagi |

`-` pada level berarti "jangan naikkan": kenaikan sudah terjadi saat jalur
pra-rilis dibuka. Promosi `-rc.1` menghasilkan `v0.3.0`, bukan `v0.4.0`.

Kematangan tidak boleh mundur. Kalau jalur sudah di `rc`, meminta `beta` ditolak.

### Versi di dalam aplikasi

Build memakai `Version` = inti tag (`0.3.0`) untuk versi assembly, dan
`InformationalVersion` = tag lengkap tanpa `v` (`0.3.0-beta.1`). Sidebar aplikasi
menampilkan yang kedua, jadi pengguna bisa melihat persis rilis mana yang
mereka pakai. Build lokal tanpa tag menampilkan `v0.0.0`.

---

## Hotfix

```sh
git switch -c hotfix/v0.1.1 v0.1.0        # cabang dari TAG, bukan dari master
# ... perbaiki, commit, push branch ...
scripts/release.sh patch --ref hotfix/v0.1.1 --push
```

Ini menghasilkan `v0.1.1` walaupun `master` sudah di `v0.2.0`, karena basis
dihitung dari `git tag --merged <ref>`: hanya tag yang benar-benar leluhur ref
itu.

> **Merge branch hotfix kembali ke `master` segera.** Kalau tidak, `v0.1.1` tidak
> pernah jadi leluhur `master`, dan perbaikannya hilang dari rilis berikutnya.

---

## Kalau ada yang aneh

| Gejala | Sebab |
|---|---|
| Tag terbit, release tidak pernah muncul | Job `build` ter-skip. Di jalur formulir, `pushed` harus `true` (dry run mati); di jalur CLI, tag harus cocok `v*`. |
| Versi yang diusulkan terlalu rendah, atau `v0.1.0` lagi | Tag lokal basi. Jangan pakai `--no-fetch`. |
| `tag ... already exists on origin` | Tag rilis immutable. Naikkan ke berikutnya, jangan timpa. |
| Job `tag` gagal `Permission denied` saat push tag | Settings → Actions → General → Workflow permissions harus mengizinkan *read and write*, atau organisasi membatasinya. |
| `worktree has untracked files` | Ada langkah yang menulis ke dalam worktree; artefak build harus ke `$RUNNER_TEMP`. |
| Sidebar aplikasi menampilkan `v0.0.0` | Exe tidak dibangun oleh workflow rilis, jadi `InformationalVersion` tidak diisi. |
