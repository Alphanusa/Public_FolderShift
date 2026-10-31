using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

namespace FolderShift
{
    public enum MoveStep { Select, Copy, Review, Done }
    public enum StepState { Upcoming, Current, Done }
    public enum BackupState { Kept, Confirm, Deleted, Restored }

    public sealed class StepItem
    {
        public StepItem(int number, string label, StepState state) { Number = number; Label = label; State = state; }
        public int Number { get; }
        public string Label { get; }
        public StepState State { get; }
    }

    public sealed class DriveOption : Observable
    {
        readonly Action<DriveOption> onPick;
        bool selected;

        public DriveOption(DriveInfo d, string sourceRoot, Action<DriveOption> onPick)
        {
            this.onPick = onPick;
            Letter = char.ToUpperInvariant(d.Name[0]);
            Root = d.Name;
            Name = string.IsNullOrWhiteSpace(d.VolumeLabel) ? (d.DriveType == DriveType.Removable ? "USB" : "Lokal") : d.VolumeLabel;
            Free = d.AvailableFreeSpace;
            Total = d.TotalSize;
            IsRemovable = d.DriveType == DriveType.Removable;
            var ntfs = string.Equals(d.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase);
            var same = string.Equals(d.Name, sourceRoot, StringComparison.OrdinalIgnoreCase);
            IsEnabled = ntfs && !same;
            Note = same ? "Sama dengan drive sumber"
                : !ntfs ? "Bukan NTFS, permission tidak bisa ikut"
                : IsRemovable ? "Drive USB, bisa dicabut"
                : "Drive internal";
            NoteIsWarning = IsEnabled && IsRemovable;
        }

        public char Letter { get; }
        public string Root { get; }
        public string Title => Letter + ":";
        public string Name { get; }
        public long Free { get; }
        public long Total { get; }
        public bool IsRemovable { get; }
        public bool IsEnabled { get; }
        public string Note { get; }
        public bool NoteIsWarning { get; }
        public double UsedPercent => Total > 0 ? 100.0 * (Total - Free) / Total : 0;
        public bool NearlyFull => UsedPercent > 85;
        public string SpaceText => Format.Bytes(Free) + " kosong dari " + Format.Bytes(Total);

        public bool IsSelected
        {
            get => selected;
            set { if (Set(ref selected, value) && value) onPick(this); }
        }

        internal void SetSelected(bool value)
        {
            selected = value;
            Raise(nameof(IsSelected));
        }
    }

    /// <summary>Alur pindah folder: pilih, salin, tinjau, selesai.</summary>
    public sealed class MoveViewModel : Page
    {
        const long SpaceMargin = 1L << 30; // sisakan 1 GB di drive tujuan

        MoveStep step;

        // Pilih
        string source;
        string destination;
        LinkKind kind;
        FolderStats stats;
        List<string> usedBy;
        bool scanning;
        CancellationTokenSource scanCts;
        DriveOption selectedDrive;

        // Salin
        string planSource, planDestination;
        LinkKind planKind;
        long totalBytes, totalFiles;
        long copiedBytes, copiedFiles;
        volatile string currentFile;
        bool verifying;
        CopyJob copyJob;
        CancellationTokenSource copyCts;
        Stopwatch watch;
        MoveLog log;
        DateTime startedAt;
        readonly ConcurrentQueue<string> pendingLog = new ConcurrentQueue<string>();
        readonly LinkedList<string> logLines = new LinkedList<string>();
        readonly DispatcherTimer timer;

        // Tinjau
        VerifyResult verify;
        bool aclFixed;

        // Selesai
        MoveRecord record;
        BackupState backupState;

        public MoveViewModel(Action goHistory)
        {
            timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            timer.Tick += (s, e) => UpdateProgress();

            PickSource = new Command(DoPickSource, () => !Busy);
            PickDestination = new Command(DoPickDestination, () => !Busy && HasSource);
            Recheck = new Command(() => { LoadDrives(); RefreshSource(); }, () => !Busy);
            Start = new Command(DoStart, () => CanStart);
            TogglePause = new Command(DoTogglePause, () => copyJob != null);
            CancelCopy = new Command(() => copyCts?.Cancel(), () => copyCts != null && !verifying);
            OpenLog = new Command(() => { if (log != null) Shell.OpenInExplorer(log.FilePath); }, () => log != null);
            FixAcl = new Command(DoFixAcl, () => !Busy && HasAclDiff);
            Swap = new Command(DoSwap, () => !Busy && CanSwap);
            Abort = new Command(DoAbort, () => !Busy);
            AskDeleteBackup = new Command(() => SetBackupState(BackupState.Confirm), () => !Busy);
            CancelDeleteBackup = new Command(() => SetBackupState(BackupState.Kept), () => !Busy);
            DeleteBackup = new Command(DoDeleteBackup, () => !Busy);
            RestoreBackup = new Command(DoRestoreBackup, () => !Busy);
            OpenDestination = new Command(() => Shell.OpenInExplorer(record.Destination), () => record != null);
            NewMove = new Command(() => Reset(false), () => !Busy);
            GoHistory = new Command(goHistory);

            Reset(false);
        }

