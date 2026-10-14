using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Threading.Tasks;
using TaskFlowAPI.Helpers;
using TaskFlowAPI.Models;

namespace TaskFlowAPI.Services;

public static class DbInitializer
{
    public static async Task SeedRolesAndAdminAsync(
        RoleManager<IdentityRole> roleManager,
        UserManager<User> userManager,
        IConfiguration config,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger("DbInitializer");

        // Seed System Roles
        string[] roleNames = { AppRoles.Admin, AppRoles.Manager, AppRoles.User };
        foreach (var roleName in roleNames)
        {
            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new IdentityRole(roleName));
            }
        }

        // Seed Admin User Safely
        var adminEmail = "admin@taskflow.com";
        var defaultAdmin = await userManager.FindByEmailAsync(adminEmail);

        if (defaultAdmin == null)
        {
            // Extract password with NO plain-text string fallbacks
            var adminPassword = config["SeedData:AdminPassword"];

            if (string.IsNullOrWhiteSpace(adminPassword))
            {
                logger.LogCritical("CRITICAL CONFIGURATION ERROR: SeedData:AdminPassword is null or empty. Seed admin user creation skipped.");
                return; // Halt execution safely without hardcoding the password
            }

            var adminUser = new User
            {
                UserName = "admin",
                Email = adminEmail,
                IsActive = true,
                EmailConfirmed = true
            };

            var result = await userManager.CreateAsync(adminUser, adminPassword);
            if (result.Succeeded)
            {
                await userManager.AddToRoleAsync(adminUser, AppRoles.Admin);
                logger.LogInformation("Master administrative seed user account generated successfully.");
            }
            else
            {
                var errorDescriptions = string.Join(", ", result.Errors.Select(e => e.Description));
                logger.LogError("Failed to seed admin user. Reasons: {Errors}", errorDescriptions);
            }
        }
    }
}
