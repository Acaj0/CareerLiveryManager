using System.IO.Compression;
using System.Text;
using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

public sealed class ArchiveExtractorTests : IDisposable
{
    private readonly TestTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private string Workspace => _tree.At("workspace");

    private ArchiveExtractor Extractor(ArchiveExtractionLimits? limits = null) => new(Workspace, limits);

    /// <summary>Builds a zip whose entry names are written exactly as given (including hostile ones).</summary>
    private string Zip(params (string Name, string Content)[] entries)
    {
        var path = _tree.At("downloads", $"pack-{Guid.NewGuid():N}.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var stream = File.Create(path);
        using var zip = new ZipArchive(stream, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
            writer.Write(content);
        }

        return path;
    }

    private static ArchiveExtractionException Fails(Action action) => Assert.Throws<ArchiveExtractionException>(action);

    private string[] WorkspaceFolders() =>
        Directory.Exists(Workspace) ? Directory.GetDirectories(Workspace) : Array.Empty<string>();

    // ---- the normal case ----------------------------------------------------------------------------

    [Fact]
    public void ExtractsFilesAndFolders_KeepingTheirStructure()
    {
        var zip = Zip(
            ("Cessna Pack/SimObjects/Airplanes/asobo_c172sp/liveries/me/N1/livery.cfg", "[GENERAL]"),
            ("Cessna Pack/SimObjects/Airplanes/asobo_c172sp/liveries/me/N1/texture/a.ktx2", "paint"),
            ("readme.txt", "hi"));

        var folder = Extractor().Extract(zip);

        Assert.Equal("[GENERAL]", File.ReadAllText(Path.Combine(folder, "Cessna Pack", "SimObjects", "Airplanes", "asobo_c172sp", "liveries", "me", "N1", "livery.cfg")));
        Assert.Equal("paint", File.ReadAllText(Path.Combine(folder, "Cessna Pack", "SimObjects", "Airplanes", "asobo_c172sp", "liveries", "me", "N1", "texture", "a.ktx2")));
        Assert.Equal("hi", File.ReadAllText(Path.Combine(folder, "readme.txt")));
    }

    [Fact]
    public void ExtractedLivery_IsFoundByTheInspector_WithItsDrPair()
    {
        var zip = Zip(
            ("Pack/SimObjects/Airplanes/asobo_c172sp/liveries/me/N1/livery.cfg", "[GENERAL]"),
            ("Pack/SimObjects/Airplanes/asobo_c172sp/liveries/me/N1_DR/livery.cfg", "[GENERAL]"),
            ("Pack/SimObjects/Airplanes/asobo_c172sp/liveries/me/N1/thumbnail/thumbnail.png", "x"));

        var folder = Extractor().Extract(zip);
        var found = new LiverySourceInspector().Inspect(folder);

        var livery = Assert.Single(found);
        Assert.Equal("N1", livery.BaseFolderName);
        Assert.True(livery.HasDr);
        Assert.Equal("asobo_c172sp", livery.DetectedSimObjectName);
        Assert.NotNull(livery.ThumbnailPath);
    }

    [Fact]
    public void TheOriginalArchive_IsNeverChanged()
    {
        var zip = Zip(("a/livery.cfg", "x"));
        var before = File.ReadAllBytes(zip);
        var modified = File.GetLastWriteTimeUtc(zip);

        Extractor().Extract(zip);

        Assert.Equal(before, File.ReadAllBytes(zip));
        Assert.Equal(modified, File.GetLastWriteTimeUtc(zip));
    }

    [Fact]
    public void EachExtraction_GetsItsOwnFolder()
    {
        var zip = Zip(("a.txt", "x"));
        var extractor = Extractor();

        var first = extractor.Extract(zip);
        var second = extractor.Extract(zip);

        Assert.NotEqual(first, second);
        Assert.Equal(2, WorkspaceFolders().Length);
    }

    [Fact]
    public void FolderEntriesAndEmptyZips_AreFine()
    {
        var zip = _tree.At("downloads", "empty.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(zip)!);
        using (var stream = File.Create(zip))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("onlyfolder/");
        }

        var folder = Extractor().Extract(zip);

        Assert.True(Directory.Exists(Path.Combine(folder, "onlyfolder")));
    }

    [Fact]
    public void FileNamesWithSpacesAndNonAsciiCharacters_Survive()
    {
        var zip = Zip(("Pack é ü/Sérié 5 !x/livery.cfg", "x"));

        var folder = Extractor().Extract(zip);

        Assert.True(File.Exists(Path.Combine(folder, "Pack é ü", "Sérié 5 !x", "livery.cfg")));
    }

    [Fact]
    public void OsMetadataFiles_AreSkipped()
    {
        var zip = Zip(
            ("Pack/livery.cfg", "x"),
            ("__MACOSX/Pack/._livery.cfg", "junk"),
            ("Pack/.DS_Store", "junk"),
            ("Pack/Thumbs.db", "junk"));

        var folder = Extractor().Extract(zip);

        Assert.Equal(new[] { "livery.cfg" }, Directory.EnumerateFiles(Path.Combine(folder, "Pack")).Select(Path.GetFileName).ToArray());
        Assert.False(Directory.Exists(Path.Combine(folder, "__MACOSX")));
    }

    // ---- hostile archives ---------------------------------------------------------------------------

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("a/../../evil.txt")]
    [InlineData("a/b/../../../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("/etc/evil.txt")]
    [InlineData("C:/Windows/evil.txt")]
    [InlineData("C:\\Windows\\evil.txt")]
    [InlineData("\\\\server\\share\\evil.txt")]
    [InlineData("a.txt:hidden-stream")]
    public void ZipSlipAndOddNames_RejectTheWholeArchive_AndWriteNothingOutside(string hostileName)
    {
        var zip = Zip(("Pack/livery.cfg", "fine"), (hostileName, "pwned"));

        var error = Fails(() => Extractor().Extract(zip));

        Assert.Equal(ArchiveProblemKind.UnsafePath, error.Kind);
        Assert.False(File.Exists(_tree.At("evil.txt")));
        Assert.False(File.Exists(Path.Combine(Workspace, "evil.txt")));
        Assert.Empty(WorkspaceFolders()); // the half-extracted folder is gone too
    }

    [Fact]
    public void AnEntryNamedLikeAParentPrefix_IsNotMistakenForInside()
    {
        // "<folder>-sibling" shares the folder's name as a prefix but is outside it.
        var zip = Zip(("../" + "x-sibling/evil.txt", "pwned"));

        Assert.Equal(ArchiveProblemKind.UnsafePath, Fails(() => Extractor().Extract(zip)).Kind);
    }

    [Fact]
    public void TooManyFiles_IsRejectedBeforeAnythingIsWritten()
    {
        var zip = Zip(Enumerable.Range(0, 11).Select(i => ($"f{i}.txt", "x")).ToArray());

        var error = Fails(() => Extractor(new ArchiveExtractionLimits { MaxFileCount = 10 }).Extract(zip));

        Assert.Equal(ArchiveProblemKind.TooManyFiles, error.Kind);
        Assert.Empty(WorkspaceFolders());
    }

    [Fact]
    public void DeclaredSizeOverTheLimit_IsRejected()
    {
        var zip = Zip(("big.bin", new string('a', 5000)));

        var error = Fails(() => Extractor(new ArchiveExtractionLimits { MaxTotalBytes = 1000 }).Extract(zip));

        Assert.Equal(ArchiveProblemKind.TooLarge, error.Kind);
        Assert.Empty(WorkspaceFolders());
    }

    [Fact]
    public void CompressionBomb_IsStoppedByTheLimit_AndLeavesNothingBehind()
    {
        // 50 MB of zeros compresses to a few KB: small on disk, large once extracted.
        var path = _tree.At("downloads", "bomb.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using var entryStream = archive.CreateEntry("zeros.bin", CompressionLevel.SmallestSize).Open();
            entryStream.Write(new byte[50 * 1024 * 1024]);
        }

        Assert.True(new FileInfo(path).Length < 1024 * 1024);

        var error = Fails(() => Extractor(new ArchiveExtractionLimits { MaxTotalBytes = 10 * 1024 * 1024 }).Extract(path));

        Assert.Equal(ArchiveProblemKind.TooLarge, error.Kind);
        Assert.Empty(WorkspaceFolders());
    }

    [Fact]
    public void ZipThatLiesAboutItsSize_NeverWritesMoreThanTheLimit()
    {
        // A hostile zip can claim 1000 bytes in its header and carry 50 MB. .NET stops reading at the
        // declared size and the extractor also counts the bytes it writes: whichever of them acts, the
        // folder must never grow past the limit.
        var path = _tree.At("downloads", "liar.zip");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var stream = File.Create(path))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            using var entryStream = archive.CreateEntry("zeros.bin", CompressionLevel.SmallestSize).Open();
            entryStream.Write(new byte[50 * 1024 * 1024]);
        }

        var bytes = File.ReadAllBytes(path);
        var centralDirectory = IndexOf(bytes, [0x50, 0x4B, 0x01, 0x02]);
        Assert.True(centralDirectory > 0);
        BitConverter.GetBytes(1000u).CopyTo(bytes, centralDirectory + 24); // uncompressed size field
        File.WriteAllBytes(path, bytes);

        const long limit = 10 * 1024 * 1024;
        try
        {
            Extractor(new ArchiveExtractionLimits { MaxTotalBytes = limit }).Extract(path);
        }
        catch (ArchiveExtractionException)
        {
            // rejecting it is fine too
        }

        var onDisk = Directory.Exists(Workspace)
            ? Directory.EnumerateFiles(Workspace, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length)
            : 0;
        Assert.True(onDisk <= limit, $"{onDisk} bytes were written, over the {limit} limit");
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }

