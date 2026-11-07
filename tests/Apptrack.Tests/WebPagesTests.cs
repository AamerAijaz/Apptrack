using Apptrack.Auth;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Apptrack.Tests;

public sealed class WebPagesTests
{
    [Theory]
    [InlineData("https://localhost:5173")]
    [InlineData("https://localhost:5173/")]
    public void Origin_yields_the_notices_and_connect_failed_pages(string origin)
    {
        var pages = WebPages.Parse(origin);

        Assert.Equal("https://localhost:5173/notices", pages.Notices.AbsoluteUri);
        Assert.Equal("https://localhost:5173/?connect=failed", pages.ConnectFailed.AbsoluteUri);
    }

    [Fact]
    public void Origin_is_read_from_web_origin()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Web:Origin"] = "http://app.example" })
            .Build();

        var pages = WebPages.FromConfiguration(configuration);

        Assert.Equal("http://app.example/notices", pages.Notices.AbsoluteUri);
    }

    [Fact]
    public void Missing_origin_aborts()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<InvalidOperationException>(() => WebPages.FromConfiguration(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("localhost:5173")]
    [InlineData("/notices")]
    [InlineData("ftp://localhost:5173")]
    [InlineData("https://localhost:5173/app")]
    [InlineData("https://localhost:5173/?x=1")]
    [InlineData("https://localhost:5173?")]
    [InlineData("https://localhost:5173/#top")]
    [InlineData("https://user@localhost:5173")]
    public void Invalid_origin_aborts(string? origin)
    {
        Assert.Throws<InvalidOperationException>(() => WebPages.Parse(origin));
    }
}
