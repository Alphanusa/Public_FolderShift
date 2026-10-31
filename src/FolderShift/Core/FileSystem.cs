using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;

namespace FolderShift
{
    public sealed class FolderStats
    {
        public long Bytes;
        public long Files;
        public long Folders;
        /// <summary>Junction dan symlink di dalam folder. Tidak ikut disalin karena robocopy /XJ.</summary>
        public long Links;
    }

    public static class Fs
    {
        static bool IsReparse(FileSystemInfo i) => (i.Attributes & FileAttributes.ReparsePoint) != 0;

        /// <summary>Jalan ke semua file seperti robocopy /XJ: tautan di dalam folder dilewati.</summary>
        public static IEnumerable<FileInfo> Walk(string root, FolderStats stats, CancellationToken ct)
        {
            var stack = new Stack<DirectoryInfo>();
            stack.Push(new DirectoryInfo(root));
            while (stack.Count > 0)
            {
                ct.ThrowIfCancellationRequested();
                var dir = stack.Pop();
                FileSystemInfo[] items;
                // shortcut: folder yang tidak bisa dibaca bahkan oleh Admin dilewati di hitungan; robocopy /B tetap menyalinnya.
                try { items = dir.GetFileSystemInfos(); }
                catch (UnauthorizedAccessException) { continue; }
                catch (IOException) { continue; }

                foreach (var item in items)
                {
                    if (IsReparse(item)) { stats.Links++; continue; }
                    if (item is DirectoryInfo d) { stats.Folders++; stack.Push(d); continue; }
                    var f = (FileInfo)item;
                    stats.Files++;
                    stats.Bytes += f.Length;
                    yield return f;
                }
            }
        }

        public static FolderStats Scan(string root, CancellationToken ct)
        {
            var stats = new FolderStats();
            foreach (var _ in Walk(root, stats, ct)) { }
            return stats;
        }

        /// <summary>Hapus folder beserta isinya. Tautan di dalamnya hanya dilepas, targetnya tidak disentuh.</summary>
        public static void DeleteTree(string path)
        {
            var dir = new DirectoryInfo(path);
            if (!dir.Exists) return;
            if (IsReparse(dir)) { dir.Delete(false); return; }
            foreach (var f in dir.GetFiles())
            {
                if (f.IsReadOnly) f.IsReadOnly = false;
                f.Delete();
            }
            foreach (var d in dir.GetDirectories()) DeleteTree(d.FullName);
            dir.Attributes = FileAttributes.Directory;
            dir.Delete(false);
        }

        public static bool IsEmptyOrMissing(string path) =>
            !Directory.Exists(path) || !Directory.EnumerateFileSystemEntries(path).Any();

        public static string Owner(string path)
        {
            try
            {
                var sid = Directory.GetAccessControl(path, AccessControlSections.Owner).GetOwner(typeof(SecurityIdentifier));
                return Acl.Name((SecurityIdentifier)sid);
            }
            catch (Exception) { return "Tidak terbaca"; }
        }
    }

    public sealed class VerifyResult
    {
        public FolderStats Source = new FolderStats();
        public FolderStats Destination = new FolderStats();
        public long Checked;
        public long Missing;
        public long Different;
        public long Unreadable;
        public string SourceOwner;
        public string DestinationOwner;

        public bool SizeSame => Source.Bytes == Destination.Bytes;
        public bool CountSame => Source.Files == Destination.Files;
        public bool OwnerSame => SourceOwner == DestinationOwner;
        public bool ContentSame => Missing == 0 && Different == 0 && Unreadable == 0;
        public bool AllSame => SizeSame && CountSame && ContentSame;
    }

    public static class Verifier
    {
        const FileAttributes Compared = FileAttributes.ReadOnly | FileAttributes.Hidden | FileAttributes.System;

        /// <summary>Bandingkan setiap file sumber dengan pasangannya di tujuan: ukuran, waktu ubah, atribut, dan opsional hash.</summary>
        public static VerifyResult Run(string source, string destination, bool hash, Action<string> progress, CancellationToken ct)
        {
            var r = new VerifyResult();
            var root = Safety.Normalize(source).Length + 1;
            var tick = Stopwatch.StartNew();
            foreach (var f in Fs.Walk(source, r.Source, ct))
            {
                var rel = f.FullName.Substring(root);
                var other = new FileInfo(Path.Combine(destination, rel));
                r.Checked++;
                if (tick.ElapsedMilliseconds > 250) { progress?.Invoke(rel); tick.Restart(); }
                if (!other.Exists) { r.Missing++; continue; }
                if (other.Length != f.Length
                    || other.LastWriteTimeUtc != f.LastWriteTimeUtc
                    || (other.Attributes & Compared) != (f.Attributes & Compared)) { r.Different++; continue; }
                if (!hash) continue;
                try { if (!SameHash(f.FullName, other.FullName)) r.Different++; }
                catch (IOException) { r.Unreadable++; }
                catch (UnauthorizedAccessException) { r.Unreadable++; }
            }
            r.Destination = Fs.Scan(destination, ct);
            r.SourceOwner = Fs.Owner(source);
            r.DestinationOwner = Fs.Owner(destination);
            return r;
        }

