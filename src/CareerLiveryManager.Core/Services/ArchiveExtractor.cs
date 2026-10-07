using System.IO.Compression;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace CareerLiveryManager.Core.Services;

public enum ArchiveProblemKind
{
    /// <summary>Not a .zip, .rar or .7z.</summary>
    Unsupported,

    /// <summary>Damaged, or in a variant the readers don't support.</summary>
    Corrupt,

    /// <summary>Needs a password (the data or even the file list is encrypted).</summary>
    PasswordProtected,

    /// <summary>An entry tries to land outside the extraction folder ("zip-slip"), or has an unusable name.</summary>
    UnsafePath,

    TooLarge,
    TooManyFiles,
    NotEnoughSpace,

    /// <summary>The operating system refused a file (path too long, disk error...).</summary>
    IoError,
}

/// <summary>An archive couldn't be extracted. <see cref="Exception.Message"/> is written for the user.</summary>
public sealed class ArchiveExtractionException : Exception
{
    public ArchiveExtractionException(ArchiveProblemKind kind, string message, Exception? inner = null)
        : base(message, inner)
    {
        Kind = kind;
    }

    public ArchiveProblemKind Kind { get; }
}

public sealed class ArchiveExtractionLimits
{
    /// <summary>Total uncompressed size allowed. Big liveries are 1-2 GB; this only stops decompression bombs.</summary>
    public long MaxTotalBytes { get; init; } = 6L * 1024 * 1024 * 1024;

    public int MaxFileCount { get; init; } = 50_000;
}

/// <summary>
/// Extracts a downloaded livery archive (.zip, .rar or .7z) into a private temporary folder so the
/// user can drop the download instead of unpacking it by hand. The original archive is only read,
/// never changed. .zip uses the .NET reader; .rar and .7z use SharpCompress (MIT).
///
/// An archive is untrusted input, so extraction is deliberately strict and identical for all three
/// formats: an entry that would land outside the destination folder (or has a drive, colon or other
/// unusable name) rejects the whole archive; total size and file count are capped, including while
/// copying (an archive can lie about its sizes); the disk must have room; and anything that fails or
/// is cancelled leaves no half-extracted folder behind.
/// </summary>
public sealed class ArchiveExtractor
{
    private static readonly string[] SupportedExtensions = [".zip", ".rar", ".7z"];

    private readonly string _workspaceRoot;
    private readonly ArchiveExtractionLimits _limits;

    /// <param name="workspaceRoot">Where extraction folders are created; defaults to a folder under the system temp path.</param>
    public ArchiveExtractor(string? workspaceRoot = null, ArchiveExtractionLimits? limits = null)
    {
        _workspaceRoot = Path.GetFullPath(workspaceRoot ?? DefaultWorkspaceRoot);
        _limits = limits ?? new ArchiveExtractionLimits();
    }

    public static string DefaultWorkspaceRoot { get; } = Path.Combine(Path.GetTempPath(), "CareerLiveryManager", "extracted");

    public string WorkspaceRoot => _workspaceRoot;

    /// <summary>The file types the user can drop, for dialog filters ("*.zip;*.rar;*.7z").</summary>
    public static string DialogFilterPattern { get; } = string.Join(";", SupportedExtensions.Select(e => "*" + e));

    public static bool CanExtract(string path) =>
        File.Exists(path) && SupportedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>Extracts <paramref name="archivePath"/> into a new folder and returns its path.</summary>
    /// <exception cref="ArchiveExtractionException">The archive can't be used; the message says why.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; nothing is left on disk.</exception>
    public string Extract(string archivePath, IProgress<double>? progress = null, CancellationToken cancellationToken = default)
    {
        if (!CanExtract(archivePath))
        {
            throw new ArchiveExtractionException(ArchiveProblemKind.Unsupported,
                "The app can open .zip, .rar and .7z files. Extract this one with 7-Zip or WinRAR first, then choose the extracted folder.");
        }

        var extension = Path.GetExtension(archivePath).ToLowerInvariant();
        var destination = Path.Combine(_workspaceRoot, Guid.NewGuid().ToString("N")[..8]);
        try
        {
            Directory.CreateDirectory(destination);
            var sink = new Sink(destination, _workspaceRoot, _limits, progress, cancellationToken);

            if (extension == ".zip")
            {
                ExtractZip(archivePath, sink);
            }
            else
            {
                ExtractWithSharpCompress(archivePath, sink);
            }

            progress?.Report(1.0);
            return destination;
        }
        catch (Exception ex)
        {
            TryDeleteFolder(destination);
            throw Describe(ex, extension);
        }
    }

