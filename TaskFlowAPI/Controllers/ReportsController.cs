using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using TaskFlowAPI.Helpers;
using TaskFlowAPI.Contexts;

namespace TaskFlowAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = AppRoles.Admin)] // Secure administrative reporting engine boundary lock
public class ReportsController : ControllerBase
{
    private readonly AppDbContext _context;

    public class ProductivityReportResult
    {
        public int CompletedTasksCount { get; set; }
        public int ActiveTasksCount { get; set; }
        public int OverdueTasksCount { get; set; }
    }

    public class BurndownReportResult
    {
        public string Status { get; set; } = string.Empty;
        public int TaskCount { get; set; }
    }

    public ReportsController(AppDbContext context)
    {
        _context = context;
    }


    [HttpGet("user-productivity/{userId}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager}")]
    public async Task<IActionResult> GetUserProductivityReport(string userId)
    {
        var param = new SqlParameter("@UserId", userId);

        // Stored procedures can't be composed with TOP(1), so load the rows first
        var rows = await _context.Database
            .SqlQueryRaw<ProductivityReportResult>("EXEC sp_GetUserProductivityReport @UserId", param)
            .ToListAsync();

        var reportData = rows.FirstOrDefault();
        if (reportData == null) return NotFound("Unable to extract productivity statistics metrics.");

        return Ok(reportData);
    }

    [HttpGet("project-burndown/{projectId}")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager}")]
    public async Task<IActionResult> GetProjectBurndownStatus(int projectId)
    {
        var param = new SqlParameter("@ProjectId", projectId);

        // Execute stored procedure 2 using FromSqlRaw
        var reportData = await _context.Database
            .SqlQueryRaw<BurndownReportResult>("EXEC sp_GetProjectBurndownStatus @ProjectId", param)
            .ToListAsync();

        return Ok(reportData);
    }


    [HttpPost("purge-tokens")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> PurgeExpiredRefreshTokens()
    {
        // Execute stored procedure 3 using ExecuteSqlRawAsync since it modifies rows instead of selecting data
        var rowsAffected = await _context.Database
            .ExecuteSqlRawAsync("EXEC sp_PurgeExpiredRefreshTokens");

        return Ok(new { Message = "System maintenance cleanup event complete.", RecordsPurged = rowsAffected });
    }
}
