using Microsoft.AspNetCore.Identity;

namespace NewtonsBiograf.Api.Data;

public class ApplicationUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}
