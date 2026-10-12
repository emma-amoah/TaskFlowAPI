namespace TaskFlowAPI.Helpers;

public static class AppRoles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string User = "User";
}

public static class AppPolicies
{
    // A policy requiring someone to be an Admin OR a Manager
    public const string RequireManagementLevel = "RequireManagementLevel";
}