    // ---- bad inputs ---------------------------------------------------------------------------------

    [Fact]
    public void NotAZipFile_IsReportedAsCorrupt_WithAnActionableMessage()
    {
        var path = _tree.Write(_tree.At("downloads", "fake.zip"), "this is not a zip");

        var error = Fails(() => Extractor().Extract(path));

        Assert.Equal(ArchiveProblemKind.Corrupt, error.Kind);
        Assert.Contains("7-Zip", error.Message);
        Assert.Empty(WorkspaceFolders());
    }

    [Fact]
    public void TruncatedZip_IsReportedAsCorrupt()
    {
        var good = Zip(("a/livery.cfg", new string('x', 4000)));
        var bytes = File.ReadAllBytes(good);
        var path = _tree.WriteBytes(_tree.At("downloads", "cut.zip"), bytes[..(bytes.Length / 2)]);

        var error = Fails(() => Extractor().Extract(path));

        Assert.Equal(ArchiveProblemKind.Corrupt, error.Kind);
        Assert.Empty(WorkspaceFolders());
    }

    [Theory]
    [InlineData("pack.txt")]
    [InlineData("pack.tar")]
    public void OtherFormats_AreUnsupported_AndSayHowToProceed(string fileName)
    {
        var path = _tree.Write(_tree.At("downloads", fileName), "x");

        var error = Fails(() => Extractor().Extract(path));

        Assert.Equal(ArchiveProblemKind.Unsupported, error.Kind);
        Assert.Contains("7-Zip", error.Message);
    }

