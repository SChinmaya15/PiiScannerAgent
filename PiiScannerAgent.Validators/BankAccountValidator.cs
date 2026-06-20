using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Validators
{
    using System;
    using System.Collections.Generic;

    public class BankAccountDetectionResult
    {
        public string AccountNumber { get; set; }
        public int Confidence { get; set; }
        public int Position { get; set; }
    }

    public static class BankAccountValidator
    {
        private static readonly string[] PositiveKeywords =
        {
        "account",
        "account no",
        "a/c",
        "acct",
        "bank",
        "savings",
        "current",
        "beneficiary",
        "ifsc",
        "branch",
        "bank account"
    };

        private static readonly string[] NegativeKeywords =
        {
        "aadhaar",
        "aadhar",
        "mobile",
        "phone",
        "credit",
        "debit",
        "visa",
        "mastercard",
        "amex",
        "cvv",
        "expiry",
        "invoice",
        "transaction",
        "utr",
        "reference",
        "pan",
        "passport"
    };

        public static List<BankAccountDetectionResult>
            ExtractBankAccounts(string text)
        {
            List<BankAccountDetectionResult> results =
                new List<BankAccountDetectionResult>();

            if (string.IsNullOrWhiteSpace(text))
                return results;

            for (int i = 0; i < text.Length; i++)
            {
                if (!IsDigit(text[i]))
                    continue;

                char[] buffer = new char[18];
                int digitCount = 0;
                int j = i;

                while (j < text.Length &&
                       digitCount < 18)
                {
                    char c = text[j];

                    // Ignore formatting chars
                    if (c == ' ' ||
                        c == '-' ||
                        c == '\r' ||
                        c == '\n')
                    {
                        j++;
                        continue;
                    }

                    // Stop if invalid
                    if (!IsDigit(c))
                        break;

                    buffer[digitCount++] = c;
                    j++;
                }

                // Indian account numbers:
                // generally 9-18 digits
                if (digitCount < 9 ||
                    digitCount > 18)
                {
                    continue;
                }

                string candidate =
                    new string(
                        buffer,
                        0,
                        digitCount);

                // Validation pipeline
                if (AllDigitsSame(candidate))
                    continue;

                if (IsSequential(candidate))
                    continue;

                if (LooksLikePhoneNumber(candidate))
                    continue;

                if (LooksLikeAadhaar(candidate))
                    continue;

                if (LooksLikeCreditCard(candidate))
                    continue;

                int confidence =
                    CalculateConfidence(
                        text,
                        i,
                        candidate);

                if (confidence >= 70)
                {
                    bool exists = false;

                    foreach (var item in results)
                    {
                        if (item.AccountNumber ==
                            candidate)
                        {
                            exists = true;
                            break;
                        }
                    }

                    if (!exists)
                    {
                        results.Add(
                            new BankAccountDetectionResult
                            {
                                AccountNumber =
                                    candidate,

                                Confidence =
                                    confidence,

                                Position = i
                            });
                    }
                }
            }

            return results;
        }

        private static int CalculateConfidence(
            string fullText,
            int position,
            string number)
        {
            int score = 40;

            // Proper account length
            if (number.Length >= 9 &&
                number.Length <= 18)
            {
                score += 20;
            }

            string context =
                ExtractNearbyText(
                    fullText,
                    position,
                    80).ToLower();

            // Positive keywords
            foreach (string keyword
                     in PositiveKeywords)
            {
                if (context.Contains(keyword))
                    score += 10;
            }

            // Negative keywords
            foreach (string keyword
                     in NegativeKeywords)
            {
                if (context.Contains(keyword))
                    score -= 20;
            }

            // IFSC nearby
            if (ContainsIFSC(context))
                score += 25;

            // Formatting bonus
            if (HasFormatting(
                    fullText,
                    position))
            {
                score += 5;
            }

            // Clamp
            if (score > 100)
                score = 100;

            if (score < 0)
                score = 0;

            return score;
        }

        private static bool ContainsIFSC(
            string text)
        {
            // Simple IFSC detection
            for (int i = 0;
                 i <= text.Length - 11;
                 i++)
            {
                bool valid = true;

                // First 4 letters
                for (int j = 0;
                     j < 4;
                     j++)
                {
                    char c = text[i + j];

                    if (c < 'a' || c > 'z')
                    {
                        valid = false;
                        break;
                    }
                }

                if (!valid)
                    continue;

                // 5th must be 0
                if (text[i + 4] != '0')
                    continue;

                // Last 6 alphanumeric
                for (int j = 5;
                     j < 11;
                     j++)
                {
                    char c = text[i + j];

                    bool alpha =
                        c >= 'a' &&
                        c <= 'z';

                    bool digit =
                        c >= '0' &&
                        c <= '9';

                    if (!alpha && !digit)
                    {
                        valid = false;
                        break;
                    }
                }

                if (valid)
                    return true;
            }

            return false;
        }

        private static bool LooksLikePhoneNumber(
            string number)
        {
            // Indian phone:
            // 10 digits starting 6-9
            return
                number.Length == 10 &&
                number[0] >= '6' &&
                number[0] <= '9';
        }

        private static bool LooksLikeAadhaar(
            string number)
        {
            // Aadhaar:
            // 12 digits starting 2-9
            return
                number.Length == 12 &&
                number[0] >= '2' &&
                number[0] <= '9';
        }

        private static bool LooksLikeCreditCard(
            string number)
        {
            // Basic card detection
            if (number.Length < 13 ||
                number.Length > 19)
            {
                return false;
            }

            return PassesLuhn(number);
        }

        private static bool PassesLuhn(
            string number)
        {
            int sum = 0;
            bool alternate = false;

            for (int i = number.Length - 1;
                 i >= 0;
                 i--)
            {
                int digit =
                    number[i] - '0';

                if (alternate)
                {
                    digit *= 2;

                    if (digit > 9)
                        digit -= 9;
                }

                sum += digit;

                alternate = !alternate;
            }

            return sum % 10 == 0;
        }

        private static bool HasFormatting(
            string text,
            int position)
        {
            int end =
                Math.Min(
                    position + 25,
                    text.Length);

            for (int i = position;
                 i < end;
                 i++)
            {
                if (text[i] == ' ' ||
                    text[i] == '-')
                {
                    return true;
                }
            }

            return false;
        }

        private static string ExtractNearbyText(
            string text,
            int position,
            int radius)
        {
            int start =
                Math.Max(
                    0,
                    position - radius);

            int length =
                Math.Min(
                    text.Length - start,
                    radius * 2);

            return text.Substring(
                start,
                length);
        }

        private static bool AllDigitsSame(
            string number)
        {
            for (int i = 1;
                 i < number.Length;
                 i++)
            {
                if (number[i] !=
                    number[0])
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsSequential(
            string number)
        {
            bool ascending = true;
            bool descending = true;

            for (int i = 1;
                 i < number.Length;
                 i++)
            {
                if (number[i] !=
                    number[i - 1] + 1)
                {
                    ascending = false;
                }

                if (number[i] !=
                    number[i - 1] - 1)
                {
                    descending = false;
                }
            }

            return ascending ||
                   descending;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' &&
                   c <= '9';
        }
    }
}

