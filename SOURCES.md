# Sumber skill & hook

File ini dibuat otomatis oleh `scripts/sync-skills`. Jangan diedit manual.
Untuk ganti versi, ubah `scripts/sources.txt` lalu jalankan `bash scripts/sync-skills`.

Terakhir sync: 2026-10-14

| Nama | Repo | Versi | Commit |
|---|---|---|---|
| caveman | [JuliusBrussee/caveman](https://github.com/JuliusBrussee/caveman) | `v4.0.1` | `d262ab388295` |
| ponytail | [DietrichGebert/ponytail](https://github.com/DietrichGebert/ponytail) | `v5.2.0` | `2961b52fc1fe` |
| i-have-adhd | [ayghri/i-have-adhd](https://github.com/ayghri/i-have-adhd) | `1ecea1993b7fce257ffbd686c070948b5e07eda6` | `1ecea1993b7f` |
| graphify | [safishamsi/graphify](https://github.com/safishamsi/graphify) | `v0.9.90` | `6996bcd3380a` |
| read-once | [Bande-a-Bonnot/Boucle-framework](https://github.com/Bande-a-Bonnot/Boucle-framework) | `v0.13.0` | `4c2dc556aa67` |

Paket pip graphify yang cocok: `graphifyy==0.9.90`

## Yang ditanam

| Sumber | Masuk ke | Catatan |
|---|---|---|
| caveman | `.claude/skills/`, `.claude/agents/cavecrew-*.md`, `.claude/caveman-hooks/` | Skill Caveman Cloud tidak ikut: caveman-discover, caveman-evidence-review, caveman-learn, caveman-manage, caveman-optimize, caveman-setup |
| ponytail | `.claude/skills/ponytail*`, `.claude/ponytail-hooks/` | `package.json` di folder hook ditambahkan sync agar selalu CommonJS |
| i-have-adhd | `.claude/skills/i-have-adhd/` | Hook always-on bawaannya tidak dipakai; diganti `.claude/hooks/session-start.sh` |
| graphify | `.claude/skills/graphify/` | Hanya skill. Programnya dipasang lewat pip |
| read-once | `.claude/read-once-hooks/` | Dipanggil lewat `.claude/hooks/read-once-guard.sh` |

Daftar lengkap path hasil sync ada di `.claude/.vendored`.

## Lisensi

| Sumber | Lisensi | Teks lengkap |
|---|---|---|
| caveman | Apache-2.0 (sebagian kode lama MIT) | `.claude/licenses/caveman/` |
| ponytail | MIT | `.claude/licenses/ponytail/` |
| i-have-adhd | MIT | `.claude/licenses/i-have-adhd/` |
| graphify | Apache-2.0 (sebagian kode lama MIT) | `.claude/licenses/graphify/` |
| read-once (Boucle-framework) | MIT | `.claude/licenses/read-once/` |

File dari sumber di atas disalin tanpa diubah. "Caveman" adalah merek dagang
Julius Brussee; starter pack ini tidak berafiliasi dengan pembuat mana pun.
