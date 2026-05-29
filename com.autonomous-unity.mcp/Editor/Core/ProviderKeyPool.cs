using System;
using System.Collections.Generic;
using System.Linq;

namespace AutonomousMcp.Editor.Core
{
    /// <summary>
    /// Provider-agnostic pool of API keys that you own, with round-robin rotation and
    /// per-key exponential-backoff cooldowns. The point is rate-limit mitigation: when a
    /// key gets a 429 / quota error, it is parked on a growing cooldown and the next usable
    /// key is leased instead. When every key is cooling, callers can fail over to another
    /// provider (e.g. a keyless one).
    ///
    /// Keys are sourced from environment variables at request time and never persisted to disk,
    /// matching the project's existing secrets convention. A single env var may hold several keys
    /// separated by commas, semicolons, whitespace or newlines — handy for rotating across
    /// multiple of your own accounts' free tiers.
    ///
    /// Thread-safe. Stateless across domain reloads is fine: cooldowns are an in-memory
    /// optimization, not correctness-critical.
    /// </summary>
    public sealed class ProviderKeyPool
    {
        private sealed class KeySlot
        {
            public string Key;
            public DateTime CooldownUntilUtc;       // DateTime.MinValue == usable now
            public int ConsecutiveFailures;
        }

        private readonly List<KeySlot> _slots;
        private readonly object _gate = new object();
        private int _cursor;

        // Backoff tuning. Cooldown = min(BaseCooldown * 2^(failures-1), MaxCooldown), with jitter.
        private static readonly TimeSpan BaseCooldown = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);
        // 401/403 (bad/blocked key) should rest far longer than a transient 429.
        private static readonly TimeSpan AuthFailureCooldown = TimeSpan.FromMinutes(30);

        public ProviderKeyPool(IEnumerable<string> keys)
        {
            _slots = (keys ?? Enumerable.Empty<string>())
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Select(k => k.Trim())
                .Distinct(StringComparer.Ordinal)
                .Select(k => new KeySlot { Key = k })
                .ToList();
        }

        /// <summary>Total number of keys held (regardless of cooldown).</summary>
        public int Count
        {
            get { lock (_gate) { return _slots.Count; } }
        }

        /// <summary>True if at least one key exists and is not currently cooling down.</summary>
        public bool HasUsableKey(DateTime nowUtc)
        {
            lock (_gate)
            {
                return _slots.Any(s => s.CooldownUntilUtc <= nowUtc);
            }
        }

        /// <summary>
        /// Lease the next usable key (round-robin, skipping cooling keys). Returns false when
        /// the pool is empty or every key is on cooldown — the caller should then fail over.
        /// </summary>
        public bool TryLease(DateTime nowUtc, out string key)
        {
            lock (_gate)
            {
                key = null;
                if (_slots.Count == 0) return false;

                for (var i = 0; i < _slots.Count; i++)
                {
                    var idx = (_cursor + i) % _slots.Count;
                    var slot = _slots[idx];
                    if (slot.CooldownUntilUtc <= nowUtc)
                    {
                        _cursor = (idx + 1) % _slots.Count;
                        key = slot.Key;
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>Soonest moment any key becomes usable again, or null if the pool is empty.</summary>
        public DateTime? NextAvailableUtc()
        {
            lock (_gate)
            {
                if (_slots.Count == 0) return null;
                return _slots.Min(s => s.CooldownUntilUtc);
            }
        }

        public void ReportSuccess(string key)
        {
            lock (_gate)
            {
                var slot = Find(key);
                if (slot == null) return;
                slot.ConsecutiveFailures = 0;
                slot.CooldownUntilUtc = DateTime.MinValue;
            }
        }

        /// <summary>
        /// Park a key after a rate-limit / quota response. Honors a server-provided Retry-After
        /// when present, otherwise applies jittered exponential backoff.
        /// </summary>
        public void ReportRateLimited(string key, TimeSpan? retryAfter, DateTime nowUtc)
        {
            lock (_gate)
            {
                var slot = Find(key);
                if (slot == null) return;
                slot.ConsecutiveFailures++;
                var backoff = retryAfter ?? ComputeBackoff(slot.ConsecutiveFailures);
                var candidate = nowUtc + backoff;
                if (candidate > slot.CooldownUntilUtc) slot.CooldownUntilUtc = candidate;
            }
        }

        /// <summary>Park a key that returned an auth failure (invalid / blocked) for a long rest.</summary>
        public void ReportAuthFailure(string key, DateTime nowUtc)
        {
            lock (_gate)
            {
                var slot = Find(key);
                if (slot == null) return;
                slot.ConsecutiveFailures++;
                slot.CooldownUntilUtc = nowUtc + AuthFailureCooldown;
            }
        }

        /// <summary>Park a key after a transient/server/network error using exponential backoff.</summary>
        public void ReportTransientError(string key, DateTime nowUtc)
        {
            lock (_gate)
            {
                var slot = Find(key);
                if (slot == null) return;
                slot.ConsecutiveFailures++;
                slot.CooldownUntilUtc = nowUtc + ComputeBackoff(slot.ConsecutiveFailures);
            }
        }

        private KeySlot Find(string key) =>
            _slots.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.Ordinal));

        private static TimeSpan ComputeBackoff(int consecutiveFailures)
        {
            var exp = Math.Min(Math.Max(consecutiveFailures, 1) - 1, 10); // cap shift to avoid overflow
            var scaled = BaseCooldown.TotalSeconds * Math.Pow(2, exp);
            var capped = Math.Min(scaled, MaxCooldown.TotalSeconds);
            // +/-20% jitter so multiple keys don't re-arm in lockstep.
            var jitter = capped * 0.2 * (Rng.NextDouble() * 2 - 1);
            return TimeSpan.FromSeconds(Math.Max(0.5, capped + jitter));
        }

        private static readonly Random Rng = new Random();

        /// <summary>
        /// Split a raw env-var value into individual keys. Accepts comma, semicolon, newline
        /// or whitespace separators so a single secret can carry a rotation set.
        /// </summary>
        public static IReadOnlyList<string> ParseKeys(string rawEnvValue)
        {
            if (string.IsNullOrWhiteSpace(rawEnvValue)) return Array.Empty<string>();
            return rawEnvValue
                .Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Build a pool from the first non-empty of the given environment variable names.</summary>
        public static ProviderKeyPool FromEnv(params string[] envVarNames)
        {
            foreach (var name in envVarNames ?? Array.Empty<string>())
            {
                var raw = Environment.GetEnvironmentVariable(name);
                var keys = ParseKeys(raw);
                if (keys.Count > 0) return new ProviderKeyPool(keys);
            }
            return new ProviderKeyPool(Array.Empty<string>());
        }
    }
}
