using Microsoft.AspNetCore.Identity;
using System.Threading.Tasks;
using TaskFlowAPI.Helpers;
using TaskFlowAPI.Models;

namespace TaskFlowAPI.Services;

public static class DbInitializer
{
    public static async Task SeedRolesAndAdminAsync(RoleManager<IdentityRole> roleManager, UserManager<User> userManager)
    {
        // Seed Roles arrays if they don't exist yet
        string[] roleNames = { AppRoles.Admin, AppRoles.Manager, AppRoles.User };

        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Optional: Seed a default master Admin user for local testing environments
        var adminEmail = "admin@taskflow.com";
        var defaultAdmin = await userManager.FindByEmailAsync(adminEmail);

        if (defaultAdmin == null)
        {
            var adminUser = new User
            {
                UserName = "admin",
                Email = adminEmail,
                IsActive = true,
                EmailConfirmed = true
            };

            var createPowerUserResult = await userManager.CreateAsync(adminUser, "SecureAdminPassword123!");
            if (createPowerUserResult.Succeeded)
            {
                // Attach the Admin role designation to this seed entity
                await userManager.AddToRoleAsync(adminUser, AppRoles.Admin);
            }
        }
    }
}
