using AutoMapper;
using Microsoft.AspNetCore.Authorization;
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
[Authorize] // All endpoints require a verified JWT bearer token
public class CategoriesController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public CategoriesController(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    // GET: api/categories

    [HttpGet]
    public async Task<ActionResult<PagedList<CategoryDto>>> GetCategories([FromQuery] QueryParameters parameters)
    {
        var query = _context.Categories.AsNoTracking();

        // Filter: Apply text search filter against the Category Name
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var search = parameters.SearchTerm.Trim().ToLower();
            query = query.Where(c => c.Name.ToLower().Contains(search));
        }

        // Sort: Apply dynamic property sorting expression helper
        string sortColumn = string.IsNullOrWhiteSpace(parameters.SortBy) ? "Id" : parameters.SortBy;
        query = query.OrderByCustom(sortColumn, parameters.IsDescending);

        // Page: Execute paging offsets directly on the database engine
        var pagedEntities = await PagedList<Category>.CreateAsync(query, parameters.PageNumber, parameters.PageSize);
        var mappedItems = _mapper.Map<List<CategoryDto>>(pagedEntities.Items);

        var response = new PagedList<CategoryDto>(
            mappedItems,
            pagedEntities.TotalCount,
            pagedEntities.CurrentPage,
            pagedEntities.PageSize
        );

        return Ok(response);
    }

    // GET: api/categories/{id}

    [HttpGet("{id}")]
    public async Task<ActionResult<CategoryDto>> GetCategory(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound($"Category with ID {id} was not found.");
        }

        return Ok(_mapper.Map<CategoryDto>(category));
    }

    // POST: api/categories

    [HttpPost]
    public async Task<ActionResult<CategoryDto>> CreateCategory([FromBody] SaveCategoryDto dto)
    {
        // Unique Constraint Check: Ensure no active category shares this name
        var normalizedName = dto.Name.Trim().ToLower();
        var nameExists = await _context.Categories
            .AnyAsync(c => c.Name.ToLower() == normalizedName); // Global Query Filter handles !IsDeleted automatically

        if (nameExists)
        {
            return BadRequest($"A category named '{dto.Name}' already exists.");
        }

        var categoryEntity = _mapper.Map<Category>(dto);
        categoryEntity.Name = dto.Name.Trim(); // Clean trailing spaces

        _context.Categories.Add(categoryEntity);
        await _context.SaveChangesAsync();

        var createdCategoryDto = _mapper.Map<CategoryDto>(categoryEntity);
        return CreatedAtAction(nameof(GetCategory), new { id = categoryEntity.Id }, createdCategoryDto);
    }

    // PUT: api/categories/{id}

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] SaveCategoryDto dto)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound($"Category with ID {id} was not found.");
        }

        // Unique Constraint Check: If name changes, verify it doesn't collide with another active category
        var normalizedName = dto.Name.Trim().ToLower();
        if (category.Name.ToLower() != normalizedName)
        {
            var nameExists = await _context.Categories
                .AnyAsync(c => c.Id != id && c.Name.ToLower() == normalizedName);

            if (nameExists)
            {
                return BadRequest($"Another active category named '{dto.Name}' already exists.");
            }
        }

        _mapper.Map(dto, category);
        category.Name = dto.Name.Trim();

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            if (!await _context.Categories.AnyAsync(c => c.Id == id))
            {
                return NotFound("The category you are updating was deleted by another user.");
            }
            return Conflict("Concurrency Conflict: The category record was modified concurrently. Please reload and try again.");
        }

        return NoContent();
    }

    // DELETE: api/categories/{id}

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        var category = await _context.Categories
            .Include(c => c.Tasks)
            .FirstOrDefaultAsync(c => c.Id == id);

        if (category == null)
        {
            return NotFound($"Category with ID {id} was not found.");
        }

        // Relational Integrity Business Rule: Prevent deletion where related records exist
        // Global query filter handles checking only active, non-deleted tasks automatically
        if (category.Tasks.Any())
        {
            return BadRequest("Cannot delete this category because it contains active tasks. Please reassign or delete the tasks first.");
        }

        // Passes control straight to the custom overridden tracking pipeline to convert into a soft-delete
        _context.Categories.Remove(category);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
