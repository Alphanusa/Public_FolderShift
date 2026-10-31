using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FolderShift
{
    /// <summary>Membuat, mengecek, dan melepas junction / symbolic link.</summary>
    public static class Linker
    {
        public static bool IsLink(string path)
        {
            try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        public static string Switch(LinkKind kind) => kind == LinkKind.Junction ? "/J" : "/D";

        public static string Preview(LinkKind kind, string link, string target) =>
            "mklink " + Switch(kind) + " " + Robocopy.Quote(link) + " " + Robocopy.Quote(target);

        public static void Create(LinkKind kind, string link, string target)
        {
            var r = Shell.Run("cmd.exe", "/c " + Preview(kind, link, target));
            if (r.ExitCode != 0 || !IsLink(link))
                throw new IOException("mklink gagal: " + (string.IsNullOrEmpty(r.Output) ? "kode " + r.ExitCode : r.Output));
        }

        /// <summary>Lepas tautannya saja. Isi di folder tujuan tidak tersentuh.</summary>
        public static void Remove(string link)
        {
            if (!IsLink(link)) throw new IOException(link + " bukan tautan, jadi tidak dihapus.");
            Directory.Delete(link, false);
        }

        /// <summary>Baca beberapa file acak lewat tautan untuk memastikan tautannya jalan.</summary>
        public static void TestRead(string link, int count = 3)
        {
            List<string> files;
            try { files = Directory.EnumerateFiles(link, "*", SearchOption.AllDirectories).Take(200).ToList(); }
            catch (UnauthorizedAccessException) { files = Directory.EnumerateFiles(link).Take(200).ToList(); }
            var rnd = new Random();
            var buffer = new byte[4096];
            foreach (var f in files.OrderBy(_ => rnd.Next()).Take(count))
                using (var s = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    s.Read(buffer, 0, buffer.Length);
        }

        public static StatusInfo Health(MoveRecord r)
        {
            if (r.Restored) return StatusInfo.Neutral("Dikembalikan");
            if (!IsLink(r.Source)) return Directory.Exists(r.Source) ? StatusInfo.Warn("Bukan tautan lagi") : StatusInfo.Warn("Tautan hilang");
            if (!Directory.Exists(r.Destination)) return StatusInfo.Warn("Tautan putus");
            return StatusInfo.Ok("Aktif");
        }
    }

    /// <summary>Langkah-langkah yang mengubah folder asli. Setiap langkah dikembalikan kalau gagal.</summary>
    public static class Mover
    {
        public static void Swap(string source, string destination, LinkKind kind, Action<string> log)
        {
            var backup = source.TrimEnd('\\') + ".bak";
            if (Directory.Exists(backup)) throw new IOException("Sudah ada " + backup + ". Pindahkan atau hapus dulu.");

            log("ren " + Robocopy.Quote(source) + " " + Path.GetFileName(backup));
            Directory.Move(source, backup);
            try
            {
                log(Linker.Preview(kind, source, destination));
                Linker.Create(kind, source, destination);
                log("Tes baca 3 file acak lewat tautan");
                Linker.TestRead(source);
            }
            catch (Exception ex)
            {
                log("Gagal: " + ex.Message + ". Mengembalikan folder asli.");
                if (Linker.IsLink(source)) Linker.Remove(source);
                if (!Directory.Exists(source)) Directory.Move(backup, source);
                throw;
            }
        }

        /// <summary>Cadangan masih ada: lepas tautan lalu kembalikan nama cadangan. Cepat, tanpa menyalin.</summary>
        public static void RestoreFromBackup(MoveRecord r)
        {
            Linker.Remove(r.Source);
            try { Directory.Move(r.Backup, r.Source); }
            catch
            {
                Linker.Create(r.Kind, r.Source, r.Destination);
                throw;
            }
        }

        /// <summary>Cadangan sudah dihapus: salin balik dari tujuan ke folder sementara, baru ganti tautannya.</summary>
        public static async Task RestoreByCopy(MoveRecord r, Action<string> onLine, CancellationToken ct)
        {
            var temp = r.Source.TrimEnd('\\') + ".restore";
            if (Directory.Exists(temp)) throw new IOException("Sudah ada " + temp + ". Pindahkan atau hapus dulu.");
            var code = await new CopyJob().RunAsync(r.Destination, temp, onLine, ct);
            if (!Robocopy.Succeeded(code))
            {
                Fs.DeleteTree(temp);
                throw new IOException("Penyalinan balik gagal (kode robocopy " + code + "). Tautan tidak diubah.");
            }
            Linker.Remove(r.Source);
            try { Directory.Move(temp, r.Source); }
            catch
            {
                Linker.Create(r.Kind, r.Source, r.Destination);
                throw;
            }
        }
    }

    public static class Processes
    {
        /// <summary>Nama proses yang kemungkinan memakai folder: exe-nya ada di dalam folder, atau namanya sama dengan nama folder.</summary>
        // shortcut: belum mendeteksi file yang dikunci proses lain; pakai Restart Manager API kalau ini kurang.
        public static List<string> Using(string folder)
        {
            var leaf = Path.GetFileName(folder.TrimEnd('\\'));
            var self = Process.GetCurrentProcess().Id;
            var hits = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    try
                    {
                        if (p.Id == self) continue;
                        if (string.Equals(p.ProcessName, leaf, StringComparison.OrdinalIgnoreCase)) { hits.Add(p.ProcessName); continue; }
                        var exe = p.MainModule?.FileName;
                        if (exe != null && Safety.IsSameOrInside(exe, folder)) hits.Add(p.ProcessName);
                    }
                    catch (Exception) { /* proses sistem yang tidak bisa dibaca */ }
                }
            }
            return hits.ToList();
        }
    }
}
