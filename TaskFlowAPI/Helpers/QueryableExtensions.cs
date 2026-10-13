using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace TaskFlowAPI.Helpers;

public static class QueryableExtensions
{
    // Restrict sorting property paths to explicit safe columns only
    private static readonly HashSet<string> WhitelistedFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "Id", "Title", "Name", "CreatedAt", "DueDate", "Status", "Priority"
    };

    public static IQueryable<T> OrderByCustom<T>(this IQueryable<T> source, string propertyName, bool isDescending)
    {
        if (string.IsNullOrWhiteSpace(propertyName) || !WhitelistedFields.Contains(propertyName.Trim()))
        {
            // Default to base column index tracking if property isn't whitelisted
            propertyName = "Id";
        }
        var type = typeof(T);
        // Find matching property via reflection case-insensitive flag maps
        var property = type.GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

        // Fallback boundary to avoid runtime crashes if user sends an invalid property name target
        if (property == null) return source;

        var parameter = Expression.Parameter(type, "p");
        var propertyAccess = Expression.MakeMemberAccess(parameter, property);
        var orderByExpression = Expression.Lambda(propertyAccess, parameter);

        string methodName = isDescending ? "OrderByDescending" : "OrderBy";

        var resultExpression = Expression.Call(
            typeof(Queryable),
            methodName,
            new Type[] { type, property.PropertyType },
            source.Expression,
            Expression.Quote(orderByExpression)
        );

        return source.Provider.CreateQuery<T>(resultExpression);
    }
}
