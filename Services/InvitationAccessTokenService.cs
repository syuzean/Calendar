using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Calendar.Services;

public interface IInvitationAccessTokenService
{
    string Create(Guid invitationId);
    bool TryRead(string token, out Guid invitationId);
}

public sealed class InvitationAccessTokenService(IDataProtectionProvider dataProtectionProvider)
    : IInvitationAccessTokenService
{
    private const string Prefix = "luma-invitation-v1.";
    private readonly IDataProtector _protector = dataProtectionProvider.CreateProtector(
        "Luma.Calendar.EventInvitation.AccessLink.v1");

    public string Create(Guid invitationId) => Prefix + _protector.Protect(invitationId.ToString("N"));

    public bool TryRead(string token, out Guid invitationId)
    {
        invitationId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(token) || token.Length > 2048 ||
            !token.StartsWith(Prefix, StringComparison.Ordinal))
            return false;

        try
        {
            return Guid.TryParseExact(_protector.Unprotect(token[Prefix.Length..]), "N", out invitationId);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
