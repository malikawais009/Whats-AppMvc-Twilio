using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using WhatsAppMvcComplete.Data;
using WhatsAppMvcComplete.Models;

namespace WhatsAppMvcComplete.Controllers;

public class UsersController : Controller
{
    private readonly AppDbContext _db;

    public UsersController(AppDbContext db)
    {
        _db = db;
    }

    public async Task<IActionResult> Index()
    {
        var users = await _db.Users
            .Include(u => u.Messages)
            .OrderBy(u => u.Name)
            .ToListAsync();

        return View(users);
    }

    public IActionResult Create()
    {
        return View();
    }

    [HttpPost]
    public async Task<IActionResult> Create(string name, string phone, string? whatsappNumber, string? email)
    {
        try
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(phone))
            {
                TempData["Error"] = "Name and phone are required";
                return View();
            }

            var user = new User
            {
                Name = name,
                Phone = phone,
                WhatsAppNumber = whatsappNumber,
                Email = email,
                CreatedAt = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            TempData["Success"] = "User created successfully";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to create user: {ex.Message}";
            return View();
        }
    }

    public async Task<IActionResult> Edit(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        return View(user);
    }

    [HttpPost]
    public async Task<IActionResult> Edit(int id, string name, string phone, string? whatsappNumber, string? email)
    {
        try
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            user.Name = name;
            user.Phone = phone;
            user.WhatsAppNumber = whatsappNumber;
            user.Email = email;

            await _db.SaveChangesAsync();

            TempData["Success"] = "User updated successfully";
            return RedirectToAction(nameof(Index));
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to update user: {ex.Message}";
            return View();
        }
    }

    public async Task<IActionResult> Details(int id)
    {
        var user = await _db.Users
            .Include(u => u.Messages)
            .ThenInclude(m => m.Logs)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        return View(user);
    }

    [HttpPost]
    public async Task<IActionResult> Delete(int id)
    {
        try
        {
            var user = await _db.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            _db.Users.Remove(user);
            await _db.SaveChangesAsync();

            TempData["Success"] = "User deleted successfully";
        }
        catch (Exception ex)
        {
            TempData["Error"] = $"Failed to delete user: {ex.Message}";
        }

        return RedirectToAction(nameof(Index));
    }
}
