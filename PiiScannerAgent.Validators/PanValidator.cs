using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Validators
{
    public static class PanValidator
    {
        public static List<string> ExtractPANs(string text)
        {
            List<string> panNumbers = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return panNumbers;

            string cleaned = text.ToUpper();

            for (int i = 0; i <= cleaned.Length - 10; i++)
            {
                string candidate = cleaned.Substring(i, 10);

                if (IsValidPAN(candidate))
                {
                    panNumbers.Add(candidate);
                }
            }

            return panNumbers;
        }

        private static bool IsValidPAN(string input)
        {
            if (input.Length != 10)
                return false;

            // First 5 must be alphabets
            for (int i = 0; i < 5; i++)
            {
                if (!IsUpperAlphabet(input[i]))
                    return false;
            }

            // Next 4 must be digits
            for (int i = 5; i < 9; i++)
            {
                if (!IsDigit(input[i]))
                    return false;
            }

            // Last must be alphabet
            if (!IsUpperAlphabet(input[9]))
                return false;

            return true;
        }

        private static bool IsUpperAlphabet(char c)
        {
            return c >= 'A' && c <= 'Z';
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}

