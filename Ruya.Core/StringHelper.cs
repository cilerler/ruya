using System;
using System.Collections.Generic;
using System.Text;

namespace Ruya.Core
{
    public static class StringHelper
    {
        // COMMENT method ConvertEncoding
        // TEST method ConvertEncoding
        public static string ConvertEncoding(this string source, Encoding from, Encoding to)
        {
            if (ReferenceEquals(from, null)) throw new ArgumentNullException(nameof(from));
            if (ReferenceEquals(to, null)) throw new ArgumentNullException(nameof(to));
            return to.GetString(Encoding.Convert(from, to, from.GetBytes(source)));
        }


        // TEST method SplitCrlf
        /// <summary>
        /// Separates the given string based on CarriageReturn and LineFeed
        /// </summary>
        /// <param name="input"></param>
        /// <param name="splitOption"></param>
        /// <returns></returns>
        public static IEnumerable<string> SplitCRLF(this string input, StringSplitOptions splitOption)
        {
            if (ReferenceEquals(input, null)) throw new ArgumentNullException(nameof(input));
            var delimiters = new[]
                             {
                                 ControlChars.CarriageReturn,
                                 ControlChars.LineFeed
                             };

            return input.Split(delimiters, splitOption);
        }

        public static string ToBase64(this string input, Encoding encoding)
        {
            if (ReferenceEquals(encoding, null)) throw new ArgumentNullException(nameof(encoding));
            byte[] encodedBytes = encoding.GetBytes(input);
            return Convert.ToBase64String(encodedBytes);
        }

        public static string FromBase64(this string input, Encoding encoding)
        {
            if (ReferenceEquals(encoding, null)) throw new ArgumentNullException(nameof(encoding));
            byte[] decodedBytes = Convert.FromBase64String(input);
            return encoding.GetString(decodedBytes);
        }

        private static readonly Random Randomizer = new Random();
        
        // TEST method GenerateRandomText
        /// <summary>
        /// Generates random text based on parameters
        /// </summary>
        /// <param name="length"></param>
        /// <param name="textElement"></param>
        /// <returns></returns>
        public static string GenerateRandomText(int length, StringFeatures textElement)
        {
            //x "!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~";
            string allowedChars = string.Empty;
            if (textElement.HasFlag(StringFeatures.SymbolEasy))
            {
                allowedChars += "#$%&*+=?";
            }
            if (textElement.HasFlag(StringFeatures.SymbolDifficult))
            {
                allowedChars += "!\"'(),-./:;<>@[\\]^_`{|}~";
            }
            if (textElement.HasFlag(StringFeatures.Number))
            {
                allowedChars += "0123456789";
            }
            if (textElement.HasFlag(StringFeatures.LetterUpper))
            {
                allowedChars += "ABCDEFGHJKLMNOPQRSTUVWXYZ";
            }
            if (textElement.HasFlag(StringFeatures.LetterLower))
            {
                allowedChars += "abcdefghijkmnopqrstuvwxyz";
            }

            var chars = new char[length];
            for (int i = 0; i < length; i++)
            {
                chars[i] = allowedChars[Randomizer.Next(0, allowedChars.Length)];
            }
            return new string(chars);
        }
    }

    [Flags]
    public enum StringFeatures
    {
        None = 0,
        Number = 1,
        LetterUpper = 2,
        LetterLower = 4,
        LetterAll = LetterLower | LetterUpper,
        SymbolEasy = 8,
        SymbolDifficult = 16,
        SymbolAll = SymbolEasy | SymbolDifficult,
        All = SymbolAll | Number | LetterAll
    }
}