        public ICommand PickSource { get; }
        public ICommand PickDestination { get; }
        public ICommand Recheck { get; }
        public ICommand Start { get; }
        public ICommand TogglePause { get; }
        public ICommand CancelCopy { get; }
        public ICommand OpenLog { get; }
        public ICommand FixAcl { get; }
        public ICommand Swap { get; }
        public ICommand Abort { get; }
        public ICommand AskDeleteBackup { get; }
        public ICommand CancelDeleteBackup { get; }
        public ICommand DeleteBackup { get; }
        public ICommand RestoreBackup { get; }
        public ICommand OpenDestination { get; }
        public ICommand NewMove { get; }
        public ICommand GoHistory { get; }

        // ---------- Langkah ----------

        public bool IsSelect => step == MoveStep.Select;
        public bool IsCopy => step == MoveStep.Copy;
        public bool IsReview => step == MoveStep.Review;
        public bool IsDone => step == MoveStep.Done;
        public ObservableCollection<StepItem> Steps { get; } = new ObservableCollection<StepItem>();

        void SetStep(MoveStep value)
        {
            step = value;
            Steps.Clear();
            var labels = new[] { "Pilih folder", "Salin", "Tinjau", "Selesai" };
            for (var i = 0; i < labels.Length; i++)
            {
                var state = i < (int)value ? StepState.Done : i == (int)value ? StepState.Current : StepState.Upcoming;
                if (value == MoveStep.Done && i == 3) state = StepState.Current;
                Steps.Add(new StepItem(i + 1, labels[i], state));
            }
            Raise(nameof(IsSelect), nameof(IsCopy), nameof(IsReview), nameof(IsDone));
        }

        void Reset(bool keepSource)
        {
            timer.Stop();
            copyJob = null;
            copyCts = null;
            verify = null;
            aclFixed = false;
            AclRows.Clear();
            record = null;
            verifying = false;
            logLines.Clear();
            if (!keepSource)
            {
                log?.Dispose();
                log = null;
                kind = AppState.Settings.Kind;
                source = null;
                destination = null;
            }
            SetStep(MoveStep.Select);
            LoadDrives();
            RefreshSource();
        }

        // ---------- Pilih ----------

        public bool HasSource => !string.IsNullOrEmpty(source);
        public string Source => source;
        public string SourceText => HasSource ? source : "Belum dipilih";
        public string Leaf => HasSource ? Path.GetFileName(source.TrimEnd('\\')) : "";
        public string SourceDrive => HasSource ? Path.GetPathRoot(source).TrimEnd('\\') : "C:";
        public string DestinationDrive => string.IsNullOrEmpty(destination) ? "" : Path.GetPathRoot(destination).TrimEnd('\\');
        public bool Scanning { get => scanning; private set => Set(ref scanning, value); }
        public string SizeText => stats != null ? Format.Bytes(stats.Bytes) : Scanning ? "Menghitung…" : "-";
        public string ContentText => stats != null ? Format.Number(stats.Files) + " file, " + Format.Number(stats.Folders) + " folder" : "-";
        public string UsedByText => usedBy == null ? "-" : usedBy.Count == 0 ? "Tidak ada yang berjalan" : string.Join(", ", usedBy);

        public ObservableCollection<DriveOption> Drives { get; } = new ObservableCollection<DriveOption>();

        public string Destination => destination ?? "";
        public string DestinationText => string.IsNullOrEmpty(destination) ? "Pilih folder sumber dulu" : destination;

