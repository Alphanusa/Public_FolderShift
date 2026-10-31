using System;
using System.IO;
using System.Runtime.Serialization;

namespace FolderShift
{
    public enum StatusState { Ok, Warn, Block, Neutral }

    /// <summary>Status yang ditampilkan sebagai warna + kata + bentuk (lihat template StatusBadge).</summary>
    public sealed class StatusInfo
    {
        public StatusInfo(StatusState state, string label) { State = state; Label = label; }
        public StatusState State { get; }
        public string Label { get; }

        public static StatusInfo Ok(string label) => new StatusInfo(StatusState.Ok, label);
        public static StatusInfo Warn(string label) => new StatusInfo(StatusState.Warn, label);
        public static StatusInfo Block(string label) => new StatusInfo(StatusState.Block, label);
        public static StatusInfo Neutral(string label) => new StatusInfo(StatusState.Neutral, label);
    }

    public sealed class CheckItem
    {
        public CheckItem(string title, string detail, StatusInfo status, bool blocking)
        {
            Title = title; Detail = detail; Status = status; Blocking = blocking;
        }
        public string Title { get; }
        public string Detail { get; }
        public StatusInfo Status { get; }
        /// <summary>Kalau true, pemindahan tidak boleh dimulai.</summary>
        public bool Blocking { get; }
    }

    public enum LinkKind { Junction = 0, Symlink = 1 }
    public enum BackupPolicy { Keep = 0, AutoDelete7Days = 1 }
    public enum VerifyMode { SizeAndTime = 0, Hash = 1 }

    [DataContract]
    public sealed class MoveRecord
    {
        [DataMember] public string Id { get; set; } = Guid.NewGuid().ToString("N");
        [DataMember] public string Source { get; set; }
        [DataMember] public string Destination { get; set; }
        [DataMember] public LinkKind Kind { get; set; }
        [DataMember] public long Bytes { get; set; }
        [DataMember] public long Files { get; set; }
        [DataMember] public DateTime MovedAt { get; set; }
        [DataMember] public bool Restored { get; set; }

        public string Name => Path.GetFileName(Source.TrimEnd('\\'));
        public string Backup => Source.TrimEnd('\\') + ".bak";
    }

    [DataContract]
    public sealed class AppSettings
    {
        [DataMember] public string DestinationRoot { get; set; }
        [DataMember] public LinkKind Kind { get; set; }
        [DataMember] public BackupPolicy Backup { get; set; }
        [DataMember] public VerifyMode Verify { get; set; }
        [DataMember] public bool CompareAcl { get; set; }
        [DataMember] public bool CheckOnOpen { get; set; }

        public AppSettings() => SetDefaults();

        // DataContractSerializer tidak memanggil constructor, jadi nilai bawaan dipasang di sini juga.
        [OnDeserializing]
        void OnDeserializing(StreamingContext _) => SetDefaults();

        void SetDefaults()
        {
            DestinationRoot = @"D:\Relokasi";
            Kind = LinkKind.Junction;
            Backup = BackupPolicy.Keep;
            Verify = VerifyMode.SizeAndTime;
            CompareAcl = true;
            CheckOnOpen = true;
        }
    }
}
