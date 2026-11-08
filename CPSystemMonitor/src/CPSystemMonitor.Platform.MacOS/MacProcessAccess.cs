using System.Diagnostics;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CPSystemMonitor.Core.Monitoring;

namespace CPSystemMonitor.Platform.MacOS;

/// <summary>Session-only, read-only process statistics. The UI never runs as root.</summary>
[SupportedOSPlatform("macos")]
public sealed class MacProcessAccess : IDisposable
{
    public const string HelperArgument = "--process-statistics-helper";
    private readonly Socket _socket;
    private readonly string _directory;
    private readonly object _gate = new();
    private bool _disposed;
    public sealed record Snapshot(ProcessReading[] Processes, int InaccessibleCount);

    private MacProcessAccess(Socket socket, string directory) { _socket = socket; _directory = directory; }

    public static async Task<MacProcessAccess> RequestAsync(string executable, string? assembly, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string directory = Path.Combine("/private/tmp", "cpsm-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        string path = Path.Combine(directory, "reader.sock");
        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        Socket? connection = null;
        try
        {
            listener.Bind(new UnixDomainSocketEndPoint(path)); listener.Listen(1);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            using var prompt = new Process { StartInfo = new("/usr/bin/osascript")
                { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true } };
            prompt.StartInfo.ArgumentList.Add("-e");
            prompt.StartInfo.ArgumentList.Add(AuthorizationScript(executable, assembly, path, token));
            prompt.Start();
            var error = prompt.StandardError.ReadToEndAsync(timeout.Token);
            var output = prompt.StandardOutput.ReadToEndAsync(timeout.Token);
            try { await prompt.WaitForExitAsync(timeout.Token); }
            catch { if (!prompt.HasExited) prompt.Kill(true); throw; }
            string message = await error; await output;
            if (prompt.ExitCode != 0)
            {
                if (message.Contains("-128", StringComparison.Ordinal)) throw new OperationCanceledException("Authorization cancelled");
                throw new IOException(message.Trim());
            }
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try { connection = await listener.AcceptAsync(timeout.Token); }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            { throw new IOException("Process statistics helper did not connect"); }
            connection.ReceiveTimeout = 3000; connection.SendTimeout = 3000;
            // Verify both the OS peer identity and the per-session secret.
            if (getpeereid(connection.Handle, out uint uid, out _) != 0 || uid != 0)
                throw new IOException("Invalid process statistics helper identity");
            byte[] hello = new byte[64]; ReceiveExactly(connection, hello);
            if (!CryptographicOperations.FixedTimeEquals(hello, Encoding.ASCII.GetBytes(token)))
                throw new IOException("Invalid process statistics helper session");
            return new(connection, directory);
        }
        catch
        {
            connection?.Dispose();
            Directory.Delete(directory, true);
            throw;
        }
    }

    // Shell quoting and AppleScript quoting are separate layers.
    public static string AuthorizationScript(string executable, string? assembly, string socket, string token)
    {
        static string Quote(string s) => "'" + s.Replace("'", "'\\''") + "'";
        string command = Quote(executable) + (assembly is null ? "" : " " + Quote(assembly)) + " " + HelperArgument
            + " " + Quote(socket) + " " + Quote(token) + " </dev/null >/dev/null 2>&1 &";
        return "do shell script \"" + command.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n") + "\" with administrator privileges";
    }

    public Snapshot Read()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _socket.Send(new byte[] { 1 });
            byte[] header = new byte[4]; ReceiveExactly(_socket, header);
            int length = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(header);
            if (length <= 0 || length > 16 * 1024 * 1024) throw new IOException("Invalid process statistics response");
            byte[] data = new byte[length]; ReceiveExactly(_socket, data);
            return JsonSerializer.Deserialize<Snapshot>(data) ?? throw new IOException("Missing process statistics");
        }
    }

    /// <summary>Runs before UI, settings, hooks, or single-instance initialization.</summary>
    public static void RunHelper(string socketPath, string token)
    {
        if (geteuid() != 0 || token.Length != 64 || !token.All(Uri.IsHexDigit)) return;
        try
        {
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            socket.ReceiveTimeout = 30000; socket.SendTimeout = 3000;
            socket.Connect(new UnixDomainSocketEndPoint(socketPath));
            SendAll(socket, Encoding.ASCII.GetBytes(token));
            using var source = new MacMonitorSource();
            byte[] request = new byte[1];
            while (socket.Receive(request) == 1 && request[0] == 1)
            {
                // The only supported operation is reading process statistics.
                var rows = source.ReadLocalProcesses(includeDisk: true);
                byte[] data = JsonSerializer.SerializeToUtf8Bytes(new Snapshot(rows.ToArray(), source.InaccessibleProcessCount));
                byte[] header = new byte[4];
                System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(header, data.Length);
                SendAll(socket, header); SendAll(socket, data);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException) { }
    }

    private static void SendAll(Socket socket, ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty) { int n = socket.Send(bytes); if (n == 0) throw new IOException("Helper disconnected"); bytes = bytes[n..]; }
    }
    private static void ReceiveExactly(Socket socket, Span<byte> bytes)
    {
        while (!bytes.IsEmpty) { int n = socket.Receive(bytes); if (n == 0) throw new IOException("Helper disconnected"); bytes = bytes[n..]; }
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true; _socket.Dispose();
            Directory.Delete(_directory, true);
        }
    }
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern uint geteuid();
    [DllImport("/usr/lib/libSystem.B.dylib")] private static extern int getpeereid(nint socket, out uint uid, out uint gid);
}
