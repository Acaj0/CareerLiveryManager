using CareerLiveryManager.Core.Models;
using CareerLiveryManager.Core.Services;
using Xunit;

namespace CareerLiveryManager.Core.Tests;

public class CommunityCatalogTests
{
    public static IEnumerable<object[]> AllEntries() =>
        CommunityCatalog.Creators.Concat(CommunityCatalog.RecommendedApps).Select(e => new object[] { e });

    [Theory]
    [MemberData(nameof(AllEntries))]
    public void Every_entry_has_a_name_an_image_and_an_https_link(CommunityEntry entry)
    {
        Assert.False(string.IsNullOrWhiteSpace(entry.Name));
        Assert.False(string.IsNullOrWhiteSpace(entry.ImageFile));
        Assert.True(CommunityCatalog.IsSafeUrl(entry.Url), entry.Url);
    }

    [Theory]
    [InlineData("http://example.com", false)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("not a url", false)]
    [InlineData("https://example.com/x", true)]
    public void IsSafeUrl_only_allows_https(string url, bool expected) =>
        Assert.Equal(expected, CommunityCatalog.IsSafeUrl(url));
}
