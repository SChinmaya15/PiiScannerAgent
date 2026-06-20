using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PiiScanner.Validators
{
    public static class EmailValidator
    {
        public static List<string> ExtractEmails(string text)
        {
            List<string> emails = new List<string>();

            if (string.IsNullOrWhiteSpace(text))
                return emails;

            string cleaned = text.Trim();

            for (int i = 0; i < cleaned.Length; i++)
            {
                if (cleaned[i] == '@')
                {
                    int start = i - 1;
                    int end = i + 1;

                    // Move backward for local part
                    while (start >= 0 && IsValidEmailCharacter(cleaned[start]))
                    {
                        start--;
                    }

                    // Move forward for domain part
                    while (end < cleaned.Length && IsValidEmailCharacter(cleaned[end]))
                    {
                        end++;
                    }

                    start++;

                    string candidate = cleaned.Substring(start, end - start);

                    if (IsValidEmail(candidate) && !emails.Contains(candidate))
                    {
                        emails.Add(candidate);
                    }
                }
            }

            return emails;
        }

        private static bool IsValidEmail(string email)
        {
            int atIndex = email.IndexOf('@');

            // Must contain exactly one @
            if (atIndex <= 0 || atIndex != email.LastIndexOf('@'))
                return false;

            string localPart = email.Substring(0, atIndex);
            string domainPart = email.Substring(atIndex + 1);

            // Local and domain must exist
            if (localPart.Length == 0 || domainPart.Length == 0)
                return false;

            // Domain must contain dot
            int dotIndex = domainPart.LastIndexOf('.');

            if (dotIndex <= 0 || dotIndex == domainPart.Length - 1)
                return false;

            // Validate local part
            foreach (char c in localPart)
            {
                if (!IsValidEmailCharacter(c))
                    return false;
            }

            // Validate domain part
            foreach (char c in domainPart)
            {
                if (!IsValidDomainCharacter(c))
                    return false;
            }

            return true;
        }

        private static bool IsValidEmailCharacter(char c)
        {
            return
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '.' ||
                c == '_' ||
                c == '%' ||
                c == '+' ||
                c == '-';
        }

        private static bool IsValidDomainCharacter(char c)
        {
            return
                (c >= 'a' && c <= 'z') ||
                (c >= 'A' && c <= 'Z') ||
                (c >= '0' && c <= '9') ||
                c == '.' ||
                c == '-';
        }
    }
}
