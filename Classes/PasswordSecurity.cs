using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace Mocha2023.Classes
{
    public static class PasswordSecurity
    {
        private const string Prefix = "$pbkdf2-sha256$";
        private const int Iterations = 120_000;
        private const int SaltSize = 16;
        private const int HashSize = 32;
        private const int MaximumLoginAttempts = 8;
        public const int MinPasswordLength = 8;
        public const int MaxPasswordLength = 256;
        private static readonly TimeSpan LoginAttemptWindow =
            TimeSpan.FromMinutes(15);
        private static readonly TimeSpan LoginLockout =
            TimeSpan.FromMinutes(15);
        private static readonly string DummyHash = Hash(
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
        private static readonly ConcurrentDictionary<string, LoginAttemptState>
            LoginAttempts = new(StringComparer.Ordinal);
        private static int LoginAttemptSweepCounter;

        private sealed class LoginAttemptState
        {
            public object Sync { get; } = new();
            public Queue<DateTimeOffset> Attempts { get; } = new();
            public DateTimeOffset BlockedUntil { get; set; }
            public DateTimeOffset LastTouched { get; set; }
        }

        public static string Hash(string password)
        {
            ArgumentNullException.ThrowIfNull(password);

            if (password.Length > MaxPasswordLength)
                throw new ArgumentException("Password is too long.", nameof(password));

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSize);

            return $"{Prefix}{Iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
        }

        public static bool Verify(
            string? password,
            string? storedPassword,
            out bool needsUpgrade)
        {
            needsUpgrade = false;

            if (password == null || storedPassword == null ||
                password.Length > MaxPasswordLength)
            {
                return false;
            }

            if (!storedPassword.StartsWith(Prefix, StringComparison.Ordinal))
            {
                needsUpgrade = true;
                return FixedTimeTextEquals(password, storedPassword);
            }

            try
            {
                string[] parts = storedPassword.Split('$');
                if (parts.Length != 5 ||
                    !int.TryParse(parts[2], out int iterations) ||
                    iterations < 10_000 || iterations > 1_000_000)
                {
                    return false;
                }

                byte[] salt = Convert.FromBase64String(parts[3]);
                byte[] expected = Convert.FromBase64String(parts[4]);
                if (salt.Length < 8 || salt.Length > 64 ||
                    expected.Length < 16 || expected.Length > 64)
                {
                    return false;
                }

                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    expected.Length);

                needsUpgrade = iterations != Iterations ||
                               salt.Length != SaltSize ||
                               expected.Length != HashSize;

                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (CryptographicException)
            {
                return false;
            }
        }

        public static bool VerifyLogin(
            string? password,
            string? storedPassword,
            out bool needsUpgrade)
        {
            bool hasStoredPassword = !string.IsNullOrEmpty(storedPassword);
            bool verified = Verify(
                password,
                hasStoredPassword ? storedPassword : DummyHash,
                out needsUpgrade);

            return hasStoredPassword && verified;
        }

        public static bool TryBeginLoginAttempt(
            string identity,
            string? clientAddress,
            out int retryAfterSeconds)
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string key = CreateLoginAttemptKey(identity, clientAddress);
            LoginAttemptState state = LoginAttempts.GetOrAdd(
                key,
                _ => new LoginAttemptState());

            lock (state.Sync)
            {
                PruneLoginAttempts(state, now);
                state.LastTouched = now;

                if (state.BlockedUntil > now)
                {
                    retryAfterSeconds = Math.Max(
                        1,
                        (int)Math.Ceiling(
                            (state.BlockedUntil - now).TotalSeconds));
                    return false;
                }

                if (state.Attempts.Count >= MaximumLoginAttempts)
                {
                    state.BlockedUntil = now + LoginLockout;
                    retryAfterSeconds = (int)LoginLockout.TotalSeconds;
                    return false;
                }

                state.Attempts.Enqueue(now);
                retryAfterSeconds = 0;
            }

            SweepLoginAttempts(now);
            return true;
        }

        public static void CompleteLoginAttempt(
            string identity,
            string? clientAddress,
            bool succeeded)
        {
            string key = CreateLoginAttemptKey(identity, clientAddress);
            if (succeeded)
            {
                LoginAttempts.TryRemove(key, out _);
                return;
            }

            if (!LoginAttempts.TryGetValue(key, out LoginAttemptState? state))
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            lock (state.Sync)
            {
                PruneLoginAttempts(state, now);
                state.LastTouched = now;
                if (state.Attempts.Count >= MaximumLoginAttempts)
                    state.BlockedUntil = now + LoginLockout;
            }
        }

        private static string CreateLoginAttemptKey(
            string identity,
            string? clientAddress)
        {
            string normalizedIdentity =
                (identity ?? string.Empty).Trim().ToLowerInvariant();
            string normalizedAddress =
                string.IsNullOrWhiteSpace(clientAddress)
                    ? "unknown"
                    : clientAddress.Trim();
            byte[] material = Encoding.UTF8.GetBytes(
                normalizedIdentity + "\n" + normalizedAddress);
            return Convert.ToHexString(SHA256.HashData(material));
        }

        private static void PruneLoginAttempts(
            LoginAttemptState state,
            DateTimeOffset now)
        {
            DateTimeOffset cutoff = now - LoginAttemptWindow;
            while (state.Attempts.Count > 0 &&
                   state.Attempts.Peek() < cutoff)
            {
                state.Attempts.Dequeue();
            }

            if (state.BlockedUntil <= now && state.Attempts.Count == 0)
                state.BlockedUntil = default;
        }

        private static void SweepLoginAttempts(DateTimeOffset now)
        {
            if ((Interlocked.Increment(ref LoginAttemptSweepCounter) & 255) != 0)
                return;

            DateTimeOffset staleBefore =
                now - LoginAttemptWindow - LoginLockout;
            foreach (KeyValuePair<string, LoginAttemptState> entry in LoginAttempts)
            {
                if (entry.Value.LastTouched < staleBefore)
                    LoginAttempts.TryRemove(entry.Key, out _);
            }
        }

        private static bool FixedTimeTextEquals(string left, string right)
        {
            byte[] leftBytes = Encoding.UTF8.GetBytes(left);
            byte[] rightBytes = Encoding.UTF8.GetBytes(right);

            return leftBytes.Length == rightBytes.Length &&
                   CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
        }
    }
}
