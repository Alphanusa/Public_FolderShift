using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization.Json;
using System.Security.Principal;

namespace FolderShift
{
    /// <summary>Data aplikasi di %ProgramData%\FolderShift: settings.json, history.json, dan logs\.</summary>
    public static class Store
    {
        public static readonly string Dir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "FolderShift");

        public static string LogDir => Path.Combine(Dir, "logs");

        public static T Load<T>(string name, Func<T> fallback)
        {
            var path = Path.Combine(Dir, name);
            if (!File.Exists(path)) return fallback();
            try
            {
                using (var s = File.OpenRead(path))
                    return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(s);
            }
            catch (Exception)
            {
                // File rusak tidak ditimpa diam-diam: disimpan dulu supaya riwayat bisa dipulihkan manual.
                File.Copy(path, path + ".rusak-" + DateTime.Now.ToString("yyyyMMddHHmmss"), true);
                return fallback();
            }
        }

        public static void Save<T>(string name, T value)
        {
            Directory.CreateDirectory(Dir);
            var path = Path.Combine(Dir, name);
            var temp = path + ".tmp";
            using (var s = File.Create(temp))
                new DataContractJsonSerializer(typeof(T)).WriteObject(s, value);
            File.Copy(temp, path, true);
            File.Delete(temp);
        }
    }

    public static class AppState
    {
        public static AppSettings Settings { get; } = Store.Load("settings.json", () => new AppSettings());
        public static List<MoveRecord> History { get; } = Store.Load("history.json", () => new List<MoveRecord>());

        public static bool IsAdmin { get; } = new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

        public static event Action HistoryChanged;

        public static void SaveSettings() => Store.Save("settings.json", Settings);

        public static void SaveHistory()
        {
            Store.Save("history.json", History);
            HistoryChanged?.Invoke();
        }

        /// <summary>Folder yang tidak boleh dipindah utuh, dan folder yang seluruh isinya terlarang.</summary>
        public static string[] ExactProtected => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        }.Where(p => !string.IsNullOrEmpty(p)).ToArray();

        public static string[] TreeProtected => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "System Volume Information"),
            Path.Combine(Path.GetPathRoot(Environment.SystemDirectory), "$Recycle.Bin"),
            Store.Dir,
        };
    }

    /// <summary>Satu file log per pemindahan, berisi perintah yang dijalankan dan hasilnya.</summary>
    public sealed class MoveLog : IDisposable
    {
        readonly StreamWriter writer;
        public string FilePath { get; }

        public MoveLog(string name)
        {
            Directory.CreateDirectory(Store.LogDir);
            var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
            FilePath = Path.Combine(Store.LogDir, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + safe + ".log");
            writer = new StreamWriter(FilePath, true) { AutoFlush = true };
        }

        /// <summary>Tulis satu baris bertanda waktu dan kembalikan baris itu untuk ditampilkan.</summary>
        public string Write(string line)
        {
            var text = DateTime.Now.ToString("HH:mm:ss") + "  " + line;
            lock (writer) writer.WriteLine(text);
            return text;
        }

        public void Dispose() { lock (writer) writer.Dispose(); }

        public static void Cleanup(int days)
        {
            if (!Directory.Exists(Store.LogDir)) return;
            foreach (var f in new DirectoryInfo(Store.LogDir).GetFiles("*.log"))
                if (f.LastWriteTime < DateTime.Now.AddDays(-days))
                    try { f.Delete(); } catch (IOException) { }
        }
    }
}
