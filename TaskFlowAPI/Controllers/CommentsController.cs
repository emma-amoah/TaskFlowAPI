using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TaskFlowAPI.Contexts;
using TaskFlowAPI.DTOs;
using TaskFlowAPI.Helpers;
using TaskFlowAPI.Models;

namespace TaskFlowAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize] // Requires valid JWT authentication headers globally
public class CommentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public CommentsController(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;
    }

    // GET: api/comments/task/{taskId}

    [HttpGet("task/{taskId}")]
    public async Task<ActionResult<PagedList<CommentDto>>> GetTaskComments(int taskId, [FromQuery] QueryParameters parameters)
    {
        // Cross-Entity Validation: Verify the parent task exists and is active
        if (!await _context.Tasks.AnyAsync(t => t.Id == taskId))
        {
            return NotFound($"Parent Task with ID {taskId} was not found.");
        }

        var query = _context.Comments
            .Include(c => c.Author)
            .Where(c => c.TaskId == taskId)
            .AsNoTracking();

        // Filter: Apply text search filter against comment text content
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var search = parameters.SearchTerm.Trim().ToLower();
            query = query.Where(c => c.Content.ToLower().Contains(search));
        }

        // Sort: Apply dynamic property sorting helper
        string sortColumn = string.IsNullOrWhiteSpace(parameters.SortBy) ? "Id" : parameters.SortBy;
        query = query.OrderByCustom(sortColumn, parameters.IsDescending);

        // Page: Execute pagination math directly on SQL database engine
        var pagedEntities = await PagedList<Comment>.CreateAsync(query, parameters.PageNumber, parameters.PageSize);
        var mappedItems = _mapper.Map<List<CommentDto>>(pagedEntities.Items);

        var response = new PagedList<CommentDto>(
            mappedItems,
            pagedEntities.TotalCount,
            pagedEntities.CurrentPage,
            pagedEntities.PageSize
        );

        return Ok(response);
    }

    // POST: api/comments

    [HttpPost]
    public async Task<ActionResult<CommentDto>> CreateComment([FromBody] CreateCommentDto dto)
    {
        if (!await _context.Tasks.AnyAsync(t => t.Id == dto.TaskId))
        {
            return BadRequest($"Parent Task with ID {dto.TaskId} does not exist.");
        }

        // Secure Extraction: Extract the current authenticated user's ID directly from claims tokens
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized("Unable to resolve user identity from active token context.");
        }

        var commentEntity = _mapper.Map<Comment>(dto);
        commentEntity.AuthorId = userId; // Bind authorship explicitly on server layer

        _context.Comments.Add(commentEntity);
        await _context.SaveChangesAsync();

        // Reload data to map complete relational data contract properties (e.g. Author name string value)
        var savedComment = await _context.Comments
            .Include(c => c.Author)
            .FirstAsync(c => c.Id == commentEntity.Id);

        return CreatedAtRoute(new { id = savedComment.Id }, _mapper.Map<CommentDto>(savedComment));
    }

    // DELETE: api/comments/{id}

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteComment(int id)
    {
        var comment = await _context.Comments.FindAsync(id);
        if (comment == null)
        {
            return NotFound($"Comment with ID {id} was not found.");
        }

        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        // Security Authorization Check: Only allow the comment author to delete their own post
        if (comment.AuthorId != userId)
        {
            // Enforce role bypass parameters if admins can drop comments
            if (!User.IsInRole(AppRoles.Admin))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access Denied: You cannot delete a comment posted by another team member." });
            }
        }

        _context.Comments.Remove(comment);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
