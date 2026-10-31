using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace FolderShift
{
    /// <summary>Aturan folder mana yang boleh dipindah dan ke mana. Fungsi murni supaya bisa dites.</summary>
    public static class Safety
    {
        /// <summary>Path lengkap tanpa backslash di ujung, kecuali root drive yang tetap "C:\".</summary>
        public static string Normalize(string path)
        {
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full);
            return full.Length > root.Length ? full.TrimEnd('\\') : root;
        }

        public static bool IsSameOrInside(string path, string root)
        {
            var p = Normalize(path);
            var r = Normalize(root);
            var prefix = r.EndsWith("\\") ? r : r + "\\";
            return p.Equals(r, StringComparison.OrdinalIgnoreCase)
                || p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        public static bool SameDrive(string a, string b) =>
            string.Equals(Path.GetPathRoot(Normalize(a)), Path.GetPathRoot(Normalize(b)), StringComparison.OrdinalIgnoreCase);

        /// <param name="exact">Folder yang dipakai Windows secara utuh (Program Files, Users, profil user, AppData).</param>
        /// <param name="trees">Folder yang seluruh isinya terlarang (Windows, data aplikasi ini).</param>
        /// <returns>Alasan tidak boleh, atau null kalau boleh.</returns>
        public static string SourceProblem(string source, IEnumerable<string> exact, IEnumerable<string> trees)
        {
            if (string.IsNullOrWhiteSpace(source)) return "Pilih folder yang mau dipindahkan.";
            if (!Path.IsPathRooted(source)) return "Path folder harus lengkap, misalnya C:\\Data.";
            var p = Normalize(source);
            if (p.Equals(Path.GetPathRoot(p), StringComparison.OrdinalIgnoreCase)) return "Drive utuh tidak bisa dipindahkan. Pilih folder di dalamnya.";
            if (trees.Any(t => IsSameOrInside(p, t))) return "Folder sistem tidak boleh dipindahkan.";
            if (exact.Any(e => p.Equals(Normalize(e), StringComparison.OrdinalIgnoreCase)))
                return "Folder ini dipakai Windows secara utuh. Pindahkan subfolder di dalamnya saja.";
            return null;
        }

        /// <returns>Alasan tujuan tidak boleh dipakai, atau null kalau boleh.</returns>
        public static string DestinationProblem(string source, string destination)
        {
            if (string.IsNullOrWhiteSpace(destination)) return "Pilih folder tujuan.";
            if (!Path.IsPathRooted(destination)) return "Path tujuan harus lengkap, misalnya D:\\Relokasi\\Data.";
            if (IsSameOrInside(destination, source)) return "Tujuan tidak boleh berada di dalam folder sumber.";
            if (IsSameOrInside(source, destination)) return "Tujuan tidak boleh berisi folder sumber.";
            if (SameDrive(source, destination)) return "Tujuan ada di drive yang sama dengan sumber, jadi tidak ada ruang yang lega.";
            return null;
        }

        /// <summary>Folder induk bawaan dipindah ke drive pilihan: D:\Relokasi + E + Claude = E:\Relokasi\Claude.</summary>
        public static string DefaultDestination(string root, char drive, string sourceLeaf)
        {
            var rest = string.IsNullOrWhiteSpace(root) ? "\\Relokasi" : root.Trim();
            if (rest.Length >= 2 && rest[1] == ':') rest = rest.Substring(2);
            rest = "\\" + rest.Trim('\\');
            return (char.ToUpperInvariant(drive) + ":" + rest).TrimEnd('\\') + "\\" + sourceLeaf;
        }
    }
}
