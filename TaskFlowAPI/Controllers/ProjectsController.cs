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
        // Start with an un-evaluated IQueryable stream
        var query = _context.Projects
            .Include(p => p.Tasks)
            .Include(p => p.ProjectMembers)
                .ThenInclude(pm => pm.User)
            .AsNoTracking(); // Optimization: bypasses EF change tracking cache for read-only streams

        // Filter: Apply text search filter against project name or description if provided
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var search = parameters.SearchTerm.Trim().ToLower();
            query = query.Where(p => p.Name.ToLower().Contains(search) ||
                                     p.Description.ToLower().Contains(search));
        }

        // Sort: Pass the sorting instructions dynamically to our expression builder helper
        // Fallback default column sorting key if the client sends an invalid property name target
        string sortColumn = string.IsNullOrWhiteSpace(parameters.SortBy) ? "Id" : parameters.SortBy;
        query = query.OrderByCustom(sortColumn, parameters.IsDescending);

        // Page: Execute pagination math directly on SQL server engine using Skip/Take via our helper
        var pagedEntities = await PagedList<Project>.CreateAsync(query, parameters.PageNumber, parameters.PageSize);

        // Map the paginated DB objects safely into structural DTO array schemas
        var mappedItems = _mapper.Map<List<ProjectDto>>(pagedEntities.Items);

        // Package the dataset with pagination metadata to provide a standard envelope contract
        var response = new PagedList<ProjectDto>(
            mappedItems,
            pagedEntities.TotalCount,
            pagedEntities.CurrentPage,
            pagedEntities.PageSize
        );

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
        if (project == null)
        {
            return NotFound($"Project with ID {id} was not found.");
        }

        // Map input updates onto the tracked database record instance
        _mapper.Map(dto, project);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            // Protection against simultaneous modifications via our BaseEntity.RowVersion token
            if (!await _context.Projects.AnyAsync(p => p.Id == id))
            {
                return NotFound("The project you are updating was deleted by another user.");
            }
            return Conflict("Concurrency Conflict: The project record was modified by another session. Please reload and try again.");
        }

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
}
