using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace FolderShift
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show(args.Exception.Message, "FolderShift", MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };
            Task.Run(Maintenance);
        }

        /// <summary>Bersihkan log lama dan, kalau diatur, hapus cadangan yang sudah lewat 7 hari dengan tautan sehat.</summary>
        void Maintenance()
        {
            try { MoveLog.Cleanup(90); } catch (Exception) { }
            if (AppState.Settings.Backup != BackupPolicy.AutoDelete7Days) return;

            var changed = false;
            foreach (var r in AppState.History.ToList())
            {
                if (r.Restored || r.MovedAt > DateTime.Now.AddDays(-7) || !Directory.Exists(r.Backup)) continue;
                if (Linker.Health(r).State != StatusState.Ok) continue;
                try
                {
                    using (var log = new MoveLog(r.Name + "-hapus-cadangan"))
                    {
                        log.Write("Hapus otomatis " + r.Backup + ": lebih dari 7 hari dan tautannya sehat");
                        Fs.DeleteTree(r.Backup);
                        log.Write("Selesai");
                    }
                    changed = true;
                }
                catch (Exception) { /* dicoba lagi saat aplikasi dibuka berikutnya */ }
            }
            if (changed) Dispatcher.Invoke(AppState.SaveHistory);
        }
    }
}
