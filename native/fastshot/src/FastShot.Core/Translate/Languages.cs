namespace FastShot.Core.Translate;

public sealed record Language(string Code, string Name);

/// <summary>The languages FastShot translates, with their Turkish names, and a guess of which one a text is in.</summary>
public static class Languages
{
    /// <summary>Firefox's version 1 models (see TranslationModels) go both ways between these and English.
    /// DeepL knows most of them too.</summary>
    public static readonly IReadOnlyList<Language> All =
    [
        new("tr", "Türkçe"), new("en", "İngilizce"), new("de", "Almanca"), new("fr", "Fransızca"), new("es", "İspanyolca"),
        new("it", "İtalyanca"), new("nl", "Felemenkçe"), new("pt", "Portekizce"), new("pl", "Lehçe"), new("cs", "Çekçe"),
        new("da", "Danca"), new("sv", "İsveççe"), new("fi", "Fince"), new("et", "Estonca"), new("ro", "Rumence"),
        new("bg", "Bulgarca"), new("el", "Yunanca"), new("hr", "Hırvatça"), new("sl", "Slovence"), new("ca", "Katalanca"),
        new("id", "Endonezce"), new("ms", "Malayca"), new("fa", "Farsça"), new("he", "İbranice"), new("hi", "Hintçe"),
        new("bn", "Bengalce"), new("gu", "Güceratça"), new("kn", "Kannada dili"), new("ml", "Malayalam dili"), new("te", "Telugu dili"),
    ];

    public static string Name(string code) => All.FirstOrDefault(l => l.Code == code)?.Name ?? code.ToUpperInvariant();

    static readonly Dictionary<string, string[]> commonWords = new()
    {
        ["tr"] = ["ve", "bir", "bu", "için", "ile", "da", "de", "çok", "daha", "olarak", "gibi", "değil", "ama", "ne", "mi"],
        ["en"] = ["the", "and", "is", "to", "of", "in", "that", "for", "it", "with", "you", "this", "are", "on", "be"],
        ["de"] = ["der", "die", "das", "und", "ist", "nicht", "ein", "eine", "zu", "mit", "den", "sie", "ich", "auf", "für"],
        ["fr"] = ["le", "la", "les", "et", "est", "des", "une", "un", "pour", "dans", "que", "pas", "vous", "sur", "du"],
        ["es"] = ["el", "la", "los", "las", "y", "es", "que", "en", "un", "una", "por", "para", "con", "del", "se"],
        ["it"] = ["il", "la", "che", "di", "e", "è", "un", "una", "per", "non", "con", "del", "della", "sono", "gli"],
        ["nl"] = ["de", "het", "een", "en", "van", "is", "niet", "dat", "op", "voor", "met", "zijn", "ik", "je", "te"],
        ["pt"] = ["o", "a", "os", "as", "e", "que", "não", "um", "uma", "para", "com", "do", "da", "em", "é"],
        ["pl"] = ["i", "w", "nie", "się", "na", "jest", "to", "że", "z", "do", "jak", "ale", "co", "po", "tak"],
    };

    /// <summary>The likely language of <paramref name="text"/>, or null when unsure. Letters only Turkish uses decide at once;
    /// otherwise the script, then the most frequent common words.</summary>
    public static string? Guess(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (text.Any(c => "ğĞşŞıİ".Contains(c))) return "tr";
        var letters = text.Where(char.IsLetter).ToList();
        if (letters.Count == 0) return null;
        static double Share(List<char> chars, int from, int to) => chars.Count(c => c >= from && c <= to) / (double)chars.Count;
        if (Share(letters, 0x0370, 0x03FF) > 0.3) return "el";
        if (Share(letters, 0x0590, 0x05FF) > 0.3) return "he";
        if (Share(letters, 0x0600, 0x06FF) > 0.3) return "fa";
        if (Share(letters, 0x0900, 0x097F) > 0.3) return "hi";
        if (Share(letters, 0x0980, 0x09FF) > 0.3) return "bn";
        if (Share(letters, 0x0400, 0x04FF) > 0.3) return "bg";
        var words = text.ToLowerInvariant().Split([' ', '\n', '\r', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '"', '\''], StringSplitOptions.RemoveEmptyEntries);
        var scores = commonWords.ToDictionary(p => p.Key, p => words.Count(w => p.Value.Contains(w)));
        var best = scores.MaxBy(p => p.Value);
        return best.Value > 0 ? best.Key : null;
    }
}
