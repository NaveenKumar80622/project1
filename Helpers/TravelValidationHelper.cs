using System.Globalization;
using System.Text.RegularExpressions;

namespace PickNBook.Api.Helpers
{
    public static class TravelValidationHelper
    {
        // Strict RFC 5322 compliant regex ensuring alphanumeric labels and >= 2 char TLD
        public static readonly Regex StrictEmailRegex = new(
            @"^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*\.[a-zA-Z]{2,24}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Indian 10-digit mobile number starting with 6, 7, 8, or 9
        public static readonly Regex IndianMobileRegex = new(
            @"^[6-9]\d{9}$",
            RegexOptions.Compiled);

        // Letters, spaces, hyphens, and apostrophes (min 2, max 50)
        public static readonly Regex NameRegex = new(
            @"^[A-Za-z]+([ '-][A-Za-z]+)*$",
            RegexOptions.Compiled);

        // Standard Indian GSTIN: 2 digits + 5 alpha + 4 numeric + 1 alpha + 1 alpha/numeric + 'Z' + 1 alpha/numeric
        public static readonly Regex GstinRegex = new(
            @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Standard Indian PAN: 5 alpha + 4 numeric + 1 alpha
        public static readonly Regex PanRegex = new(
            @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // Passport number: 6 to 9 alphanumeric characters
        public static readonly Regex PassportRegex = new(
            @"^[A-Za-z0-9]{6,9}$",
            RegexOptions.Compiled);

        // Dictionary of common domain typos with correct suggestions
        private static readonly Dictionary<string, string> DomainTypoSuggestions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Gmail typos
            ["gmil.com"] = "gmail.com",
            ["gmeil.com"] = "gmail.com",
            ["gmal.com"] = "gmail.com",
            ["gail.com"] = "gmail.com",
            ["gmial.com"] = "gmail.com",
            ["gamil.com"] = "gmail.com",
            ["gmai.com"] = "gmail.com",
            ["gmail.co"] = "gmail.com",
            ["gmaill.com"] = "gmail.com",
            ["gmail.cpm"] = "gmail.com",
            ["gmail.con"] = "gmail.com",
            ["gmaik.com"] = "gmail.com",

            // Yahoo typos
            ["yaho.com"] = "yahoo.com",
            ["yahooo.com"] = "yahoo.com",
            ["yaho.co.in"] = "yahoo.co.in",
            ["yahoo.co"] = "yahoo.com",

            // Microsoft typos
            ["hotmial.com"] = "hotmail.com",
            ["hotmale.com"] = "hotmail.com",
            ["hotmai.com"] = "hotmail.com",
            ["outlok.com"] = "outlook.com",
            ["outloo.com"] = "outlook.com",
            ["outlokk.com"] = "outlook.com",

            // Indian Providers
            ["redifmail.com"] = "rediffmail.com",
            ["rediff.com"] = "rediffmail.com",
            ["redif.com"] = "rediffmail.com",

            // Apple
            ["iclud.com"] = "icloud.com",
            ["icoud.com"] = "icloud.com"
        };

        // Known legitimate email providers that might be 1 edit away from major ones (e.g. mail.com or ymail.com vs gmail.com)
        private static readonly HashSet<string> LegitimateExemptDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "mail.com",
            "ymail.com",
            "zoho.com",
            "proton.me",
            "protonmail.com",
            "live.com"
        };

        // Major email providers for universal algorithmic typo detection
        private static readonly string[] MajorEmailProviders =
        {
            "gmail.com",
            "yahoo.com",
            "yahoo.co.in",
            "outlook.com",
            "hotmail.com",
            "icloud.com",
            "rediffmail.com"
        };

        // Disposable temporary email domains blacklist
        private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
        {
            "mailinator.com",
            "10minutemail.com",
            "tempmail.com",
            "temp-mail.org",
            "guerrillamail.com",
            "throwawaymail.com",
            "trashmail.com",
            "sharklasers.com",
            "yopmail.com"
        };

        // Dummy repetitive / sequential phone numbers blacklist
        private static readonly HashSet<string> DummyPhoneNumbers = new(StringComparer.OrdinalIgnoreCase)
        {
            "0000000000",
            "1111111111",
            "2222222222",
            "3333333333",
            "4444444444",
            "5555555555",
            "6666666666",
            "7777777777",
            "8888888888",
            "9999999999",
            "1234567890",
            "9876543210"
        };

        /// <summary>
        /// Algorithmic typo detector using Damerau-Levenshtein distance (handles single insertion, deletion, substitution, or transposition).
        /// Returns suggested major domain if a typo is detected, otherwise null.
        /// </summary>
        public static string? CheckMajorDomainTypo(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain) || LegitimateExemptDomains.Contains(domain))
            {
                return null;
            }

