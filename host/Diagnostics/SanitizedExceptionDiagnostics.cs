using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NowPlayingOverlay.Host.Diagnostics;

internal static class SanitizedExceptionDiagnostics
{
    private const int MaximumExceptionDepth = 8;
    private const int MaximumMethods = 12;

    public static string Create(Exception error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return CreateCore(error);
    }

    private static string CreateCore(Exception error)
    {
        var exceptions = new List<string>();
        var methods = new List<string>();
        var seenMethods = new HashSet<string>(StringComparer.Ordinal);
        var current = error;
        for (var depth = 0; current is not null && depth < MaximumExceptionDepth; depth++)
        {
            exceptions.Add(Describe(current));
            AddMethods(current, methods, seenMethods);
            current = current.InnerException;
        }

        var result = new StringBuilder()
            .Append("Diagnostic ")
            .Append(CreateId())
            .Append(": ")
            .AppendJoin(" -> ", exceptions);
        if (methods.Count > 0)
        {
            result.Append("; methods ").AppendJoin(" > ", methods);
        }

        return result.Append('.').ToString();
    }

    private static string Describe(Exception error)
    {
        var description = new StringBuilder(error.GetType().FullName ?? error.GetType().Name)
            .Append("[HRESULT=0x")
            .Append(unchecked((uint)error.HResult).ToString("X8"));
        switch (error)
        {
            case Win32Exception win32:
                description.Append(",NativeError=").Append(win32.NativeErrorCode);
                break;
            case HttpRequestException { StatusCode: { } statusCode }:
                description.Append(",HTTP=").Append((int)statusCode);
                break;
            case JsonException json:
                if (json.LineNumber is not null)
                {
                    description.Append(",JsonLine=").Append(json.LineNumber.Value);
                }

                if (json.BytePositionInLine is not null)
                {
                    description.Append(",JsonByte=").Append(json.BytePositionInLine.Value);
                }

                break;
        }

        return description.Append(']').ToString();
    }

    private static void AddMethods(
        Exception error,
        List<string> methods,
        HashSet<string> seenMethods)
    {
        foreach (var frame in new StackTrace(error, fNeedFileInfo: false).GetFrames() ?? [])
        {
            var method = frame.GetMethod();
            if (method is null)
            {
                continue;
            }

            var name = $"{method.DeclaringType?.FullName ?? "<global>"}.{method.Name}";
            if (seenMethods.Add(name))
            {
                methods.Add(name);
                if (methods.Count == MaximumMethods)
                {
                    return;
                }
            }
        }
    }

    private static string CreateId()
    {
        return Guid.NewGuid().ToString("N")[..12];
    }
}
