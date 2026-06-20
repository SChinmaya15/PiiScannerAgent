using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Validators
{
    using System;
    using System.Collections.Generic;

    public class CreditCardDetectionResult
    {
        public string CardNumber { get; set; }
        public string CardType { get; set; }
        public int Confidence { get; set; }
        public int Position { get; set; }
    }

    public static class CreditCardValidator
    {
        private static readonly string[] PositiveKeywords =
        {
        "card",
        "credit",
        "debit",
        "visa",
        "mastercard",
        "amex",
        "rupay",
        "cvv",
        "expiry",
        "valid thru",
        "valid through"
    };

        private static readonly string[] NegativeKeywords =
        {
        "invoice",
        "transaction",
        "utr",
        "reference",
        "ref no",
        "aadhaar",
        "account",
        "mobile",
        "phone",
        "order id"
    };

        public static List<CreditCardDetectionResult>
            ExtractCreditCards(string text)
        {
            List<CreditCardDetectionResult> results =
                new List<CreditCardDetectionResult>();

            if (string.IsNullOrWhiteSpace(text))
                return results;

            for (int i = 0; i < text.Length; i++)
            {
                if (!IsDigit(text[i]))
                    continue;

                char[] buffer = new char[19];
                int digitCount = 0;
                int j = i;

                while (j < text.Length && digitCount < 19)
                {
                    char c = text[j];

                    if (c == ' ' ||
                        c == '-' ||
                        c == '\r' ||
                        c == '\n')
                    {
                        j++;
                        continue;
                    }

                    if (!IsDigit(c))
                        break;

                    buffer[digitCount++] = c;
                    j++;
                }

                if (digitCount < 13 || digitCount > 19)
                    continue;

                string candidate =
                    new string(buffer, 0, digitCount);

                if (AllDigitsSame(candidate))
                    continue;

                if (IsSequential(candidate))
                    continue;

                string cardType;

                if (!MatchesKnownCardPattern(
                        candidate,
                        out cardType))
                    continue;

                if (!PassesLuhn(candidate))
                    continue;

                int confidence =
                    CalculateConfidence(
                        text,
                        i,
                        candidate,
                        cardType);

                if (confidence >= 70)
                {
                    bool alreadyExists = false;

                    foreach (var existing in results)
                    {
                        if (existing.CardNumber == candidate)
                        {
                            alreadyExists = true;
                            break;
                        }
                    }

                    if (!alreadyExists)
                    {
                        results.Add(
                            new CreditCardDetectionResult
                            {
                                CardNumber = candidate,
                                CardType = cardType,
                                Confidence = confidence,
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
            string number,
            string cardType)
        {
            int score = 0;

            // Length
            score += 10;

            // BIN validation
            score += 30;

            // Luhn
            score += 40;

            // Formatting bonus
            if (HasFormattedPattern(fullText, position))
                score += 10;

            string context =
                ExtractNearbyText(fullText, position, 80)
                    .ToLower();

            // Positive keywords
            foreach (string keyword in PositiveKeywords)
            {
                if (context.Contains(keyword))
                    score += 10;
            }

            // Negative keywords
            foreach (string keyword in NegativeKeywords)
            {
                if (context.Contains(keyword))
                    score -= 20;
            }

            // Expiry pattern nearby
            if (ContainsExpiryPattern(context))
                score += 20;

            // CVV nearby
            if (context.Contains("cvv"))
                score += 15;

            // Clamp score
            if (score > 100)
                score = 100;

            if (score < 0)
                score = 0;

            return score;
        }

        private static bool ContainsExpiryPattern(
            string text)
        {
            for (int i = 0; i < text.Length - 4; i++)
            {
                if (IsDigit(text[i]) &&
                    IsDigit(text[i + 1]) &&
                    (text[i + 2] == '/' ||
                     text[i + 2] == '-') &&
                    IsDigit(text[i + 3]) &&
                    IsDigit(text[i + 4]))
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
            int start = Math.Max(0, position - radius);

            int length =
                Math.Min(
                    text.Length - start,
                    radius * 2);

            return text.Substring(start, length);
        }

        private static bool HasFormattedPattern(
            string text,
            int position)
        {
            int end =
                Math.Min(position + 25, text.Length);

            for (int i = position; i < end; i++)
            {
                if (text[i] == ' ' || text[i] == '-')
                    return true;
            }

            return false;
        }

        private static bool MatchesKnownCardPattern(
            string number,
            out string cardType)
        {
            cardType = "Unknown";

            int length = number.Length;

            // VISA
            if (number[0] == '4' &&
                (length == 13 ||
                 length == 16 ||
                 length == 19))
            {
                cardType = "Visa";
                return true;
            }

            // MasterCard
            if (length == 16)
            {
                int firstTwo =
                    ParseInt(number, 0, 2);

                int firstFour =
                    ParseInt(number, 0, 4);

                if ((firstTwo >= 51 &&
                     firstTwo <= 55) ||
                    (firstFour >= 2221 &&
                     firstFour <= 2720))
                {
                    cardType = "MasterCard";
                    return true;
                }
            }

            // AMEX
            if (length == 15 &&
                number[0] == '3' &&
                (number[1] == '4' ||
                 number[1] == '7'))
            {
                cardType = "Amex";
                return true;
            }

            // Discover
            if (length == 16 &&
                number.StartsWith("6011"))
            {
                cardType = "Discover";
                return true;
            }

            // RuPay
            if ((length == 16 ||
                 length == 19) &&
                (number.StartsWith("60") ||
                 number.StartsWith("65") ||
                 number.StartsWith("81") ||
                 number.StartsWith("82")))
            {
                cardType = "RuPay";
                return true;
            }

            return false;
        }

        private static bool PassesLuhn(string number)
        {
            int sum = 0;
            bool alternate = false;

            for (int i = number.Length - 1;
                 i >= 0;
                 i--)
            {
                int digit = number[i] - '0';

                if (alternate)
                {
                    digit *= 2;

                    if (digit > 9)
                        digit -= 9;
                }

                sum += digit;

                alternate = !alternate;
            }

            return (sum % 10) == 0;
        }

        private static bool AllDigitsSame(
            string number)
        {
            for (int i = 1;
                 i < number.Length;
                 i++)
            {
                if (number[i] != number[0])
                    return false;
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
                if (number[i] != number[i - 1] + 1)
                    ascending = false;

                if (number[i] != number[i - 1] - 1)
                    descending = false;
            }

            return ascending || descending;
        }

        private static int ParseInt(
            string text,
            int start,
            int length)
        {
            int result = 0;

            for (int i = start;
                 i < start + length;
                 i++)
            {
                result =
                    result * 10 +
                    (text[i] - '0');
            }

            return result;
        }

        private static bool IsDigit(char c)
        {
            return c >= '0' && c <= '9';
        }
    }
}
