using System;
using System.Globalization;

namespace FolderShift
{
    /// <summary>Format angka dan waktu gaya Indonesia: 18,4 GB, 12.304 file.</summary>
    public static class Format
    {
        static readonly CultureInfo Id = new CultureInfo("id-ID");

        public static string Bytes(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(Id) + " B";
            string[] units = { "KB", "MB", "GB", "TB" };
            double v = bytes;
            var i = -1;
            do { v /= 1024; i++; } while (v >= 1024 && i < units.Length - 1);
            return v.ToString(v >= 100 ? "0" : "0.0", Id) + " " + units[i];
        }

        public static string Number(long n) => n.ToString("N0", Id);

        public static string Date(DateTime t) =>
            t.Date == DateTime.Today ? "Hari ini, " + t.ToString("HH:mm", Id) : t.ToString("d MMM yyyy", Id);

        public static string Seconds(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds)) return "-";
            var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
            if (t.TotalMinutes < 1) return t.Seconds + " dtk";
            if (t.TotalHours < 1) return (int)t.TotalMinutes + " mnt " + t.Seconds + " dtk";
            return (int)t.TotalHours + " jam " + t.Minutes + " mnt";
        }
    }
}
