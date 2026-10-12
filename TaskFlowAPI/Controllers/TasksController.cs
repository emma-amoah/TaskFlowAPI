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
        var query = _context.Tasks
            .Include(t => t.Project)
            .Include(t => t.Category)
            .Include(t => t.AssignedTo)
            .AsNoTracking();

        // Filter: Apply text search across Title and Description
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var search = parameters.SearchTerm.Trim().ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(search) ||
                                     t.Description.ToLower().Contains(search));
        }

        // Filter: Filter by Status string conversion match
        if (!string.IsNullOrWhiteSpace(parameters.Status))
        {
            if (Enum.TryParse<TaskFlowAPI.Models.TaskStatus>(parameters.Status, true, out var statusEnum))
            {
                query = query.Where(t => t.Status == statusEnum);
            }
            else
            {
                return BadRequest($"Invalid status filter parameter provided. Supported statuses are: {string.Join(", ", Enum.GetNames(typeof(TaskFlowAPI.Models.TaskStatus)))}");
            }
        }

        // Filter: Filter by Priority string conversion match
        if (!string.IsNullOrWhiteSpace(parameters.Priority))
        {
            if (Enum.TryParse<TaskPriority>(parameters.Priority, true, out var priorityEnum))
            {
                query = query.Where(t => t.Priority == priorityEnum);
            }
            else
            {
                return BadRequest($"Invalid priority filter parameter provided. Supported priorities are: {string.Join(", ", Enum.GetNames(typeof(TaskPriority)))}");
            }
        }

        // Sort: Apply dynamic column selection reflection helper
        string sortColumn = string.IsNullOrWhiteSpace(parameters.SortBy) ? "Id" : parameters.SortBy;
        query = query.OrderByCustom(sortColumn, parameters.IsDescending);

        // Page: Execute paging offsets directly on the database engine
        var pagedEntities = await PagedList<ProjectTask>.CreateAsync(query, parameters.PageNumber, parameters.PageSize);
        var mappedItems = _mapper.Map<List<TaskDto>>(pagedEntities.Items);

        var response = new PagedList<TaskDto>(
            mappedItems,
            pagedEntities.TotalCount,
            pagedEntities.CurrentPage,
            pagedEntities.PageSize
        );

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
            .Include(t => t.Comments)
                .ThenInclude(c => c.Author)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id);

        if (task == null)
        {
            return NotFound($"Task with ID {id} was not found.");
        }

        return Ok(_mapper.Map<TaskDto>(task));
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
    public async Task<IActionResult> UpdateTask(int id, [FromBody] CreateTaskDto dto)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null)
        {
            return NotFound($"Task with ID {id} was not found.");
        }

        // Relational boundary integrity verification checks
        if (!await _context.Projects.AnyAsync(p => p.Id == dto.ProjectId))
        {
            return BadRequest($"Parent Project with ID {dto.ProjectId} does not exist.");
        }

        if (!await _context.Categories.AnyAsync(c => c.Id == dto.CategoryId))
        {
            return BadRequest($"Category with ID {dto.CategoryId} does not exist.");
        }

        if (!string.IsNullOrWhiteSpace(dto.AssignedToId))
        {
            var user = await _userManager.FindByIdAsync(dto.AssignedToId);
            if (user == null || !user.IsActive)
            {
                return BadRequest("Cannot assign task to the specified user because the account is invalid or inactive.");
            }
        }

        // Map updates cleanly onto the active database context tracking object
        _mapper.Map(dto, task);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Tasks.AnyAsync(t => t.Id == id))
            {
                return NotFound("The task you are updating was deleted by another session.");
            }
            return Conflict("Concurrency Conflict: The task record was modified by another session. Please refresh and try again.");
        }

        return NoContent();
    }


    // DELETE: api/tasks/{id}

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        var task = await _context.Tasks.FindAsync(id);
        if (task == null)
        {
            return NotFound($"Task with ID {id} was not found.");
        }

        // Triggers the automated cascade state tracking block inside AppDbContext
        _context.Tasks.Remove(task);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