            foreach (var major in MajorEmailProviders)
            {
                if (domain.Equals(major, StringComparison.OrdinalIgnoreCase))
                {
                    return null;
                }

                if (ComputeDamerauLevenshteinDistance(domain, major) == 1)
                {
                    return major;
                }
            }

            return null;
        }

        /// <summary>
        /// Computes Damerau-Levenshtein distance between two strings with early exit if length difference > 1.
        /// </summary>
        public static int ComputeDamerauLevenshteinDistance(string s, string t)
        {
            int n = s.Length;
            int m = t.Length;

            if (Math.Abs(n - m) > 1) return 99;

            int[,] d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = (char.ToLowerInvariant(s[i - 1]) == char.ToLowerInvariant(t[j - 1])) ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);

                    // Adjacent character transposition
                    if (i > 1 && j > 1 &&
                        char.ToLowerInvariant(s[i - 1]) == char.ToLowerInvariant(t[j - 2]) &&
                        char.ToLowerInvariant(s[i - 2]) == char.ToLowerInvariant(t[j - 1]))
                    {
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                    }
                }
            }

            return d[n, m];
        }

        /// <summary>
        /// Validates email address: RFC 5322 regex, TLD length >= 2, domain typo guard, and disposable domain blacklist.
        /// </summary>
        public static (bool IsValid, string? ErrorMessage, string CleanedEmail) ValidateEmail(string? email, bool isRequired, string fieldName = "Email")
        {
            if (string.IsNullOrWhiteSpace(email))
            {
                if (isRequired)
                {
                    return (false, $"{fieldName} is required.", string.Empty);
                }
                return (true, null, string.Empty);
            }

            var clean = email.Trim().ToLowerInvariant();

            if (clean.Length > 254)
            {
                return (false, $"{fieldName} must not exceed 254 characters.", clean);
            }

            if (clean.Contains(".."))
            {
                return (false, $"{fieldName} cannot contain consecutive dots.", clean);
            }

            if (!StrictEmailRegex.IsMatch(clean))
            {
                return (false, $"{fieldName} must be a valid email address with a valid domain (e.g., user@example.com).", clean);
            }

            var atIdx = clean.LastIndexOf('@');
            if (atIdx > 0 && atIdx < clean.Length - 1)
            {
                var domain = clean.Substring(atIdx + 1);

                // 1. Explicit fast dictionary lookup
                if (DomainTypoSuggestions.TryGetValue(domain, out var suggestedDomain))
                {
                    return (false, $"Invalid email domain '@{domain}'. Did you mean '@{suggestedDomain}'?", clean);
                }

                // 2. Universal algorithmic typo check (Damerau-Levenshtein distance == 1)
                var algorithmicSuggestion = CheckMajorDomainTypo(domain);
                if (!string.IsNullOrEmpty(algorithmicSuggestion))
                {
                    return (false, $"Invalid email domain '@{domain}'. Did you mean '@{algorithmicSuggestion}'?", clean);
                }

                // 3. Disposable temporary email check
                if (DisposableDomains.Contains(domain))
                {
                    return (false, $"{fieldName}: Disposable or temporary email addresses are not allowed.", clean);
                }
            }

            return (true, null, clean);
        }

        /// <summary>
        /// Validates 10-digit mobile number: strips +91 / 0 / spaces, checks ^[6-9]\d{9}$, and rejects dummy patterns.
        /// </summary>
        public static (bool IsValid, string? ErrorMessage, string CleanedPhone) ValidateMobileNumber(string? phone, bool isRequired, string fieldName = "Mobile number")
        {
            if (string.IsNullOrWhiteSpace(phone))
            {
                if (isRequired)
                {
                    return (false, $"{fieldName} is required.", string.Empty);
                }
                return (true, null, string.Empty);
            }

            // Extract only numeric digits
            var digitsOnly = new string(phone.Where(char.IsDigit).ToArray());

            // If prefix 91 is present on 12-digit number, strip it
            if (digitsOnly.Length == 12 && digitsOnly.StartsWith("91"))
            {
                digitsOnly = digitsOnly.Substring(2);
            }
            // If leading 0 is present on 11-digit number, strip it
            else if (digitsOnly.Length == 11 && digitsOnly.StartsWith("0"))
            {
                digitsOnly = digitsOnly.Substring(1);
            }

            if (digitsOnly.Length != 10)
            {
                return (false, $"{fieldName} must be exactly 10 digits.", digitsOnly);
            }

            if (!IndianMobileRegex.IsMatch(digitsOnly))
            {
                return (false, $"{fieldName} must be a valid 10-digit mobile number starting with 6, 7, 8, or 9.", digitsOnly);
            }

            if (DummyPhoneNumbers.Contains(digitsOnly))
            {
                return (false, $"{fieldName} contains an invalid test or dummy number.", digitsOnly);
            }

            return (true, null, digitsOnly);
        }

        /// <summary>
        /// Validates passenger/guest name. FirstName is mandatory; LastName is optional.
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidateName(string? name, bool isRequired, string fieldName)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                if (isRequired)
                {
                    return (false, $"{fieldName} is required.");
                }
                return (true, null);
            }

            var clean = name.Trim();
            if (clean.Length < 1 || clean.Length > 50)
            {
                return (false, $"{fieldName} must be between 1 and 50 characters.");
            }

            if (!NameRegex.IsMatch(clean))
            {
                return (false, $"{fieldName} must contain letters only (spaces, hyphens, and apostrophes allowed).");
            }

            return (true, null);
        }

        /// <summary>
        /// Validates Title and its compatibility with Gender.
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidateTitleAndGender(string? title, int gender, string fieldName = "Passenger")
        {
            if (string.IsNullOrWhiteSpace(title))
            {
                return (false, $"{fieldName}: Title is required.");
            }

            var cleanTitle = title.Trim();
            var maleTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Mr", "Mstr", "Master" };
            var femaleTitles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Mrs", "Ms", "Miss" };

            if (gender == 1) // Male
            {
                if (femaleTitles.Contains(cleanTitle))
                {
                    return (false, $"{fieldName}: Title '{cleanTitle}' does not match Male gender.");
                }
            }
            else if (gender == 2) // Female
            {
                if (maleTitles.Contains(cleanTitle))
                {
                    return (false, $"{fieldName}: Title '{cleanTitle}' does not match Female gender.");
                }
            }

            return (true, null);
        }

        /// <summary>
        /// Validates PaxType (1: Adult, 2: Child, 3: Infant) against DateOfBirth on travel date.
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidatePaxTypeAndAge(int paxType, string? dateOfBirthStr, DateTime departureDate, string fieldName = "Passenger")
        {
            bool isDobRequired = paxType == 2 || paxType == 3;

            if (string.IsNullOrWhiteSpace(dateOfBirthStr))
            {
                if (isDobRequired)
                {
                    var typeLabel = paxType == 3 ? "Infant" : "Child";
                    return (false, $"{fieldName} ({typeLabel}): Date of birth is required.");
                }
                return (true, null);
            }

            if (!DateTime.TryParse(dateOfBirthStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dob) &&
                !DateTime.TryParse(dateOfBirthStr, out dob))
            {
                return (false, $"{fieldName}: Invalid Date of Birth format. Please use YYYY-MM-DD.");
            }

            if (dob >= DateTime.UtcNow.Date)
            {
                return (false, $"{fieldName}: Date of Birth must be in the past.");
            }

            var refDate = departureDate != default ? departureDate.Date : DateTime.UtcNow.Date;
            if (dob > refDate)
            {
                return (false, $"{fieldName}: Date of Birth cannot be after the departure date.");
            }

            int ageInYears = refDate.Year - dob.Year;
            if (dob.Date > refDate.AddYears(-ageInYears)) ageInYears--;

            if (paxType == 3) // Infant (< 2 years)
            {
                if (ageInYears >= 2)
                {
                    return (false, $"{fieldName}: Date of Birth indicates age {ageInYears}, which exceeds Infant fare limit (must be under 2 years on travel date).");
                }
            }
            else if (paxType == 2) // Child (2 to 11 years)
            {
                if (ageInYears < 2)
                {
                    return (false, $"{fieldName}: Passenger is under 2 years on travel date. Please select Infant fare.");
                }
                if (ageInYears >= 12)
                {
                    return (false, $"{fieldName}: Date of Birth indicates age {ageInYears}, which exceeds Child fare limit (must be 2 to 11 years on travel date).");
                }
            }
            else if (paxType == 1) // Adult (>= 12 years)
            {
                if (ageInYears < 12)
                {
                    return (false, $"{fieldName}: Passenger is under 12 years on travel date. Please select Child or Infant fare.");
                }
            }

            return (true, null);
        }

        /// <summary>
        /// Validates Indian GSTIN number (15 alphanumeric characters).
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidateGstin(string? gstin)
        {
            if (string.IsNullOrWhiteSpace(gstin))
            {
                return (true, null);
            }

            var clean = gstin.Trim();
            if (!GstinRegex.IsMatch(clean))
            {
                return (false, "Invalid GSTIN format. Must be a valid 15-character Indian GST number (e.g., 22AAAAA0000A1Z5).");
            }

            return (true, null);
        }

        /// <summary>
        /// Validates Indian PAN number (10 alphanumeric characters).
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidatePan(string? pan)
        {
            if (string.IsNullOrWhiteSpace(pan))
            {
                return (true, null);
            }

            var clean = pan.Trim();
            if (!PanRegex.IsMatch(clean))
            {
                return (false, "Invalid PAN format. Must be a valid 10-character Indian PAN (e.g., ABCDE1234F).");
            }

            return (true, null);
        }

        /// <summary>
        /// Validates Passport details including 6-month validity beyond departure date.
        /// </summary>
        public static (bool IsValid, string? ErrorMessage) ValidatePassport(
            string? passportNo, 
            string? expiryDateStr, 
            string? issueDateStr, 
            string? countryCode, 
            DateTime departureDate, 
            bool isInternational, 
            string fieldName = "Passenger")
        {
            if (string.IsNullOrWhiteSpace(passportNo))
            {
                if (isInternational)
                {
                    return (false, $"{fieldName}: Passport number is required for international flights.");
                }
                return (true, null);
            }

            var cleanPassport = passportNo.Trim();
            if (!PassportRegex.IsMatch(cleanPassport))
            {
                return (false, $"{fieldName}: Invalid passport number format. Must be 6 to 9 alphanumeric characters.");
            }

            if (!string.IsNullOrWhiteSpace(expiryDateStr))
            {
                if (!DateTime.TryParse(expiryDateStr, out var expiryDate))
                {
                    return (false, $"{fieldName}: Invalid passport expiry date format.");
                }

                var minValidityDate = (departureDate != default ? departureDate.Date : DateTime.UtcNow.Date).AddMonths(6);
                if (expiryDate < minValidityDate)
                {
                    return (false, $"{fieldName}: Passport must be valid for at least 6 months beyond the travel date (expires: {expiryDate:yyyy-MM-dd}).");
                }
            }
            else if (isInternational)
            {
                return (false, $"{fieldName}: Passport expiry date is required for international flights.");
            }

            if (!string.IsNullOrWhiteSpace(issueDateStr))
            {
                if (!DateTime.TryParse(issueDateStr, out var issueDate))
                {
                    return (false, $"{fieldName}: Invalid passport issue date format.");
                }
                if (issueDate > DateTime.UtcNow.Date)
                {
                    return (false, $"{fieldName}: Passport issue date cannot be in the future.");
                }
            }

            if (isInternational && string.IsNullOrWhiteSpace(countryCode))
            {
                return (false, $"{fieldName}: Passport issue country code is required for international flights.");
            }

            return (true, null);
        }
    }
}
