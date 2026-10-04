using EventImageServer.Contexts;
using EventImageServer.Models;
using EventImageServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

[Route("[controller]")]
[ApiController]
[Authorize]
public class BudgetController : ControllerBase
{
    private readonly AppDbContext _dbContext;
    private readonly EventOwnerResolver _ownerResolver;

    public BudgetController(AppDbContext dbContext, EventOwnerResolver ownerResolver)
    {
        _dbContext = dbContext;
        _ownerResolver = ownerResolver;
    }

    // Loads the current user and verifies they are an EventOwner, delegating
    // the auto-provisioning/role-check logic to the shared EventOwnerResolver
    // (also used by SeatingController and VendorsController).
    private async Task<(Users? Owner, IActionResult? Error)> RequireEventOwnerAsync()
    {
        var resolution = await _ownerResolver.ResolveAsync(User, "Only EventOwners manage budgets.");
        if (resolution.Owner == null)
        {
            return (null, StatusCode(resolution.ErrorStatusCode!.Value, new { message = resolution.ErrorMessage }));
        }

        if (resolution.IsReadOnlyViewer && !HttpMethods.IsGet(Request.Method) && !HttpMethods.IsHead(Request.Method))
        {
            return (null, StatusCode(403, new { message = "Viewers have read-only access." }));
        }

        return (resolution.Owner, null);
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
        var (owner, _) = await RequireEventOwnerAsync();
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
            var (owner, error) = await RequireEventOwnerAsync();
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

    // PUT /Budget — update total budget
    [HttpPut]
    public async Task<IActionResult> UpdateTotal([FromBody] UpdateBudgetDto request)
    {
            var (owner, error) = await RequireEventOwnerAsync();
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

    // POST /Budget/categories — create a new category
    [HttpPost("categories")]
    public async Task<IActionResult> CreateCategory([FromBody] BudgetCategoryDto request)
    {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Category name is required" });
            }

            var (owner, error) = await RequireEventOwnerAsync();
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

    // PUT /Budget/categories/{id} — update a category
    [HttpPut("categories/{id}")]
    public async Task<IActionResult> UpdateCategory(string id, [FromBody] BudgetCategoryDto request)
    {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Category name is required" });
            }

            var (owner, error) = await RequireEventOwnerAsync();
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

    // DELETE /Budget/categories/{id} — delete a category (cascade deletes expenses)
    [HttpDelete("categories/{id}")]
    public async Task<IActionResult> DeleteCategory(string id)
    {
            var (owner, error) = await RequireEventOwnerAsync();
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

    // POST /Budget/expenses — create a new expense
    [HttpPost("expenses")]
    public async Task<IActionResult> CreateExpense([FromBody] BudgetExpenseDto request)
    {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Expense name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.CategoryId))
            {
                return BadRequest(new { message = "Category ID is required" });
            }

            var (owner, error) = await RequireEventOwnerAsync();
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

    // PUT /Budget/expenses/{id} — update an expense
    [HttpPut("expenses/{id}")]
    public async Task<IActionResult> UpdateExpense(string id, [FromBody] BudgetExpenseDto request)
    {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest(new { message = "Expense name is required" });
            }

            if (string.IsNullOrWhiteSpace(request.CategoryId))
            {
                return BadRequest(new { message = "Category ID is required" });
            }

            var (owner, error) = await RequireEventOwnerAsync();
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

    // DELETE /Budget/expenses/{id} — delete an expense
    [HttpDelete("expenses/{id}")]
    public async Task<IActionResult> DeleteExpense(string id)
    {
            var (owner, error) = await RequireEventOwnerAsync();
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
}