    // ---- per format -----------------------------------------------------------------------------

    private static void ExtractZip(string archivePath, Sink sink)
    {
        using var zip = ZipFile.OpenRead(archivePath);

        var entries = zip.Entries.Where(e => !IsIgnored(e.FullName)).ToList();
        sink.Begin(entries.Count, entries.Sum(e => e.Length));

        foreach (var entry in entries)
        {
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');
            if (isDirectory)
            {
                sink.WriteEntry(entry.FullName, isDirectory: true, content: null);
                continue;
            }

            using var stream = entry.Open();
            sink.WriteEntry(entry.FullName, isDirectory: false, stream);
        }
    }

    private static void ExtractWithSharpCompress(string archivePath, Sink sink)
    {
        using var archive = ArchiveFactory.Open(archivePath);

        var entries = archive.Entries.Where(e => !IsIgnored(e.Key ?? string.Empty)).ToList();

        if (entries.Any(e => e.IsEncrypted))
        {
            throw PasswordProblem();
        }

        sink.Begin(entries.Count, entries.Where(e => !e.IsDirectory).Sum(e => e.Size));

        // A solid archive (and any .7z) is only efficient read front to back; otherwise each entry opens on its own.
        if (archive.IsSolid || archive.Type == ArchiveType.SevenZip)
        {
            using var reader = archive.ExtractAllEntries();
            while (reader.MoveToNextEntry())
            {
                var entry = reader.Entry;
                var name = entry.Key ?? string.Empty;
                if (IsIgnored(name))
                {
                    continue;
                }

                if (entry.IsDirectory)
                {
                    sink.WriteEntry(name, isDirectory: true, content: null);
                    continue;
                }

                using var stream = reader.OpenEntryStream();
                sink.WriteEntry(name, isDirectory: false, stream);
            }

            return;
        }

        foreach (var entry in entries)
        {
            var name = entry.Key ?? string.Empty;
            if (entry.IsDirectory)
            {
                sink.WriteEntry(name, isDirectory: true, content: null);
                continue;
            }

            using var stream = entry.OpenEntryStream();
            sink.WriteEntry(name, isDirectory: false, stream);
        }
    }

    // ---- the one place files are written --------------------------------------------------------

    /// <summary>Writes entries of any format into the destination, enforcing every safety rule in one place.</summary>
    private sealed class Sink
    {
        private readonly string _root;
        private readonly string _workspaceRoot;
        private readonly ArchiveExtractionLimits _limits;
        private readonly IProgress<double>? _progress;
        private readonly CancellationToken _cancellationToken;
        private readonly byte[] _buffer = new byte[128 * 1024];
        private long _declaredTotal;
        private long _written;

