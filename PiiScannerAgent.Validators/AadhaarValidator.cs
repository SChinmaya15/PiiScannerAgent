using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Validators
{
    public static class AadhaarValidator
    {
        // Verhoeff multiplication table
        private static readonly int[,] d = new int[,]
        {
        {0,1,2,3,4,5,6,7,8,9},
        {1,2,3,4,0,6,7,8,9,5},
        {2,3,4,0,1,7,8,9,5,6},
        {3,4,0,1,2,8,9,5,6,7},
        {4,0,1,2,3,9,5,6,7,8},
        {5,9,8,7,6,0,4,3,2,1},
        {6,5,9,8,7,1,0,4,3,2},
        {7,6,5,9,8,2,1,0,4,3},
        {8,7,6,5,9,3,2,1,0,4},
        {9,8,7,6,5,4,3,2,1,0}
        };

        // Verhoeff permutation table
        private static readonly int[,] p = new int[,]
        {
        {0,1,2,3,4,5,6,7,8,9},
        {1,5,7,6,2,8,3,0,9,4},
        {5,8,0,3,7,9,6,1,4,2},
        {8,9,1,6,0,4,3,5,2,7},
        {9,4,5,3,1,2,6,8,7,0},
        {4,2,8,6,5,7,3,9,0,1},
        {2,7,9,3,8,0,6,4,1,5},
        {7,0,4,6,9,1,3,2,5,8}
        };

        public static List<string> ExtractAadhaarNumbers(string text)
        {
            List<string> aadhaars = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return aadhaars;

            string cleaned = RemoveSpacesAndHyphens(text);

            for (int i = 0; i <= cleaned.Length - 12; i++)
            {
                string candidate = cleaned.Substring(i, 12);

                if (IsValidAadhaar(candidate))
                {
                    aadhaars.Add(candidate);
                }
            }

            return aadhaars;
        }

        private static string RemoveSpacesAndHyphens(string input)
        {
            char[] buffer = new char[input.Length];
            int index = 0;

            foreach (char c in input)
            {
                if (c != ' ' && c != '-' && c != '\r' && c != '\n')
                {
                    buffer[index++] = c;
                }
            }

            return new string(buffer, 0, index);
        }

        public static bool IsValidAadhaar(string input)
        {
            if (input.Length != 12)
                return false;

            // First digit must be 2-9
            if (input[0] < '2' || input[0] > '9')
                return false;

            // All must be digits
            for (int i = 0; i < 12; i++)
            {
                if (input[i] < '0' || input[i] > '9')
                    return false;
            }

            // Verhoeff checksum validation
            return ValidateVerhoeff(input);
        }

        private static bool ValidateVerhoeff(string num)
        {
            int c = 0;

            for (int i = 0; i < num.Length; i++)
            {
                int digit = num[num.Length - 1 - i] - '0';
                c = d[c, p[i % 8, digit]];
            }

            return c == 0;
        }
    }
}
