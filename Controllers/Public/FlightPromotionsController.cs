using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickNBook.Api.Data;
using PickNBook.Api.Models;
using PickNBook.Api.Models.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace PickNBook.Api.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class FlightPromotionsController : BaseApiController
    {
        private readonly AppDbContext _context;

        public FlightPromotionsController(AppDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetActivePromotions()
        {
            // Flight promotions are inactive; flight discount engine mirrors Bus (coupons only)
            await Task.CompletedTask;
            return Ok(new List<FlightPromotionResponseDto>());
        }
    }
}
