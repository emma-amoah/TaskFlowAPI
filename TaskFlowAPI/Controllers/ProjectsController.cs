using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TaskFlowAPI.Contexts;
using TaskFlowAPI.DTOs;
using TaskFlowAPI.Helpers;
using TaskFlowAPI.Models;

namespace TaskFlowAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Enforces that all project administration requests carry a valid JWT token
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public ProjectsController(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }


    // GET: api/projects

    [HttpGet]
    public async Task<ActionResult<PagedList<ProjectDto>>> GetProjects([FromQuery] QueryParameters parameters)
    {
        // retrieve the base queryable set of projects without tracking to optimize read-only operations
        var query = _context.Projects.AsNoTracking();

        // Filter: Apply text search filter against project name or description
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var search = parameters.SearchTerm.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(search) ||
                                     p.Description.ToLower().Contains(search));
        }

        // Sort: Pass the sorting instructions dynamically to the expression builder helper
        string sortColumn = string.IsNullOrWhiteSpace(parameters.SortBy) ? "Id" : parameters.SortBy;
        query = query.OrderByCustom(sortColumn, parameters.IsDescending);

        var projectedQuery = query.Select(p => new ProjectDto
        {
            Id = p.Id,
            Name = p.Name,
            Description = p.Description,
            CreatedAt = p.CreatedAt,
            // Inline aggregate calculation translated directly into a SQL COUNT() statement
            TotalTasksCount = p.Tasks.Count(t => !t.IsDeleted),

            // Project nested collections cleanly without loading entire IdentityUser entities
            Members = p.ProjectMembers.Select(pm => new ProjectMemberDto
            {
                UserId = pm.UserId,

                // 1. FIXED: Handle both cases where the User object OR the underlying UserName string is null
                UserName = (pm.User != null && pm.User.UserName != null)
                ? pm.User.UserName
                : string.Empty,

                // 2. FIXED: Fallback to a default string if the database column contains a null value
                Role = pm.Role ?? "Member"
            }).ToList()

        });

        // PagedList script now handles the Skip/Take logic entirely on the projected DTO query
        var response = await PagedList<ProjectDto>.CreateAsync(projectedQuery, parameters.PageNumber, parameters.PageSize);

        return Ok(response);
    }


    // GET: api/projects/{id}

    [HttpGet("{id}")]
    public async Task<ActionResult<ProjectDto>> GetProject(int id)
    {
        var project = await _context.Projects
            .Include(p => p.Tasks)
            .Include(p => p.ProjectMembers)
                .ThenInclude(pm => pm.User)
            .FirstOrDefaultAsync(p => p.Id == id);

        if (project == null)
        {
            return NotFound($"Project with ID {id} was not found.");
        }

        return Ok(_mapper.Map<ProjectDto>(project));
    }

    // POST: api/projects

    [HttpPost]
    public async Task<ActionResult<ProjectDto>> CreateProject([FromBody] SaveProjectDto dto)
    {
        var projectEntity = _mapper.Map<Project>(dto);

        _context.Projects.Add(projectEntity);
        await _context.SaveChangesAsync();

        // Map to return populated structural data contract payload back to client
        var createdProjectDto = _mapper.Map<ProjectDto>(projectEntity);

        return CreatedAtAction(nameof(GetProject), new { id = projectEntity.Id }, createdProjectDto);
    }

    // PUT: api/projects/{id}

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateProject(int id, [FromBody] SaveProjectDto dto)
    {
        var project = await _context.Projects.FindAsync(id);
        if (project == null) return NotFound();

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Ensure only the creator or management tiers can edit structural project details
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Manager) && project.CreatedBy != currentUserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access Denied: You do not have permission to modify this project configuration." });
        }

        _mapper.Map(dto, project);
        await _context.SaveChangesAsync();
        return NoContent();
    }

    // DELETE: api/projects/{id}

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteProject(int id)
    {
        var project = await _context.Projects.FindAsync(id);
        if (project == null)
        {
            return NotFound($"Project with ID {id} was not found.");
        }

        // Triggers the custom overridden context engine to convert this call safely into a soft-delete cascade operation
        _context.Projects.Remove(project);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // POST: api/projects/{id}/archive-and-cleanup
    [HttpPost("{id}/archive-and-cleanup")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Manager}")] // FIXED: Restrict access by role parameters explicitly
    public async Task<IActionResult> ArchiveProjectAndCleanupTasks(int id)
    {
        var project = await _context.Projects.FindAsync(id);
        if (project == null) return NotFound($"Project with ID {id} was not found.");

        // Prevent double-archiving stacking states cleanly
        if (project.IsArchived)
        {
            return BadRequest("This project asset has already been archived.");
        }

        using var transaction = await _context.Database.BeginTransactionAsync();

        try
        {
            // Toggle the structural flag clean state parameter
            project.IsArchived = true;
            _context.Projects.Update(project);
            await _context.SaveChangesAsync();

            // Automatically sweep up and complete all child task elements inline atomic structures
            var activeTasks = await _context.Tasks.Where(t => t.ProjectId == id && !t.IsDeleted).ToListAsync();
            foreach (var task in activeTasks)
            {
                task.Status = TaskFlowAPI.Models.TaskStatus.Completed;
                _context.Tasks.Update(task);
            }
            await _context.SaveChangesAsync();

            // Release and flush associated project membership data links safely
            var projectMembers = await _context.ProjectMembers.Where(pm => pm.ProjectId == id).ToListAsync();
            _context.ProjectMembers.RemoveRange(projectMembers);
            await _context.SaveChangesAsync();

            await transaction.CommitAsync();

            return Ok(new { Message = "Project flag switched to Archived and nested tasks compiled successfully." });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync();
            return StatusCode(StatusCodes.Status500InternalServerError, "Transaction Failed: Atomic multi-step operations aborted. Reversion executed.");
        }
    }

}
