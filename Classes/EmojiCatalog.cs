using System.Globalization;

namespace Mocha2023.Classes
{

    public static class EmojiCatalog
    {
        private static readonly Lazy<string[]> EmojiValues =
            new(Load, LazyThreadSafetyMode.ExecutionAndPublication);
        private static readonly Lazy<HashSet<string>> EmojiSet =
            new(
                () => EmojiValues.Value.ToHashSet(StringComparer.Ordinal),
                LazyThreadSafetyMode.ExecutionAndPublication);

        public static IReadOnlyList<string> All => EmojiValues.Value;

        public static bool Contains(string? value) =>
            !string.IsNullOrWhiteSpace(value) &&
            EmojiSet.Value.Contains(value);

        private static string[] Load()
        {
            string path = Path.Combine(Program.dataDir, "emoji-test.txt");
            if (!File.Exists(path))
            {
                path = Path.Combine(
                    AppContext.BaseDirectory,
                    "Data",
                    "emoji-test.txt");
            }

            if (!File.Exists(path))
            {
                Console.WriteLine(
                    "[EMOJI] Data/emoji-test.txt was not found; using fallback set.");
                return ["😀", "😂", "❤️", "👍", "🎉"];
            }

            var emojis = new List<string>();
            foreach (string line in File.ReadLines(path))
            {
                if (!line.Contains("; fully-qualified", StringComparison.Ordinal))
                    continue;

                int semicolon = line.IndexOf(';');
                if (semicolon <= 0)
                    continue;

                string[] codePoints = line[..semicolon].Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries |
                    StringSplitOptions.TrimEntries);
                try
                {
                    string emoji = string.Concat(codePoints.Select(value =>
                        char.ConvertFromUtf32(int.Parse(
                            value,
                            NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture))));
                    if (emoji.Length > 0)
                        emojis.Add(emoji);
                }
                catch (Exception ex) when (
                    ex is FormatException or
                    OverflowException or
                    ArgumentOutOfRangeException)
                {
                    Console.WriteLine(
                        $"[EMOJI] Skipped invalid Unicode data row: {line}");
                }
            }

            string[] result = emojis
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            Console.WriteLine(
                $"[EMOJI] Loaded {result.Length} Unicode RGI emoji sequences.");
            return result;
        }
    }
}
