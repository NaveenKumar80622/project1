using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;

namespace PickNBook.Api.Services;

public class AboutUsService : IAboutUsService
{
    private readonly AppDbContext _context;

    public AboutUsService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<AboutUsDto?> GetAsync(string module)
    {
        var aboutUs = await _context.AboutUs
            .AsNoTracking()
            .Include(x => x.Counts)
            .Include(x => x.TeamMembers)
            .FirstOrDefaultAsync(x => x.Module.ToLower() == module.ToLower());

        if (aboutUs == null) return null;

        return new AboutUsDto
        {
            Id = aboutUs.Id,
            AboutDescription = aboutUs.AboutDescription,
            Status = aboutUs.Status,
            Module = aboutUs.Module,
            WhoWeAre = new WhoWeAreDto
            {
                Heading = aboutUs.WhoWeAreHeading,
                Details = aboutUs.WhoWeAreDescription,
                ImageUrl = aboutUs.WhoWeAreImageUrl
            },
            CountSection = aboutUs.Counts
                .OrderBy(c => c.DisplayOrder)
                .Select(c => new AboutUsCountDto
                {
                    CountValue = c.CountValue,
                    CountTitle = c.CountTitle,
                    DisplayOrder = c.DisplayOrder
                }).ToList(),
            TeamMembers = aboutUs.TeamMembers
                .OrderBy(t => t.DisplayOrder)
                .Select(t => new TeamMemberDto
                {
                    Id = t.Id,
                    Name = t.Name,
                    Designation = t.Designation,
                    ImageUrl = t.ImageUrl,
                    DisplayOrder = t.DisplayOrder
                }).ToList()
        };
    }

    public async Task UpdateAsync(UpdateAboutUsDto dto)
    {
        using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            var existing = await _context.AboutUs
                .Include(x => x.Counts)
                .Include(x => x.TeamMembers)
                .FirstOrDefaultAsync(x => x.Module.ToLower() == dto.Module.ToLower());

            if (existing == null)
            {
                var entry = new AboutUs
                {
                    Module = dto.Module,
                    Status = dto.Status,
                    AboutDescription = dto.AboutDescription,
                    WhoWeAreHeading = dto.WhoWeAre.Heading,
                    WhoWeAreDescription = dto.WhoWeAre.Details,
                    WhoWeAreImageUrl = dto.WhoWeAre.ImageUrl,
                    CreatedAtUtc = DateTime.UtcNow,
                    UpdatedAtUtc = DateTime.UtcNow,
                    Counts = dto.CountSection.Select(c => new AboutUsCount
                    {
                        CountValue = c.CountValue,
                        CountTitle = c.CountTitle,
                        DisplayOrder = c.DisplayOrder
                    }).ToList(),
                    TeamMembers = dto.TeamMembers.Select(t => new AboutUsTeamMember
                    {
                        Name = t.Name,
                        Designation = t.Designation,
                        ImageUrl = t.ImageUrl,
                        DisplayOrder = t.DisplayOrder
                    }).ToList()
                };
                _context.AboutUs.Add(entry);
            }
            else
            {
                existing.Status = dto.Status;
                existing.AboutDescription = dto.AboutDescription;
                existing.WhoWeAreHeading = dto.WhoWeAre.Heading;
                existing.WhoWeAreDescription = dto.WhoWeAre.Details;
                existing.WhoWeAreImageUrl = dto.WhoWeAre.ImageUrl;
                existing.UpdatedAtUtc = DateTime.UtcNow;

                // Atomic replacement of Count elements
                _context.AboutUsCounts.RemoveRange(existing.Counts);
                existing.Counts = dto.CountSection.Select(c => new AboutUsCount
                {
                    CountValue = c.CountValue,
                    CountTitle = c.CountTitle,
                    DisplayOrder = c.DisplayOrder
                }).ToList();

                // Atomic replacement of TeamMember elements
                _context.AboutUsTeamMembers.RemoveRange(existing.TeamMembers);
                existing.TeamMembers = dto.TeamMembers.Select(t => new AboutUsTeamMember
                {
                    Name = t.Name,
                    Designation = t.Designation,
                    ImageUrl = t.ImageUrl,
                    DisplayOrder = t.DisplayOrder
                }).ToList();
            }

            await _context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}
