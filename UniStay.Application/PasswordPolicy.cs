using System.IO.Compression;
using System.Text.RegularExpressions;
using UniStay.Core;
namespace UniStay.Application;

public static class PasswordPolicy
{
    private static readonly Lazy<HashSet<string>> Common = new(() =>
    {
        using var stream = typeof(PasswordPolicy).Assembly.GetManifestResourceStream("UniStay.Application.Resources.common-passwords.txt.gz")!;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip);
        var words = new HashSet<string>(StringComparer.Ordinal);
        while (reader.ReadLine() is { } line) words.Add(line.Trim());
        return words;
    });
    public static void Validate(string password, Account account)
    {
        var lower = password.ToLowerInvariant();
        bool Similar(string value)
        {
            if (value.Length == 0) return false;
            var available = value.GroupBy(c => c).ToDictionary(g => g.Key, g => g.Count());
            var matches = 0;
            foreach (var character in lower) if (available.GetValueOrDefault(character) > 0) { matches++; available[character]--; }
            return 2.0 * matches / (lower.Length + value.Length) >= 0.7;
        }
        var email = account.Email.ToLowerInvariant();
        if (password.Length is < 10 or > 128 || password.All(char.IsDigit) || Common.Value.Contains(lower.Trim()) ||
            Regex.Split(email, @"\W+").Append(email).Any(Similar))
            throw new ApiFault(400, new { password = new[] { "Нужен пароль от 10 до 128 символов, не слишком распространённый, не только из цифр и не похожий на email." } });
    }
}
