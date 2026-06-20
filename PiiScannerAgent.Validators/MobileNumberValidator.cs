using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Validators
{
    using System;
    using System.Collections.Generic;

    public class PhoneDetectionResult
    {
        public string PhoneNumber { get; set; }
        public int Confidence { get; set; }
        public int Position { get; set; }
    }

    public static class MobileNumberValidator
    {
        private static readonly string[] PositiveKeywords =
        {
        "phone",
        "mobile",
        "contact",
        "mob",
        "call",
        "tel",
        "whatsapp",
        "customer mobile",
        "customer phone"
    };

        private static readonly string[] NegativeKeywords =
        {
        "aadhaar",
        "aadhar",
        "account",
        "bank",
        "ifsc",
        "invoice",
        "transaction",
        "utr",
        "reference",
        "credit",
        "debit",
        "visa",
        "mastercard",
        "amex",
        "cvv",
        "expiry",
        "pan",
        "passport",
        "gst"
    };

        public static List<PhoneDetectionResult>
            ExtractPhoneNumbers(string text)
        {
            List<PhoneDetectionResult> results =
                new List<PhoneDetectionResult>();

            if (string.IsNullOrWhiteSpace(text))
                return results;

            for (int i = 0; i < text.Length; i++)
            {
                if (!CanStartPhone(text[i]))
                    continue;

                int current = i;

                // Skip country code
                if (text[current] == '+')
                {
                    current++;

                    if (current + 1 >= text.Length)
                        continue;

                    if (text[current] == '9' &&
                        text[current + 1] == '1')
                    {
                        current += 2;
                    }
                    else
                    {
                        continue;
                    }
                }

                List<char> digits =
                    new List<char>();

                int endPosition = current;

                while (endPosition < text.Length)
                {
                    char c = text[endPosition];

                    if (IsDigit(c))
                    {
                        digits.Add(c);
                    }
                    else if (IsAllowedSeparator(c))
                    {
                        // allowed
                    }
                    else
                    {
                        break;
                    }

                    // Prevent huge blocks
                    if (digits.Count > 10)
                        break;

                    endPosition++;
                }

                // Must be exactly 10 digits
                if (digits.Count != 10)
                    continue;

                string candidate =
                    new string(digits.ToArray());

                // Must start 6-9
                if (!IsValidIndianMobile(candidate))
                    continue;

                // CRITICAL:
                // Prevent substring extraction
                if (!HasValidBoundaries(
                        text,
                        i,
                        endPosition))
                {
                    continue;
                }

                // Reject if inside bigger numeric block
                if (IsInsideLargeNumericBlock(
                        text,
                        i,
                        endPosition))
                {
                    continue;
                }

                // Reject repeated digits
                if (AllDigitsSame(candidate))
                    continue;

                // Reject sequential
                if (IsSequential(candidate))
                    continue;

                // Reject Aadhaar-like context
                if (LooksLikeAadhaarContext(
                        text,
                        i))
                {
                    continue;
                }

                // Reject bank context
                if (LooksLikeBankContext(
                        text,
                        i))
                {
                    continue;
                }

                // Reject card context
                if (LooksLikeCardContext(
                        text,
                        i))
                {
                    continue;
                }

                int confidence =
                    CalculateConfidence(
                        text,
                        i,
                        candidate);

                if (confidence < 70)
                    continue;

                bool alreadyExists = false;

                foreach (var existing in results)
                {
                    if (existing.PhoneNumber ==
                        candidate)
                    {
                        alreadyExists = true;
                        break;
                    }
                }

                if (!alreadyExists)
                {
                    results.Add(
                        new PhoneDetectionResult
                        {
                            PhoneNumber =
                                candidate,

                            Confidence =
                                confidence,

                            Position = i
                        });
                }
            }

            return results;
        }

        private static int CalculateConfidence(
            string text,
            int position,
            string number)
        {
            int score = 50;

            // Valid Indian pattern
            score += 20;

            string context =
                ExtractNearbyText(
                    text,
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

            // Formatting bonus
            if (HasFormatting(
                    text,
                    position))
            {
                score += 10;
            }

            // Country code
            if (context.Contains("+91"))
                score += 10;

            // Clamp
            if (score > 100)
                score = 100;

            if (score < 0)
                score = 0;

            return score;
        }

        private static bool HasValidBoundaries(
            string text,
            int start,
            int end)
        {
            bool leftValid =
                start == 0 ||
                !IsDigit(text[start - 1]);

            bool rightValid =
                end >= text.Length ||
                !IsDigit(text[end]);

            return leftValid &&
                   rightValid;
        }

        private static bool IsInsideLargeNumericBlock(
            string text,
            int start,
            int end)
        {
            int left = start - 1;

            while (left >= 0 &&
                   IsDigit(text[left]))
            {
                left--;
            }

            int right = end;

            while (right < text.Length &&
                   IsDigit(text[right]))
            {
                right++;
            }

            int totalDigits =
                right - left - 1;

            return totalDigits > 10;
        }

        private static bool LooksLikeAadhaarContext(
            string text,
            int position)
        {
            string context =
                ExtractNearbyText(
                    text,
                    position,
                    50).ToLower();

            return
                context.Contains("aadhaar") ||
                context.Contains("aadhar");
        }

        private static bool LooksLikeBankContext(
            string text,
            int position)
        {
            string context =
                ExtractNearbyText(
                    text,
                    position,
                    50).ToLower();

            return
                context.Contains("account") ||
                context.Contains("bank") ||
                context.Contains("ifsc") ||
                context.Contains("a/c");
        }

        private static bool LooksLikeCardContext(
            string text,
            int position)
        {
            string context =
                ExtractNearbyText(
                    text,
                    position,
                    50).ToLower();

            return
                context.Contains("visa") ||
                context.Contains("mastercard") ||
                context.Contains("amex") ||
                context.Contains("credit") ||
                context.Contains("debit") ||
                context.Contains("cvv") ||
                context.Contains("expiry");
        }

        private static bool IsValidIndianMobile(
            string number)
        {
            if (number.Length != 10)
                return false;

            return number[0] >= '6' &&
                   number[0] <= '9';
        }

        private static bool HasFormatting(
            string text,
            int position)
        {
            int end =
                Math.Min(
                    position + 20,
                    text.Length);

            for (int i = position;
                 i < end;
                 i++)
            {
                if (text[i] == ' ' ||
                    text[i] == '-' ||
                    text[i] == '(' ||
                    text[i] == ')')
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

        private static bool CanStartPhone(
            char c)
        {
            return
                (c >= '6' && c <= '9') ||
                c == '+';
        }

        private static bool IsAllowedSeparator(
            char c)
        {
            return
                c == ' ' ||
                c == '-' ||
                c == '(' ||
                c == ')' ||
                c == '\r' ||
                c == '\n';
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' &&
                   c <= '9';
        }
    }
}
