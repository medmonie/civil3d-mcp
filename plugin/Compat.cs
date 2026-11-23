#if NETFRAMEWORK
// Civil 3D 2024 runs on .NET Framework 4.8. This file supplies the handful of .NET 6+ library members the plugin uses,
// as C# 14 extension members, so the shared sources compile unchanged for both releases. Compiled for net48 only.
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace C3dMcpBridge;

internal static class Net48Compat
{
    extension(Convert)
    {
        public static string ToHexString(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (var b in bytes) sb.Append(b.ToString("X2"));
            return sb.ToString();
        }
    }

    extension(RandomNumberGenerator)
    {
        public static byte[] GetBytes(int count)
        {
            var bytes = new byte[count];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return bytes;
        }
    }

    extension(SHA256)
    {
        public static byte[] HashData(byte[] data)
        {
            using var sha = SHA256.Create();
            return sha.ComputeHash(data);
        }
    }

    extension(Environment)
    {
        public static int ProcessId
        {
            get { using var p = System.Diagnostics.Process.GetCurrentProcess(); return p.Id; }
        }
    }

    extension(double)
    {
        public static bool IsFinite(double d) => !double.IsNaN(d) && !double.IsInfinity(d);
    }

    extension(Math)
    {
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static double Clamp(double value, double min, double max) => value < min ? min : value > max ? max : value;
    }

    extension(Enum)
    {
        public static string[] GetNames<T>() where T : struct, Enum => Enum.GetNames(typeof(T));
        public static T[] GetValues<T>() where T : struct, Enum => (T[])Enum.GetValues(typeof(T));
    }

    extension(Path)
    {
        public static bool IsPathFullyQualified(string path) =>
            path.Length >= 3 && char.IsLetter(path[0]) && path[1] == ':' && (path[2] == '\\' || path[2] == '/')
            || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal);
    }

    extension(File)
    {
        public static void Move(string source, string destination, bool overwrite)
        {
            if (overwrite && File.Exists(destination)) File.Delete(destination);
            File.Move(source, destination);
        }
    }

    extension(string s)
    {
        public bool StartsWith(char c) => s.Length > 0 && s[0] == c;
    }

    extension<T>(IEnumerable<T> source)
    {
        public T? MinBy<TKey>(Func<T, TKey> key)
        {
            var cmp = Comparer<TKey>.Default;
            using var e = source.GetEnumerator();
            if (!e.MoveNext()) return default;
            T best = e.Current;
            TKey bestKey = key(best);
            while (e.MoveNext())
            {
                var k = key(e.Current);
                if (cmp.Compare(k, bestKey) < 0) { best = e.Current; bestKey = k; }
            }
            return best;
        }
    }

    extension<T>(Task<T> task)
    {
        /// <summary>Completes with the task's result, or throws TimeoutException once the timeout elapses first.</summary>
        public async Task<T> WaitAsync(TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource();
            var done = await Task.WhenAny(task, Task.Delay(timeout, cts.Token)).ConfigureAwait(false);
            if (done != task) throw new TimeoutException();
            cts.Cancel();
            return await task.ConfigureAwait(false);
        }
    }

    extension(TcpListener listener)
    {
        public async Task<TcpClient> AcceptTcpClientAsync(CancellationToken ct)
        {
            // Stop() on the listener (done on shutdown) ends a pending accept with an exception.
            using (ct.Register(() => { try { listener.Stop(); } catch { /* already stopped */ } }))
                return await listener.AcceptTcpClientAsync().ConfigureAwait(false);
        }
    }

    extension(StreamReader reader)
    {
        /// <summary>Reads into the array behind <paramref name="buffer"/>; cancellation abandons the read (the caller then closes the connection).</summary>
        public async Task<int> ReadAsync(Memory<char> buffer, CancellationToken ct)
        {
            if (!System.Runtime.InteropServices.MemoryMarshal.TryGetArray<char>(buffer, out var seg))
                throw new NotSupportedException("buffer must be array-backed");
            var read = reader.ReadAsync(seg.Array!, seg.Offset, seg.Count);
            var tcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            using (ct.Register(() => tcs.TrySetCanceled()))
            {
                var done = await Task.WhenAny(read, tcs.Task).ConfigureAwait(false);
                if (done != read) throw new OperationCanceledException(ct);
                return await read.ConfigureAwait(false);
            }
        }
    }
}

/// <summary>Constant-time comparison (System.Security.Cryptography.CryptographicOperations is .NET Core only).</summary>
internal static class CryptographicOperations
{
    public static bool FixedTimeEquals(byte[] left, byte[] right)
    {
        if (left.Length != right.Length) return false;
        int diff = 0;
        for (int i = 0; i < left.Length; i++) diff |= left[i] ^ right[i];
        return diff == 0;
    }
}
#endif
