using EventImageServer.Contexts;
using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class TasksController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly EventOwnerResolver _ownerResolver;

    public TasksController(AppDbContext dbContext, EventOwnerResolver ownerResolver)
    {
        _dbContext = dbContext;
        _ownerResolver = ownerResolver;
    }

    private Users? RequireEventOwner(out IActionResult? errorResult)
    {
        var resolution = _ownerResolver.Resolve(User, "Only EventOwners manage a planning checklist.");
        if (resolution.Owner == null)
        {
            errorResult = StatusCode(resolution.ErrorStatusCode!.Value, new { message = resolution.ErrorMessage });
            return null;
        }

        if (resolution.IsReadOnlyViewer && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
        {
            errorResult = StatusCode(403, new { message = "Viewers have read-only access." });
            return null;
        }

        errorResult = null;
        return resolution.Owner;
    }

    public class TaskRequest
    {
        public string Title { get; set; } = string.Empty;
        public string? Notes { get; set; }
        public DateTime? DueDate { get; set; }
        public int? VendorId { get; set; }
    }

    // Default checklist offsets (months before the wedding day) used to seed
    // a new owner's task list the first time they load /Tasks, if EventDate
    // is set and they have no tasks yet.
    private static readonly (int MonthsBefore, string Title)[] DefaultChecklist = new[]
    {
        (12, "Book venue"),
        (12, "Set guest list draft"),
        (9, "Book photographer & videographer"),
        (9, "Book catering / menu tasting"),
        (6, "Order invitations"),
        (6, "Book entertainment / DJ"),
        (3, "Send invitations"),
        (3, "Finalize seating plan"),
        (1, "Confirm final headcount with vendors"),
        (1, "Confirm seating chart and table names"),
    };

    // Lazily seeds the default checklist (once) if the owner has an
    // EventDate set and no tasks yet — mirrors BudgetController's
    // GetOrCreateBudgetAsync lazy-provisioning pattern.
    private async Task SeedDefaultChecklistIfNeeded(Users owner)
    {
        if (owner.EventDate == null)
        {
            return;
        }

        var hasTasks = await _dbContext.PlanningTasks.AnyAsync(t => t.OwnerId == owner.Id);
        if (hasTasks)
        {
            return;
        }

        var sortOrder = 0;
        foreach (var (monthsBefore, title) in DefaultChecklist)
        {
            _dbContext.PlanningTasks.Add(new PlanningTask
            {
                OwnerId = owner.Id!,
                Title = title,
                DueDate = owner.EventDate.Value.AddMonths(-monthsBefore),
                SortOrder = sortOrder++,
            });
        }

        await _dbContext.SaveChangesAsync();
    }

    // GET /Tasks
    [HttpGet]
    public async Task<IActionResult> GetTasks()
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            await SeedDefaultChecklistIfNeeded(owner);

            var tasks = await _dbContext.PlanningTasks
                .Where(t => t.OwnerId == owner.Id)
                .OrderBy(t => t.IsDone)
                .ThenBy(t => t.SortOrder)
                .ThenBy(t => t.DueDate)
                .ToListAsync();

            return Ok(tasks);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving tasks", error = e.Message });
        }
    }

    // POST /Tasks
    [HttpPost]
    public async Task<IActionResult> CreateTask([FromBody] TaskRequest request)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { message = "Title is required." });
            }

            var maxSortOrder = await _dbContext.PlanningTasks
                .Where(t => t.OwnerId == owner.Id)
                .Select(t => (int?)t.SortOrder)
                .MaxAsync() ?? -1;

            var task = new PlanningTask
            {
                OwnerId = owner.Id!,
                Title = request.Title.Trim(),
                Notes = request.Notes,
                DueDate = request.DueDate,
                VendorId = request.VendorId,
                SortOrder = maxSortOrder + 1,
            };

            _dbContext.PlanningTasks.Add(task);
            await _dbContext.SaveChangesAsync();

            return Ok(task);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error creating task", error = e.Message });
        }
    }

    // PUT /Tasks/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTask(int id, [FromBody] TaskRequest request)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var task = await _dbContext.PlanningTasks.FirstOrDefaultAsync(t => t.TaskId == id && t.OwnerId == owner.Id);
            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest(new { message = "Title is required." });
            }

            task.Title = request.Title.Trim();
            task.Notes = request.Notes;
            task.DueDate = request.DueDate;
            task.VendorId = request.VendorId;

            await _dbContext.SaveChangesAsync();

            return Ok(task);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error updating task", error = e.Message });
        }
    }

    // PATCH /Tasks/{id}/Done { isDone }
    [HttpPatch("{id}/Done")]
    public async Task<IActionResult> SetTaskDone(int id, [FromBody] SetDoneRequest request)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var task = await _dbContext.PlanningTasks.FirstOrDefaultAsync(t => t.TaskId == id && t.OwnerId == owner.Id);
            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            task.IsDone = request.IsDone;
            task.CompletedAt = request.IsDone ? DateTime.UtcNow : null;

            await _dbContext.SaveChangesAsync();

            return Ok(task);
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error updating task", error = e.Message });
        }
    }

    public class SetDoneRequest
    {
        public bool IsDone { get; set; }
    }

    // DELETE /Tasks/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTask(int id)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var task = await _dbContext.PlanningTasks.FirstOrDefaultAsync(t => t.TaskId == id && t.OwnerId == owner.Id);
            if (task == null)
            {
                return NotFound(new { message = "Task not found." });
            }

            _dbContext.PlanningTasks.Remove(task);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Deleted." });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error deleting task", error = e.Message });
        }
    }
}
