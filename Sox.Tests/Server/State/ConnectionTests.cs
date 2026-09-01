using NUnit.Framework;
using Sox.Server.State;
using Sox.Websocket.Rfc6455;
using Sox.Websocket.Rfc6455.Framing;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Sox.Tests.Server.State
{
    [TestFixture]
    public class ConnectionTests
    {
        [Test]
        public async Task Send_ManyConcurrentCalls_AllMessagesAreEventuallyWrittenWithoutLoss()
        {
            // This race (Connection.EnqueueAsync's old "Reader.Count == 1" drain trigger) only
            // reproduces on a lucky thread-scheduling interleaving, so a single burst catches it
            // only some of the time. Repeat the whole experiment across several independent
            // rounds (fresh Connection each time) to make an actual regression fail reliably,
            // without making a correctly-behaving Connection's result depend on luck at all - a
            // fix passes every round, every time.
            const int rounds = 8;
            const int messageCount = 100;

            for (var round = 0; round < rounds; round++)
            {
                var stream = new RecordingStream();
                using var connection = new Connection(stream, maxMessageBytes: 1024 * 1024);

                // Act - fire N concurrent Send() calls, each with a uniquely identifiable
                // payload. Awaiting Send() itself only proves the frame was accepted into the
                // internal write queue, not that it was actually flushed to the stream - the bug
                // this test targets could accept every frame instantly while still stranding
                // some of them unflushed forever. So the real assertion below polls the stream's
                // actual content instead of trusting Send()'s completion.
                //
                // Task.Run is essential here, not decoration: Connection.Send's whole chain
                // (Channel<>.WriteAsync on an unbounded channel, then this test's fake stream
                // write) completes synchronously with nothing to suspend on, so a plain
                // `Enumerable.Select(i => connection.Send(...))` loop runs each call to
                // completion one at a time on the calling thread - no real interleaving, so it
                // can't exercise a race at all. Task.Run forces each Send() onto the thread pool
                // so they can actually execute concurrently.
                var sendTasks = Enumerable.Range(0, messageCount)
                    .Select(i => Task.Run(() => connection.Send($"{i:D4}:{Guid.NewGuid()}")))
                    .ToArray();
                await Task.WhenAll(sendTasks);

                // Assert - every message eventually makes it onto the wire, with no loss/duplication
                var writtenFrames = await PollUntilAsync(
                    () => ParseFrames(stream.ToArray()),
                    frames => frames.Count >= messageCount,
                    TimeSpan.FromSeconds(1));

                var receivedIndices = writtenFrames
                    .Select(f => int.Parse(f.DecodedData.Split(':')[0]))
                    .ToHashSet();

                Assert.Multiple(() =>
                {
                    Assert.AreEqual(messageCount, writtenFrames.Count,
                        $"[round {round}] Expected exactly one frame per Send() call - some were lost/stranded unflushed.");
                    // Set-equality, not sequence-equality: receivedIndices is in wire arrival
                    // order (whatever order concurrent sends happened to interleave in), which
                    // has no reason to match Enumerable.Range's 0..N-1 order even when nothing
                    // is lost.
                    CollectionAssert.AreEquivalent(Enumerable.Range(0, messageCount), receivedIndices,
                        $"[round {round}] Some message indices were lost or duplicated.");
                });
            }
        }

        [Test]
        public async Task Close_CalledConcurrently_OnlyWritesOneCloseFrame()
        {
            // Arrange - a fresh Connection starts in ConnectionState.Connecting, which Close()
            // treats the same as Open, so this exercises the guard without a full handshake.
            var stream = new RecordingStream();
            using var connection = new Connection(stream, maxMessageBytes: 1024);

            // Act - call Close() concurrently from many callers, as could happen once several
            // independent close triggers exist (a received Close frame, the server tearing every
            // connection down, future ping/idle timeouts).
            var closeTasks = Enumerable.Range(0, 20)
                .Select(_ => Task.Run(() => connection.Close(CloseStatusCode.Normal)))
                .ToArray();
            await Task.WhenAll(closeTasks);

            // Assert - the connection's Close() body ran exactly once
            var closeFrames = ParseFrames(stream.ToArray())
                .Where(f => f.OpCode == OpCode.Close)
                .ToList();

            Assert.Multiple(() =>
            {
                Assert.AreEqual(1, closeFrames.Count,
                    "Close() should only ever write one Close frame, even when called concurrently.");
                Assert.AreEqual(ConnectionState.Closed, connection.State);
            });
        }

        private static async Task<List<WebSocketFrame>> PollUntilAsync(
            Func<List<WebSocketFrame>> read, Func<List<WebSocketFrame>, bool> isDone, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            List<WebSocketFrame> result;
            do
            {
                result = read();
                if (isDone(result))
                {
                    return result;
                }
                await Task.Delay(20);
            }
            while (DateTime.UtcNow < deadline);

            return result;
        }

        private static List<WebSocketFrame> ParseFrames(byte[] bytes)
        {
            var frames = new List<WebSocketFrame>();
            var buffer = new ReadOnlySequence<byte>(bytes);
            while (WebSocketFrame.TryParse(ref buffer, out var frame))
            {
                frames.Add(frame);
            }
            return frames;
        }

        /// <summary>
        /// A minimal write-capturing <see cref="Stream"/> standing in for the socket stream a
        /// real <c>Connection</c> would own. Supports <c>WriteTimeout</c> (unlike a plain
        /// <see cref="MemoryStream"/>, which throws on that setter) since <c>Connection</c>'s
        /// constructor sets it unconditionally. Reads are never exercised by these tests (no
        /// code here calls <c>Connection.ReadFramesAsync</c>), so <see cref="Read"/> is a stub.
        /// </summary>
        private sealed class RecordingStream : Stream
        {
            private readonly MemoryStream _written = new();

            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override bool CanTimeout => true;
            public override int ReadTimeout { get; set; }
            public override int WriteTimeout { get; set; }
            public override long Length => throw new NotSupportedException();
            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public byte[] ToArray()
            {
                lock (_written)
                {
                    return _written.ToArray();
                }
            }

            public override void Flush()
            {
            }

            public override Task FlushAsync(CancellationToken cancellationToken) => Task.CompletedTask;

            public override int Read(byte[] buffer, int offset, int count) => 0;

            public override void Write(byte[] buffer, int offset, int count)
            {
                lock (_written)
                {
                    _written.Write(buffer, offset, count);
                }
            }

            public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            {
                // Force a real async suspension point on every write, widening the window for
                // concurrent callers to interleave with an in-progress drain - without this,
                // everything in the Send()/EnqueueAsync/DrainAsync chain completes synchronously
                // and concurrent calls never get a chance to actually race.
                await Task.Yield();
                Write(buffer, offset, count);
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();
        }
    }
}
