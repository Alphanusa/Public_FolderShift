using System.IO;
using System.Windows.Input;

namespace FolderShift
{
    /// <summary>Pengaturan langsung disimpan setiap kali diubah.</summary>
    public sealed class SettingsViewModel : Observable
    {
        static AppSettings S => AppState.Settings;

        public SettingsViewModel()
        {
            PickRoot = new Command(() =>
            {
                var path = FolderPicker.Pick("Pilih folder induk tujuan");
                if (path == null) return;
                S.DestinationRoot = Safety.Normalize(path);
                Save(nameof(DestinationRoot), nameof(RootExample));
            });
            OpenLogs = new Command(() =>
            {
                Directory.CreateDirectory(Store.LogDir);
                Shell.OpenInExplorer(Store.LogDir);
            });
        }

        public ICommand PickRoot { get; }
        public ICommand OpenLogs { get; }

        public string DestinationRoot => S.DestinationRoot;
        public string RootExample => Path.Combine(S.DestinationRoot, "Claude");
        public string LogDir => Store.LogDir;

        public bool IsJunction { get => S.Kind == LinkKind.Junction; set { if (value) { S.Kind = LinkKind.Junction; Save(nameof(IsJunction), nameof(IsSymlink)); } } }
        public bool IsSymlink { get => S.Kind == LinkKind.Symlink; set { if (value) { S.Kind = LinkKind.Symlink; Save(nameof(IsJunction), nameof(IsSymlink)); } } }

        public bool IsKeepBackup { get => S.Backup == BackupPolicy.Keep; set { if (value) { S.Backup = BackupPolicy.Keep; Save(nameof(IsKeepBackup), nameof(IsAutoDelete)); } } }
        public bool IsAutoDelete { get => S.Backup == BackupPolicy.AutoDelete7Days; set { if (value) { S.Backup = BackupPolicy.AutoDelete7Days; Save(nameof(IsKeepBackup), nameof(IsAutoDelete)); } } }

        public bool IsVerifySize { get => S.Verify == VerifyMode.SizeAndTime; set { if (value) { S.Verify = VerifyMode.SizeAndTime; Save(nameof(IsVerifySize), nameof(IsVerifyHash)); } } }
        public bool IsVerifyHash { get => S.Verify == VerifyMode.Hash; set { if (value) { S.Verify = VerifyMode.Hash; Save(nameof(IsVerifySize), nameof(IsVerifyHash)); } } }

        public bool CompareAcl { get => S.CompareAcl; set { S.CompareAcl = value; Save(nameof(CompareAcl)); } }
        public bool CheckOnOpen { get => S.CheckOnOpen; set { S.CheckOnOpen = value; Save(nameof(CheckOnOpen)); } }

        void Save(params string[] names)
        {
            AppState.SaveSettings();
            Raise(names);
        }
    }
}
