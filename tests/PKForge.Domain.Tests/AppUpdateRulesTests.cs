using PKForge.Domain;
using Xunit;

namespace PKForge.Domain.Tests;

public sealed class AppUpdateRulesTests
{
    [Theory]
    [InlineData("1.3.0", "v1.3.1", true)]
    [InlineData("1.3.0", "v1.10.0", true)]
    [InlineData("1.3.0", "v1.3.0", false)]
    [InlineData("1.3.0", "v1.2.9", false)]
    [InlineData("3.0.3", "v3.1.0", true)] // the 3.1 release, as a 3.0.3 install sees it
    [InlineData("3.1.0", "v3.0.3", false)]
    [InlineData("1.4.0", "v1.4.0-beta+build.1", false)]
    [InlineData("garbage", "v1.4.0", false)]
    public void ComparesStableReleaseTags(string installed, string candidate, bool expected) =>
        Assert.Equal(expected, AppUpdateRules.IsNewerVersion(installed, candidate));

    [Theory]
    [InlineData("3.1.1-fork.1", "v3.1.1-fork.1", false)] // the installed fork release is not an update to itself
    [InlineData("3.1.1-fork.1", "v3.1.1-fork.2", true)]
    [InlineData("3.1.1-fork.2", "v3.1.1-fork.1", false)]
    [InlineData("3.1.1-fork.9", "v3.1.1-fork.10", true)] // dotted numeric identifiers compare as numbers
    [InlineData("3.1.1-fork.10", "v3.1.1-fork.9", false)]
    [InlineData("3.1.1-fork.1", "v3.2.0-fork.1", true)]
    [InlineData("3.1.1-fork.7", "v3.2.0-fork.1", true)]
    [InlineData("3.1.1-fork.1", "v3.1.2", true)] // upstream-style tag
    [InlineData("3.1.1-fork.1", "v3.1.0-fork.5", false)]
    public void ComparesForkReleaseTags(string installed, string candidate, bool expected) =>
        Assert.Equal(expected, AppUpdateRules.IsNewerVersion(installed, candidate));

    [Fact]
    public void SkippedReleaseDoesNotPromptButNewerReleaseDoes()
    {
        Assert.False(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.3.1", "1.3.1"));
        Assert.True(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.4.0", "1.3.1"));
        Assert.False(AppUpdateRules.ShouldPromptAutomatically("1.3.0", "1.3.0", ""));
    }
}
