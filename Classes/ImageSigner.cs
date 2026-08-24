using System.Security.Cryptography;
using System.Text.Json;

namespace Mocha2023.Classes
{

    public sealed class ImageSigner : IDisposable
    {
        private static readonly Lazy<ImageSigner> SharedInstance =
            new(() => LoadFromDataDirectory(), LazyThreadSafetyMode.ExecutionAndPublication);

        private readonly RSA _rsa;
        private readonly object _signLock = new();

        private ImageSigner(RSAParameters parameters)
        {
            _rsa = RSA.Create();
            _rsa.ImportParameters(parameters);
        }

        public static ImageSigner Shared => SharedInstance.Value;

        public byte[] SignImage(byte[] image)
        {
            ArgumentNullException.ThrowIfNull(image);

            lock (_signLock)
            {
                return _rsa.SignData(
                    image,
                    HashAlgorithmName.SHA1,
                    RSASignaturePadding.Pkcs1);
            }
        }

        public void Dispose()
        {
            _rsa.Dispose();
        }

        private static ImageSigner LoadFromDataDirectory()
        {
            string path = Path.Combine(Program.dataDir, "ImageRSAParams.json");
            if (!File.Exists(path))
            {
                throw new FileNotFoundException(
                    "Image signing key was not found. Expected Data/ImageRSAParams.json.",
                    path);
            }

            ImageRsaParameters? stored;
            try
            {
                stored = JsonSerializer.Deserialize<ImageRsaParameters>(
                    File.ReadAllText(path),
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
            }
            catch (Exception ex) when (
                ex is JsonException or FormatException or IOException)
            {
                throw new InvalidDataException(
                    "Data/ImageRSAParams.json is not a valid RSA private key.",
                    ex);
            }

            if (stored == null)
                throw new InvalidDataException("The image signing key is empty.");

            try
            {
                return new ImageSigner(new RSAParameters
                {
                    Modulus = Decode(stored.Modulus, nameof(stored.Modulus)),
                    Exponent = Decode(stored.Exponent, nameof(stored.Exponent)),
                    P = Decode(stored.P, nameof(stored.P)),
                    Q = Decode(stored.Q, nameof(stored.Q)),
                    DP = Decode(stored.DP, nameof(stored.DP)),
                    DQ = Decode(stored.DQ, nameof(stored.DQ)),
                    InverseQ = Decode(stored.InverseQ, nameof(stored.InverseQ)),
                    D = Decode(stored.D, nameof(stored.D))
                });
            }
            catch (CryptographicException ex)
            {
                throw new InvalidDataException(
                    "Data/ImageRSAParams.json contains inconsistent RSA parameters.",
                    ex);
            }
        }

        private static byte[] Decode(string? value, string field)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidDataException($"Image RSA field {field} is missing.");

            try
            {
                return Convert.FromBase64String(value);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException(
                    $"Image RSA field {field} is not valid Base64.",
                    ex);
            }
        }

        private sealed class ImageRsaParameters
        {
            public string? Modulus { get; set; }
            public string? Exponent { get; set; }
            public string? P { get; set; }
            public string? Q { get; set; }
            public string? DP { get; set; }
            public string? DQ { get; set; }
            public string? InverseQ { get; set; }
            public string? D { get; set; }
        }
    }
}
