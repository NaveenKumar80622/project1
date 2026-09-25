using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers.Public
{
    [AllowAnonymous]
    [Route("api/public/theme")]
    public class PublicThemeController : BaseApiController
    {
        private readonly AppDbContext _context;

        public PublicThemeController(AppDbContext context)
        {
            _context = context;
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

        // ---------------- GET ACTIVE THEME VARIABLES ----------------
        [HttpGet("active")]
        public async Task<IActionResult> GetActiveTheme()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var activeTheme = await _context.Themes.FirstOrDefaultAsync(t => t.IsActive);
            if (activeTheme == null)
            {
                // Fallback to first available theme
                activeTheme = await _context.Themes.FirstOrDefaultAsync();
            }

            if (activeTheme == null)
            {
                return NotFound("No active theme found.");
            }

            var vars = JsonSerializer.Deserialize<Dictionary<string, string>>(activeTheme.VariablesJson) ?? new();

            // Map variables to public CSS property structure
            string primary = vars.TryGetValue("primaryColor", out var p) ? p : "#dc1e26";
            string primaryStrong = vars.TryGetValue("primaryStrongColor", out var ps) ? ps : "#b8141b";
            string page = vars.TryGetValue("pageBgColor", out var pg) ? pg : "#F3F4F6";
            string surface = vars.TryGetValue("surfaceColor", out var s) ? s : "#ffffff";
            string text = vars.TryGetValue("textColor", out var t) ? t : "#162126";
            string border = vars.TryGetValue("borderColor", out var b) ? b : "#E5E7EB";
            string gradient = $"linear-gradient(135deg, {primary} 0%, {primaryStrong} 100%)";

            var responseVariables = new Dictionary<string, string>
            {
                { "--theme-primary", primary },
                { "--theme-primary-strong", primaryStrong },
                { "--theme-page", page },
                { "--theme-surface", surface },
                { "--theme-text", text },
                { "--theme-border", border },
                { "--theme-gradient-action", gradient }
            };

            return Ok(new { variables = responseVariables });
        }

        // ---------------- GET ACTIVE LAYOUT CONFIGURATIONS ----------------
        [HttpGet("layout")]
        public async Task<IActionResult> GetActiveLayout()
        {
            await EnsureDefaultThemesAndConfigsAsync();

            var configs = await _context.ThemeConfigs.ToListAsync();

            var headerConfig = configs.FirstOrDefault(c => c.Key == "header");
            var homeConfig = configs.FirstOrDefault(c => c.Key == "home");
            var footerConfig = configs.FirstOrDefault(c => c.Key == "footer");

            var header = headerConfig != null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(headerConfig.ValueJson)
                : new();

            var home = homeConfig != null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(homeConfig.ValueJson)
                : new();

            var footer = footerConfig != null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(footerConfig.ValueJson)
                : new();

            return Ok(new
            {
                header,
                home,
                footer
            });
        }
    }
}
