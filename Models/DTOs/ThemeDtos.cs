using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Generic;

namespace PickNBook.Api.Models.DTOs
{
    public class ThemeDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public Dictionary<string, string> Variables { get; set; } = new();
    }

    public class CreateThemeRequest
    {
        public string Name { get; set; } = string.Empty;
        public Dictionary<string, string> Variables { get; set; } = new();
    }

    public class HeaderConfigDto
    {
        public string LogoUrl { get; set; } = string.Empty;
        public string BgColor { get; set; } = string.Empty;
        public string TextColor { get; set; } = string.Empty;
        public string NavHoverColor { get; set; } = string.Empty;
        public string LayoutType { get; set; } = string.Empty;
    }

    public class UpdateHeaderConfigRequest
    {
        public IFormFile? Logo { get; set; }
        public string? BgColor { get; set; }
        public string? TextColor { get; set; }
        public string? NavHoverColor { get; set; }
        public string? LayoutType { get; set; }
    }

    public class HomeConfigDto
    {
        public string HeroBackgroundImageUrl { get; set; } = string.Empty;
        public string HeroTitle { get; set; } = string.Empty;
        public string HeroSubtitle { get; set; } = string.Empty;
        public string HeroOverlayColor { get; set; } = string.Empty;
        public string SearchCardStyle { get; set; } = string.Empty;
    }

    public class UpdateHomeConfigRequest
    {
        public IFormFile? HeroBackgroundImage { get; set; }
        public string? HeroTitle { get; set; }
        public string? HeroSubtitle { get; set; }
        public string? HeroOverlayColor { get; set; }
        public string? SearchCardStyle { get; set; }
    }

    public class FooterConfigDto
    {
        public string BgColor { get; set; } = string.Empty;
        public string GradientColor { get; set; } = string.Empty;
        public string TextColor { get; set; } = string.Empty;
        public string BottomLineText { get; set; } = string.Empty;
        public string SocialIconColor { get; set; } = string.Empty;
    }
}
