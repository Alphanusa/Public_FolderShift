using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace FolderShift
{
    /// <summary>Perintah dan parser output robocopy.</summary>
    public static class Robocopy
    {
        /// <summary>Salin isi + ACL + owner + auditing + atribut + timestamp, mode backup, lewati junction di dalam sumber.</summary>
        public const string Flags = "/E /COPYALL /DCOPY:DAT /B /XJ /R:1 /W:1";

        public static string Path => System.IO.Path.Combine(Environment.SystemDirectory, "robocopy.exe");

        // Backslash di ujung path akan meng-escape tanda kutip penutup, jadi dibuang.
        public static string Quote(string path) => "\"" + path.TrimEnd('\\') + "\"";

        /// <summary>Argumen untuk dijalankan aplikasi: output per file dengan ukuran byte dan path lengkap.</summary>
        public static string Args(string source, string destination) =>
            Quote(source) + " " + Quote(destination) + " " + Flags + " /BYTES /NP /NDL /FP /NJH";

        /// <summary>Perintah yang ditampilkan ke user di preview.</summary>
        public static string Preview(string source, string destination) =>
            "robocopy " + Quote(source) + " " + Quote(destination) + " " + Flags;

        /// <summary>Baris file robocopy: "\t    New File  \t\t   12345\tC:\x\y.txt".</summary>
        public static bool TryParseFile(string line, out long bytes, out string path)
        {
            bytes = 0;
            path = null;
            if (string.IsNullOrEmpty(line) || line.IndexOf('\t') < 0) return false;
            var parts = line.Split('\t').Select(s => s.Trim()).Where(s => s.Length > 0).ToArray();
            if (parts.Length < 2) return false;
            var candidate = parts[parts.Length - 1];
            if (candidate.Length < 3 || candidate[1] != ':') return false;
            if (!long.TryParse(parts[parts.Length - 2], NumberStyles.None, CultureInfo.InvariantCulture, out bytes)) return false;
            path = candidate;
            return true;
        }

        static readonly Regex ErrorLine = new Regex(@"\bERROR \d+ \(0x[0-9A-Fa-f]{8}\)", RegexOptions.Compiled);

        public static bool IsError(string line) => line != null && ErrorLine.IsMatch(line);

        /// <summary>Kode keluar robocopy di bawah 8 berarti tidak ada kegagalan.</summary>
        public static bool Succeeded(int exitCode) => exitCode >= 0 && exitCode < 8;
    }

    /// <summary>Satu proses robocopy yang bisa dijeda dan dibatalkan.</summary>
    public sealed class CopyJob
    {
        Process process;
        public bool Paused { get; private set; }

        public Task<int> RunAsync(string source, string destination, Action<string> onLine, CancellationToken ct)
        {
            var psi = new ProcessStartInfo(Robocopy.Path, Robocopy.Args(source, destination))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Shell.ConsoleEncoding,
                StandardErrorEncoding = Shell.ConsoleEncoding,
            };
            process = new Process { StartInfo = psi };
            process.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            var reg = ct.Register(() =>
            {
                try
                {
                    if (Paused) Resume();
                    if (!process.HasExited) process.Kill();
                }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            });

            return Task.Run(() =>
            {
                process.WaitForExit(); // tanpa timeout: menunggu output async selesai dibaca juga
                reg.Dispose();
                ct.ThrowIfCancellationRequested();
                return process.ExitCode;
            });
        }

        public void Pause()
        {
            if (process == null || Paused) return;
            NtSuspendProcess(process.Handle);
            Paused = true;
        }

        public void Resume()
        {
            if (process == null || !Paused) return;
            NtResumeProcess(process.Handle);
            Paused = false;
        }

        [DllImport("ntdll.dll")] static extern int NtSuspendProcess(IntPtr handle);
        [DllImport("ntdll.dll")] static extern int NtResumeProcess(IntPtr handle);
    }

    public static class Shell
    {
        public static Encoding ConsoleEncoding => Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);

        /// <summary>Jalankan perintah tanpa jendela dan kembalikan kode keluar + output.</summary>
        public static (int ExitCode, string Output) Run(string file, string args)
        {
            var psi = new ProcessStartInfo(file, args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = ConsoleEncoding,
                StandardErrorEncoding = ConsoleEncoding,
            };
            using (var p = Process.Start(psi))
            {
                var err = p.StandardError.ReadToEndAsync();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                return (p.ExitCode, (output + err.Result).Trim());
            }
        }

        public static void OpenInExplorer(string path) =>
            Process.Start(new ProcessStartInfo("explorer.exe", Robocopy.Quote(path)) { UseShellExecute = true });
    }
}
