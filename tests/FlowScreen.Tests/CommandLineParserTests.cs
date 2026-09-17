using FlowScreen.CommandLine;
using Xunit;

namespace FlowScreen.Tests;

/// <summary>
/// The screensaver argument contract. Every form here has been observed in the
/// wild from some combination of the shell, the Screen Saver control panel and
/// the lock screen, which is why the parser is deliberately permissive.
/// </summary>
public class CommandLineParserTests
{
    [Theory]
    [InlineData("/s")]
    [InlineData("/S")]
    [InlineData("-s")]
    [InlineData("--s")]
    [InlineData("/s:")]
    public void RecognisesScreenSaver(string argument)
    {
        var options = CommandLineParser.Parse([argument]);
        Assert.Equal(StartupMode.ScreenSaver, options.Mode);
    }

    [Theory]
    [InlineData("/c")]
    [InlineData("/C")]
    [InlineData("-c")]
    public void RecognisesConfigure(string argument)
    {
        var options = CommandLineParser.Parse([argument]);
        Assert.Equal(StartupMode.Configure, options.Mode);
    }

    [Fact]
    public void NoArgumentsOpensTheSettingsDialog()
    {
        Assert.Equal(StartupMode.Configure, CommandLineParser.Parse([]).Mode);
    }

    [Fact]
    public void ParsesPreviewWithAColon()
    {
        var options = CommandLineParser.Parse(["/p:123456"]);
        Assert.Equal(StartupMode.Preview, options.Mode);
        Assert.Equal(new IntPtr(123456), options.TargetWindow);
    }

    [Fact]
    public void ParsesPreviewWithASeparateArgument()
    {
        var options = CommandLineParser.Parse(["/p", "123456"]);
        Assert.Equal(StartupMode.Preview, options.Mode);
        Assert.Equal(new IntPtr(123456), options.TargetWindow);
    }

    [Fact]
    public void ParsesPreviewWithAnEqualsSign()
    {
        var options = CommandLineParser.Parse(["/p=987654"]);
        Assert.Equal(StartupMode.Preview, options.Mode);
        Assert.Equal(new IntPtr(987654), options.TargetWindow);
    }

    [Fact]
    public void ParsesUppercasePreview()
    {
        var options = CommandLineParser.Parse(["/P:4242"]);
        Assert.Equal(StartupMode.Preview, options.Mode);
        Assert.Equal(new IntPtr(4242), options.TargetWindow);
    }

    [Fact]
    public void ParsesHexadecimalHandles()
    {
        var options = CommandLineParser.Parse(["/p:0x1E0848"]);
        Assert.Equal(StartupMode.Preview, options.Mode);
        Assert.Equal(new IntPtr(0x1E0848), options.TargetWindow);
    }

    [Fact]
    public void ParsesHandlesBeyondIntRange()
    {
        // 64-bit HWNDs routinely exceed int.MaxValue.
        const long handle = 4294967297L;
        var options = CommandLineParser.Parse([$"/p:{handle}"]);
        Assert.Equal(new IntPtr(handle), options.TargetWindow);
    }

    [Fact]
    public void PreviewWithoutAHandleFallsBackToSettings()
    {
        // A preview with no surface to draw into is meaningless, and showing
        // nothing at all would look like a crash.
        var options = CommandLineParser.Parse(["/p"]);
        Assert.Equal(StartupMode.Configure, options.Mode);
        Assert.Equal(IntPtr.Zero, options.TargetWindow);
    }

    [Fact]
    public void PreviewWithAZeroHandleFallsBackToSettings()
    {
        Assert.Equal(StartupMode.Configure, CommandLineParser.Parse(["/p:0"]).Mode);
    }

    [Fact]
    public void ConfigureCarriesItsOwnerWindow()
    {
        var options = CommandLineParser.Parse(["/c:555"]);
        Assert.Equal(StartupMode.Configure, options.Mode);
        Assert.Equal(new IntPtr(555), options.TargetWindow);
    }

    [Fact]
    public void ObsoletePasswordSwitchOpensSettings()
    {
        var options = CommandLineParser.Parse(["/a", "777"]);
        Assert.Equal(StartupMode.Configure, options.Mode);
    }

    [Fact]
    public void IgnoresLeadingNoise()
    {
        var options = CommandLineParser.Parse(["", "   ", "/s"]);
        Assert.Equal(StartupMode.ScreenSaver, options.Mode);
    }

    [Fact]
    public void UnknownSwitchesFallBackToSettings()
    {
        Assert.Equal(StartupMode.Configure, CommandLineParser.Parse(["/zzz"]).Mode);
    }

    [Fact]
    public void FirstRecognisedSwitchWins()
    {
        var options = CommandLineParser.Parse(["/s", "/c"]);
        Assert.Equal(StartupMode.ScreenSaver, options.Mode);
    }

    [Fact]
    public void RecognisesDebug()
    {
        Assert.Equal(StartupMode.Debug, CommandLineParser.Parse(["/debug"]).Mode);
    }
}
