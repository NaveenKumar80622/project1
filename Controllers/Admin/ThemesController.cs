using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers.Admin
{
    [Route("api/admin/themes")]
    public class ThemesController : AdminApiController
    {
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _environment;
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        public ThemesController(AppDbContext context, IWebHostEnvironment environment)
        {
            _context = context;
            _environment = environment;
        }

        private async Task EnsureDefaultThemesAndConfigsAsync()
        {
            // 1. Ensure Default Theme
            if (!await _context.Themes.AnyAsync())
            {
                var defaultTheme = new Theme
                {
                    Id = "theme-1",
                    Name = "Atlas Default",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    VariablesJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        { "primaryColor", "#dc1e26" },
                        { "primaryStrongColor", "#b8141b" },
                        { "pageBgColor", "#F3F4F6" },
                        { "surfaceColor", "#ffffff" },
                        { "textColor", "#162126" },
                        { "borderColor", "#E5E7EB" }
                    })
                };
                _context.Themes.Add(defaultTheme);
            }

            // 2. Ensure Header Config
            if (!await _context.ThemeConfigs.AnyAsync(x => x.Key == "header"))
            {
                var headerConfig = new ThemeConfig
                {
                    Key = "header",
                    ValueJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        { "logoUrl", "https://cdn.example.com/assets/logo.png" },
                        { "bgColor", "#ffffff" },
                        { "textColor", "#162126" },
                        { "navHoverColor", "#dc1e26" },
                        { "layoutType", "default" }
                    })
                };
                _context.ThemeConfigs.Add(headerConfig);
            }

            // 3. Ensure Home Config
            if (!await _context.ThemeConfigs.AnyAsync(x => x.Key == "home"))
            {
                var homeConfig = new ThemeConfig
                {
                    Key = "home",
                    ValueJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        { "heroBackgroundImageUrl", "https://cdn.example.com/assets/banner.jpg" },
                        { "heroTitle", "Explore the World Together" },
                        { "heroSubtitle", "Find your perfect destination" },
                        { "heroOverlayColor", "rgba(0, 0, 0, 0.4)" },
                        { "searchCardStyle", "glassmorphic" }
                    })
                };
                _context.ThemeConfigs.Add(homeConfig);
            }

            // 4. Ensure Footer Config
            if (!await _context.ThemeConfigs.AnyAsync(x => x.Key == "footer"))
            {
                var footerConfig = new ThemeConfig
                {
                    Key = "footer",
                    ValueJson = JsonSerializer.Serialize(new Dictionary<string, string>
                    {
                        { "bgColor", "#101e24" },
                        { "gradientColor", "linear-gradient(135deg, #101e24, #162a30)" },
                        { "textColor", "#f7fbf8" },
                        { "bottomLineText", "© 2026 Travelsite. All rights reserved." },
                        { "socialIconColor", "#dc1e26" }
                    })
                };
                _context.ThemeConfigs.Add(footerConfig);
            }

            await _context.SaveChangesAsync();
        }

        private async Task<string> SaveFileAsync(IFormFile file, string subFolder)
        {
            var root = _environment.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
            var targetFolder = $"uploads/themes/{subFolder}";
            var uploadPath = Path.Combine(root, targetFolder.Replace('/', Path.DirectorySeparatorChar));

            if (!Directory.Exists(uploadPath))
            {
                Directory.CreateDirectory(uploadPath);
            }

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            var uniqueName = $"{Guid.NewGuid():N}{ext}";
            var fullPath = Path.Combine(uploadPath, uniqueName);

            await using (var stream = new FileStream(fullPath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/{targetFolder}/{uniqueName}".Replace("\\", "/");
        }

        // ---------------- GET ALL THEMES ----------------
        [HttpGet]
        public async Task<IActionResult> GetThemes()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var themes = await _context.Themes
                .OrderByDescending(t => t.IsActive)
                .ThenBy(t => t.CreatedAt)
                .ToListAsync();

            var list = themes.Select(t => new ThemeDto
            {
                Id = t.Id,
                Name = t.Name,
                IsActive = t.IsActive,
                CreatedAt = t.CreatedAt,
                Variables = JsonSerializer.Deserialize<Dictionary<string, string>>(t.VariablesJson) ?? new()
            }).ToList();

            return Ok(list);
        }

        // ---------------- CREATE A THEME ----------------
        [HttpPost]
        public async Task<IActionResult> CreateTheme([FromBody] CreateThemeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Theme name is required.");
            }

            await EnsureDefaultThemesAndConfigsAsync();

            var theme = new Theme
            {
                Id = $"theme-{Guid.NewGuid():N}",
                Name = request.Name.Trim(),
                IsActive = false,
                CreatedAt = DateTime.UtcNow,
                VariablesJson = JsonSerializer.Serialize(request.Variables ?? new())
            };

            _context.Themes.Add(theme);
            await _context.SaveChangesAsync();

            var dto = new ThemeDto
            {
                Id = theme.Id,
                Name = theme.Name,
                IsActive = theme.IsActive,
                CreatedAt = theme.CreatedAt,
                Variables = request.Variables ?? new()
            };

            return Ok(dto);
        }

        // ---------------- ACTIVATE THEME ----------------
        [HttpPut("{id}/activate")]
        public async Task<IActionResult> ActivateTheme(string id)
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var theme = await _context.Themes.FirstOrDefaultAsync(t => t.Id == id);
            if (theme == null)
            {
                return NotFound("Theme not found.");
            }

            // Deactivate all themes
            var activeThemes = await _context.Themes.Where(t => t.IsActive).ToListAsync();
            foreach (var active in activeThemes)
            {
                active.IsActive = false;
            }

            // Activate chosen theme
            theme.IsActive = true;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Theme activated successfully" });
        }

        // ---------------- DELETE THEME ----------------
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteTheme(string id)
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var theme = await _context.Themes.FirstOrDefaultAsync(t => t.Id == id);
            if (theme == null)
            {
                return NotFound("Theme not found.");
            }

            if (theme.IsActive)
            {
                return BadRequest("Cannot delete the active theme.");
            }

            _context.Themes.Remove(theme);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Theme deleted successfully." });
        }

        // ---------------- GET HEADER CONFIG ----------------
        [HttpGet("~/api/admin/theme-configs/header")]
        public async Task<IActionResult> GetHeaderConfig()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var config = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "header");
            if (config == null)
            {
                return NotFound("Header configuration not found.");
            }

            var dto = JsonSerializer.Deserialize<HeaderConfigDto>(config.ValueJson, JsonOptions);
            return Ok(dto);
        }

        // ---------------- UPDATE HEADER CONFIG ----------------
        [HttpPut("~/api/admin/theme-configs/header")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateHeaderConfig([FromForm] UpdateHeaderConfigRequest request)
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var config = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "header");
            if (config == null)
            {
                return NotFound("Header configuration not found.");
            }

            var current = JsonSerializer.Deserialize<Dictionary<string, string>>(config.ValueJson) ?? new();

            if (request.Logo != null && request.Logo.Length > 0)
            {
                var logoUrl = await SaveFileAsync(request.Logo, "header");
                current["logoUrl"] = logoUrl;
            }

            if (request.BgColor != null) current["bgColor"] = request.BgColor;
            if (request.TextColor != null) current["textColor"] = request.TextColor;
            if (request.NavHoverColor != null) current["navHoverColor"] = request.NavHoverColor;
            if (request.LayoutType != null) current["layoutType"] = request.LayoutType;

            config.ValueJson = JsonSerializer.Serialize(current);
            await _context.SaveChangesAsync();

            var updatedDto = JsonSerializer.Deserialize<HeaderConfigDto>(config.ValueJson, JsonOptions);
            return Ok(updatedDto);
        }

        // ---------------- GET HOME CONFIG ----------------
        [HttpGet("~/api/admin/theme-configs/home")]
        public async Task<IActionResult> GetHomeConfig()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var config = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "home");
            if (config == null)
            {
                return NotFound("Home configuration not found.");
            }

            var dto = JsonSerializer.Deserialize<HomeConfigDto>(config.ValueJson, JsonOptions);
            return Ok(dto);
        }

        // ---------------- UPDATE HOME CONFIG ----------------
        [HttpPut("~/api/admin/theme-configs/home")]
        [Consumes("multipart/form-data")]
        public async Task<IActionResult> UpdateHomeConfig([FromForm] UpdateHomeConfigRequest request)
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var config = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "home");
            if (config == null)
            {
                return NotFound("Home configuration not found.");
            }

            var current = JsonSerializer.Deserialize<Dictionary<string, string>>(config.ValueJson) ?? new();

            if (request.HeroBackgroundImage != null && request.HeroBackgroundImage.Length > 0)
            {
                var backgroundUrl = await SaveFileAsync(request.HeroBackgroundImage, "home");
                current["heroBackgroundImageUrl"] = backgroundUrl;
            }

            if (request.HeroTitle != null) current["heroTitle"] = request.HeroTitle;
            if (request.HeroSubtitle != null) current["heroSubtitle"] = request.HeroSubtitle;
            if (request.HeroOverlayColor != null) current["heroOverlayColor"] = request.HeroOverlayColor;
            if (request.SearchCardStyle != null) current["searchCardStyle"] = request.SearchCardStyle;

            config.ValueJson = JsonSerializer.Serialize(current);
            await _context.SaveChangesAsync();

            var updatedDto = JsonSerializer.Deserialize<HomeConfigDto>(config.ValueJson, JsonOptions);
            return Ok(updatedDto);
        }

        // ---------------- GET FOOTER CONFIG ----------------
        [HttpGet("~/api/admin/theme-configs/footer")]
        public async Task<IActionResult> GetFooterConfig()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var config = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "footer");
            if (config == null)
            {
                return NotFound("Footer configuration not found.");
            }

            var dto = JsonSerializer.Deserialize<FooterConfigDto>(config.ValueJson, JsonOptions);
            return Ok(dto);
        }

        // ---------------- UPDATE FOOTER CONFIG ----------------
        [HttpPut("~/api/admin/theme-configs/footer")]
        public async Task<IActionResult> UpdateFooterConfig([FromBody] FooterConfigDto request)
        {
            if (request == null)
            {
                return BadRequest("Invalid footer configuration.");
            }

            await EnsureDefaultThemesAndConfigsAsync();

            var config = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "footer");
            if (config == null)
            {
                return NotFound("Footer configuration not found.");
            }

            config.ValueJson = JsonSerializer.Serialize(request);
            await _context.SaveChangesAsync();

            var updatedDto = JsonSerializer.Deserialize<FooterConfigDto>(config.ValueJson, JsonOptions);
            return Ok(updatedDto);
        }
        // ---------------- PUBLIC: GET ACTIVE THEME ----------------
        [AllowAnonymous]
        [HttpGet("~/api/public/themes/active")]
        public async Task<IActionResult> GetActiveThemePublic()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var theme = await _context.Themes.FirstOrDefaultAsync(t => t.IsActive);
            if (theme == null)
            {
                return NotFound("Active theme not found.");
            }

            var dto = new ThemeDto
            {
                Id = theme.Id,
                Name = theme.Name,
                IsActive = theme.IsActive,
                CreatedAt = theme.CreatedAt,
                Variables = JsonSerializer.Deserialize<Dictionary<string, string>>(theme.VariablesJson) ?? new()
            };

            return Ok(dto);
        }

        // ---------------- PUBLIC: GET ACTIVE LAYOUT ----------------
        [AllowAnonymous]
        [HttpGet("~/api/public/themes/layout")]
        public async Task<IActionResult> GetActiveLayoutPublic()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var headerConfig = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "header");
            var homeConfig = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "home");
            var footerConfig = await _context.ThemeConfigs.FirstOrDefaultAsync(c => c.Key == "footer");

            var headerDto = headerConfig != null ? JsonSerializer.Deserialize<HeaderConfigDto>(headerConfig.ValueJson, JsonOptions) : null;
            var homeDto = homeConfig != null ? JsonSerializer.Deserialize<HomeConfigDto>(homeConfig.ValueJson, JsonOptions) : null;
            var footerDto = footerConfig != null ? JsonSerializer.Deserialize<FooterConfigDto>(footerConfig.ValueJson, JsonOptions) : null;

            return Ok(new
            {
                header = headerDto,
                home = homeDto,
                footer = footerDto
            });
        }
    }
}