        public bool IsJunction
        {
            get => kind == LinkKind.Junction;
            set { if (value) { kind = LinkKind.Junction; RaiseSelect(); } }
        }
        public bool IsSymlink
        {
            get => kind == LinkKind.Symlink;
            set { if (value) { kind = LinkKind.Symlink; RaiseSelect(); } }
        }
        public string LinkName => kind == LinkKind.Junction ? "Junction" : "Symbolic link";
        public string BackupPath => HasSource ? source.TrimEnd('\\') + ".bak" : "";
        public string SourceSummary => "Folder asli, " + SizeText + " di drive " + SourceDrive;
        public string DestinationSummary => "Isi folder, " + SizeText + " di drive " + DestinationDrive;
        public string CommandPreview => !HasSource || string.IsNullOrEmpty(destination) ? "-" :
            Robocopy.Preview(source, destination) + "\n" +
            "ren " + Robocopy.Quote(source) + " " + Path.GetFileName(BackupPath) + "\n" +
            Linker.Preview(kind, source, destination);

        public ObservableCollection<CheckItem> Checks { get; } = new ObservableCollection<CheckItem>();
        public bool CanStart => IsSelect && !Busy && !Scanning && Checks.Count > 0 && Checks.All(c => !c.Blocking);

        void DoPickSource()
        {
            var path = FolderPicker.Pick("Pilih folder yang mau dipindahkan");
            if (path == null) return;
            source = Safety.Normalize(path);
            destination = null;
            Error = null;
            LoadDrives();
            RefreshSource();
        }

        void DoPickDestination()
        {
            var parent = FolderPicker.Pick("Pilih folder induk tujuan");
            if (parent == null) return;
            destination = Safety.Normalize(Path.Combine(parent, Leaf));
            var root = Path.GetPathRoot(destination);
            foreach (var d in Drives) d.SetSelected(string.Equals(d.Root, root, StringComparison.OrdinalIgnoreCase));
            selectedDrive = Drives.FirstOrDefault(d => d.IsSelected);
            RaiseSelect();
        }

        void LoadDrives()
        {
            var previous = selectedDrive?.Letter;
            var sourceRoot = HasSource ? Path.GetPathRoot(source) : Path.GetPathRoot(Environment.SystemDirectory);
            Drives.Clear();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if ((d.DriveType == DriveType.Fixed || d.DriveType == DriveType.Removable) && d.IsReady)
                        Drives.Add(new DriveOption(d, sourceRoot, SelectDrive));
                }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }

