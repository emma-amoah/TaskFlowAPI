using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
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
[Authorize] // Every transaction demands valid JWT identity headers
public class TasksController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly UserManager<User> _userManager;
    private readonly IMapper _mapper;

    public TasksController(AppDbContext context, UserManager<User> userManager, IMapper mapper)
    {
        _context = context;
        _userManager = userManager;
        _mapper = mapper;
    }


    // GET: api/tasks

    [HttpGet]
    public async Task<ActionResult<PagedList<TaskDto>>> GetTasks([FromQuery] QueryParameters parameters)
    {
        var query = _context.Tasks.AsNoTracking();

        // Apply text searching across Title or Description if provided
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var search = parameters.SearchTerm.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(search) ||
                                     t.Description.ToLower().Contains(search));
        }

        // Apply TaskStatus Enum matching logic safely
        if (!string.IsNullOrWhiteSpace(parameters.Status))
        {
            if (Enum.TryParse<TaskFlowAPI.Models.TaskStatus>(parameters.Status, true, out var statusEnum))
            {
                query = query.Where(t => t.Status == statusEnum);
            }
            else
            {
                return BadRequest($"Invalid status filter parameter. Supported values: {string.Join(", ", Enum.GetNames(typeof(TaskFlowAPI.Models.TaskStatus)))}");
            }
        }

        // Apply TaskPriority Enum matching logic safely
        if (!string.IsNullOrWhiteSpace(parameters.Priority))
        {
            if (Enum.TryParse<TaskPriority>(parameters.Priority, true, out var priorityEnum))
            {
                query = query.Where(t => t.Priority == priorityEnum);
            }
            else
            {
                return BadRequest($"Invalid priority filter parameter. Supported values: {string.Join(", ", Enum.GetNames(typeof(TaskPriority)))}");
            }
        }

        // Pass dynamic parameters to the whitelisted expression sorting helper
        string sortColumn = string.IsNullOrWhiteSpace(parameters.SortBy) ? "Id" : parameters.SortBy;
        query = query.OrderByCustom(sortColumn, parameters.IsDescending);

        // LINQ Projection: Shape the precise database payload layout
        var projectedQuery = query.Select(t => new TaskDto
        {
            Id = t.Id,
            Title = t.Title,
            Description = t.Description,
            Status = t.Status.ToString(),
            Priority = t.Priority.ToString(),
            DueDate = t.DueDate,

            CreatedById = t.CreatedBy ?? string.Empty,
            CreatedByName = _context.Users.Where(u => u.Id == t.CreatedBy).Select(u => u.UserName).FirstOrDefault() ?? "System",

            ProjectId = t.ProjectId,
            ProjectName = t.Project != null ? t.Project.Name : string.Empty,
            CategoryId = t.CategoryId,
            CategoryName = t.Category != null ? t.Category.Name : string.Empty,
            AssignedToId = t.AssignedToId,
            AssignedToName = t.AssignedTo != null ? t.AssignedTo.UserName : string.Empty
        });

        // Execute pagination Skip or Take math natively on the database server
        var response = await PagedList<TaskDto>.CreateAsync(projectedQuery, parameters.PageNumber, parameters.PageSize);

        return Ok(response);
    }




    // GET: api/tasks/{id}

    [HttpGet("{id}")]
    public async Task<ActionResult<TaskDto>> GetTask(int id)
    {
        var task = await _context.Tasks
            .Include(t => t.Project)
            .Include(t => t.Category)
            .Include(t => t.AssignedTo)
            .Include(t => t.Comments).ThenInclude(c => c.Author)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task == null) return NotFound($"Task with ID {id} was not found.");

        // Fetch the creator's username to pass into the mapping tracking array
        var creatorName = await _context.Users
        .Where(u => u.Id == task.CreatedBy)
        .Select(u => u.UserName)
        .FirstOrDefaultAsync();

        var creatorsMap = new Dictionary<string, string>();
        if (task.CreatedBy != null && creatorName != null)
        {
            creatorsMap[task.CreatedBy] = creatorName;
        }
        var baseUrl = $"{Request.Scheme}://{Request.Host}";

        var taskDto = _mapper.Map<TaskDto>(task, opts =>
        {
            opts.Items["BaseUrl"] = baseUrl;
            opts.Items["CreatorsMap"] = creatorsMap; // Delivers data cleanly down to mapping profile
        });

        return Ok(taskDto);
    }



    // POST: api/tasks

    [HttpPost]
    public async Task<ActionResult<TaskDto>> CreateTask([FromBody] CreateTaskDto dto)
    {
        // 1. Cross-Entity Validation: Ensure Parent Project exists and is active
        if (!await _context.Projects.AnyAsync(p => p.Id == dto.ProjectId))
        {
            return BadRequest($"Parent Project with ID {dto.ProjectId} does not exist.");
        }

        // 2. Cross-Entity Validation: Ensure target Category exists and is active
        if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
        {
            return BadRequest($"Category with ID {dto.CategoryId} does not exist.");
        }

        // 3. User Validation: If an assignee is designated, verify the user exists and is active
        if (!string.IsNullOrWhiteSpace(dto.AssignedToId))
        {
            var user = await _userManager.FindByIdAsync(dto.AssignedToId);
            if (user == null || !user.IsActive)
            {
                return BadRequest("Cannot assign task to the specified user because the account is invalid or inactive.");
            }
        }

        // Parse and map inbound payloads directly into domain records via AutoMapper profiles
        ProjectTask taskEntity;
        try
        {
            taskEntity = _mapper.Map<ProjectTask>(dto);
        }
        catch (ArgumentException)
        {
            return BadRequest("Invalid Status or Priority values provided.");
        }

        _context.Tasks.Add(taskEntity);
        await _context.SaveChangesAsync();

        // Reload data to pull relational tracking names for the outbound payload mapping
        var savedTask = await _context.Tasks
            .Include(t => t.Project)
            .Include(t => t.Category)
            .Include(t => t.AssignedTo)
            .FirstAsync(t => t.Id == taskEntity.Id);

        return CreatedAtAction(nameof(GetTask), new { id = savedTask.Id }, _mapper.Map<TaskDto>(savedTask));
    }


    // PUT: api/tasks/{id}

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(int id, [FromBody] UpdateTaskDto dto)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null) return NotFound($"Task with ID {id} was not found.");

        // Task Update Ownership Lockout Check
        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Manager) && task.CreatedBy != currentUserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access Denied: You do not have permission to update this task." });
        }

        // CRITICAL CONCURRENCY FIX: Tell EF Core's tracker to validate against the incoming Client Token
        _context.Entry(task).Property(t => t.RowVersion).OriginalValue = dto.RowVersion;

        // Map your remaining scalar property fields from the inbound contract request payload
        _mapper.Map(dto, task);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Tasks.AnyAsync(t => t.Id == id)) return NotFound("The task you are updating was deleted by another session.");
            return Conflict("Concurrency Conflict: This record was modified by another user after you fetched it. Please refresh your sheet.");
        }

        return NoContent();
    }



    // DELETE: api/tasks/{id}

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null) return NotFound($"Task with ID {id} was not found.");

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Dynamic Role-Based Ownership Authorization Checks:
        // regular users can ONLY delete a task if they are the explicit creator of that task record
        var isAuthorized = User.IsInRole(AppRoles.Admin) ||
                           User.IsInRole(AppRoles.Manager) ||
                           task.CreatedBy == currentUserId;

        if (!isAuthorized)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access Denied: You do not have permissions to delete this task. Only the creator, a Manager, or an Admin can perform this action." });
        }

        _context.Tasks.Remove(task);
        await _context.SaveChangesAsync();

        return NoContent();
    }

}