        public Sink(string destination, string workspaceRoot, ArchiveExtractionLimits limits, IProgress<double>? progress, CancellationToken cancellationToken)
        {
            _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destination)) + Path.DirectorySeparatorChar;
            _workspaceRoot = workspaceRoot;
            _limits = limits;
            _progress = progress;
            _cancellationToken = cancellationToken;
        }

        /// <summary>Checks what the archive claims before anything is written.</summary>
        public void Begin(int fileCount, long declaredTotalBytes)
        {
            if (fileCount > _limits.MaxFileCount)
            {
                throw new ArchiveExtractionException(ArchiveProblemKind.TooManyFiles,
                    $"This archive has {fileCount:N0} files, far more than any livery. It can't be a normal livery pack, so the app won't extract it.");
            }

            if (declaredTotalBytes > _limits.MaxTotalBytes)
            {
                throw new ArchiveExtractionException(ArchiveProblemKind.TooLarge,
                    $"This archive would expand to {FileSizeFormatter.Format(declaredTotalBytes)}, far more than any livery. The app won't extract it.");
            }

            var space = DiskSpaceChecker.Check(_workspaceRoot, declaredTotalBytes);
            if (space.IsCertainlyShort)
            {
                throw new ArchiveExtractionException(ArchiveProblemKind.NotEnoughSpace,
                    $"Not enough free disk space to extract this archive: it needs {FileSizeFormatter.Format(declaredTotalBytes)} and only {FileSizeFormatter.Format(space.FreeBytes)} is free.");
            }

            _declaredTotal = declaredTotalBytes;
        }

        public void WriteEntry(string entryName, bool isDirectory, Stream? content)
        {
            _cancellationToken.ThrowIfCancellationRequested();

            var target = ResolveSafeTarget(entryName);
            if (isDirectory || content is null)
            {
                Directory.CreateDirectory(target);
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);

            using var output = new FileStream(target, FileMode.Create, FileAccess.Write, FileShare.None);
            int read;
            while ((read = content.Read(_buffer, 0, _buffer.Length)) > 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                // Counted while copying: the sizes an archive declares can't be trusted.
                _written += read;
                if (_written > _limits.MaxTotalBytes)
                {
                    throw new ArchiveExtractionException(ArchiveProblemKind.TooLarge,
                        "This archive expands to far more than any livery. The app stopped extracting it.");
                }

                output.Write(_buffer, 0, read);
                if (_declaredTotal > 0)
                {
                    _progress?.Report(Math.Min(1.0, (double)_written / _declaredTotal));
                }
            }
        }

        /// <summary>The full path an entry would be written to, or an exception when it would escape the destination.</summary>
        private string ResolveSafeTarget(string entryName)
        {
            // A colon is a drive ("C:\x") or an NTFS alternate data stream ("a.txt:evil"): never in a livery.
            if (entryName.Contains(':') || entryName.Contains('\0'))
            {
                throw new ArchiveExtractionException(ArchiveProblemKind.UnsafePath,
                    "This archive contains a file with a name Windows treats specially, so the app refused to extract it.");
            }

            var normalized = entryName.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            var target = Path.GetFullPath(Path.Combine(_root, normalized));
            if (!target.StartsWith(_root, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArchiveExtractionException(ArchiveProblemKind.UnsafePath,
                    "This archive contains files that try to leave the extraction folder, so the app refused to extract it. Only open archives from sources you trust.");
            }

            return target;
        }
    }

    // ---- errors ---------------------------------------------------------------------------------

    private static ArchiveExtractionException PasswordProblem() =>
        new(ArchiveProblemKind.PasswordProtected,
            "This archive is password-protected, which the app can't open. Extract it with 7-Zip or WinRAR (they'll ask for the password), then choose the extracted folder.");

    private static Exception Describe(Exception ex, string extension) => ex switch
    {
        ArchiveExtractionException or OperationCanceledException => ex,
        CryptographicException => PasswordProblem(),
        InvalidDataException or NotSupportedException or EndOfStreamException or SharpCompressException or InvalidOperationException or ArgumentException =>
            new ArchiveExtractionException(ArchiveProblemKind.Corrupt,
                $"This {extension} couldn't be read. It may be damaged, password-protected or in a variant the app can't open. Try extracting it with 7-Zip or WinRAR.", ex),
        PathTooLongException => new ArchiveExtractionException(ArchiveProblemKind.IoError,
            "A file inside this archive has a path that's too long for Windows. Extract it with 7-Zip into a short folder (for example C:\\Liveries) and choose that folder.", ex),
        IOException or UnauthorizedAccessException => new ArchiveExtractionException(
            ArchiveProblemKind.IoError, $"Couldn't extract the archive: {ex.Message}", ex),
        _ => ex,
    };

    /// <summary>Metadata Windows/macOS add that no livery needs.</summary>
    private static bool IsIgnored(string entryName)
    {
        var name = entryName.Replace('\\', '/');
        var fileName = name[(name.LastIndexOf('/') + 1)..];
        return name.StartsWith("__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
               name.Contains("/__MACOSX/", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, ".DS_Store", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase);
    }

    // ---- cleanup --------------------------------------------------------------------------------

    /// <summary>Deletes an extraction folder this extractor created. Refuses any folder outside its workspace.</summary>
    public void Delete(string extractedFolder)
    {
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(extractedFolder));
        var root = Path.TrimEndingDirectorySeparator(_workspaceRoot) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || string.Equals(full + Path.DirectorySeparatorChar, root, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryDeleteFolder(full);
    }

    /// <summary>Removes extraction folders older than <paramref name="olderThan"/> (left behind by a crash or a forced close).
    /// Returns how many were removed. Never throws.</summary>
    public int CleanupStale(TimeSpan olderThan)
    {
        var removed = 0;
        try
        {
            if (!Directory.Exists(_workspaceRoot))
            {
                return 0;
            }

            foreach (var folder in Directory.EnumerateDirectories(_workspaceRoot))
            {
                if (DateTime.UtcNow - Directory.GetCreationTimeUtc(folder) > olderThan)
                {
                    TryDeleteFolder(folder);
                    if (!Directory.Exists(folder))
                    {
                        removed++;
                    }
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Cleanup is best-effort.
        }

        return removed;
    }

    private static void TryDeleteFolder(string folder)
    {
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A file still held open by antivirus or Explorer: left for CleanupStale.
        }
    }
}
