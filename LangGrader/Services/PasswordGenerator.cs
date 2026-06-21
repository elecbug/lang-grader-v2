using System.Security.Cryptography;
using System.Text;

namespace LangGrader.Services;

public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@#$%";
    private const string All = Upper + Lower + Digits + Symbols;

    public static string Generate(int length = 12)
    {
        if (length < 8)
        {
            length = 8;
        }

        var chars = new List<char>
        {
            Pick(Upper),
            Pick(Lower),
            Pick(Digits),
            Pick(Symbols)
        };

        while (chars.Count < length)
        {
            chars.Add(Pick(All));
        }

        Shuffle(chars);

        return new string(chars.ToArray());
    }

    private static char Pick(string source)
    {
        var index = RandomNumberGenerator.GetInt32(source.Length);
        return source[index];
    }

    private static void Shuffle(List<char> chars)
    {
        for (var i = chars.Count - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }
    }
}