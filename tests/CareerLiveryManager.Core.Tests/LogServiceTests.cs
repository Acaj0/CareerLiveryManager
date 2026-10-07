using CareerLiveryManager.Core.Services;
using CareerLiveryManager.Core.Tests.Support;

namespace CareerLiveryManager.Core.Tests;

public sealed class LogServiceTests : IDisposable
{
    private readonly TestTree _tree = new();

    public void Dispose() => _tree.Dispose();

    private string LogDir => _tree.At("logs");

    private string[] LogFiles() =>
        Directory.Exists(LogDir)
            ? Directory.EnumerateFiles(LogDir).Select(f => Path.GetFileName(f)).OrderBy(n => n).ToArray()
            : Array.Empty<string>();

    [Fact]
    public void Info_AppendsATimestampedLine()
    {
        var log = new LogService(LogDir, LogService.DefaultMaxBytes);

        log.Info("hello");
        log.Error("boom", new InvalidOperationException("bad"));

        var text = File.ReadAllText(Path.Combine(LogDir, "app.log"));
        Assert.Matches(@"^\[\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2}\] \[INFO\] hello", text);
        Assert.Contains("[ERROR] boom", text);
        Assert.Contains("InvalidOperationException: bad", text);
    }

    [Fact]
    public void LogRollsOverAtTheLimit_KeepingThreeFilesAtMost()
    {
        var log = new LogService(LogDir, maxBytes: 400);

        for (var i = 0; i < 200; i++)
        {
            log.Info($"line {i:D3} " + new string('x', 60));
        }

        Assert.Equal(new[] { "app.1.log", "app.2.log", "app.log" }, LogFiles());
        Assert.All(LogFiles(), f => Assert.True(new FileInfo(Path.Combine(LogDir, f)).Length < 400 + 200, $"{f} is too big"));
    }

    [Fact]
    public void RolledFiles_HoldOlderLinesThanTheCurrentOne()
    {
        var log = new LogService(LogDir, maxBytes: 300);

        for (var i = 0; i < 40; i++)
        {
            log.Info($"line {i:D3} " + new string('x', 40));
        }

        var current = File.ReadAllText(Path.Combine(LogDir, "app.log"));
        var rolled = File.ReadAllText(Path.Combine(LogDir, "app.1.log"));
        Assert.Contains("line 039", current);
        Assert.DoesNotContain("line 039", rolled);
        Assert.True(string.CompareOrdinal(rolled[^70..], current[..70]) != 0);
    }

    [Fact]
    public void NewestLine_IsNeverLostInARollOver()
    {
        var log = new LogService(LogDir, maxBytes: 100);

        for (var i = 0; i < 30; i++)
        {
            log.Info($"entry-{i:D2}");
        }

        var all = string.Concat(LogFiles().Select(f => File.ReadAllText(Path.Combine(LogDir, f))));
        Assert.Contains("entry-29", all);
    }

    [Fact]
    public void LoggingIntoAnUnwritableLocation_NeverThrows()
    {
        // A file where the log directory should be makes Directory.CreateDirectory fail.
        var blocker = _tree.Write(_tree.At("blocker"), "i am a file");
        var log = new LogService(Path.Combine(blocker, "logs"), LogService.DefaultMaxBytes);

        log.Info("still fine");
        log.Error("also fine", new Exception("x"));
        log.InfoIfChanged("k", "message");
    }

    [Fact]
    public void InfoIfChanged_WritesOnlyWhenTheMessageForThatKeyChanges()
    {
        var log = new LogService(LogDir, LogService.DefaultMaxBytes);

        log.InfoIfChanged("scan", "Scanned 66 aircraft");
        log.InfoIfChanged("scan", "Scanned 66 aircraft");
        log.InfoIfChanged("scan", "Scanned 66 aircraft");
        log.InfoIfChanged("scan", "Scanned 67 aircraft");
        log.InfoIfChanged("scan", "Scanned 66 aircraft");
        log.InfoIfChanged("other", "Scanned 66 aircraft");

        var lines = File.ReadAllLines(Path.Combine(LogDir, "app.log"));
        Assert.Equal(4, lines.Length);
        Assert.EndsWith("Scanned 66 aircraft", lines[0]);
        Assert.EndsWith("Scanned 67 aircraft", lines[1]);
        Assert.EndsWith("Scanned 66 aircraft", lines[2]);
        Assert.EndsWith("Scanned 66 aircraft", lines[3]);
    }
}
