using AutoMapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using TaskFlowAPI.Contexts;
using TaskFlowAPI.DTOs;
using TaskFlowAPI.Models;
using TaskFlowAPI.Helpers;

namespace TaskFlowAPI.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IMapper _mapper;
    private readonly string _storagePath;

    public AttachmentsController(AppDbContext context, IMapper mapper)
    {
        _context = context;
        _mapper = mapper;

        // Define directory root path pointing entirely inside a subfolder framework on local host
        _storagePath = Path.Combine(Directory.GetCurrentDirectory(), "Storage", "Attachments");
        if (!Directory.Exists(_storagePath))
        {
            Directory.CreateDirectory(_storagePath);
        }
    }

    // POST: api/attachments/upload/{taskId}

    private static readonly long MaxFileSizeInBytes = 5 * 1024 * 1024; // Strict 5MB file ceiling
    private static readonly string[] AllowedExtensions = { ".pdf", ".png", ".jpg", ".jpeg", ".docx", ".xlsx" };

    [HttpPost("upload/{taskId}")]
    [Consumes("multipart/form-data")]
    public async Task<ActionResult<AttachmentFileDto>> UploadFile(int taskId, IFormFile file)
    {
        if (file == null || file.Length == 0) return BadRequest("No file payload data was provided.");

        // 1. HARDEN SIZE: Enforce max payload limit barriers
        if (file.Length > MaxFileSizeInBytes) return BadRequest("File size exceeds the maximum limit of 5MB.");

        // 2. HARDEN TYPE: Enforce explicit extension boundaries
        var originalName = Path.GetFileName(file.FileName);
        var fileExtension = Path.GetExtension(originalName).ToLower();
        if (!AllowedExtensions.Contains(fileExtension)) return BadRequest($"Unsupported file extension. Allowed types: {string.Join(", ", AllowedExtensions)}");

        if (!await _context.Tasks.AnyAsync(t => t.Id == taskId)) return NotFound($"Parent Task with ID {taskId} does not exist.");

        var uniqueStorageName = $"{Guid.NewGuid()}{fileExtension}";
        var physicalSaveLocation = Path.Combine(_storagePath, uniqueStorageName);

        // 3. SECURE INTEGRITY: Save binary to disk first
        await using (var stream = new FileStream(physicalSaveLocation, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        var attachmentRecord = new AttachmentFile
        {
            OriginalFileName = originalName,
            StorageFileName = uniqueStorageName,
            FilePath = physicalSaveLocation,
            ContentType = file.ContentType,
            FileSizeInBytes = file.Length,
            TaskId = taskId
        };

        _context.Attachments.Add(attachmentRecord);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (Exception)
        {
            // HARDEN ORPHANS: If saving the DB record fails, delete the physical disk file immediately to prevent orphan trash leak build-up
            if (System.IO.File.Exists(physicalSaveLocation))
            {
                System.IO.File.Delete(physicalSaveLocation);
            }
            throw; // Re-throw to be cleanly captured by global exception middleware
        }

        return Ok(_mapper.Map<AttachmentFileDto>(attachmentRecord));
    }


    // GET: api/attachments/download/{id}

    [HttpGet("download/{id}")]
    public async Task<IActionResult> DownloadFile(int id)
    {
        var attachment = await _context.Attachments.Include(a => a.Task).FirstOrDefaultAsync(a => a.Id == id);
        if (attachment == null || attachment.Task == null) return NotFound();

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Verify user is linked to the parent task ecosystem before serving the file stream asset
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Manager) && attachment.CreatedBy != currentUserId && attachment.Task.AssignedToId != currentUserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access Denied: You do not have permissions to download this file asset." });
        }

        var memoryStream = new MemoryStream();
        await using (var stream = new FileStream(attachment.FilePath, FileMode.Open))
        {
            await stream.CopyToAsync(memoryStream);
        }
        memoryStream.Position = 0;

        return File(memoryStream, attachment.ContentType, attachment.OriginalFileName);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAttachment(int id)
    {
        var attachment = await _context.Attachments.FindAsync(id);
        if (attachment == null) return NotFound();

        var currentUserId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        // Only allow the original file uploader, a Manager, or an Admin to wipe the physical asset
        if (!User.IsInRole(AppRoles.Admin) && !User.IsInRole(AppRoles.Manager) && attachment.CreatedBy != currentUserId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { Message = "Access Denied: You cannot delete an file attachment asset uploaded by another team member." });
        }

        if (System.IO.File.Exists(attachment.FilePath)) System.IO.File.Delete(attachment.FilePath);

        _context.Attachments.Remove(attachment);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
