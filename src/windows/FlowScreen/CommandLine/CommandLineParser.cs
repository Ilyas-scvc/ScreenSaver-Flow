using System.Globalization;

namespace FlowScreen.CommandLine;

/// <summary>
/// Parses the arguments Windows passes to a <c>.scr</c>.
///
/// There is no single documented format: over the years the shell, the Screen
/// Saver control panel, the lock screen and third-party tools have all used
/// slightly different spellings. All of the following occur in the wild and are
/// accepted here:
///
/// <code>
///   /s      /S      -s      --s
///   /c      /c:12345        /c 12345
///   /p 12345        /p:12345        /P:12345
///   /a 12345        (obsolete password dialog - treated as /c)
/// </code>
///
/// The handle may arrive as a decimal or a hexadecimal string, and on 64-bit it
/// can exceed <see cref="int"/>, so it is parsed as a signed 64-bit value.
/// </summary>
public static class CommandLineParser
{
    public static StartupOptions Parse(IReadOnlyList<string> args)
    {
        if (args.Count == 0) return StartupOptions.Default;

        for (var i = 0; i < args.Count; i++)
        {
            var token = args[i];
            if (string.IsNullOrWhiteSpace(token)) continue;

            if (!TrySplitSwitch(token, out var name, out var inlineValue)) continue;

            switch (name)
            {
                case 's':
                    return new StartupOptions(StartupMode.ScreenSaver, IntPtr.Zero);

                case 'p':
                {
                    var handle = ResolveHandle(inlineValue, args, ref i);
                    // A preview with no surface to draw into is meaningless; fall
                    // back to the settings dialog rather than showing nothing.
                    return handle == IntPtr.Zero
                        ? new StartupOptions(StartupMode.Configure, IntPtr.Zero)
                        : new StartupOptions(StartupMode.Preview, handle);
                }

                case 'c':
                case 'a':
                    return new StartupOptions(StartupMode.Configure, ResolveHandle(inlineValue, args, ref i));

                case 'd':
                    return new StartupOptions(StartupMode.Debug, IntPtr.Zero);
            }

            if (string.Equals(name.ToString(), "?", StringComparison.Ordinal))
            {
                return StartupOptions.Default;
            }
        }

        return StartupOptions.Default;
    }

    /// <summary>
    /// Splits <c>/p:1234</c>, <c>-s</c>, <c>--debug</c> into a lowercase leading
    /// letter and whatever followed a ':' or '='.
    /// </summary>
    private static bool TrySplitSwitch(string token, out char name, out string? inlineValue)
    {
        name = '\0';
        inlineValue = null;

        var start = 0;
        while (start < token.Length && (token[start] == '/' || token[start] == '-')) start++;
        if (start == 0 || start >= token.Length) return false;

        var body = token[start..];
        var separator = body.IndexOfAny([':', '=']);
        if (separator >= 0)
        {
            inlineValue = body[(separator + 1)..];
            body = body[..separator];
        }

        if (body.Length == 0) return false;
        name = char.ToLowerInvariant(body[0]);
        return true;
    }

    /// <summary>
    /// Takes the handle from the switch itself if it was inline, otherwise
    /// consumes the following argument. Advances <paramref name="index"/> when it
    /// consumes one.
    /// </summary>
    private static IntPtr ResolveHandle(string? inlineValue, IReadOnlyList<string> args, ref int index)
    {
        if (TryParseHandle(inlineValue, out var handle)) return handle;

        if (index + 1 < args.Count && TryParseHandle(args[index + 1], out handle))
        {
            index++;
            return handle;
        }

        return IntPtr.Zero;
    }

    private static bool TryParseHandle(string? value, out IntPtr handle)
    {
        handle = IntPtr.Zero;
        if (string.IsNullOrWhiteSpace(value)) return false;

        var text = value.Trim();
        var hex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
        if (hex) text = text[2..];

        var style = hex ? NumberStyles.HexNumber : NumberStyles.Integer;
        if (!long.TryParse(text, style, CultureInfo.InvariantCulture, out var raw)) return false;
        if (raw == 0) return false;

        handle = new IntPtr(raw);
        return true;
    }
}
