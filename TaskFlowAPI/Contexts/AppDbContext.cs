using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TaskFlowAPI.Models;
using TaskFlowAPI.Interfaces;

namespace TaskFlowAPI.Contexts;

public class AppDbContext : IdentityDbContext<User>
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public DbSet<Project> Projects { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<ProjectTask> Tasks { get; set; }
    public DbSet<Comment> Comments { get; set; }
    public DbSet<AttachmentFile> Attachments { get; set; }
    public DbSet<ProjectMember> ProjectMembers { get; set; }
    public DbSet<RefreshToken> RefreshTokens { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options, IHttpContextAccessor httpContextAccessor)
        : base(options)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ==========================================
        // 1. PROJECT CONFIGURATION
        // ==========================================
        modelBuilder.Entity<Project>(entity =>
        {
            entity.Property(p => p.Name).IsRequired().HasMaxLength(150);
            entity.Property(p => p.Description).HasMaxLength(1000); // Fixed: Added limit

            entity.HasMany(p => p.Tasks)
                .WithOne(t => t.Project)
                .HasForeignKey(t => t.ProjectId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ==========================================
        // 2. CATEGORY CONFIGURATION (Filtered Unique Index)
        // ==========================================
        modelBuilder.Entity<Category>(entity =>
        {
            entity.Property(c => c.Name).IsRequired().HasMaxLength(50);

            // Fixed: Filtered index ignores soft-deleted items so names can be recycled!
            entity.HasIndex(c => c.Name)
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            entity.HasMany(c => c.Tasks)
                .WithOne(t => t.Category)
                .HasForeignKey(t => t.CategoryId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // 3. TASK CONFIGURATION
        // ==========================================
        modelBuilder.Entity<ProjectTask>(entity =>
        {
            entity.Property(t => t.Title).IsRequired().HasMaxLength(200);
            entity.Property(t => t.Description).HasMaxLength(4000); // Fixed: Added limit

            // Fixed: Save enums as human-readable strings instead of arbitrary integer numbers
            entity.Property(t => t.Status)
                .HasConversion<string>()
                .HasMaxLength(30);

            entity.Property(t => t.Priority)
                .HasConversion<string>()
                .HasMaxLength(20);

            entity.HasMany(t => t.Comments)
                .WithOne(c => c.Task)
                .HasForeignKey(c => c.TaskId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasMany(t => t.Attachments)
                .WithOne(a => a.Task)
                .HasForeignKey(a => a.TaskId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ==========================================
        // 4. USER INVERSE RELATIONSHIPS
        // ==========================================
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasMany(u => u.AssignedTasks)
                .WithOne(t => t.AssignedTo)
                .HasForeignKey(t => t.AssignedToId)
                .IsRequired(false); // Fixed: Explicitly marked optional in DB
        });

        // ==========================================
        // 5. COMMENT CONFIGURATION
        // ==========================================
        modelBuilder.Entity<Comment>(entity =>
        {
            entity.Property(c => c.Content).IsRequired().HasMaxLength(2000);

            entity.HasOne(c => c.Author)
                .WithMany(u => u.AuthoredComments)
                .HasForeignKey(c => c.AuthorId)
                .IsRequired()
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ==========================================
        // 6. ATTACHMENTFILE CONFIGURATION
        // ==========================================
        modelBuilder.Entity<AttachmentFile>(entity =>
        {
            entity.Property(a => a.OriginalFileName).IsRequired().HasMaxLength(255);
            entity.Property(a => a.StorageFileName).IsRequired().HasMaxLength(255);
            entity.Property(a => a.FilePath).IsRequired().HasMaxLength(2048);
            entity.Property(a => a.ContentType).IsRequired().HasMaxLength(100);
        });

        // ==========================================
        // 7. REFRESH TOKEN CONFIGURATION
        // ==========================================
        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.Property(rt => rt.TokenHash).IsRequired().HasMaxLength(64);
            entity.Property(rt => rt.CreatedByIp).IsRequired().HasMaxLength(45);
            entity.Property(rt => rt.RevokedByIp).HasMaxLength(45);

            entity.HasIndex(rt => rt.TokenHash).IsUnique();
        });

        // ==========================================
        // 8. MANY-TO-MANY JOIN TABLE CONFIGURATION
        // ==========================================
        modelBuilder.Entity<ProjectMember>(entity =>
        {
            entity.HasKey(pm => new { pm.ProjectId, pm.UserId });
            entity.Property(pm => pm.Role).HasMaxLength(50);

            entity.HasOne(pm => pm.Project)
                .WithMany(p => p.ProjectMembers)
                .HasForeignKey(pm => pm.ProjectId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(pm => pm.User)
                .WithMany(u => u.ProjectMembers)
                .HasForeignKey(pm => pm.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ==========================================
        // 9. AUTOMATED AUDIT & CONCURRENCY LOOP
        // ==========================================
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(ISoftDelete).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .HasQueryFilter(ConvertFilterExpression(entityType.ClrType));
            }

            if (typeof(BaseEntity).IsAssignableFrom(entityType.ClrType))
            {
                modelBuilder.Entity(entityType.ClrType)
                    .Property(nameof(BaseEntity.RowVersion))
                    .IsRowVersion();
            }
        }
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var currentUserId = _httpContextAccessor.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                            ?? "System";

        // EXTRACT ENTRIES TO ARRAY: Avoids collections mutating mid-execution loop
        var entries = ChangeTracker.Entries().ToList();

        // Handle soft deletion state conversions first
        foreach (var entry in entries)
        {
            if (entry.Entity is ISoftDelete softDelete && entry.State == EntityState.Deleted)
            {
                // Intercept hard drop statement and change it to a modified state
                entry.State = EntityState.Modified;
                softDelete.IsDeleted = true;
                softDelete.DeletedAt = DateTime.UtcNow;

                // CASCADE WORKFLOW: Handle nested Child ProjectTask records automatically
                if (entry.Entity is Project project)
                {
                    // Pull down any associated tasks under this project currently tracked or in the db
                    var linkedTasks = Tasks.Where(t => t.ProjectId == project.Id && !t.IsDeleted).ToList();
                    foreach (var task in linkedTasks)
                    {
                        task.IsDeleted = true;
                        task.DeletedAt = DateTime.UtcNow;
                        task.UpdatedAt = DateTime.UtcNow;
                        task.UpdatedBy = currentUserId;

                        // Deep-Cascade: Push down to soft-delete the comments on these cascading tasks too
                        var linkedComments = Comments.Where(c => c.TaskId == task.Id && !c.IsDeleted).ToList();
                        foreach (var comment in linkedComments)
                        {
                            comment.IsDeleted = true;
                            comment.DeletedAt = DateTime.UtcNow;
                            comment.UpdatedAt = DateTime.UtcNow;
                            comment.UpdatedBy = currentUserId;
                        }
                    }
                }

                // CASCADE WORKFLOW: Soft-deleting an isolated Task drops its comments
                if (entry.Entity is ProjectTask singleTask)
                {
                    var linkedComments = Comments.Where(c => c.TaskId == singleTask.Id && !c.IsDeleted).ToList();
                    foreach (var comment in linkedComments)
                    {
                        comment.IsDeleted = true;
                        comment.DeletedAt = DateTime.UtcNow;
                        comment.UpdatedAt = DateTime.UtcNow;
                        comment.UpdatedBy = currentUserId;
                    }
                }
            }
        }

        // Log Auditable metrics on final state models (Added or Modified)
        foreach (var entry in ChangeTracker.Entries())
        {
            if (entry.Entity is IAuditableEntity auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.CreatedAt = DateTime.UtcNow;
                    auditable.CreatedBy = currentUserId;
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.UpdatedAt = DateTime.UtcNow;
                    auditable.UpdatedBy = currentUserId;
                }
            }
        }

        return await base.SaveChangesAsync(cancellationToken);
    }


    private static System.Linq.Expressions.LambdaExpression ConvertFilterExpression(Type type)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(type, "e");
        var property = System.Linq.Expressions.Expression.Property(parameter, nameof(ISoftDelete.IsDeleted));
        var notExpression = System.Linq.Expressions.Expression.Not(property);
        return System.Linq.Expressions.Expression.Lambda(notExpression, parameter);
    }
}
