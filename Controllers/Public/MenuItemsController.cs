using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers;

public class MenuItemsController : BaseApiController
{
    private readonly AppDbContext _context;

    public MenuItemsController(AppDbContext context)
    {
        _context = context;
    }

    // ---------------- PUBLIC API ENDPOINTS ----------------

    [HttpGet]
    public async Task<IActionResult> GetActiveMenuItems([FromQuery] string? module, [FromQuery] string? location)
    {
        var query = _context.MenuItems
            .AsNoTracking()
            .Where(x => x.Status == "active");

        if (!string.IsNullOrWhiteSpace(module))
        {
            query = query.Where(x => x.Module == module);
        }

        if (!string.IsNullOrWhiteSpace(location))
        {
            query = query.Where(x => x.Location == location);
        }

        var items = await query
            .OrderBy(x => x.Order)
            .ToListAsync();

        return Ok(items);
    }

    // ---------------- ADMIN API ENDPOINTS ----------------

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpGet("admin/list")]
    public async Task<IActionResult> GetAdminMenuItems()
    {
        var items = await _context.MenuItems
            .AsNoTracking()
            .OrderBy(x => x.Module)
            .ThenBy(x => x.Location)
            .ThenBy(x => x.Order)
            .ToListAsync();

        return Ok(items);
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPost("admin")]
    public async Task<IActionResult> CreateMenuItem([FromBody] UpsertMenuItemRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
        var normalizedModule = request.Module.Trim();
        var normalizedLocation = request.Location.Trim();

        // Enforce uniqueness composite constraint check (Module, Location, Slug)
        var exists = await _context.MenuItems.AnyAsync(x =>
            x.Module == normalizedModule &&
            x.Location == normalizedLocation &&
            x.Slug == normalizedSlug);

        if (exists)
        {
            return BadRequest($"A menu item with slug '{request.Slug}' already exists for module '{request.Module}' at location '{request.Location}'.");
        }

        var menuItem = new MenuItem
        {
            Name = request.Name.Trim(),
            Slug = normalizedSlug,
            DisplayTitle = request.DisplayTitle.Trim(),
            Order = request.Order,
            Module = normalizedModule,
            Location = normalizedLocation,
            Status = request.Status,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _context.MenuItems.Add(menuItem);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Menu item created successfully.",
            menuItemId = menuItem.Id
        });
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpPut("admin/{id:long}")]
    public async Task<IActionResult> UpdateMenuItem(long id, [FromBody] UpsertMenuItemRequest request)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest(ModelState);
        }

        var menuItem = await _context.MenuItems.FirstOrDefaultAsync(x => x.Id == id);
        if (menuItem == null)
        {
            return NotFound("Menu item not found.");
        }

        var normalizedSlug = request.Slug.Trim().ToLowerInvariant();
        var normalizedModule = request.Module.Trim();
        var normalizedLocation = request.Location.Trim();

        // Enforce uniqueness composite constraint check (Module, Location, Slug)
        var exists = await _context.MenuItems.AnyAsync(x =>
            x.Id != id &&
            x.Module == normalizedModule &&
            x.Location == normalizedLocation &&
            x.Slug == normalizedSlug);

        if (exists)
        {
            return BadRequest($"A menu item with slug '{request.Slug}' already exists for module '{request.Module}' at location '{request.Location}'.");
        }

        menuItem.Name = request.Name.Trim();
        menuItem.Slug = normalizedSlug;
        menuItem.DisplayTitle = request.DisplayTitle.Trim();
        menuItem.Order = request.Order;
        menuItem.Module = normalizedModule;
        menuItem.Location = normalizedLocation;
        menuItem.Status = request.Status;
        menuItem.UpdatedAtUtc = DateTime.UtcNow;

        await _context.SaveChangesAsync();

        return Ok(new
        {
            message = "Menu item updated successfully.",
            menuItemId = menuItem.Id
        });
    }

    [Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
    [HttpDelete("admin/{id:long}")]
    public async Task<IActionResult> DeleteMenuItem(long id)
    {
        var menuItem = await _context.MenuItems.FirstOrDefaultAsync(x => x.Id == id);
        if (menuItem == null)
        {
            return NotFound("Menu item not found.");
        }

        _context.MenuItems.Remove(menuItem);
        await _context.SaveChangesAsync();

        return Ok("Menu item deleted successfully.");
    }
}
