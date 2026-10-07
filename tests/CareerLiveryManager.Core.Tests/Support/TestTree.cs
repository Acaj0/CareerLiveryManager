using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace CareerLiveryManager.Core.Tests.Support;

/// <summary>
/// A throwaway directory tree under the system temp folder, deleted on dispose. Tests describe the
/// fake Official content, the fake third-party livery download and the Community folder as plain
/// files inside it, then assert on what PackageBuilder wrote.
/// </summary>
internal sealed class TestTree : IDisposable
{
    public string Root { get; }

    public TestTree()
    {
        // A space and non-ASCII characters on purpose, so every test also covers paths like a real
        // "C:\Users\José Silva\Downloads\Livery Pack" instead of a tidy ASCII-only one.
        Root = Path.Combine(Path.GetTempPath(), "clm tests ünï", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
    }

    public string At(params string[] parts) => Path.Combine(new[] { Root }.Concat(parts).ToArray());

    public string Write(string absolutePath, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllText(absolutePath, content);
        return absolutePath;
    }

    public string WriteBytes(string absolutePath, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        File.WriteAllBytes(absolutePath, bytes);
        return absolutePath;
    }

    /// <summary>Writes a ".fsc" file the way Official content stores geometry: plain zlib.</summary>
    public string WriteFsc(string absolutePath, byte[] plain) => WriteBytes(absolutePath, ZlibCompress(plain));

    public string WriteFsc(string absolutePath, string plainText) => WriteFsc(absolutePath, Encoding.UTF8.GetBytes(plainText));

    public static byte[] ZlibCompress(byte[] data)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Relative path -> SHA-256 of every file under <paramref name="directory"/>, for
    /// "this folder was not modified" assertions.</summary>
    public static SortedDictionary<string, string> Snapshot(string directory)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
        {
            using var stream = File.OpenRead(file);
            result[Path.GetRelativePath(directory, file)] = Convert.ToHexString(SHA256.HashData(stream));
        }

        return result;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
