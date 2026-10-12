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

    [HttpPost("upload/{taskId}")]
    [Consumes("multipart/form-data")] // Explicitly binds file data payload pipeline context streams
    public async Task<ActionResult<AttachmentFileDto>> UploadFile(int taskId, IFormFile file)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest("No file payload data was provided.");
        }

        if (!await _context.Tasks.AnyAsync(t => t.Id == taskId))
        {
            return NotFound($"Parent Task with ID {taskId} does not exist.");
        }

        // DISAMBIGUATE FILENAMES: Extract original display token text string
        var originalName = Path.GetFileName(file.FileName);
        var fileExtension = Path.GetExtension(originalName);

        // GENERATE STORAGE UNIQUE KEY: Prevents name collisions entirely
        var uniqueStorageName = $"{Guid.NewGuid()}{fileExtension}";
        var physicalSaveLocation = Path.Combine(_storagePath, uniqueStorageName);

        // COMIT STREAM TO DISK
        using (var stream = new FileStream(physicalSaveLocation, FileMode.Create))
        {
            await file.CopyToAsync(stream);
        }

        // MAP TO DATABSE ENTITY RECORD BOUNDARIES
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
        await _context.SaveChangesAsync();

        return Ok(_mapper.Map<AttachmentFileDto>(attachmentRecord));
    }

    // GET: api/attachments/download/{id}

    [HttpGet("download/{id}")]
    public async Task<IActionResult> DownloadFile(int id)
    {
        var attachment = await _context.Attachments.FindAsync(id);
        if (attachment == null)
        {
            return NotFound($"Attachment with ID {id} was not found.");
        }

        if (!System.IO.File.Exists(attachment.FilePath))
        {
            return NotFound("The physical file is missing from backend file infrastructure storage repositories.");
        }

        var memoryStream = new MemoryStream();
        using (var stream = new FileStream(attachment.FilePath, FileMode.Open))
        {
            await stream.CopyToAsync(memoryStream);
        }
        memoryStream.Position = 0;

        // Serves file back to browser seamlessly streaming its original user display name back into place
        return File(memoryStream, attachment.ContentType, attachment.OriginalFileName);
    }

    // DELETE: api/attachments/{id}

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAttachment(int id)
    {
        var attachment = await _context.Attachments.FindAsync(id);
        if (attachment == null)
        {
            return NotFound($"Attachment with ID {id} was not found.");
        }

        // 1. Remove Physical binary file off server disk memory assets first
        if (System.IO.File.Exists(attachment.FilePath))
        {
            System.IO.File.Delete(attachment.FilePath);
        }

        // 2. Remove meta record context out of table map metrics
        _context.Attachments.Remove(attachment);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}
