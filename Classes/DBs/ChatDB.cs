using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;
using SystemJsonSerializer = System.Text.Json.JsonSerializer;

namespace Mocha2023.Classes.DBs
{

    public static class ChatDB
    {
        private static readonly object Sync = new();
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "Chat.db"));

        private static readonly ILiteCollection<ChatThread> Threads =
            Database.GetCollection<ChatThread>("Threads", BsonAutoId.Int64);

        private static readonly ILiteCollection<ChatMessage> Messages =
            Database.GetCollection<ChatMessage>("Messages", BsonAutoId.Int64);

        private static readonly ILiteCollection<ChatReadState> ReadStates =
            Database.GetCollection<ChatReadState>("ReadStates");

        static ChatDB()
        {
            Threads.EnsureIndex(value => value.UpdatedAt);
            Messages.EnsureIndex(value => value.ThreadId);
            Messages.EnsureIndex(value => value.CreatedAt);
            ReadStates.EnsureIndex(value => value.ThreadId);
            ReadStates.EnsureIndex(value => value.AccountId);
        }

        public static int DeveloperThreadCount
        {
            get
            {
                lock (Sync)
                    return Threads.Count();
            }
        }

        public static int DeveloperMessageCount
        {
            get
            {
                lock (Sync)
                    return Messages.Count();
            }
        }

        public sealed class ChatThread
        {
            [BsonId]
            public long ThreadId { get; set; }
            public int Type { get; set; }
            public string? Name { get; set; }
            public long CreatedByAccountId { get; set; }
            public List<long> MemberIds { get; set; } = new();
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public sealed class ChatMessage
        {
            [BsonId]
            public long MessageId { get; set; }
            public long ThreadId { get; set; }
            public long SenderAccountId { get; set; }
            public string Body { get; set; } = string.Empty;
            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        }

        public sealed class ChatReadState
        {
            [BsonId]
            public string Id { get; set; } = string.Empty;
            public long ThreadId { get; set; }
            public long AccountId { get; set; }
            public long LastReadMessageId { get; set; }
            public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        }

        public static List<ChatThread> GetThreadsForPlayer(long accountId, int maxCount)
        {
            maxCount = Math.Clamp(maxCount, 1, 100);

            lock (Sync)
            {
                List<ChatThread> threads = Threads.FindAll()
                    .Where(value => value.MemberIds?.Contains(accountId) == true)
                    .OrderByDescending(value => value.UpdatedAt)
                    .Take(maxCount)
                    .ToList();

                foreach (ChatThread thread in threads)
                    EnsureStarterMessageNoLock(thread);

                return threads;
            }
        }

        public static ChatThread? GetThread(long threadId, long accountId)
        {
            lock (Sync)
            {
                ChatThread? thread = Threads.FindById(threadId);
                if (thread?.MemberIds?.Contains(accountId) != true)
                    return null;

                EnsureStarterMessageNoLock(thread);
                return thread;
            }
        }

        public static ChatThread? FindThreadWithMembers(
            long requestingAccountId,
            IEnumerable<long> requestedMemberIds)
        {
            long[] normalized = NormalizeMembers(
                requestingAccountId,
                requestedMemberIds);

            if (normalized.Length < 2)
                return null;

            lock (Sync)
            {
                return Threads.FindAll().FirstOrDefault(thread =>
                    NormalizeMembers(0, thread.MemberIds).SequenceEqual(normalized));
            }
        }

        public static ChatThread GetOrCreateThread(
            long requestingAccountId,
            IEnumerable<long> requestedMemberIds,
            string? name = null,
            int type = 0)
        {
            long[] normalized = NormalizeMembers(
                requestingAccountId,
                requestedMemberIds);

            if (normalized.Length < 2)
            {
                throw new ArgumentException(
                    "A chat thread requires at least two distinct members.",
                    nameof(requestedMemberIds));
            }

            lock (Sync)
            {
                ChatThread? existing = Threads.FindAll().FirstOrDefault(thread =>
                    NormalizeMembers(0, thread.MemberIds).SequenceEqual(normalized));

                if (existing != null)
                {
                    EnsureStarterMessageNoLock(existing);
                    return existing;
                }

                var thread = new ChatThread
                {
                    ThreadId = GetNextThreadIdNoLock(),
                    Type = type,
                    Name = string.IsNullOrWhiteSpace(name) ? null : name.Trim(),
                    CreatedByAccountId = requestingAccountId,
                    MemberIds = normalized.ToList(),
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                Threads.Insert(thread);
                EnsureStarterMessageNoLock(thread);
                return thread;
            }
        }

        public static List<ChatMessage> GetMessages(
            long threadId,
            long requestingAccountId,
            int maxCount = 50,
            long? beforeMessageId = null)
        {
            maxCount = Math.Clamp(maxCount, 1, 100);

            lock (Sync)
            {
                ChatThread? thread = Threads.FindById(threadId);
                if (thread?.MemberIds?.Contains(requestingAccountId) != true)
                    return new List<ChatMessage>();

                IEnumerable<ChatMessage> query =
                    Messages.Find(value => value.ThreadId == threadId);

                if (beforeMessageId.HasValue && beforeMessageId.Value > 0)
                    query = query.Where(value => value.MessageId < beforeMessageId.Value);

                return query
                    .OrderByDescending(value => value.MessageId)
                    .Take(maxCount)
                    .OrderBy(value => value.MessageId)
                    .ToList();
            }
        }

        public static ChatMessage? GetLastMessage(long threadId)
        {
            lock (Sync)
            {
                return Messages.Find(value => value.ThreadId == threadId)
                    .OrderByDescending(value => value.CreatedAt)
                    .FirstOrDefault();
            }
        }

        public static List<ChatMessage> GetMessagesForDeveloper(
            int maxCount = 100,
            long? beforeMessageId = null)
        {
            maxCount = Math.Clamp(maxCount, 1, 250);

            lock (Sync)
            {
                IEnumerable<ChatMessage> query = Messages.FindAll();
                if (beforeMessageId.HasValue && beforeMessageId.Value > 0)
                    query = query.Where(value =>
                        value.MessageId < beforeMessageId.Value);

                return query
                    .OrderByDescending(value => value.MessageId)
                    .Take(maxCount)
                    .ToList();
            }
        }

        public static Dictionary<long, ChatThread> GetThreadsForDeveloper(
            IEnumerable<long> threadIds)
        {
            long[] ids = threadIds
                .Where(value => value > 0)
                .Distinct()
                .ToArray();

            lock (Sync)
            {
                return Threads.FindAll()
                    .Where(value => ids.Contains(value.ThreadId))
                    .ToDictionary(value => value.ThreadId);
            }
        }

        public static long GetLastReadMessageId(long threadId, long accountId)
        {
            lock (Sync)
            {
                ChatThread? thread = Threads.FindById(threadId);
                if (thread?.MemberIds?.Contains(accountId) != true)
                    return 0;

                return ReadStates.FindById(BuildReadStateId(threadId, accountId))
                    ?.LastReadMessageId ?? 0;
            }
        }

        public static bool MarkMessageRead(
            long threadId,
            long messageId,
            long accountId)
        {
            if (threadId <= 0 || messageId <= 0 || accountId <= 0)
                return false;

            lock (Sync)
            {
                ChatThread? thread = Threads.FindById(threadId);
                if (thread?.MemberIds?.Contains(accountId) != true)
                    return false;

                ChatMessage? message = Messages.FindById(messageId);
                if (message == null || message.ThreadId != threadId)
                    return false;

                SetLastReadNoLock(threadId, accountId, messageId);
                return true;
            }
        }

        public static ChatMessage? AddMessage(
            long threadId,
            long senderAccountId,
            string body)
        {
            body = body?.Trim() ?? string.Empty;
            if (body.Length is < 1 or > 2000 || body.Any(char.IsControl))
                return null;

            lock (Sync)
            {
                ChatThread? thread = Threads.FindById(threadId);
                if (thread?.MemberIds?.Contains(senderAccountId) != true)
                    return null;

                var message = new ChatMessage
                {
                    MessageId = GetNextMessageIdNoLock(),
                    ThreadId = threadId,
                    SenderAccountId = senderAccountId,
                    Body = body,
                    CreatedAt = DateTime.UtcNow
                };

                Messages.Insert(message);
                thread.UpdatedAt = message.CreatedAt;
                Threads.Update(thread);
                SetLastReadNoLock(threadId, senderAccountId, message.MessageId);
                return message;
            }
        }

        public static bool LeaveThread(long threadId, long requestingAccountId)
        {
            lock (Sync)
            {
                ChatThread? thread = Threads.FindById(threadId);
                if (thread?.MemberIds?.Contains(requestingAccountId) != true)
                    return false;

                if (thread.MemberIds.Count <= 2)
                {
                    Messages.DeleteMany(value => value.ThreadId == threadId);
                    ReadStates.DeleteMany(value => value.ThreadId == threadId);
                    return Threads.Delete(threadId);
                }

                thread.MemberIds.Remove(requestingAccountId);
                ReadStates.Delete(BuildReadStateId(threadId, requestingAccountId));
                thread.UpdatedAt = DateTime.UtcNow;
                return Threads.Update(thread);
            }
        }

        public static bool DeleteThread(long threadId, long requestingAccountId) =>
            LeaveThread(threadId, requestingAccountId);

        private static string BuildReadStateId(long threadId, long accountId) =>
            $"{threadId}:{accountId}";

        private static void SetLastReadNoLock(
            long threadId,
            long accountId,
            long messageId)
        {
            string id = BuildReadStateId(threadId, accountId);
            ChatReadState? state = ReadStates.FindById(id);

            if (state == null)
            {
                state = new ChatReadState
                {
                    Id = id,
                    ThreadId = threadId,
                    AccountId = accountId,
                    LastReadMessageId = messageId,
                    UpdatedAt = DateTime.UtcNow
                };
            }
            else
            {
                state.LastReadMessageId = Math.Max(
                    state.LastReadMessageId,
                    messageId);
                state.UpdatedAt = DateTime.UtcNow;
            }

            ReadStates.Upsert(state);
        }

        private static ChatMessage EnsureStarterMessageNoLock(ChatThread thread)
        {
            ChatMessage? existing = Messages
                .Find(value => value.ThreadId == thread.ThreadId)
                .OrderBy(value => value.MessageId)
                .FirstOrDefault();

            if (existing != null)
                return existing;

            long creatorId = thread.CreatedByAccountId > 0
                ? thread.CreatedByAccountId
                : thread.MemberIds.FirstOrDefault();

            string contents = SystemJsonSerializer.Serialize(new
            {
                Type = 0,
                Version = 1,
                Data = $"Player <@U{creatorId}> started a chat"
            });

            var starter = new ChatMessage
            {
                MessageId = GetNextMessageIdNoLock(),
                ThreadId = thread.ThreadId,
                SenderAccountId = -5,
                Body = contents,
                CreatedAt = thread.CreatedAt == default
                    ? DateTime.UtcNow
                    : thread.CreatedAt
            };

            Messages.Insert(starter);

            if (creatorId > 0)
                SetLastReadNoLock(thread.ThreadId, creatorId, starter.MessageId);

            if (thread.UpdatedAt < starter.CreatedAt)
            {
                thread.UpdatedAt = starter.CreatedAt;
                Threads.Update(thread);
            }

            Console.WriteLine(
                $"[CHAT] backfilled starter message={starter.MessageId} thread={thread.ThreadId}");

            return starter;
        }

        private static long[] NormalizeMembers(
            long requestingAccountId,
            IEnumerable<long>? memberIds)
        {
            IEnumerable<long> values = memberIds ?? Array.Empty<long>();

            if (requestingAccountId > 0)
                values = values.Append(requestingAccountId);

            return values
                .Where(value => value > 0)
                .Distinct()
                .OrderBy(value => value)
                .ToArray();
        }

        private static long GetNextThreadIdNoLock() =>
            Threads.Count() == 0
                ? 1
                : Threads.Max(value => value.ThreadId) + 1;

        private static long GetNextMessageIdNoLock() =>
            Messages.Count() == 0
                ? 1
                : Messages.Max(value => value.MessageId) + 1;
    }
}
