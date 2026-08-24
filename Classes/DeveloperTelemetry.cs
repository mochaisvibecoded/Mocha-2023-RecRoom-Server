using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace Mocha2023.Classes
{

    internal static class DeveloperTelemetry
    {
        private const int MaximumRequests = 500;
        private const int MaximumPoints = 300;
        private static readonly object Sync = new();
        private static readonly Queue<RequestEntry> Requests = new();
        private static readonly Queue<TelemetryPoint> Points = new();

        private static long _currentSecond;
        private static long _currentRequests;
        private static long _currentInboundBytes;
        private static long _currentOutboundBytes;
        private static long _currentErrors;
        private static long _currentLatencyMilliseconds;
        private static long _totalRequests;
        private static long _totalInboundBytes;
        private static long _totalOutboundBytes;
        private static long _totalErrors;

        public static long EstimateInboundBytes(HttpRequest request)
        {
            long bytes = Math.Max(0, request.ContentLength ?? 0);
            bytes += request.Method.Length +
                (request.Path.Value?.Length ?? 0);

            foreach (var header in request.Headers)
            {
                bytes += header.Key.Length + 4;
                foreach (string? value in header.Value)
                    bytes += value?.Length ?? 0;
            }

            return bytes;
        }

        public static void RecordInboundBytes(long bytes)
        {
            if (bytes <= 0)
                return;

            lock (Sync)
            {
                RotateNoLock(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                _currentInboundBytes += bytes;
                _totalInboundBytes += bytes;
            }
        }

        public static void RecordOutboundBytes(long bytes)
        {
            if (bytes <= 0)
                return;

            lock (Sync)
            {
                RotateNoLock(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
                _currentOutboundBytes += bytes;
                _totalOutboundBytes += bytes;
            }
        }

        public static void RecordRequest(
            int statusCode,
            string method,
            string path,
            long elapsedMilliseconds,
            long inboundBytes,
            long outboundBytes)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string safePath = string.IsNullOrWhiteSpace(path)
                ? "/"
                : path.Length > 300
                    ? path[..300]
                    : path;

            lock (Sync)
            {
                RotateNoLock(now.ToUnixTimeSeconds());

                _currentRequests++;
                _currentLatencyMilliseconds += Math.Max(0, elapsedMilliseconds);
                _totalRequests++;

                if (statusCode >= StatusCodes.Status400BadRequest)
                {
                    _currentErrors++;
                    _totalErrors++;
                }

                Requests.Enqueue(new RequestEntry(
                    now,
                    method,
                    safePath,
                    statusCode,
                    elapsedMilliseconds,
                    inboundBytes,
                    outboundBytes));

                while (Requests.Count > MaximumRequests)
                    Requests.Dequeue();
            }
        }

        public static TelemetrySnapshot Snapshot(int requestCount = 100)
        {
            requestCount = Math.Clamp(requestCount, 1, 250);

            lock (Sync)
            {
                long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
                RotateNoLock(now);

                var points = Points.ToList();
                points.Add(CurrentPointNoLock());

                return new TelemetrySnapshot(
                    _totalRequests,
                    _totalInboundBytes,
                    _totalOutboundBytes,
                    _totalErrors,
                    Requests.Reverse().Take(requestCount).ToList(),
                    points.TakeLast(120).ToList());
            }
        }

        private static void RotateNoLock(long second)
        {
            if (_currentSecond == 0)
            {
                _currentSecond = second;
                return;
            }

            if (second <= _currentSecond)
                return;

            Points.Enqueue(CurrentPointNoLock());
            while (Points.Count > MaximumPoints)
                Points.Dequeue();

            long gap = Math.Min(second - _currentSecond - 1, MaximumPoints);
            for (long offset = gap; offset > 0; offset--)
            {
                Points.Enqueue(new TelemetryPoint(
                    second - offset,
                    0,
                    0,
                    0,
                    0,
                    0));

                while (Points.Count > MaximumPoints)
                    Points.Dequeue();
            }

            _currentSecond = second;
            _currentRequests = 0;
            _currentInboundBytes = 0;
            _currentOutboundBytes = 0;
            _currentErrors = 0;
            _currentLatencyMilliseconds = 0;
        }

        private static TelemetryPoint CurrentPointNoLock() =>
            new(
                _currentSecond,
                _currentRequests,
                _currentInboundBytes,
                _currentOutboundBytes,
                _currentErrors,
                _currentRequests == 0
                    ? 0
                    : (double)_currentLatencyMilliseconds / _currentRequests);

        internal sealed record RequestEntry(
            DateTimeOffset At,
            string Method,
            string Path,
            int StatusCode,
            long ElapsedMilliseconds,
            long InboundBytes,
            long OutboundBytes);

        internal sealed record TelemetryPoint(
            long UnixSecond,
            long Requests,
            long InboundBytes,
            long OutboundBytes,
            long Errors,
            double AverageLatencyMilliseconds);

        internal sealed record TelemetrySnapshot(
            long TotalRequests,
            long TotalInboundBytes,
            long TotalOutboundBytes,
            long TotalErrors,
            IReadOnlyList<RequestEntry> Requests,
            IReadOnlyList<TelemetryPoint> Points);
    }

    internal sealed class CountingResponseStream : Stream
    {
        private readonly Stream _inner;
        private readonly Action<int> _onWrite;

        public CountingResponseStream(Stream inner, Action<int> onWrite)
        {
            _inner = inner;
            _onWrite = onWrite;
        }

        public long BytesWritten { get; private set; }
        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;
        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override Task FlushAsync(CancellationToken cancellationToken) =>
            _inner.FlushAsync(cancellationToken);
        public override int Read(byte[] buffer, int offset, int count) =>
            _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) =>
            _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            _inner.Write(buffer, offset, count);
            Count(count);
        }

        public override async Task WriteAsync(
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            await _inner.WriteAsync(
                buffer.AsMemory(offset, count),
                cancellationToken);
            Count(count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            _inner.Write(buffer);
            Count(buffer.Length);
        }

        public override async ValueTask WriteAsync(
            ReadOnlyMemory<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            await _inner.WriteAsync(buffer, cancellationToken);
            Count(buffer.Length);
        }

        private void Count(int bytes)
        {
            if (bytes <= 0)
                return;

            BytesWritten += bytes;
            _onWrite(bytes);
        }
    }
}
