using System.Security.Claims;

namespace GameLibrary.Api.Auth;

/// <summary>
/// Reads the authenticated Supabase user ID from the validated JWT <c>sub</c> claim.
/// The <c>sub</c> claim name is preserved because the JWT bearer handler runs with
/// <c>MapInboundClaims = false</c>.
/// </summary>
public static class ClaimsPrincipalExtensions
{
    public const string SubjectClaimType = "sub";

    /// <summary>
    /// Returns the value of the <c>sub</c> claim, or <c>null</c> when the principal is
    /// unauthenticated or lacks a usable <c>sub</c> claim. A missing <c>sub</c> must be
    /// treated as unauthenticated by callers.
    /// </summary>
    public static string? GetSupabaseUserId(this ClaimsPrincipal principal) =>
        principal.FindFirst(SubjectClaimType)?.Value;
}