using System;
using System.Threading;
using System.Threading.Tasks;

namespace QuestPatcher.Core.Utils
{
    public class AsyncLock
    {
        private readonly SemaphoreSlim _semaphore = new(1, 1);

        public async Task<Releaser> LockAsync(CancellationToken token = default)
        {
            await _semaphore.WaitAsync(token).ConfigureAwait(false);
            return new Releaser(_semaphore);
        }

        public Releaser Lock()
        {
            _semaphore.Wait();
            return new Releaser(_semaphore);
        }

        public sealed class Releaser : IDisposable
        {
            private readonly SemaphoreSlim _semaphore;
            private int _released;

            public Releaser(SemaphoreSlim semaphore)
            {
                _semaphore = semaphore;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _released, 1) == 0)
                {
                    _semaphore.Release();
                }
            }
        }
    }
}
