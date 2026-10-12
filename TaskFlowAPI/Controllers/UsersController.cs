using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
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
[Authorize(Policy = AppPolicies.RequireManagementLevel)] // Enforces that ONLY Admins or Managers can ever interact with this controller area
public class UsersController : ControllerBase
{
    private readonly UserManager<User> _userManager;
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;

    public UsersController(UserManager<User> userManager, AppDbContext context, IMapper mapper)
    {
        _userManager = userManager;
        _context = context;
        _mapper = mapper;
    }



    [HttpGet]
    public async Task<ActionResult<PagedList<UserDto>>> GetUsers([FromQuery] QueryParameters parameters)
    {
        // Start with an un-evaluated query stream of Identity Users
        var query = _userManager.Users.AsNoTracking();

        // Filter: Apply text search filter against Username or Email address strings
        if (!string.IsNullOrWhiteSpace(parameters.SearchTerm))
        {
            var search = parameters.SearchTerm.Trim().ToLower();
            query = query.Where(u => u.UserName!.ToLower().Contains(search) ||
                                     u.Email!.ToLower().Contains(search));
        }

        // Sort: Pass the sorting instruction keys cleanly to our expression builder helper
        string sortColumn = string.IsNullOrWhiteSpace(parameters.SortBy) ? "Id" : parameters.SortBy;
        query = query.OrderByCustom(sortColumn, parameters.IsDescending);

        // Page: Execute pagination math directly on SQL server engine using Skip/Take via our helper
        var pagedEntities = await PagedList<User>.CreateAsync(query, parameters.PageNumber, parameters.PageSize);

        // Map the paginated Identity User entities into clean structural DTO objects
        var mappedItems = _mapper.Map<List<UserDto>>(pagedEntities.Items);

        var response = new PagedList<UserDto>(
            mappedItems,
            pagedEntities.TotalCount,
            pagedEntities.CurrentPage,
            pagedEntities.PageSize
        );

        return Ok(response);
    }


    [HttpGet("{id}")]
    public async Task<ActionResult<UserDto>> GetUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound($"User with ID '{id}' was not found.");
        }

        return Ok(_mapper.Map<UserDto>(user));
    }


    [HttpPut("{id}/toggle-status")]
    public async Task<IActionResult> ToggleUserStatus(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null)
        {
            return NotFound($"User with ID '{id}' was not found.");
        }

        // 1. Flip the manual administrative activation state flag
        user.IsActive = !user.IsActive;

        // 2. Security Protocol: If an account is deactivated, forcefully revoke all their active login refresh sessions
        if (!user.IsActive)
        {
            var activeRefreshTokens = await _context.RefreshTokens
                .Where(rt => rt.UserId == user.Id && rt.RevokedAt == null)
                .ToListAsync();

            foreach (var token in activeRefreshTokens)
            {
                token.RevokedAt = System.DateTime.UtcNow;
                token.RevokedByIp = "Administrative Account Deactivation";
            }
        }

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        string accountStateMessage = user.IsActive ? "activated" : "deactivated and forced logged out";
        return Ok(new { Message = $"User account has been successfully {accountStateMessage}." });
    }
}
