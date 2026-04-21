using System.Collections.Concurrent;
using Aiursoft.Scanner.Abstractions;

namespace Aiursoft.Apkg.Services.FileStorage;

/// <summary>
/// Provides a thread-safe mechanism to lock on certain file paths
/// so that concurrent read/write operations do not clash.
/// </summary>
public class FileLockProvider : ISingletonDependency
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();

    public SemaphoreSlim GetLock(string path)
    {
        return _locks.GetOrAdd(path, _ => new SemaphoreSlim(1, 1));
    }
}
