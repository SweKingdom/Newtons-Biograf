using Microsoft.AspNetCore.Authorization;

namespace NewtonsBiograf.Api.Auth;

public static class Permissions
{
    public const string ClaimType = "permission";
    public const string ManageMovies = "ManageMovies";
    public const string ManageShowings = "ManageShowings";
    public const string ManageBookings = "ManageBookings";

    public static readonly string[] All = [ManageMovies, ManageShowings, ManageBookings];
}

public static class Roles
{
    public const string Admin = "Admin";
    public const string Staff = "Staff";
    public const string Customer = "Customer";
}

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
