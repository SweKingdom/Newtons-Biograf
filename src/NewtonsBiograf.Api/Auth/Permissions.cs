using Microsoft.AspNetCore.Authorization;
using NewtonsBiograf.Shared;

namespace NewtonsBiograf.Api.Auth;

public static class Policies
{
    public const string AdminOnly = "AdminOnly";
    public const string CustomerOnly = "CustomerOnly";
    public const string ManageMovies = Permissions.ManageMovies;
    public const string ManageShowings = Permissions.ManageShowings;
    public const string ManageBookings = Permissions.ManageBookings;
}

public static class AuthorizationPolicyExtensions
{
    /// <summary>Registers the app policies. Admin passes every permission policy; Staff needs the specific claim.</summary>
    public static void AddAppPolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(Policies.AdminOnly, p => p.RequireRole(Roles.Admin));
        options.AddPolicy(Policies.CustomerOnly, p => p.RequireRole(Roles.Customer));
        foreach (var permission in Permissions.All)
        {
            options.AddPolicy(permission, p => p
                .RequireAuthenticatedUser()
                .RequireAssertion(ctx =>
                    ctx.User.IsInRole(Roles.Admin) || ctx.User.HasClaim(Permissions.ClaimType, permission)));
        }
    }
}