            var settingsLetter = AppState.Settings.DestinationRoot?.Length > 1 ? char.ToUpperInvariant(AppState.Settings.DestinationRoot[0]) : '\0';
            var pick = Drives.FirstOrDefault(d => d.IsEnabled && d.Letter == previous)
                ?? Drives.FirstOrDefault(d => d.IsEnabled && d.Letter == settingsLetter)
                ?? Drives.FirstOrDefault(d => d.IsEnabled && !d.IsRemovable)
                ?? Drives.FirstOrDefault(d => d.IsEnabled);
            selectedDrive = null;
            if (pick != null) { pick.SetSelected(true); SelectDrive(pick); }
        }

        void SelectDrive(DriveOption drive)
        {
            foreach (var d in Drives) if (d != drive) d.SetSelected(false);
            selectedDrive = drive;
            if (HasSource) destination = Safety.DefaultDestination(AppState.Settings.DestinationRoot, drive.Letter, Leaf);
            RaiseSelect();
        }

        void RefreshSource()
        {
            scanCts?.Cancel();
            stats = null;
            usedBy = null;
            Scanning = false;
            if (HasSource && Directory.Exists(source) && !Linker.IsLink(source))
            {
                var cts = scanCts = new CancellationTokenSource();
                Scanning = true;
                ScanAsync(source, cts);
            }
            RaiseSelect();
        }

        async void ScanAsync(string path, CancellationTokenSource cts)
        {
            try
            {
                var s = await Task.Run(() => Fs.Scan(path, cts.Token));
                var u = await Task.Run(() => Processes.Using(path));
                if (cts.IsCancellationRequested) return;
                stats = s;
                usedBy = u;
            }
            catch (OperationCanceledException) { return; }
            catch (Exception ex) { Error = ex.Message; }
            if (scanCts == cts) Scanning = false;
            RaiseSelect();
        }

        void RaiseSelect()
        {
            Checks.Clear();
            foreach (var c in BuildChecks()) Checks.Add(c);
            Raise(nameof(HasSource), nameof(Source), nameof(SourceText), nameof(Leaf), nameof(SourceDrive), nameof(DestinationDrive),
                nameof(SizeText), nameof(ContentText), nameof(UsedByText), nameof(Destination), nameof(DestinationText),
                nameof(IsJunction), nameof(IsSymlink), nameof(LinkName), nameof(BackupPath), nameof(SourceSummary),
                nameof(DestinationSummary), nameof(CommandPreview), nameof(CanStart));
            CommandManager.InvalidateRequerySuggested();
        }

        static CheckItem Pass(string title, string detail) => new CheckItem(title, detail, StatusInfo.Ok("Lolos"), false);
        static CheckItem Stop(string title, string detail) => new CheckItem(title, detail, StatusInfo.Block("Tidak bisa"), true);

        List<CheckItem> BuildChecks()
        {
            var list = new List<CheckItem>();
            try
            {
                var srcProblem = Safety.SourceProblem(source, AppState.ExactProtected, AppState.TreeProtected);
                if (srcProblem == null && !Directory.Exists(source)) srcProblem = "Folder sumber tidak ditemukan.";
                if (srcProblem == null && Linker.IsLink(source)) srcProblem = "Folder ini sudah berupa tautan. Kelola lewat Riwayat.";
                if (srcProblem == null && Directory.Exists(BackupPath)) srcProblem = "Sudah ada " + BackupPath + " dari pemindahan sebelumnya. Hapus atau pindahkan dulu.";
                if (srcProblem != null)
                {
                    list.Add(Stop(HasSource ? "Folder sumber belum bisa dipakai" : "Belum ada folder sumber", srcProblem));
                    return list;
                }

                var dstProblem = Safety.DestinationProblem(source, destination);
                if (dstProblem == null && !Fs.IsEmptyOrMissing(destination)) dstProblem = "Folder tujuan sudah berisi file. Pilih folder lain atau kosongkan dulu.";
                if (dstProblem != null) list.Add(Stop("Tujuan belum bisa dipakai", dstProblem));

                if (stats == null)
                {
                    list.Add(new CheckItem("Menghitung ukuran folder", "Sekaligus mengecek aplikasi yang sedang memakainya.", StatusInfo.Neutral("Berjalan"), true));
                }
                else if (dstProblem == null)
                {
                    long free = -1;
                    try { free = new DriveInfo(Path.GetPathRoot(destination)).AvailableFreeSpace; } catch (Exception) { }
                    if (free < 0) list.Add(Stop("Drive " + DestinationDrive + " tidak terbaca", "Pastikan drive tujuan terhubung."));
                    else if (free < stats.Bytes + SpaceMargin)
                        list.Add(Stop("Ruang drive " + DestinationDrive + " kurang", Format.Bytes(free) + " kosong, butuh " + Format.Bytes(stats.Bytes) + " ditambah sisa 1 GB."));
                    else list.Add(Pass("Ruang drive " + DestinationDrive + " cukup", Format.Bytes(free) + " kosong, butuh " + Format.Bytes(stats.Bytes) + "."));
                }

                list.Add(AppState.IsAdmin
                    ? Pass("Berjalan sebagai Administrator", "Permission, owner, dan atribut ikut tersalin.")
                    : Stop("Tidak berjalan sebagai Administrator", "Tutup aplikasi, lalu buka lagi dengan Run as administrator."));

                if (usedBy != null)
                {
                    if (usedBy.Count > 0)
                        list.Add(new CheckItem(string.Join(", ", usedBy) + " masih berjalan",
                            "Tutup dulu supaya tidak ada file yang terkunci saat disalin, lalu tekan Cek ulang.",
                            StatusInfo.Warn("Perlu tindakan"), true));
                    else list.Add(Pass("Tidak ada aplikasi yang memakai folder ini", "Tidak ada file yang terkunci."));
                }

                if (stats?.Links > 0)
                    list.Add(new CheckItem("Ada " + Format.Number(stats.Links) + " tautan di dalam folder ini",
                        "Tautan di dalamnya tidak ikut disalin (robocopy /XJ). Pastikan aplikasinya tidak membutuhkannya.",
                        StatusInfo.Warn("Perhatikan"), false));

                var destDrive = Drives.FirstOrDefault(d => string.Equals(d.Root, Path.GetPathRoot(destination), StringComparison.OrdinalIgnoreCase));
                if (destDrive?.IsRemovable == true)
                    list.Add(new CheckItem("Drive " + DestinationDrive + " bisa dicabut",
                        "Kalau " + DestinationDrive + " tidak terhubung saat aplikasinya dibuka, tautan putus dan datanya tidak ditemukan.",
                        StatusInfo.Warn("Perhatikan"), false));
            }
            catch (ArgumentException ex) { list.Add(Stop("Path tidak valid", ex.Message)); }
            catch (NotSupportedException ex) { list.Add(Stop("Path tidak valid", ex.Message)); }
            return list;
        }

        // ---------- Salin ----------

        public bool IsCopying => IsCopy && !verifying;
        public bool IsVerifying => IsCopy && verifying;
        public bool Paused => copyJob?.Paused == true;
        public string PauseLabel => Paused ? "Lanjutkan" : "Jeda";
        public string CopyTitle => "Memindahkan " + Leaf;
        public string PlanSource => planSource;
        public string PlanDestination => planDestination;
        public string PlanKind => planKind == LinkKind.Junction ? "Junction" : "Symbolic link";
        public string StartedText => startedAt.ToString("HH:mm");
        public double Percent { get; private set; }
        public string PercentText => Math.Floor(Percent) + "%";
        public string CopiedText { get; private set; }
        public string FilesText { get; private set; }
        public string SpeedText { get; private set; }
        public string EtaText { get; private set; }
        public string CurrentFile { get; private set; }
        public string LogText { get; private set; }
        public StatusInfo CopyStatus => verifying ? StatusInfo.Ok("Selesai") : Paused ? StatusInfo.Warn("Dijeda") : StatusInfo.Neutral("Berjalan");
        public StatusInfo VerifyStatus => verifying ? StatusInfo.Neutral("Berjalan") : StatusInfo.Neutral("Menunggu");
        public string CopyNote => verifying
            ? "Salinan lengkap. Sedang membandingkan dengan sumber."
            : "Kalau dibatalkan, salinan di " + DestinationDrive + " dihapus. Folder asli tetap utuh.";

        void AddLog(string line)
        {
            logLines.AddLast(line);
            while (logLines.Count > 300) logLines.RemoveFirst();
        }

        void OnRobocopyLine(string line)
        {
            log.Write(line);
            if (Robocopy.TryParseFile(line, out var bytes, out var path))
            {
                Interlocked.Add(ref copiedBytes, bytes);
                Interlocked.Increment(ref copiedFiles);
                currentFile = path;
            }
            else if (Robocopy.IsError(line)) pendingLog.Enqueue(DateTime.Now.ToString("HH:mm:ss") + "  " + line.Trim());
        }

        void UpdateProgress()
        {
            var copied = Interlocked.Read(ref copiedBytes);
            var files = Interlocked.Read(ref copiedFiles);
            Percent = totalBytes > 0 ? Math.Min(100, 100.0 * copied / totalBytes)
                : totalFiles > 0 ? Math.Min(100, 100.0 * files / totalFiles) : 100;
            if (verifying) Percent = 100;
            CopiedText = Format.Bytes(Math.Min(copied, totalBytes)) + " dari " + Format.Bytes(totalBytes);
            FilesText = Format.Number(Math.Min(files, totalFiles)) + " dari " + Format.Number(totalFiles) + " file";
            var seconds = watch?.Elapsed.TotalSeconds ?? 0;
            var speed = seconds > 0.5 ? copied / seconds : 0;
            SpeedText = speed > 0 ? Format.Bytes((long)speed) + "/dtk" : "";
            EtaText = verifying ? "Membandingkan isi" : Paused ? "Dijeda" : speed > 0 ? "sisa sekitar " + Format.Seconds((totalBytes - copied) / speed) : "";
            var f = currentFile;
            CurrentFile = f != null && planSource != null && f.StartsWith(planSource, StringComparison.OrdinalIgnoreCase)
                ? f.Substring(planSource.Length).TrimStart('\\') : f;
            while (pendingLog.TryDequeue(out var line)) AddLog(line);
            LogText = string.Join("\n", logLines);
            Raise(nameof(Percent), nameof(PercentText), nameof(CopiedText), nameof(FilesText), nameof(SpeedText), nameof(EtaText),
                nameof(CurrentFile), nameof(LogText), nameof(Paused), nameof(PauseLabel), nameof(CopyStatus), nameof(VerifyStatus),
                nameof(IsCopying), nameof(IsVerifying), nameof(CopyNote));
        }

        void DoTogglePause()
        {
            if (copyJob == null) return;
            if (copyJob.Paused) { copyJob.Resume(); watch.Start(); }
            else { copyJob.Pause(); watch.Stop(); }
            UpdateProgress();
        }

        void DoStart() => Run(async () =>
        {
            planSource = Safety.Normalize(source);
            planDestination = Safety.Normalize(destination);
            planKind = kind;
            totalBytes = stats.Bytes;
            totalFiles = stats.Files;
            copiedBytes = 0;
            copiedFiles = 0;
            currentFile = null;
            verifying = false;
            startedAt = DateTime.Now;
            log?.Dispose();
            log = new MoveLog(Leaf);
            logLines.Clear();
            AddLog(log.Write("Mulai memindahkan " + planSource + " ke " + planDestination));
            AddLog(log.Write(Robocopy.Preview(planSource, planDestination)));
            SetStep(MoveStep.Copy);
            Raise(nameof(CopyTitle), nameof(PlanSource), nameof(PlanDestination), nameof(PlanKind), nameof(StartedText));

            copyCts = new CancellationTokenSource();
            copyJob = new CopyJob();
            watch = Stopwatch.StartNew();
            UpdateProgress();
            timer.Start();

            int code;
            try { code = await copyJob.RunAsync(planSource, planDestination, OnRobocopyLine, copyCts.Token); }
            catch (OperationCanceledException)
            {
                log.Write("Dibatalkan. Menghapus salinan di " + planDestination);
                await Task.Run(() => Fs.DeleteTree(planDestination));
                log.Write("Salinan dihapus");
                Reset(true);
                Error = "Pemindahan dibatalkan. Salinan di tujuan sudah dihapus, folder asli tidak berubah.";
                return;
            }

            copyJob = null;
            copyCts = null;
            UpdateProgress();
            AddLog(log.Write("robocopy selesai dengan kode " + code));
            if (!Robocopy.Succeeded(code))
            {
                await Task.Run(() => Fs.DeleteTree(planDestination));
                log.Write("Salinan dihapus karena penyalinan gagal");
                var path = log.FilePath;
                Reset(true);
                throw new IOException("Penyalinan gagal (kode robocopy " + code + "). Salinan di tujuan sudah dihapus dan folder asli tidak berubah. Detail ada di log: " + path);
            }

            verifying = true;
            UpdateProgress();
            var hash = AppState.Settings.Verify == VerifyMode.Hash;
            AddLog(log.Write(hash ? "Membandingkan isi setiap file dengan hash SHA-256" : "Membandingkan ukuran, waktu ubah, dan atribut setiap file"));
            verify = await Task.Run(() => Verifier.Run(planSource, planDestination, hash, rel => currentFile = Path.Combine(planSource, rel), CancellationToken.None));
            log.Write("Hasil: " + verify.Checked + " file dicek, " + verify.Missing + " hilang, " + verify.Different + " berbeda, " + verify.Unreadable + " tidak terbaca");

            AclRows.Clear();
            if (AppState.Settings.CompareAcl)
                foreach (var row in await Task.Run(() => Acl.Compare(planSource, planDestination))) AclRows.Add(row);

            timer.Stop();
            SetStep(MoveStep.Review);
            RaiseReview();
        });

        // ---------- Tinjau ----------

        public ObservableCollection<AclRow> AclRows { get; } = new ObservableCollection<AclRow>();
        public bool ShowAcl => AclRows.Count > 0;
        public bool HasAclDiff => AclRows.Any(r => r.Status.State != StatusState.Ok);
        public bool AclFixed => aclFixed && !HasAclDiff;
        public bool CanSwap => verify?.AllSame == true;
        public bool VerifyFailed => verify != null && !verify.AllSame;

        public string SizeCompare => verify == null ? "" : verify.SizeSame
            ? Format.Bytes(verify.Source.Bytes) + " di sumber dan tujuan"
            : "Sumber " + Format.Bytes(verify.Source.Bytes) + ", tujuan " + Format.Bytes(verify.Destination.Bytes);
        public StatusInfo SizeStatus => Same(verify?.SizeSame);
        public string CountCompare => verify == null ? "" : verify.CountSame
            ? Format.Number(verify.Source.Files) + " di sumber dan tujuan"
            : "Sumber " + Format.Number(verify.Source.Files) + ", tujuan " + Format.Number(verify.Destination.Files);
        public StatusInfo CountStatus => Same(verify?.CountSame);
        public string OwnerCompare => verify == null ? "" : verify.OwnerSame
            ? verify.SourceOwner
            : "Sumber " + verify.SourceOwner + ", tujuan " + verify.DestinationOwner;
        public StatusInfo OwnerStatus => Same(verify?.OwnerSame);
        public string ContentLabel => AppState.Settings.Verify == VerifyMode.Hash ? "ISI FILE (HASH)" : "UKURAN, WAKTU, ATRIBUT";
        public string ContentCompare => verify == null ? "" : verify.ContentSame
            ? Format.Number(verify.Checked) + " dari " + Format.Number(verify.Checked) + " file cocok"
            : Format.Number(verify.Missing) + " hilang, " + Format.Number(verify.Different) + " berbeda, " + Format.Number(verify.Unreadable) + " tidak terbaca";
        public StatusInfo ContentStatus => Same(verify?.ContentSame);
        public string SwapLabel => "Ganti dengan " + (planKind == LinkKind.Junction ? "junction" : "symbolic link");
        public string SwapNote => VerifyFailed
            ? "Salinan tidak sama dengan sumber, jadi folder asli tidak diganti."
            : HasAclDiff ? "Masih ada perbedaan permission. Kamu tetap bisa lanjut."
            : "Isi dan permission di tujuan sudah sama dengan sumber.";
        public bool SwapNoteIsWarning => VerifyFailed || HasAclDiff;
        public string BackupName => Path.GetFileName((planSource ?? "").TrimEnd('\\')) + ".bak";

        static StatusInfo Same(bool? same) => same == true ? StatusInfo.Ok("Sama") : StatusInfo.Warn("Berbeda");

        void RaiseReview()
        {
            Raise(nameof(ShowAcl), nameof(HasAclDiff), nameof(AclFixed), nameof(CanSwap), nameof(VerifyFailed),
                nameof(SizeCompare), nameof(SizeStatus), nameof(CountCompare), nameof(CountStatus), nameof(OwnerCompare),
                nameof(OwnerStatus), nameof(ContentLabel), nameof(ContentCompare), nameof(ContentStatus), nameof(SwapLabel),
                nameof(SwapNote), nameof(SwapNoteIsWarning), nameof(BackupName), nameof(PlanSource), nameof(PlanDestination));
            CommandManager.InvalidateRequerySuggested();
        }

        void DoFixAcl() => Run(async () =>
        {
            log.Write("Menyamakan ACL " + planDestination + " dengan " + planSource);
            await Task.Run(() => Acl.CopyFromSource(planSource, planDestination));
            var rows = await Task.Run(() => Acl.Compare(planSource, planDestination));
            AclRows.Clear();
            foreach (var r in rows) AclRows.Add(r);
            aclFixed = true;
            log.Write(HasAclDiff ? "Masih ada perbedaan ACL setelah disamakan" : "ACL tujuan sama dengan sumber");
            RaiseReview();
        }, "Menyamakan permission…");

        void DoAbort() => Run(async () =>
        {
            log.Write("Dibatalkan setelah tinjauan. Menghapus salinan di " + planDestination);
            await Task.Run(() => Fs.DeleteTree(planDestination));
            log.Write("Salinan dihapus");
            Reset(true);
        }, "Menghapus salinan…");

        void DoSwap() => Run(async () =>
        {
            try
            {
                await Task.Run(() => Mover.Swap(planSource, planDestination, planKind, l => log.Write(l)));
            }
            catch (Exception ex)
            {
                throw new IOException("Folder asli belum diganti: " + ex.Message + " Folder asli sudah dikembalikan seperti semula.", ex);
            }
            log.Write("Selesai. " + planSource + " sekarang tautan ke " + planDestination);
            record = new MoveRecord
            {
                Source = planSource,
                Destination = planDestination,
                Kind = planKind,
                Bytes = verify.Source.Bytes,
                Files = verify.Source.Files,
                MovedAt = DateTime.Now,
            };
            AppState.History.Insert(0, record);
            AppState.SaveHistory();
            backupState = BackupState.Kept;
            SetStep(MoveStep.Done);
            RaiseDone();
        }, "Mengganti folder asli dengan tautan…");

        // ---------- Selesai ----------

        public MoveRecord Record => record;
        public string DoneTitle => record == null ? "" : record.Name + " sudah pindah ke " + Path.GetPathRoot(record.Destination).TrimEnd('\\');
        public string DoneBody => record == null ? "" : record.Name + " tetap dibuka dari lokasi lamanya seperti biasa. Isinya sekarang tersimpan di " +
            Path.GetPathRoot(record.Destination).TrimEnd('\\') + ", dan tautannya sudah dites.";
        public StatusInfo LinkStatus => record == null ? null : Linker.Health(record);
        public string LinkKindText => record == null ? "" : (record.Kind == LinkKind.Junction ? "Junction" : "Symbolic link") + ", 0 byte";
        public string RecordSize => record == null ? "" : Format.Bytes(record.Bytes);
        public string BackupDrive => record == null ? "" : Path.GetPathRoot(record.Source).TrimEnd('\\');
        public string RecordDestDrive => record == null ? "" : Path.GetPathRoot(record.Destination).TrimEnd('\\');
        public bool IsBackupKept => backupState == BackupState.Kept;
        public bool IsBackupConfirm => backupState == BackupState.Confirm;
        public bool IsBackupDeleted => backupState == BackupState.Deleted;
        public bool IsRestored => backupState == BackupState.Restored;
        public bool ShowBackup => backupState != BackupState.Restored;
        public string DeleteConfirmTitle => record == null ? "" : "Hapus " + Path.GetFileName(record.Backup) + "?";
        public string DeleteConfirmBody => record == null ? "" : RecordSize + " dihapus permanen dari " + BackupDrive +
            ". Setelah ini, kembali ke " + BackupDrive + " hanya bisa dengan menyalin ulang dari " + RecordDestDrive + ".";
        public string RestoredText => record == null ? "" : record.Name + " sudah kembali ke " + BackupDrive + ". Salinan di " +
            RecordDestDrive + " masih ada. Hapus lewat Explorer kalau sudah tidak perlu.";

        public string SourceFreeText => FreeText(BackupDrive);
        public double SourceUsedPercent => UsedPercent(BackupDrive);
        public string SourceSpaceNote => record == null ? "" : IsBackupKept || IsBackupConfirm
            ? "Bertambah " + RecordSize + " lagi setelah cadangan dihapus."
            : "Sudah lega " + RecordSize + ".";
        public string DestFreeText => FreeText(RecordDestDrive);
        public double DestUsedPercent => UsedPercent(RecordDestDrive);
        public string DestSpaceNote => record == null ? "" : "Bertambah " + RecordSize + " dari isi " + record.Name + ".";

        static DriveInfo Drive(string letter)
        {
            try { return string.IsNullOrEmpty(letter) ? null : new DriveInfo(letter); }
            catch (ArgumentException) { return null; }
        }

        static string FreeText(string letter)
        {
            try { var d = Drive(letter); return d == null ? "" : Format.Bytes(d.AvailableFreeSpace) + " kosong"; }
            catch (IOException) { return "Tidak terbaca"; }
        }

        static double UsedPercent(string letter)
        {
            try { var d = Drive(letter); return d == null || d.TotalSize == 0 ? 0 : 100.0 * (d.TotalSize - d.AvailableFreeSpace) / d.TotalSize; }
            catch (IOException) { return 0; }
        }

        void SetBackupState(BackupState value)
        {
            backupState = value;
            RaiseDone();
        }

        void RaiseDone()
        {
            Raise(nameof(Record), nameof(DoneTitle), nameof(DoneBody), nameof(LinkStatus), nameof(LinkKindText), nameof(RecordSize),
                nameof(BackupDrive), nameof(RecordDestDrive), nameof(IsBackupKept), nameof(IsBackupConfirm), nameof(IsBackupDeleted),
                nameof(IsRestored), nameof(ShowBackup), nameof(DeleteConfirmTitle), nameof(DeleteConfirmBody), nameof(RestoredText),
                nameof(SourceFreeText), nameof(SourceUsedPercent), nameof(SourceSpaceNote), nameof(DestFreeText),
                nameof(DestUsedPercent), nameof(DestSpaceNote), nameof(PlanSource), nameof(PlanDestination));
            CommandManager.InvalidateRequerySuggested();
        }

        void DoDeleteBackup() => Run(async () =>
        {
            var r = record;
            log.Write("Menghapus cadangan " + r.Backup);
            await Task.Run(() => Fs.DeleteTree(r.Backup));
            log.Write("Cadangan dihapus");
            SetBackupState(BackupState.Deleted);
        }, "Menghapus cadangan…");

        void DoRestoreBackup() => Run(async () =>
        {
            var r = record;
            log.Write("Mengembalikan " + r.Source + " dari cadangan");
            await Task.Run(() => Mover.RestoreFromBackup(r));
            r.Restored = true;
            AppState.SaveHistory();
            log.Write("Selesai. " + r.Source + " kembali menjadi folder biasa");
            SetBackupState(BackupState.Restored);
        }, "Mengembalikan folder asli…");
    }
}
