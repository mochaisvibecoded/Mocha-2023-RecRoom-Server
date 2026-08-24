using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Mocha2023.Classes
{
    public enum ProfanitySeverity
    {
        None,

        Mild,

        Severe,

        SevereBypass
    }

    public sealed record ProfanityMatch(
        string CanonicalWord,
        string MatchedText,
        ProfanitySeverity Severity,
        int Index,
        int Length,
        bool WasBypassed
    );

    public sealed record ProfanityAnalysis(
        ProfanitySeverity Severity,
        string SanitizedText,
        IReadOnlyList<ProfanityMatch> Matches
    );

    public static class ProfanityFilter
    {
        private const int MaxRepeatedCharacterCount = 16;
        private const int MaxSeparatorsBetweenLetters = 12;
        private const int MaxAnalyzedCharacters = 4096;

        private static readonly TimeSpan RegexTimeout =
            TimeSpan.FromMilliseconds(150);

        private static readonly string[] MildWords =
        {
            "fuck",
            "fucks",
            "fucked",
            "fucker",
            "fuckers",
            "fucking",
            "motherfucker",
            "motherfuckers",
            "motherfucking",

            "shit",
            "shits",
            "shitty",
            "shitting",
            "bullshit",

            "bitch",
            "bitches",
            "bitchy",

            "ass",
            "asses",
            "asshole",
            "assholes",
            "dumbass",
            "jackass",

            "dick",
            "dicks",
            "dickhead",
            "dickheads",

            "cock",
            "cocks",

            "pussy",
            "pussies",

            "whore",
            "whores",

            "slut",
            "sluts",

            "bastard",
            "bastards",

            "twat",
            "twats",

            "damn",
            "dammit",

            "hell",
            "crap",
            "piss",
            "pissed",

            "sex",
            "sexy",

            "cum",
            "dildo",
            "dildos",

            "dealdough"
        };

        private static readonly string[] SevereWords =
        {
            "nigger",
            "niggers",
            "nigga",
            "niggas",

            "faggot",
            "faggots",
            "fag",
            "fags",

            "retard",
            "retards",
            "retarded",

            "cunt",
            "cunts",

            "rape",
            "rapes",
            "raped",
            "raping",
            "rapist",
            "rapists",

            "cumslut",
            "cumsluts",

            "kike",
            "kikes",

            "chink",
            "chinks",

            "spic",
            "spics",

            "wetback",
            "wetbacks",

            "tranny",
            "trannies"
        };

        private static readonly Dictionary<char, string[]> CharacterAliases =
            new()
            {
                ['a'] =
                [
                    "4", "@", "à", "á", "â", "ã", "ä", "å",
                    "ɑ", "α", "а"
                ],

                ['b'] =
                [
                    "8", "ß", "þ", "Ь", "в"
                ],

                ['c'] =
                [
                    "k", "(", "<", "¢", "с"
                ],

                ['d'] =
                [
                    "đ", "ԁ"
                ],

                ['e'] =
                [
                    "3", "€", "è", "é", "ê", "ë",
                    "ε", "е"
                ],

                ['f'] =
                [
                    "ph", "ƒ"
                ],

                ['g'] =
                [
                    "9", "q", "ɡ", "ģ"
                ],

                ['h'] =
                [
                    "#", "н"
                ],

                ['i'] =
                [
                    "1", "!", "|", "l", "ì", "í", "î", "ï",
                    "ι", "і"
                ],

                ['j'] =
                [
                    "ј"
                ],

                ['k'] =
                [
                    "c", "|<", "κ", "к"
                ],

                ['l'] =
                [
                    "1", "!", "|", "i", "ł", "ⅼ"
                ],

                ['m'] =
                [
                    "rn", "м"
                ],

                ['n'] =
                [
                    "ñ", "η", "п"
                ],

                ['o'] =
                [
                    "0", "°", "ò", "ó", "ô", "õ", "ö",
                    "ο", "о"
                ],

                ['p'] =
                [
                    "ρ", "р"
                ],

                ['q'] =
                [
                    "9"
                ],

                ['r'] =
                [
                    "®", "ŕ", "я"
                ],

                ['s'] =
                [
                    "5", "$", "z", "ś", "ѕ"
                ],

                ['t'] =
                [
                    "7", "+", "ţ", "т"
                ],

                ['u'] =
                [
                    "v", "ù", "ú", "û", "ü",
                    "υ", "ц"
                ],

                ['v'] =
                [
                    "u", "\\/"
                ],

                ['w'] =
                [
                    "vv", "\\/\\/"
                ],

                ['x'] =
                [
                    "%", "×", "х"
                ],

                ['y'] =
                [
                    "¥", "ý", "ÿ", "у"
                ],

                ['z'] =
                [
                    "2", "ѕ"
                ]
            };

        private static readonly Dictionary<char, char>
            NormalizedConfusableCharacters =
                BuildNormalizedConfusableCharacters();

        private static readonly CompiledRule[] MildRules =
            BuildRules(MildWords, ProfanitySeverity.Mild);

        private static readonly CompiledRule[] SevereRules =
            BuildRules(SevereWords, ProfanitySeverity.Severe);

        public static ProfanityAnalysis Analyze(string? input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return new ProfanityAnalysis(
                    ProfanitySeverity.None,
                    input ?? string.Empty,
                    Array.Empty<ProfanityMatch>()
                );
            }

            string analyzedInput = input.Length > MaxAnalyzedCharacters
                ? input[..MaxAnalyzedCharacters]
                : input;
            var candidates = new List<ProfanityMatch>();

            AddMatches(analyzedInput, SevereRules, candidates);
            AddMatches(analyzedInput, MildRules, candidates);
            AddNormalizedMatches(analyzedInput, SevereRules, candidates);
            AddNormalizedMatches(analyzedInput, MildRules, candidates);

            var selectedMatches = RemoveOverlappingMatches(candidates);

            ProfanitySeverity finalSeverity;

            if (selectedMatches.Any(x =>
                    x.Severity == ProfanitySeverity.SevereBypass))
            {
                finalSeverity = ProfanitySeverity.SevereBypass;
            }
            else if (selectedMatches.Any(x =>
                         x.Severity == ProfanitySeverity.Severe))
            {
                finalSeverity = ProfanitySeverity.Severe;
            }
            else if (selectedMatches.Any(x =>
                         x.Severity == ProfanitySeverity.Mild))
            {
                finalSeverity = ProfanitySeverity.Mild;
            }
            else
            {
                finalSeverity = ProfanitySeverity.None;
            }

            string sanitized = ApplyBlur(input, selectedMatches);

            return new ProfanityAnalysis(
                finalSeverity,
                sanitized,
                selectedMatches
            );
        }

        public static ProfanitySeverity GetSeverity(string input)
        {
            return Analyze(input).Severity;
        }

        public static string Blur(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            return Analyze(input).SanitizedText;
        }

        public static bool IsPure(string? input)
        {
            return Analyze(input).Severity == ProfanitySeverity.None;
        }

        public static bool ContainsProfanity(string? input)
        {
            return Analyze(input).Severity != ProfanitySeverity.None;
        }

        public static IReadOnlyList<ProfanityMatch> GetMatches(string? input)
        {
            return Analyze(input).Matches;
        }

        private static void AddMatches(
            string input,
            IEnumerable<CompiledRule> rules,
            ICollection<ProfanityMatch> results)
        {
            foreach (CompiledRule rule in rules)
            {
                try
                {
                    MatchCollection matches = rule.FuzzyRegex.Matches(input);

                    foreach (Match match in matches)
                    {
                        if (!match.Success || match.Length == 0)
                            continue;

                        bool wasBypassed =
                            !rule.ExactRegex.IsMatch(match.Value);

                        ProfanitySeverity severity = rule.BaseSeverity;

                        if (severity == ProfanitySeverity.Severe &&
                            wasBypassed)
                        {
                            severity = ProfanitySeverity.SevereBypass;
                        }

                        results.Add(new ProfanityMatch(
                            rule.CanonicalWord,
                            match.Value,
                            severity,
                            match.Index,
                            match.Length,
                            wasBypassed
                        ));
                    }
                }
                catch (RegexMatchTimeoutException)
                {

                }
            }
        }

        private static void AddNormalizedMatches(
            string input,
            IEnumerable<CompiledRule> rules,
            ICollection<ProfanityMatch> results)
        {
            NormalizedDetectionText normalized =
                NormalizeForDetection(input);
            if (!normalized.Changed || normalized.Text.Length == 0)
                return;

            foreach (CompiledRule rule in rules)
            {
                try
                {
                    foreach (Match match in rule.FuzzyRegex.Matches(
                                 normalized.Text))
                    {
                        if (!match.Success || match.Length == 0)
                            continue;

                        int normalizedEnd = match.Index + match.Length - 1;
                        if (match.Index >= normalized.SourceStarts.Count ||
                            normalizedEnd >= normalized.SourceEnds.Count)
                        {
                            continue;
                        }

                        int sourceStart =
                            normalized.SourceStarts[match.Index];
                        int sourceEnd =
                            normalized.SourceEnds[normalizedEnd];
                        int sourceLength = sourceEnd - sourceStart;
                        if (sourceLength <= 0)
                            continue;

                        bool duplicate = results.Any(existing =>
                            existing.CanonicalWord.Equals(
                                rule.CanonicalWord,
                                StringComparison.OrdinalIgnoreCase) &&
                            existing.Index == sourceStart &&
                            existing.Length == sourceLength);
                        if (duplicate)
                            continue;

                        ProfanitySeverity severity =
                            rule.BaseSeverity ==
                            ProfanitySeverity.Severe
                                ? ProfanitySeverity.SevereBypass
                                : rule.BaseSeverity;
                        results.Add(new ProfanityMatch(
                            rule.CanonicalWord,
                            input.Substring(sourceStart, sourceLength),
                            severity,
                            sourceStart,
                            sourceLength,
                            WasBypassed: true));
                    }
                }
                catch (RegexMatchTimeoutException)
                {

                }
            }
        }

        private static NormalizedDetectionText NormalizeForDetection(
            string input)
        {
            var text = new StringBuilder(input.Length);
            var starts = new List<int>(input.Length);
            var ends = new List<int>(input.Length);
            bool changed = false;
            int sourceIndex = 0;

            foreach (Rune sourceRune in input.EnumerateRunes())
            {
                int sourceEnd =
                    sourceIndex + sourceRune.Utf16SequenceLength;
                string compatibilityForm = sourceRune
                    .ToString()
                    .Normalize(NormalizationForm.FormKD);
                int outputStart = text.Length;

                foreach (Rune normalizedRune in
                         compatibilityForm.EnumerateRunes())
                {
                    UnicodeCategory category =
                        Rune.GetUnicodeCategory(normalizedRune);
                    if (category is
                        UnicodeCategory.NonSpacingMark or
                        UnicodeCategory.SpacingCombiningMark or
                        UnicodeCategory.EnclosingMark)
                    {
                        changed = true;
                        continue;
                    }

                    char output;
                    if (normalizedRune.IsAscii)
                    {
                        output = char.ToLowerInvariant(
                            (char)normalizedRune.Value);
                    }
                    else if (normalizedRune.Value <= char.MaxValue &&
                             NormalizedConfusableCharacters.TryGetValue(
                                 (char)normalizedRune.Value,
                                 out char mapped))
                    {
                        output = mapped;
                    }
                    else if (Rune.IsLetterOrDigit(normalizedRune) &&
                             normalizedRune.Value <= char.MaxValue)
                    {
                        output = char.ToLowerInvariant(
                            (char)normalizedRune.Value);
                    }
                    else
                    {

                        output = ' ';
                    }

                    text.Append(output);
                    starts.Add(sourceIndex);
                    ends.Add(sourceEnd);
                }

                if (text.Length == outputStart)
                {
                    text.Append(' ');
                    starts.Add(sourceIndex);
                    ends.Add(sourceEnd);
                }

                string produced = text
                    .ToString(outputStart, text.Length - outputStart);
                if (!produced.Equals(
                        sourceRune.ToString(),
                        StringComparison.Ordinal))
                {
                    changed = true;
                }

                sourceIndex = sourceEnd;
            }

            return new NormalizedDetectionText(
                text.ToString(),
                starts,
                ends,
                changed);
        }

        private static Dictionary<char, char>
            BuildNormalizedConfusableCharacters()
        {
            var result = new Dictionary<char, char>();
            foreach ((char canonical, string[] aliases) in CharacterAliases)
            {
                foreach (string alias in aliases)
                {
                    if (alias.Length != 1)
                        continue;

                    char candidate = alias[0];
                    if (candidate <= 127)
                        continue;

                    UnicodeCategory category =
                        CharUnicodeInfo.GetUnicodeCategory(candidate);
                    if (category is
                        UnicodeCategory.UppercaseLetter or
                        UnicodeCategory.LowercaseLetter or
                        UnicodeCategory.TitlecaseLetter or
                        UnicodeCategory.ModifierLetter or
                        UnicodeCategory.OtherLetter)
                    {
                        result.TryAdd(
                            char.ToLowerInvariant(candidate),
                            canonical);
                    }
                }
            }

            return result;
        }

        private static IReadOnlyList<ProfanityMatch>
            RemoveOverlappingMatches(IEnumerable<ProfanityMatch> matches)
        {

            var prioritized = matches
                .OrderByDescending(x => GetSeverityPriority(x.Severity))
                .ThenByDescending(x => x.Length)
                .ThenBy(x => x.Index)
                .ToList();

            var selected = new List<ProfanityMatch>();

            foreach (ProfanityMatch candidate in prioritized)
            {
                bool overlapsExisting = selected.Any(existing =>
                    RangesOverlap(
                        candidate.Index,
                        candidate.Length,
                        existing.Index,
                        existing.Length
                    ));

                if (!overlapsExisting)
                    selected.Add(candidate);
            }

            return selected
                .OrderBy(x => x.Index)
                .ToList();
        }

        private static int GetSeverityPriority(
            ProfanitySeverity severity)
        {
            return severity switch
            {
                ProfanitySeverity.SevereBypass => 3,
                ProfanitySeverity.Severe => 2,
                ProfanitySeverity.Mild => 1,
                _ => 0
            };
        }

        private static bool RangesOverlap(
            int firstIndex,
            int firstLength,
            int secondIndex,
            int secondLength)
        {
            int firstEnd = firstIndex + firstLength;
            int secondEnd = secondIndex + secondLength;

            return firstIndex < secondEnd &&
                   secondIndex < firstEnd;
        }

        private static string ApplyBlur(
            string input,
            IEnumerable<ProfanityMatch> matches)
        {
            char[] characters = input.ToCharArray();

            foreach (ProfanityMatch match in matches)
            {
                int start = Math.Max(0, match.Index);
                int end = Math.Min(
                    characters.Length,
                    match.Index + match.Length
                );

                for (int i = start; i < end; i++)
                {

                    if (!char.IsWhiteSpace(characters[i]))
                        characters[i] = '*';
                }
            }

            return new string(characters);
        }

        private static CompiledRule[] BuildRules(
            IEnumerable<string> words,
            ProfanitySeverity baseSeverity)
        {
            return words
                .Where(word => !string.IsNullOrWhiteSpace(word))
                .Select(word => word.Trim().ToLowerInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(word => word.Length)
                .Select(word => new CompiledRule(
                    word,
                    baseSeverity,
                    BuildFuzzyRegex(word),
                    BuildExactRegex(word)
                ))
                .ToArray();
        }

        private static Regex BuildFuzzyRegex(string word)
        {
            string pattern = BuildFuzzyWordPattern(word);

            return new Regex(
                pattern,
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled,
                RegexTimeout
            );
        }

        private static Regex BuildExactRegex(string word)
        {
            string escapedWord = Regex.Escape(word);

            string pattern =
                $@"(?<![\p{{L}}\p{{N}}])" +
                escapedWord +
                $@"(?![\p{{L}}\p{{N}}])";

            return new Regex(
                pattern,
                RegexOptions.IgnoreCase |
                RegexOptions.CultureInvariant |
                RegexOptions.Compiled,
                RegexTimeout
            );
        }

        private static string BuildFuzzyWordPattern(string word)
        {
            var builder = new StringBuilder();

            builder.Append(@"(?<![\p{L}\p{N}])");

            for (int i = 0; i < word.Length; i++)
            {
                char character = char.ToLowerInvariant(word[i]);

                builder.Append(
                    BuildCharacterPattern(character)
                );

                if (i < word.Length - 1)
                {

                    builder.Append(
                        $@"[\s\p{{P}}\p{{S}}\p{{Cf}}]" +
                        $"{{0,{MaxSeparatorsBetweenLetters}}}"
                    );
                }
            }

            builder.Append(@"(?![\p{L}\p{N}])");

            return builder.ToString();
        }

        private static string BuildCharacterPattern(char character)
        {
            var alternatives = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase)
            {
                character.ToString()
            };

            if (character is >= 'a' and <= 'z')
            {
                char fullWidthCharacter =
                    (char)('\uFF41' + (character - 'a'));

                alternatives.Add(fullWidthCharacter.ToString());
            }

            if (CharacterAliases.TryGetValue(
                    character,
                    out string[]? aliases))
            {
                foreach (string alias in aliases)
                    alternatives.Add(alias);
            }

            string alternativesPattern = string.Join(
                "|",
                alternatives
                    .OrderByDescending(value => value.Length)
                    .Select(Regex.Escape)
            );

            return
                $"(?:{alternativesPattern})" +
                $"{{1,{MaxRepeatedCharacterCount}}}";
        }

        private sealed class CompiledRule
        {
            public string CanonicalWord { get; }

            public ProfanitySeverity BaseSeverity { get; }

            public Regex FuzzyRegex { get; }

            public Regex ExactRegex { get; }

            public CompiledRule(
                string canonicalWord,
                ProfanitySeverity baseSeverity,
                Regex fuzzyRegex,
                Regex exactRegex)
            {
                CanonicalWord = canonicalWord;
                BaseSeverity = baseSeverity;
                FuzzyRegex = fuzzyRegex;
                ExactRegex = exactRegex;
            }
        }

        private sealed record NormalizedDetectionText(
            string Text,
            IReadOnlyList<int> SourceStarts,
            IReadOnlyList<int> SourceEnds,
            bool Changed);
    }
}