    [Fact]
    public void MissingFile_IsUnsupported_NotACrash()
    {
        var error = Fails(() => Extractor().Extract(_tree.At("downloads", "gone.zip")));

        Assert.Equal(ArchiveProblemKind.Unsupported, error.Kind);
    }

    [Theory]
    [InlineData("a.zip", true)]
    [InlineData("a.ZIP", true)]
    [InlineData("a.tar", false)]
    [InlineData("a.txt", false)]
    public void CanExtract_OnlyForExistingZipFiles(string name, bool expected)
    {
        var path = _tree.Write(_tree.At("downloads", name), "x");

        Assert.Equal(expected, ArchiveExtractor.CanExtract(path));
        Assert.False(ArchiveExtractor.CanExtract(_tree.At("downloads", "missing.zip")));
        Assert.False(ArchiveExtractor.CanExtract(_tree.At("downloads")));
    }

    // ---- progress and cancel ------------------------------------------------------------------------

    [Fact]
    public void Progress_IsReported_AndEndsAtOne()
    {
        var zip = Zip(("a.bin", new string('a', 300_000)), ("b.bin", new string('b', 300_000)));
        var reports = new List<double>();

        Extractor().Extract(zip, new Progress<double>(reports.Add));
        // Progress<T> posts to the synchronization context; give the thread pool a moment.
        SpinWait.SpinUntil(() => reports.Count > 0 && reports[^1] >= 1.0, TimeSpan.FromSeconds(3));

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports.Max());
        Assert.All(reports, r => Assert.InRange(r, 0.0, 1.0));
    }

    [Fact]
    public void Cancelling_StopsAndRemovesWhatWasExtracted()
    {
        var zip = Zip(("a.bin", new string('a', 600_000)), ("b.bin", new string('b', 600_000)));
        using var cts = new CancellationTokenSource();
        var progress = new SyncProgress(_ => cts.Cancel());

        Assert.ThrowsAny<OperationCanceledException>(() => Extractor().Extract(zip, progress, cts.Token));

        Assert.Empty(WorkspaceFolders());
    }

    private sealed class SyncProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }

    // ---- cleanup ------------------------------------------------------------------------------------

    [Fact]
    public void Delete_RemovesAnExtractionFolder()
    {
        var extractor = Extractor();
        var folder = extractor.Extract(Zip(("a.txt", "x")));

        extractor.Delete(folder);

        Assert.False(Directory.Exists(folder));
    }

    [Fact]
    public void Delete_RefusesFoldersOutsideTheWorkspace()
    {
        var innocent = _tree.At("my-important-folder");
        _tree.Write(Path.Combine(innocent, "keep.txt"), "x");
        var extractor = Extractor();
        Directory.CreateDirectory(Workspace);

        extractor.Delete(innocent);
        extractor.Delete(Workspace);
        extractor.Delete(Path.Combine(Workspace, "..", "my-important-folder"));

        Assert.True(File.Exists(Path.Combine(innocent, "keep.txt")));
        Assert.True(Directory.Exists(Workspace));
    }

    [Fact]
    public void CleanupStale_RemovesOnlyOldFolders()
    {
        var extractor = Extractor();
        var oldOne = extractor.Extract(Zip(("a.txt", "x")));
        var fresh = extractor.Extract(Zip(("b.txt", "x")));
        Directory.SetCreationTimeUtc(oldOne, DateTime.UtcNow.AddDays(-3));

        var removed = extractor.CleanupStale(TimeSpan.FromHours(12));

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(oldOne));
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public void CleanupStale_WithNoWorkspaceYet_DoesNothing()
    {
        Assert.Equal(0, Extractor().CleanupStale(TimeSpan.Zero));
    }
}
