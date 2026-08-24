using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using LiteDB;

namespace Mocha2023.Classes.DBs
{

    public static class AgeVerificationDB
    {
        private static readonly LiteDatabase Database =
            new(Path.Combine(Program.dataDir, "DBs", "AgeVerification.db"));

        private static readonly ILiteCollection<AgeVerificationRequest> Requests =
            Database.GetCollection<AgeVerificationRequest>("Requests");

        private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        private static readonly Random Rng = new();
        private static readonly object Sync = new();

        static AgeVerificationDB()
        {
            LiteDbMaintenance.StartPeriodicCheckpoint("AgeVerification.db", Database);
            Requests.EnsureIndex(value => value.AccountId);
            Requests.EnsureIndex(value => value.Status);
        }

        public sealed class AgeVerificationRequest
        {
            [BsonId]
            public string Code { get; set; } = string.Empty;
            public long AccountId { get; set; }

            public string Method { get; set; } = string.Empty;

            public string Status { get; set; } = "Pending";

            public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
            public DateTime? SubmittedAt { get; set; }
            public DateTime? ReviewedAt { get; set; }
            public long? ReviewedByAccountId { get; set; }
            public string? RejectionReason { get; set; }
        }

        public static string GenerateCode(long accountId)
        {
            lock (Sync)
            {

                foreach (AgeVerificationRequest stale in Requests
                             .Find(value => value.AccountId == accountId &&
                                 (value.Status == "Pending" || value.Status == "UnderReview"))
                             .ToList())
                {
                    Requests.Delete(stale.Code);
                }

                string code;
                do
                {
                    code = NewCode();
                } while (Requests.Exists(value => value.Code == code));

                Requests.Insert(new AgeVerificationRequest
                {
                    Code = code,
                    AccountId = accountId,
                    Status = "Pending"
                });

                return code;
            }
        }

        private static string NewCode()
        {
            char[] buffer = new char[6];
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] = CodeAlphabet[Rng.Next(CodeAlphabet.Length)];
            return new string(buffer);
        }

        public static AgeVerificationRequest? GetByCode(string? code)
        {
            string normalized = (code ?? string.Empty).Trim().ToUpperInvariant();
            return normalized.Length == 0 ? null : Requests.FindById(normalized);
        }

        public static AgeVerificationRequest? GetActiveForAccount(long accountId)
        {
            return Requests.FindAll()
                .Where(value => value.AccountId == accountId &&
                    (value.Status == "Pending" || value.Status == "UnderReview"))
                .OrderByDescending(value => value.CreatedAt)
                .FirstOrDefault();
        }

        public static bool MarkUnderReview(string code, long accountId, string method)
        {
            lock (Sync)
            {
                AgeVerificationRequest? request = GetByCode(code);
                if (request == null ||
                    request.AccountId != accountId ||
                    request.Status != "Pending")
                {
                    return false;
                }

                request.Method = method;
                request.Status = "UnderReview";
                request.SubmittedAt = DateTime.UtcNow;
                return Requests.Update(request);
            }
        }

        public static List<AgeVerificationRequest> GetForReview()
        {
            return Requests.Find(value => value.Status == "UnderReview")
                .OrderBy(value => value.SubmittedAt)
                .ToList();
        }

        public static bool Review(string code, bool approve, long reviewerId, string? reason)
        {
            lock (Sync)
            {
                AgeVerificationRequest? request = GetByCode(code);
                if (request == null || request.Status != "UnderReview")
                    return false;

                request.Status = approve ? "Approved" : "Rejected";
                request.ReviewedAt = DateTime.UtcNow;
                request.ReviewedByAccountId = reviewerId;
                request.RejectionReason = reason;
                return Requests.Update(request);
            }
        }
    }
}
