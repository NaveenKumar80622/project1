using System;
using System.Collections.Generic;
using System.Text.Json;

namespace PickNBook.Api.Helpers
{
    public static class PaymentMethodHelper
    {
        /// <summary>
        /// Normalizes raw payment method names into customer-facing display labels
        /// (e.g. "UPI", "Debit Card", "Credit Card", "Card Payment", "Net Banking").
        /// </summary>
        public static string Normalize(string? rawMethod)
        {
            if (string.IsNullOrWhiteSpace(rawMethod)) return "Online Payment";

            var trimmed = rawMethod.Trim();
            var lower = trimmed.ToLowerInvariant();

            if (lower == "upi") return "UPI";
            if (lower.Contains("debit")) return "Debit Card";
            if (lower.Contains("credit")) return "Credit Card";
            if (lower == "card" || lower.Contains("card")) return "Card Payment";
            if (lower == "netbanking" || lower.Contains("net_banking") || lower.Contains("net banking") || lower == "nb") return "Net Banking";
            if (lower == "wallet" || lower == "app") return "Wallet";
            if (lower == "agent wallet") return "Agent Wallet";
            if (lower == "paylater") return "Pay Later";
            if (lower == "emi" || lower == "cardlessemi") return "EMI";

            if (lower.StartsWith("hybrid")) return trimmed;
            if (lower == "cashfree") return "Online Payment";

            return char.ToUpperInvariant(trimmed[0]) + (trimmed.Length > 1 ? trimmed[1..] : "");
        }

        /// <summary>
        /// Resolves the effective customer-facing payment method by prioritizing specific gateway
        /// instruments over generic gateway brand names like "Cashfree".
        /// </summary>
        public static string ResolvePaymentMethod(
            string? paymentMethod,
            string? gatewayPaymentMethod,
            decimal walletUsed = 0,
            decimal gatewayPaid = 0)
        {
            bool isHybrid = (walletUsed > 0 && gatewayPaid > 0) ||
                            string.Equals(paymentMethod, "Hybrid", StringComparison.OrdinalIgnoreCase) ||
                            paymentMethod?.StartsWith("Hybrid", StringComparison.OrdinalIgnoreCase) == true;

            // Pure wallet
            if (walletUsed > 0 && gatewayPaid == 0)
            {
                return "Wallet";
            }

            // Pick the most specific available instrument
            string? candidate = null;
            if (!string.IsNullOrWhiteSpace(gatewayPaymentMethod) && !string.Equals(gatewayPaymentMethod, "Cashfree", StringComparison.OrdinalIgnoreCase))
            {
                candidate = gatewayPaymentMethod;
            }
            else if (!string.IsNullOrWhiteSpace(paymentMethod) && !string.Equals(paymentMethod, "Cashfree", StringComparison.OrdinalIgnoreCase))
            {
                candidate = paymentMethod;
            }

            if (candidate == null)
            {
                return isHybrid ? "Hybrid" : "Online Payment";
            }

            string normalized = Normalize(candidate);
            if (isHybrid && !normalized.StartsWith("Hybrid", StringComparison.OrdinalIgnoreCase))
            {
                return $"Hybrid (Wallet + {normalized})";
            }

            return normalized;
        }

        /// <summary>
        /// Parses Cashfree payment_method JsonElement from webhooks or payment verification.
        /// Extracts whether the customer paid via UPI, Debit Card, Credit Card, Netbanking, etc.
        /// </summary>
        public static string ParseCashfreePaymentMethod(JsonElement methodEl)
        {
            try
            {
                if (methodEl.ValueKind != JsonValueKind.Object) return "Cashfree";

                foreach (var prop in methodEl.EnumerateObject())
                {
                    var instrument = prop.Name.ToLowerInvariant();
                    if (instrument == "upi")
                    {
                        return "UPI";
                    }
                    if (instrument == "card")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object)
                        {
                            if (prop.Value.TryGetProperty("card_type", out var typeEl))
                            {
                                var typeStr = typeEl.GetString()?.ToLowerInvariant() ?? "";
                                if (typeStr.Contains("debit")) return "Debit Card";
                                if (typeStr.Contains("credit")) return "Credit Card";
                            }
                        }
                        return "Card Payment";
                    }
                    if (instrument == "netbanking")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object &&
                            prop.Value.TryGetProperty("netbanking_bank_name", out var bankEl))
                        {
                            var bankName = bankEl.GetString();
                            if (!string.IsNullOrWhiteSpace(bankName)) return $"Net Banking ({bankName.Trim()})";
                        }
                        return "Net Banking";
                    }
                    if (instrument == "app")
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Object &&
                            prop.Value.TryGetProperty("provider", out var provEl))
                        {
                            var prov = provEl.GetString();
                            if (!string.IsNullOrWhiteSpace(prov)) return $"Wallet ({char.ToUpperInvariant(prov[0]) + prov[1..]})";
                        }
                        return "Wallet";
                    }
                    if (instrument == "paylater") return "Pay Later";
                    if (instrument == "emi" || instrument == "cardlessemi") return "EMI";

                    return Normalize(instrument);
                }
            }
            catch
            {
                // Fallback
            }
            return "Cashfree";
        }
    }
}
