using System.IO;

namespace LastEpochHelper.Tests;

public class ScreenReaderTests
{
    /// <summary>
    /// Reads the real screen, so it only runs on request: set LEH_OCR_OUT to a file path and the
    /// recognised lines (with letter heights) are written there. LEH_OCR_RECT = "left,top,right,bottom".
    /// </summary>
    [Fact]
    public async Task ReadsTheScreen_WhenAskedTo()
    {
        string? output = Environment.GetEnvironmentVariable("LEH_OCR_OUT");
        if (output is null) return;

        var parts = (Environment.GetEnvironmentVariable("LEH_OCR_RECT") ?? "0,0,1920,1080").Split(',').Select(int.Parse).ToArray();
        var reader = new ScreenReader();
        var started = DateTime.UtcNow;
        var lines = await reader.ReadAsync(
            new Native.RECT { Left = parts[0], Top = parts[1], Right = parts[2], Bottom = parts[3] }, Array.Empty<Native.RECT>());

        File.WriteAllLines(output, new[] { $"available={reader.Available} lines={lines.Count} ms={(DateTime.UtcNow - started).TotalMilliseconds:0}" }
            .Concat(lines.Select(l => $"{l.Height,5:0}  {l.Text}")));
        Assert.True(reader.Available);
    }
}
