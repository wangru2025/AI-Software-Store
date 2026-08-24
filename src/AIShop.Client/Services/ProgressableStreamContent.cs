using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace AIShop.Client.Services
{
    public sealed class ProgressableStreamContent : HttpContent
    {
        private readonly Stream _source;
        private readonly Action<long, long> _progress;
        private readonly Action _beforeChunk;
        private readonly int _bufferSize;

        public ProgressableStreamContent(Stream source, Action<long, long> progress, Action beforeChunk = null, int bufferSize = 81920)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _progress = progress;
            _beforeChunk = beforeChunk;
            _bufferSize = bufferSize;
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _source.Length - _source.Position;
            return true;
        }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext context)
        {
            var buffer = new byte[_bufferSize];
            var total = _source.Length - _source.Position;
            long sent = 0;

            while (true)
            {
                _beforeChunk?.Invoke();
                var read = await _source.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
                if (read == 0)
                {
                    break;
                }

                _beforeChunk?.Invoke();
                await stream.WriteAsync(buffer, 0, read).ConfigureAwait(false);
                sent += read;
                _progress?.Invoke(sent, total);
            }
        }
    }
}
