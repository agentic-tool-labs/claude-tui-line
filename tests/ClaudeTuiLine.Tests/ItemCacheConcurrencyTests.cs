namespace ClaudeTuiLine.Tests;

// #69: ItemCache.Write/WriteWidth already write via a GUID-suffixed temp file in cacheDir
// followed by File.Move(..., overwrite: true) — an atomic rename, not an in-place write — so
// concurrent CLI subprocesses racing on the same cache key can never produce a torn/partial
// read. This pins that property empirically: many concurrent writers to one key, then confirms
// the file a reader sees afterward always deserializes cleanly and exactly matches one of the
// entries that was written, never a byte-level mix of two. Reverting Write to a direct
// File.WriteAllText(path, ...) (no temp file, no rename) makes this test fail intermittently,
// confirming it actually exercises the atomicity guarantee rather than being green by construction.
public class ItemCacheConcurrencyTests
{
    [Fact]
    public void ConcurrentWritesToSameKey_NeverProduceATornOrUnreadableCacheFile()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"item-cache-concurrency-{Guid.NewGuid():N}");
        const string key = "shared-key";
        const int writerCount = 32;

        var entries = Enumerable.Range(0, writerCount)
            .Select(i => new CacheEntry($"value-{i}", DateTimeOffset.FromUnixTimeSeconds(i), i))
            .ToArray();

        try
        {
            Parallel.ForEach(entries, entry => ItemCache.Write(cacheDir, key, entry));

            var result = ItemCache.TryRead(cacheDir, key);

            Assert.NotNull(result);
            Assert.Contains(entries, e => e == result);
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }

    [Fact]
    public void ConcurrentFirstWritesToDistinctKeys_AllSucceedDespiteSharedCacheDirCreation()
    {
        var cacheDir = Path.Combine(Path.GetTempPath(), $"item-cache-concurrency-{Guid.NewGuid():N}");
        const int writerCount = 32;

        var keyedEntries = Enumerable.Range(0, writerCount)
            .Select(i => (Key: $"key-{i}", Entry: new CacheEntry($"value-{i}", DateTimeOffset.FromUnixTimeSeconds(i), i)))
            .ToArray();

        try
        {
            Parallel.ForEach(keyedEntries, ke => ItemCache.Write(cacheDir, ke.Key, ke.Entry));

            foreach (var (key, entry) in keyedEntries)
            {
                Assert.Equal(entry, ItemCache.TryRead(cacheDir, key));
            }
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }
}
