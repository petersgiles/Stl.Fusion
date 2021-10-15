using Stl.Locking;

namespace Stl.Caching;

public abstract class ComputingCacheBase<TKey, TValue> : AsyncKeyResolverBase<TKey, TValue>
    where TKey : notnull
{
    public IAsyncCache<TKey, TValue> Cache { get; }
    public IAsyncLockSet<TKey> Locks { get; }

    protected ComputingCacheBase(IAsyncCache<TKey, TValue> cache, IAsyncLockSet<TKey>? lockSet = null)
    {
        Cache = cache;
        Locks = lockSet ?? new AsyncLockSet<TKey>(ReentryMode.CheckedFail);
    }

    public override async ValueTask<TValue> Get(TKey key, CancellationToken cancellationToken = default)
    {
        // Read-Lock-RetryRead-Compute-Store pattern

        var maybeValue = await Cache.TryGet(key, cancellationToken).ConfigureAwait(false);
        if (maybeValue.IsSome(out var value))
            return value;

        using var @lock = await Locks.Lock(key, cancellationToken).ConfigureAwait(false);

        maybeValue = await Cache.TryGet(key, cancellationToken).ConfigureAwait(false);
        if (maybeValue.IsSome(out value))
            return value;

        var result = await Compute(key, cancellationToken).ConfigureAwait(false);
        await Cache.Set(key, result, cancellationToken).ConfigureAwait(false);
        return result;
    }

    public override async ValueTask<Option<TValue>> TryGet(TKey key, CancellationToken cancellationToken = default)
    {
        var value = await Get(key, cancellationToken).ConfigureAwait(false);
        return Option.Some(value);
    }

    protected abstract ValueTask<TValue> Compute(TKey key, CancellationToken cancellationToken = default);
}

public class ComputingCache<TKey, TValue> : ComputingCacheBase<TKey, TValue>
    where TKey : notnull
{
    private Func<TKey, CancellationToken, ValueTask<TValue>> Computer { get; }

    public ComputingCache(IAsyncCache<TKey, TValue> cache, Func<TKey, CancellationToken, ValueTask<TValue>> computer)
        : base(cache)
        => Computer = computer;

    public ComputingCache(IAsyncCache<TKey, TValue> cache, IAsyncLockSet<TKey> lockSet, Func<TKey, CancellationToken, ValueTask<TValue>> computer)
        : base(cache, lockSet)
        => Computer = computer;

    protected override ValueTask<TValue> Compute(TKey key, CancellationToken cancellationToken = default)
        => Computer.Invoke(key, cancellationToken);
}
