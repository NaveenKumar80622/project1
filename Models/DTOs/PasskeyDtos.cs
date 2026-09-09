using System;
using Fido2NetLib;

namespace PickNBook.Api.Models.DTOs
{
    public class PasskeyRegisterOptionsRequest
    {
        public string? DeviceName { get; set; }
    }

    public class PasskeyRegisterCompleteRequest
    {
        public AuthenticatorAttestationRawResponse? AttestationResponse { get; set; }
        public string? DeviceName { get; set; }
    }

    public class PasskeyLoginOptionsRequest
    {
        public string? Email { get; set; }
    }

    public class PasskeyLoginOptionsResponse
    {
        public string SessionId { get; set; } = string.Empty;
        public AssertionOptions? Options { get; set; }
    }

    public class PasskeyLoginCompleteRequest
    {
        public string SessionId { get; set; } = string.Empty;
        public AuthenticatorAssertionRawResponse? AssertionResponse { get; set; }
    }

    public class UserPasskeyDto
    {
        public int Id { get; set; }
        public string? DeviceName { get; set; }
        public string CredType { get; set; } = "public-key";
        public DateTime CreatedAtUtc { get; set; }
        public DateTime? LastUsedAtUtc { get; set; }
    }

    public class PasskeyRenameRequest
    {
        public string DeviceName { get; set; } = string.Empty;
    }
}
