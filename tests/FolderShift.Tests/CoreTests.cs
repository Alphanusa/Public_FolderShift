using Xunit;

namespace FolderShift.Tests
{
    public class FormatTests
    {
        [Theory]
        [InlineData(0L, "0 B")]
        [InlineData(1536L, "1,5 KB")]
        [InlineData(19756849561L, "18,4 GB")]
        [InlineData(442381631488L, "412 GB")]
        public void Bytes_uses_indonesian_decimal_comma(long bytes, string expected) =>
            Assert.Equal(expected, Format.Bytes(bytes));

        [Fact]
        public void Number_uses_dot_thousands() => Assert.Equal("12.304", Format.Number(12304));
    }

    public class RobocopyTests
    {
        [Fact]
        public void Parses_file_line_with_bytes_and_full_path()
        {
            Assert.True(Robocopy.TryParseFile("\t    New File  \t\t   12345\tC:\\Users\\budi\\My Data\\a b.txt", out var bytes, out var path));
            Assert.Equal(12345, bytes);
            Assert.Equal("C:\\Users\\budi\\My Data\\a b.txt", path);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   Files :     12304     12304         0         0         0         0")]
        [InlineData("2026/10/31 18:42:31 ERROR 5 (0x00000005) Copying File C:\\x\\y.txt")]
        [InlineData("\t  New Dir          3\tC:\\x\\")]
        public void Ignores_lines_that_are_not_files(string line) =>
            Assert.False(Robocopy.TryParseFile(line, out _, out _));

        [Fact]
        public void Detects_error_lines() =>
            Assert.True(Robocopy.IsError("2026/10/31 18:42:31 ERROR 5 (0x00000005) Copying File C:\\x\\y.txt"));

        [Fact]
        public void Quote_drops_trailing_backslash_so_it_cannot_escape_the_quote() =>
            Assert.Equal("\"D:\\Relokasi\"", Robocopy.Quote("D:\\Relokasi\\"));

        [Theory]
        [InlineData(0, true)]
        [InlineData(1, true)]
        [InlineData(7, true)]
        [InlineData(8, false)]
        [InlineData(16, false)]
        public void Exit_codes_below_8_succeed(int code, bool ok) => Assert.Equal(ok, Robocopy.Succeeded(code));
    }

    public class SafetyTests
    {
        static readonly string[] Exact = { "C:\\Program Files", "C:\\Users", "C:\\Users\\budi", "C:\\Users\\budi\\AppData\\Roaming" };
        static readonly string[] Trees = { "C:\\Windows" };

        [Theory]
        [InlineData("C:\\")]
        [InlineData("C:\\Windows\\System32")]
        [InlineData("C:\\Program Files")]
        [InlineData("C:\\Users\\budi\\")]
        public void Blocks_system_folders(string path) => Assert.NotNull(Safety.SourceProblem(path, Exact, Trees));

        [Theory]
        [InlineData("C:\\Users\\budi\\AppData\\Roaming\\Claude")]
        [InlineData("C:\\Program Files (x86)\\Steam\\steamapps")]
        public void Allows_app_data_subfolders(string path) => Assert.Null(Safety.SourceProblem(path, Exact, Trees));

        [Fact]
        public void Program_Files_x86_is_not_treated_as_inside_Program_Files() =>
            Assert.False(Safety.IsSameOrInside("C:\\Program Files (x86)\\Steam", "C:\\Program Files"));

        [Fact]
        public void Root_is_a_parent_of_everything_on_the_drive() =>
            Assert.True(Safety.IsSameOrInside("C:\\Data", "C:\\"));

        [Fact]
        public void Rejects_same_drive() =>
            Assert.NotNull(Safety.DestinationProblem("C:\\Data\\App", "C:\\Relokasi\\App"));

        [Fact]
        public void Accepts_other_drive() => Assert.Null(Safety.DestinationProblem("C:\\Data\\App", "D:\\Relokasi\\App"));

        [Fact]
        public void Rejects_destination_inside_source() =>
            Assert.NotNull(Safety.DestinationProblem("C:\\Data\\App", "C:\\Data\\App\\copy"));

        [Theory]
        [InlineData("D:\\Relokasi", 'e', "Claude", "E:\\Relokasi\\Claude")]
        [InlineData("D:\\", 'D', "Claude", "D:\\Claude")]
        [InlineData("", 'D', "Claude", "D:\\Relokasi\\Claude")]
        public void Default_destination_follows_selected_drive(string root, char drive, string leaf, string expected) =>
            Assert.Equal(expected, Safety.DefaultDestination(root, drive, leaf));

        [Fact]
        public void Normalize_keeps_drive_root() => Assert.Equal("C:\\", Safety.Normalize("C:\\"));
    }
}
