using EventImageServer.Contexts;
using EventImageServer.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly AppDbContext _dbContext;

    public BudgetController(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    private string GetUID()
    {
        var user = User.FindFirst("user_id");
        if (user == null)
        {
            return string.Empty;
        }
        return user.Value;
    }

    private Users? RequireEventOwner(out IActionResult? errorResult)
    {
        var userId = GetUID();
        if (string.IsNullOrEmpty(userId))
        {
            errorResult = Unauthorized(new { message = "Invalid token, UID not found." });
            return null;
        }

        var user = _dbContext.Clients.FirstOrDefault(u => u.Id == userId);
        if (user == null)
        {
            user = new Users
            {
                Id = userId,
                Email = User.FindFirst("email")?.Value,
                FullName = User.FindFirst("name")?.Value,
                Role = RoleType.EventOwner
            };
            _dbContext.Clients.Add(user);
            _dbContext.SaveChanges();
        }

        if (user.Role != RoleType.EventOwner)
        {
            errorResult = StatusCode(403, new { message = "Only EventOwners manage budgets." });
            return null;
        }

        errorResult = null;
        return user;
    }

    // Request DTOs
    public class UpdateBudgetDto
    {
        public decimal TotalBudget { get; set; }
    }

    public class BudgetCategoryDto
    {
        public string Name { get; set; } = string.Empty;
        public decimal PlannedAmount { get; set; }
        public int? LinkedVendorCategory { get; set; }
    }

    public class BudgetExpenseDto
    {
        public string Name { get; set; } = string.Empty;
        public string CategoryId { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal PaidAmount { get; set; }
        public DateTime? DueDate { get; set; }
        public string? VendorId { get; set; }
    }

    // Helper: Get or create budget for the current user
    private async Task<Budget> GetOrCreateBudgetAsync()
    {
        var owner = RequireEventOwner(out _);
        if (owner == null)
        {
            throw new InvalidOperationException("User not authenticated.");
        }

        var userId = owner.Id!;
        var budget = await _dbContext.Budgets
            .Include(b => b.Categories)
            .Include(b => b.Expenses)
            .FirstOrDefaultAsync(b => b.UserId == userId);

        if (budget == null)
        {
            budget = new Budget { UserId = userId, TotalBudget = 0 };
            _dbContext.Budgets.Add(budget);
            await _dbContext.SaveChangesAsync();
        }

        return budget;
    }

    // GET /Budget — returns the full budget (never null)
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();
            return Ok(new
            {
                totalBudget = budget.TotalBudget,
                categories = budget.Categories.Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    plannedAmount = c.PlannedAmount,
                    linkedVendorCategory = c.LinkedVendorCategory
                }).ToList(),
                expenses = budget.Expenses.Select(e => new
                {
                    id = e.Id,
                    name = e.Name,
                    categoryId = e.CategoryId,
                    amount = e.Amount,
                    paidAmount = e.PaidAmount,
                    dueDate = e.DueDate,
                    vendorId = e.VendorId
                }).ToList()
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error retrieving budget", error = e.Message });
        }
    }

    // PUT /Budget — update total budget
    [HttpPut]
    public async Task<IActionResult> UpdateTotal([FromBody] UpdateBudgetDto request)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();
            budget.TotalBudget = request.TotalBudget;
            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                totalBudget = budget.TotalBudget,
                categories = budget.Categories.Select(c => new
                {
                    id = c.Id,
                    name = c.Name,
                    plannedAmount = c.PlannedAmount,
                    linkedVendorCategory = c.LinkedVendorCategory
                }).ToList(),
                expenses = budget.Expenses.Select(e => new
                {
                    id = e.Id,
                    name = e.Name,
                    categoryId = e.CategoryId,
                    amount = e.Amount,
                    paidAmount = e.PaidAmount,
                    dueDate = e.DueDate,
                    vendorId = e.VendorId
                }).ToList()
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error updating budget", error = e.Message });
        }
    }

    // POST /Budget/categories — create a new category
    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] BudgetCategoryDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Category name is required" });
            }

            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();

            var category = new BudgetCategory
            {
                BudgetId = budget.Id,
                Name = request.Name,
                PlannedAmount = request.PlannedAmount,
                LinkedVendorCategory = request.LinkedVendorCategory
            };

            _dbContext.BudgetCategories.Add(category);
            await _dbContext.SaveChangesAsync();

            return StatusCode(201, new
            {
                id = category.Id,
                name = category.Name,
                plannedAmount = category.PlannedAmount,
                linkedVendorCategory = category.LinkedVendorCategory
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error creating category", error = e.Message });
        }
    }

    // PUT /Budget/categories/{id} — update a category
    [HttpPut("categories/{id}")]
    public async Task<IActionResult> UpdateCategory(string id, [FromBody] BudgetCategoryDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Category name is required" });
            }

            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();
            var category = budget.Categories.FirstOrDefault(c => c.Id == id);

            if (category == null)
            {
                return NotFound(new { message = "Category not found" });
            }

            category.Name = request.Name;
            category.PlannedAmount = request.PlannedAmount;
            category.LinkedVendorCategory = request.LinkedVendorCategory;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                id = category.Id,
                name = category.Name,
                plannedAmount = category.PlannedAmount,
                linkedVendorCategory = category.LinkedVendorCategory
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error updating category", error = e.Message });
        }
    }

    // DELETE /Budget/categories/{id} — delete a category (cascade deletes expenses)
    [HttpDelete("categories/{id}")]
    public async Task<IActionResult> DeleteCategory(string id)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();
            var category = budget.Categories.FirstOrDefault(c => c.Id == id);

            if (category == null)
            {
                return NotFound(new { message = "Category not found" });
            }

            _dbContext.BudgetCategories.Remove(category);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Category deleted" });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error deleting category", error = e.Message });
        }
    }

    // POST /Budget/expenses — create a new expense
    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] BudgetExpenseDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Expense name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.CategoryId))
            {
                return BadRequest(new { message = "Category ID is required" });
            }

            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();

            // Verify the category belongs to this budget
            var category = budget.Categories.FirstOrDefault(c => c.Id == request.CategoryId);
            if (category == null)
            {
                return BadRequest(new { message = "Category not found" });
            }

            var expense = new BudgetExpense
            {
                BudgetId = budget.Id,
                Name = request.Name,
                CategoryId = request.CategoryId,
                Amount = request.Amount,
                PaidAmount = request.PaidAmount,
                DueDate = request.DueDate,
                VendorId = request.VendorId
            };

            _dbContext.BudgetExpenses.Add(expense);
            await _dbContext.SaveChangesAsync();

            return StatusCode(201, new
            {
                id = expense.Id,
                name = expense.Name,
                categoryId = expense.CategoryId,
                amount = expense.Amount,
                paidAmount = expense.PaidAmount,
                dueDate = expense.DueDate,
                vendorId = expense.VendorId
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error creating expense", error = e.Message });
        }
    }

    // PUT /Budget/expenses/{id} — update an expense
    [HttpPut("expenses/{id}")]
    public async Task<IActionResult> UpdateExpense(string id, [FromBody] BudgetExpenseDto request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Expense name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.CategoryId))
            {
                return BadRequest(new { message = "Category ID is required" });
            }

            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();

            // Verify the category exists in this budget
            var category = budget.Categories.FirstOrDefault(c => c.Id == request.CategoryId);
            if (category == null)
            {
                return BadRequest(new { message = "Category not found" });
            }

            var expense = budget.Expenses.FirstOrDefault(e => e.Id == id);
            if (expense == null)
            {
                return NotFound(new { message = "Expense not found" });
            }

            expense.Name = request.Name;
            expense.CategoryId = request.CategoryId;
            expense.Amount = request.Amount;
            expense.PaidAmount = request.PaidAmount;
            expense.DueDate = request.DueDate;
            expense.VendorId = request.VendorId;

            await _dbContext.SaveChangesAsync();

            return Ok(new
            {
                id = expense.Id,
                name = expense.Name,
                categoryId = expense.CategoryId,
                amount = expense.Amount,
                paidAmount = expense.PaidAmount,
                dueDate = expense.DueDate,
                vendorId = expense.VendorId
            });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error updating expense", error = e.Message });
        }
    }

    // DELETE /Budget/expenses/{id} — delete an expense
    [HttpDelete("expenses/{id}")]
    public async Task<IActionResult> DeleteExpense(string id)
    {
        try
        {
            var owner = RequireEventOwner(out var error);
            if (owner == null)
            {
                return error!;
            }

            var budget = await GetOrCreateBudgetAsync();
            var expense = budget.Expenses.FirstOrDefault(e => e.Id == id);

            if (expense == null)
            {
                return NotFound(new { message = "Expense not found" });
            }

            _dbContext.BudgetExpenses.Remove(expense);
            await _dbContext.SaveChangesAsync();

            return Ok(new { message = "Expense deleted" });
        }
        catch (Exception e)
        {
            return StatusCode(500, new { message = "Error deleting expense", error = e.Message });
        }
    }
}
