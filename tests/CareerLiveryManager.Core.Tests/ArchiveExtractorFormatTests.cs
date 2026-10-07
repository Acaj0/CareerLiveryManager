using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

/// <summary>.rar and .7z, with real archives made by WinRAR and Windows' tar (see Fixtures/README.md).</summary>
public sealed class ArchiveExtractorFormatTests : IDisposable
{
    private readonly TestTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private string Workspace => _tree.At("workspace");

    private ArchiveExtractor Extractor(ArchiveExtractionLimits? limits = null) => new(Workspace, limits);

    private static string Fixture(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    /// <summary>A copy in the temp tree, so tests can also check the "original is untouched" rule.</summary>
    private string Download(string fixtureName)
    {
        var path = _tree.At("downloads", fixtureName);
        _tree.WriteBytes(path, File.ReadAllBytes(Fixture(fixtureName)));
        return path;
    }

    private static ArchiveExtractionException Fails(Action action) => Assert.Throws<ArchiveExtractionException>(action);

    private string[] WorkspaceFolders() =>
        Directory.Exists(Workspace) ? Directory.GetDirectories(Workspace) : Array.Empty<string>();

    private const string LiveryRelative = @"SimObjects\Airplanes\asobo_c172sp\liveries\me\N1";

    // ---- real archives of each kind -------------------------------------------------------------------

    [Theory]
    [InlineData("pack.7z")]
    [InlineData("pack.rar")]
    [InlineData("pack-solid.rar")]
    public void RealArchive_IsExtractedWithItsFullStructure(string fixture)
    {
        var folder = Extractor().Extract(Download(fixture));

        var livery = Path.Combine(folder, "Pack", LiveryRelative);
        Assert.Equal("paint-bytes", File.ReadAllText(Path.Combine(livery, "texture", "body_albd.png.ktx2")));
        Assert.Equal("thumb", File.ReadAllText(Path.Combine(livery, "thumbnail", "thumbnail.png")));
        Assert.StartsWith("[fltsim]", File.ReadAllText(Path.Combine(livery, "texture", "texture.cfg")));
        Assert.Contains("Name=\"N1\"", File.ReadAllText(Path.Combine(livery, "livery.cfg")));
    }

    [Theory]
    [InlineData("pack.7z")]
    [InlineData("pack.rar")]
    [InlineData("pack-solid.rar")]
    public void ExtractedRealArchive_IsRecognisedByTheInspector(string fixture)
    {
        var folder = Extractor().Extract(Download(fixture));

        var livery = Assert.Single(new LiverySourceInspector().Inspect(folder));

        Assert.Equal("N1", livery.BaseFolderName);
        Assert.Equal("asobo_c172sp", livery.DetectedSimObjectName);
        Assert.NotNull(livery.ThumbnailPath);
    }

    [Theory]
    [InlineData("pack.7z")]
    [InlineData("pack.rar")]
    public void TheOriginalArchive_IsNeverChanged(string fixture)
    {
        var download = Download(fixture);
        var before = File.ReadAllBytes(download);

        Extractor().Extract(download);

        Assert.Equal(before, File.ReadAllBytes(download));
    }

    [Theory]
    [InlineData("pack.7z")]
    [InlineData("pack.rar")]
    public void Progress_EndsAtOne(string fixture)
    {
        var reports = new List<double>();

        Extractor().Extract(Download(fixture), new SyncProgress(reports.Add));

        Assert.NotEmpty(reports);
        Assert.Equal(1.0, reports[^1]);
    }

    private sealed class SyncProgress(Action<double> handler) : IProgress<double>
    {
        public void Report(double value) => handler(value);
    }

    // ---- the same safety rules as zip ---------------------------------------------------------------------

    [Fact]
    public void SevenZipWithDotDotEntry_IsRefused_AndNothingIsWrittenOutside()
    {
        var error = Fails(() => Extractor().Extract(Download("evil-dots.7z")));

        Assert.Equal(ArchiveProblemKind.UnsafePath, error.Kind);
        Assert.False(File.Exists(_tree.At("evil.txt")));
        Assert.False(File.Exists(Path.Combine(Workspace, "evil.txt")));
        Assert.Empty(WorkspaceFolders());
    }

    [Fact]
    public void RarWithAFullDrivePath_NeverWritesOutsideTheWorkspace()
    {
        // WinRAR stores a drive path as a relative one (the colon becomes an underscore), and the extractor
        // also refuses any name with a colon or a way out. Whichever applies, nothing may land outside the workspace.
        var download = Download("evil-abs.rar");
        try
        {
            Extractor().Extract(download);
        }
        catch (ArchiveExtractionException ex)
        {
            Assert.Equal(ArchiveProblemKind.UnsafePath, ex.Kind);
        }

        var evilFiles = Directory.EnumerateFiles(_tree.Root, "evil.txt", SearchOption.AllDirectories).ToList();
        Assert.All(evilFiles, f => Assert.StartsWith(Workspace, f, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("pack.7z")]
    [InlineData("pack.rar")]
    public void FileCountLimit_AppliesToTheseFormatsToo(string fixture)
    {
        var error = Fails(() => Extractor(new ArchiveExtractionLimits { MaxFileCount = 2 }).Extract(Download(fixture)));

        Assert.Equal(ArchiveProblemKind.TooManyFiles, error.Kind);
        Assert.Empty(WorkspaceFolders());
    }

    [Theory]
    [InlineData("pack.7z")]
    [InlineData("pack.rar")]
    public void SizeLimit_AppliesToTheseFormatsToo(string fixture)
    {
        var error = Fails(() => Extractor(new ArchiveExtractionLimits { MaxTotalBytes = 20 }).Extract(Download(fixture)));

        Assert.Equal(ArchiveProblemKind.TooLarge, error.Kind);
        Assert.Empty(WorkspaceFolders());
    }

    [Fact]
    public void Cancelling_LeavesNothingBehind()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.ThrowsAny<OperationCanceledException>(() => Extractor().Extract(Download("pack.7z"), cancellationToken: cts.Token));

        Assert.Empty(WorkspaceFolders());
    }

    // ---- passwords and damage ---------------------------------------------------------------------------------

    [Theory]
    [InlineData("pack-pw.rar")]
    [InlineData("pack-hp.rar")]
    public void PasswordProtectedRar_IsRefusedWithAClearMessage(string fixture)
    {
        var error = Fails(() => Extractor().Extract(Download(fixture)));

        Assert.Equal(ArchiveProblemKind.PasswordProtected, error.Kind);
        Assert.Contains("password", error.Message);
        Assert.Contains("7-Zip", error.Message);
        Assert.Empty(WorkspaceFolders());
    }

    [Theory]
    [InlineData("pack.rar", 300)]
    [InlineData("pack.7z", 200)]
    [InlineData("pack-solid.rar", 400)]
    public void TruncatedArchive_IsReportedAsCorrupt(string fixture, int keepBytes)
    {
        var cut = _tree.WriteBytes(_tree.At("downloads", "cut-" + fixture), File.ReadAllBytes(Fixture(fixture))[..keepBytes]);

        var error = Fails(() => Extractor().Extract(cut));

        Assert.Equal(ArchiveProblemKind.Corrupt, error.Kind);
        Assert.Empty(WorkspaceFolders());
    }

    [Theory]
    [InlineData("fake.rar")]
    [InlineData("fake.7z")]
    public void NotReallyThatFormat_IsReportedAsCorrupt(string fileName)
    {
        var path = _tree.Write(_tree.At("downloads", fileName), "this is plain text, not an archive");

        var error = Fails(() => Extractor().Extract(path));

        Assert.Equal(ArchiveProblemKind.Corrupt, error.Kind);
        Assert.Contains("7-Zip", error.Message);
        Assert.Empty(WorkspaceFolders());
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("pack.tar")]
    [InlineData("pack.gz")]
    public void OtherFileTypes_AreStillUnsupported_AndNameTheSupportedOnes(string fileName)
    {
        var path = _tree.Write(_tree.At("downloads", fileName), "x");

        var error = Fails(() => Extractor().Extract(path));

        Assert.Equal(ArchiveProblemKind.Unsupported, error.Kind);
        Assert.Contains(".zip, .rar and .7z", error.Message);
    }

    [Theory]
    [InlineData("a.zip", true)]
    [InlineData("a.RAR", true)]
    [InlineData("a.7Z", true)]
    [InlineData("a.tar", false)]
    [InlineData("a.001", false)]
    public void CanExtract_CoversZipRarAnd7z(string name, bool expected)
    {
        var path = _tree.Write(_tree.At("downloads", name), "x");

        Assert.Equal(expected, ArchiveExtractor.CanExtract(path));
    }

    [Fact]
    public void DialogFilterPattern_ListsTheSupportedTypes()
    {
        Assert.Equal("*.zip;*.rar;*.7z", ArchiveExtractor.DialogFilterPattern);
    }
}
