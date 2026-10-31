using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace FolderShift
{
    public sealed class HistoryItem : Observable
    {
        readonly Action<HistoryItem> onPick;
        bool selected;

        public HistoryItem(MoveRecord record, Action<HistoryItem> onPick)
        {
            Record = record;
            this.onPick = onPick;
            Refresh();
        }

        public MoveRecord Record { get; }
        public StatusInfo Status { get; private set; }
        public bool HasBackup { get; private set; }

        public string Name => Record.Name;
        public string Source => Record.Source;
        public string Destination => Record.Destination;
        public string KindText => Record.Kind == LinkKind.Junction ? "Junction" : "Symbolic link";
        public string SizeText => Format.Bytes(Record.Bytes);
        public string DateText => Format.Date(Record.MovedAt);
        public string SummaryText => SizeText + " · " + DateText;
        public string SourceDrive => Path.GetPathRoot(Record.Source).TrimEnd('\\');
        public string DestinationDrive => Path.GetPathRoot(Record.Destination).TrimEnd('\\');
        public bool IsBroken => Status.State == StatusState.Warn;
        public bool IsActive => Status.State == StatusState.Ok;

        public string BackupText => Record.Restored ? "-" : HasBackup
            ? Path.GetFileName(Record.Backup) + " masih ada di " + SourceDrive + "."
            : "Sudah dihapus.";

        public bool CanRestore => !Record.Restored && (HasBackup ? Linker.IsLink(Record.Source) : IsActive);

        public string RestoreHow =>
            Record.Restored ? "Folder ini sudah dikembalikan."
            : HasBackup ? "Cadangan masih ada, jadi cukup melepas tautan dan mengganti nama " + Path.GetFileName(Record.Backup) + " kembali. Cepat, tanpa menyalin ulang."
            : IsActive ? "Isi folder disalin balik dari " + DestinationDrive + " ke " + SourceDrive + ", lalu tautannya diganti folder biasa."
            : "Butuh drive " + DestinationDrive + " terhubung, karena isinya hanya ada di sana.";

        public string RestoreConfirm => HasBackup
            ? "Tautan dilepas, lalu " + Path.GetFileName(Record.Backup) + " diganti nama jadi " + Name + ". Tutup aplikasi yang memakainya dulu."
            : SizeText + " disalin dari " + DestinationDrive + " ke " + SourceDrive + " beserta permission-nya. Butuh beberapa menit.";

        public string BrokenText => "Aplikasi yang memakai " + Name + " tidak menemukan datanya sampai " +
            (Directory.Exists(Record.Destination) ? "tautannya dibuat ulang." : "drive " + DestinationDrive + " disambungkan lagi. Sambungkan, lalu cek ulang.");

        public bool IsSelected
        {
            get => selected;
            set { if (Set(ref selected, value) && value) onPick(this); }
        }

        internal void SetSelected(bool value) { selected = value; Raise(nameof(IsSelected)); }

        public void Refresh()
        {
            Status = Linker.Health(Record);
            HasBackup = !Record.Restored && Directory.Exists(Record.Backup);
            Raise(nameof(Status), nameof(HasBackup), nameof(IsBroken), nameof(IsActive), nameof(BackupText), nameof(CanRestore),
                nameof(RestoreHow), nameof(RestoreConfirm), nameof(BrokenText));
        }
    }

    public enum HistoryFilter { All, Active, Attention }
    public enum HistoryConfirm { None, Restore, DeleteBackup }

    public sealed class HistoryViewModel : Page
    {
        HistoryFilter filter;
        HistoryItem selected;
        HistoryConfirm confirm;
        string info;
        DateTime checkedAt;

        public HistoryViewModel()
        {
            CheckAll = new Command(Reload, () => !Busy);
            OpenFolder = new Command(() => Shell.OpenInExplorer(selected.Destination), () => selected != null && Directory.Exists(selected.Destination));
            AskRestore = new Command(() => SetConfirm(HistoryConfirm.Restore), () => !Busy && selected?.CanRestore == true);
            AskDeleteBackup = new Command(() => SetConfirm(HistoryConfirm.DeleteBackup), () => !Busy && selected?.HasBackup == true);
            CancelConfirm = new Command(() => SetConfirm(HistoryConfirm.None), () => !Busy);
            ConfirmRestore = new Command(DoRestore, () => !Busy);
            ConfirmDeleteBackup = new Command(DoDeleteBackup, () => !Busy);
            AppState.HistoryChanged += Reload;
            Reload();
        }

        public ICommand CheckAll { get; }
        public ICommand OpenFolder { get; }
        public ICommand AskRestore { get; }
        public ICommand AskDeleteBackup { get; }
        public ICommand CancelConfirm { get; }
        public ICommand ConfirmRestore { get; }
        public ICommand ConfirmDeleteBackup { get; }

        public ObservableCollection<HistoryItem> All { get; } = new ObservableCollection<HistoryItem>();
        public ObservableCollection<HistoryItem> Visible { get; } = new ObservableCollection<HistoryItem>();

        public HistoryItem Selected => selected;
        public bool HasSelection => selected != null;
        public bool HasItems => All.Count > 0;
        public bool IsEmptyFilter => All.Count > 0 && Visible.Count == 0;

        public int Count => All.Count;
        public int ActiveCount => All.Count(i => i.IsActive);
        public int AttentionCount => All.Count(i => i.IsBroken);
        public string TotalText => Format.Bytes(All.Where(i => !i.Record.Restored).Sum(i => i.Record.Bytes));
        public string AttentionNote => AttentionCount > 0 ? "Ada tautan yang perlu dicek" : "Semua tautan sehat";
        public string CheckedText => "Dicek " + Format.Date(checkedAt);

        public bool IsAll { get => filter == HistoryFilter.All; set { if (value) SetFilter(HistoryFilter.All); } }
        public bool IsActiveFilter { get => filter == HistoryFilter.Active; set { if (value) SetFilter(HistoryFilter.Active); } }
        public bool IsAttention { get => filter == HistoryFilter.Attention; set { if (value) SetFilter(HistoryFilter.Attention); } }

        public bool IsConfirmRestore => confirm == HistoryConfirm.Restore;
        public bool IsConfirmDeleteBackup => confirm == HistoryConfirm.DeleteBackup;
        public string Info { get => info; private set { Set(ref info, value); Raise(nameof(HasInfo)); } }
        public bool HasInfo => !string.IsNullOrEmpty(info);

        /// <summary>Baca ulang riwayat dan cek ulang kesehatan setiap tautan.</summary>
        public void Reload()
        {
            var keep = selected?.Record.Id;
            All.Clear();
            foreach (var r in AppState.History) All.Add(new HistoryItem(r, Pick));
            checkedAt = DateTime.Now;
            selected = null;
            ApplyFilter();
            var again = Visible.FirstOrDefault(i => i.Record.Id == keep) ?? Visible.FirstOrDefault();
            if (again != null) { again.SetSelected(true); Pick(again); }
            RaiseAll();
        }

        void SetFilter(HistoryFilter value)
        {
            filter = value;
            ApplyFilter();
            if (selected == null || !Visible.Contains(selected))
            {
                var first = Visible.FirstOrDefault();
                if (first != null) { first.SetSelected(true); Pick(first); }
                else { selected = null; }
            }
            RaiseAll();
        }

        void ApplyFilter()
        {
            Visible.Clear();
            foreach (var i in All.Where(i => filter == HistoryFilter.All
                || (filter == HistoryFilter.Active && i.IsActive)
                || (filter == HistoryFilter.Attention && i.IsBroken)))
                Visible.Add(i);
        }

        void Pick(HistoryItem item)
        {
            foreach (var i in All) if (i != item) i.SetSelected(false);
            selected = item;
            confirm = HistoryConfirm.None;
            RaiseAll();
        }

        void SetConfirm(HistoryConfirm value)
        {
            confirm = value;
            Raise(nameof(IsConfirmRestore), nameof(IsConfirmDeleteBackup));
        }

        void RaiseAll()
        {
            Raise(nameof(Selected), nameof(HasSelection), nameof(HasItems), nameof(IsEmptyFilter), nameof(Count), nameof(ActiveCount),
                nameof(AttentionCount), nameof(TotalText), nameof(AttentionNote), nameof(CheckedText), nameof(IsAll),
                nameof(IsActiveFilter), nameof(IsAttention), nameof(IsConfirmRestore), nameof(IsConfirmDeleteBackup));
            CommandManager.InvalidateRequerySuggested();
        }

        void DoRestore()
        {
            var item = selected;
            var message = item.HasBackup ? "Mengembalikan " + item.Name + "…" : "Menyalin balik " + item.Name + ". Jangan tutup aplikasi.";
            Run(async () =>
            {
                var r = item.Record;
                using (var log = new MoveLog(r.Name + "-kembalikan"))
                {
                    log.Write("Mengembalikan " + r.Source + " dari " + (item.HasBackup ? r.Backup : r.Destination));
                    if (item.HasBackup) await Task.Run(() => Mover.RestoreFromBackup(r));
                    else await Mover.RestoreByCopy(r, l => log.Write(l), CancellationToken.None);
                    log.Write("Selesai");
                }
                r.Restored = true;
                AppState.SaveHistory();
                Info = r.Name + " sudah kembali ke " + item.SourceDrive + ". Salinan di " + item.DestinationDrive +
                       " masih ada. Hapus lewat Explorer kalau sudah tidak perlu.";
            }, message);
        }

        void DoDeleteBackup()
        {
            var item = selected;
            Run(async () =>
            {
                var r = item.Record;
                using (var log = new MoveLog(r.Name + "-hapus-cadangan"))
                {
                    log.Write("Menghapus cadangan " + r.Backup);
                    await Task.Run(() => Fs.DeleteTree(r.Backup));
                    log.Write("Selesai");
                }
                Info = "Cadangan " + item.Name + " dihapus. Ruang di " + item.SourceDrive + " lega " + item.SizeText + ".";
                Reload();
            }, "Menghapus cadangan…");
        }
    }
}
