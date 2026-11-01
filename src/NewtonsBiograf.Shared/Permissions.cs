namespace NewtonsBiograf.Shared;

/// <summary>Permission names a staff member can hold. Shared so the API and the client use one definition.</summary>
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