        static bool SameHash(string a, string b)
        {
            using (var sha = SHA256.Create())
            using (var fa = new FileStream(a, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var fb = new FileStream(b, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            {
                var ha = sha.ComputeHash(fa);
                var hb = sha.ComputeHash(fb);
                return ha.SequenceEqual(hb);
            }
        }
    }

    public sealed class AclRow
    {
        public string Account { get; set; }
        public string Source { get; set; }
        public string Destination { get; set; }
        public StatusInfo Status { get; set; }
    }

    /// <summary>Perbandingan dan penyamaan permission folder induk sumber dan tujuan.</summary>
    public static class Acl
    {
        public static string Name(SecurityIdentifier sid)
        {
            try { return ((NTAccount)sid.Translate(typeof(NTAccount))).Value; }
            catch (IdentityNotMappedException) { return sid.Value; }
            catch (SystemException) { return sid.Value; }
        }

        public static List<AclRow> Compare(string source, string destination)
        {
            var a = Read(source);
            var b = Read(destination);
            return a.Keys.Union(b.Keys, StringComparer.OrdinalIgnoreCase)
                .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                .Select(k =>
                {
                    a.TryGetValue(k, out var sa);
                    b.TryGetValue(k, out var sb);
                    var same = string.Equals(sa, sb, StringComparison.Ordinal);
                    return new AclRow
                    {
                        Account = k,
                        Source = sa ?? "Tidak ada",
                        Destination = sb ?? "Tidak ada",
                        Status = same ? StatusInfo.Ok("Sama") : StatusInfo.Warn("Berbeda"),
                    };
                })
                .ToList();
        }

        /// <summary>Hak efektif per akun di folder itu sendiri (ACE inherit-only diabaikan).</summary>
        static Dictionary<string, string> Read(string path)
        {
            var allow = new Dictionary<string, FileSystemRights>(StringComparer.OrdinalIgnoreCase);
            var deny = new Dictionary<string, FileSystemRights>(StringComparer.OrdinalIgnoreCase);
            var rules = Directory.GetAccessControl(path, AccessControlSections.Access)
                .GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (FileSystemAccessRule rule in rules)
            {
                if ((rule.PropagationFlags & PropagationFlags.InheritOnly) != 0) continue;
                var name = Name((SecurityIdentifier)rule.IdentityReference);
                var map = rule.AccessControlType == AccessControlType.Allow ? allow : deny;
                map.TryGetValue(name, out var r);
                map[name] = r | rule.FileSystemRights;
            }
            return allow.Keys.Union(deny.Keys, StringComparer.OrdinalIgnoreCase).ToDictionary(
                k => k,
                k =>
                {
                    var text = allow.TryGetValue(k, out var al) ? Rights(al) : "";
                    if (deny.TryGetValue(k, out var de)) text += (text.Length > 0 ? "; " : "") + "Tolak " + Rights(de);
                    return text;
                },
                StringComparer.OrdinalIgnoreCase);
        }

        public static string Rights(FileSystemRights r)
        {
            const int GenericAll = 0x10000000;
            r &= ~FileSystemRights.Synchronize;
            if (((int)r & GenericAll) != 0 || (r & FileSystemRights.FullControl) == FileSystemRights.FullControl) return "Full control";
            if ((r & FileSystemRights.Modify) == FileSystemRights.Modify) return "Modify";
            if ((r & FileSystemRights.ReadAndExecute) == FileSystemRights.ReadAndExecute)
                return (r & FileSystemRights.Write) == FileSystemRights.Write ? "Read, execute, write" : "Read & execute";
            if ((r & FileSystemRights.Read) == FileSystemRights.Read) return "Read";
            if ((r & FileSystemRights.Write) == FileSystemRights.Write) return "Write";
            return "Khusus";
        }

        /// <summary>
        /// Putus warisan izin di tujuan dan pasang ACL yang sama persis dengan sumber.
        /// Windows lalu menurunkan ulang izin warisan ke isi folder tujuan.
        /// </summary>
        public static void CopyFromSource(string source, string destination)
        {
            var sddl = Directory.GetAccessControl(source, AccessControlSections.Access)
                .GetSecurityDescriptorSddlForm(AccessControlSections.Access);
            var target = new DirectorySecurity();
            target.SetSecurityDescriptorSddlForm(sddl, AccessControlSections.Access);
            target.SetAccessRuleProtection(true, true);
            Directory.SetAccessControl(destination, target);
        }
    }
}
