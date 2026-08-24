using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using LiteDB;

namespace Mocha2023.Classes
{
    public static class RoomBlobStorage
    {
        private static readonly object DatabaseLock = new();

        public static readonly string BlobDirectory =
            Path.Combine(
                AppContext.BaseDirectory,
                "RoomBlobs"
            );

        public static readonly string DatabasePath =
            Path.Combine(
                AppContext.BaseDirectory,
                "RoomBlobs.db"
            );

        public sealed class BlobRecord
        {
            [BsonId]
            public string BlobName { get; set; } = string.Empty;

            public string Hash { get; set; } = string.Empty;

            public long Size { get; set; }

            public DateTime CreatedAt { get; set; }
        }

        public static async Task<BlobRecord> StoreAsync(
            Stream input)
        {
            Directory.CreateDirectory(BlobDirectory);

            string blobName =
                Guid.NewGuid().ToString("N");

            string finalPath =
                GetBlobPath(blobName);

            string temporaryPath =
                finalPath + ".tmp";

            await using (
                var output = new FileStream(
                    temporaryPath,
                    FileMode.Create,
                    FileAccess.Write,
                    FileShare.None,
                    81920,
                    useAsync: true
                )
            )
            {
                await input.CopyToAsync(output);
                await output.FlushAsync();
            }

            var fileInfo =
                new FileInfo(temporaryPath);

            if (!fileInfo.Exists || fileInfo.Length <= 0)
            {
                TryDelete(temporaryPath);

                throw new InvalidDataException(
                    "Uploaded room blob was empty."
                );
            }

            string hash;

            await using (
                var hashStream = new FileStream(
                    temporaryPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    81920,
                    useAsync: true
                )
            )
            {
                byte[] hashBytes =
                    await SHA256.HashDataAsync(hashStream);

                hash = Convert
                    .ToHexString(hashBytes)
                    .ToLowerInvariant();
            }

            File.Move(
                temporaryPath,
                finalPath,
                overwrite: true
            );

            var record = new BlobRecord
            {
                BlobName = blobName,
                Hash = hash,
                Size = fileInfo.Length,
                CreatedAt = DateTime.UtcNow
            };

            lock (DatabaseLock)
            {
                using var database =
                    new LiteDatabase(DatabasePath);

                var blobs =
                    database.GetCollection<BlobRecord>(
                        "RoomBlobs"
                    );

                blobs.Upsert(record);
            }

            return record;
        }

        public static BlobRecord? Find(
            string blobName)
        {
            string safeName =
                NormalizeBlobName(blobName);

            lock (DatabaseLock)
            {
                using var database =
                    new LiteDatabase(DatabasePath);

                return database
                    .GetCollection<BlobRecord>(
                        "RoomBlobs"
                    )
                    .FindById(safeName);
            }
        }

        public static bool Exists(
            string blobName)
        {
            string safeName =
                NormalizeBlobName(blobName);

            return File.Exists(
                GetBlobPath(safeName)
            );
        }

        public static string GetBlobPath(
            string blobName)
        {
            string safeName =
                NormalizeBlobName(blobName);

            return Path.Combine(
                BlobDirectory,
                safeName + ".blob"
            );
        }

        public static string NormalizeBlobName(
            string blobName)
        {
            if (string.IsNullOrWhiteSpace(blobName))
            {
                throw new ArgumentException(
                    "Blob name was empty.",
                    nameof(blobName)
                );
            }

            string safeName =
                Path.GetFileName(blobName.Trim());

            if (safeName.EndsWith(
                    ".blob",
                    StringComparison.OrdinalIgnoreCase
                ))
            {
                safeName =
                    safeName[..^5];
            }

            foreach (char character in safeName)
            {
                if (!char.IsLetterOrDigit(character) &&
                    character != '-' &&
                    character != '_')
                {
                    throw new ArgumentException(
                        "Blob name contained invalid characters.",
                        nameof(blobName)
                    );
                }
            }

            return safeName;
        }

        private static void TryDelete(
            string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {

            }
        }
    }
}
