using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Ruya.Core.Interfaces;

namespace Ruya.Core
{
    public static class CommaSeparatedValueHelper
    {
        public static IEnumerable<string> SplitCSV(this string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return null;
            }

            var row = new List<string>();
            var position = 0;

            var column = new StringBuilder();
            var inQuote = false;
            while (position < input.Length)
            {
                char character = input[position];
                bool lastCharacter = position.Equals(input.Length - 1);
                bool doubleQuoteFound = character.Equals(ControlChars.Quote);
                bool commaFound = character.Equals(ControlChars.Comma);
                position++;

                var append = true;
                if (doubleQuoteFound)
                {
                    if (inQuote)
                    {
                        if (!lastCharacter)
                        {
                            char nextCharacter = input[position];
                            bool duplicatedDoubleQuoteFound = nextCharacter.Equals(ControlChars.Quote);
                            if (duplicatedDoubleQuoteFound)
                            {
                                position++;
                            }
                            else
                            {
                                inQuote = false;
                                append = false;
                            }
                        }
                        else
                        {
                            inQuote = false;
                            append = false;
                        }
                    }
                    else
                    {
                        inQuote = true;
                        append = false;
                    }
                }

                bool commaMeansEndOfColumn = commaFound && !inQuote;
                bool endOfColumn = commaMeansEndOfColumn || lastCharacter;
                bool shouldAppend = !commaMeansEndOfColumn && append;
                if (shouldAppend)
                {
                    column.Append(character);
                }
                // ReSharper disable once InvertIf
                if (endOfColumn)
                {
                    row.Add(column.ToString());
                    column = new StringBuilder();
                    inQuote = false;
                }
            }
            return row;
        }

        public static string JoinCSV(this IEnumerable<string> row)
        {   
            if (ReferenceEquals(row, null)) throw new ArgumentNullException(nameof(row));

            var firstColumn = true;
            var output = new StringBuilder();
            foreach (string column in row)
            {
                if (firstColumn)
                {
                    firstColumn = false;
                }
                else
                {
                    output.Append(ControlChars.Comma);
                }
                bool includesSpecialCharacters = column.IndexOfAny(new[]
                                                                   {
                                                                       ControlChars.Quote,
                                                                       ControlChars.Comma
                                                                   }) != -1;
                if (includesSpecialCharacters)
                {
                    // HARD-CODED constant
                    output.AppendFormat(CultureInfo.InvariantCulture, "{0}{1}{0}", ControlChars.Quote, column.Replace(ControlChars.Quote.ToString(), new string(ControlChars.Quote, 2)));
                }
                else
                {
                    output.Append(column);
                }
            }
            return output.ToString();
        }

        public static IEnumerable<T> ReadCSV<T>(this string input, bool skipHeader, bool throwException) where T : ICommaSeparatedValue, new()
        {
            List<string> rows = input.SplitCRLF(StringSplitOptions.RemoveEmptyEntries)
                                     .ToList();
            if (!rows.Any())
            {
                return new List<T>();
            }

            if (skipHeader)
            {
                rows.RemoveAt(0);
            }

            IEnumerable<T> output = rows.Select(row =>
                                                   {
                                                       var item = new T();
                                                       item.FromCSV(row, throwException);
                                                       return item;
                                                   });
            return output;
        }

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
        public static bool TryReadCSV<T>(this string input, bool skipHeader, out IEnumerable<T> output) where T : ICommaSeparatedValue, new()
        {
            bool processed;
            try
            {
                output = input.ReadCSV<T>(skipHeader, true).ToList();
                processed = true;
            }
            catch (Exception)
            {
                output = null;
                processed = false;
            }
            return processed;
        }        
    }
}