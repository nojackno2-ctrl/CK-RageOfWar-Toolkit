using System.Security.Cryptography;
using System.Text;

namespace CKToolkit.Core.Saves;

/// <summary>Serializes player.ini writers across CKToolkit processes in this logon session.</summary>
internal sealed class PlayerIniLock : IDisposable
{
    private readonly Mutex _mutex;
    private bool _held;

    private PlayerIniLock(Mutex mutex, bool held)
    {
        _mutex = mutex;
        _held = held;
    }

    public static PlayerIniLock Acquire(string playerIniPath)
    {
        string fullPath = Path.GetFullPath(playerIniPath).ToUpperInvariant();
        string key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fullPath)));
        var mutex = new Mutex(false, @"Local\CKToolkit-playerini-" + key);
        bool held;
        try
        {
            held = mutex.WaitOne(TimeSpan.FromSeconds(10));
        }
        catch (AbandonedMutexException)
        {
            held = true;
        }
        if (!held)
        {
            mutex.Dispose();
            throw new IOException("Timed out waiting for another CKToolkit process to finish updating player.ini.");
        }
        return new PlayerIniLock(mutex, true);
    }

    public void Dispose()
    {
        if (_held)
        {
            _mutex.ReleaseMutex();
            _held = false;
        }
        _mutex.Dispose();
    }
}
