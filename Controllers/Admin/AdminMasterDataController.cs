using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using PickNBook.Api.Models.DTOs;
using PickNBook.Api.Services.Interfaces;

namespace PickNBook.Api.Controllers
{
    [ApiController]
    [Route("api/admin/master-data")]
    public class AdminMasterDataController : AdminApiController
    {
        private readonly ISrdvMasterDataImporter _importer;
        private readonly ILogger<AdminMasterDataController> _logger;
        private static readonly SemaphoreSlim _importLock = new SemaphoreSlim(1, 1);

        public AdminMasterDataController(
            ISrdvMasterDataImporter importer,
            ILogger<AdminMasterDataController> logger)
        {
            _importer = importer;
            _logger = logger;
        }

        [HttpPost("import/bus")]
        public async Task<IActionResult> ImportBusCities(CancellationToken cancellationToken)
        {
            if (!await _importLock.WaitAsync(0, cancellationToken))
            {
                return Conflict(new { message = "Another master data import is currently in progress." });
            }

            try
            {
                var result = await _importer.ImportBusCitiesAsync(cancellationToken);
                return result.Success ? Ok(result) : StatusCode(500, result);
            }
            finally
            {
                _importLock.Release();
            }
        }

        [HttpPost("import/hotel")]
        public async Task<IActionResult> ImportHotelCities(CancellationToken cancellationToken)
        {
            if (!await _importLock.WaitAsync(0, cancellationToken))
            {
                return Conflict(new { message = "Another master data import is currently in progress." });
            }

            try
            {
                var result = await _importer.ImportHotelCitiesAsync(cancellationToken);
                return result.Success ? Ok(result) : StatusCode(500, result);
            }
            finally
            {
                _importLock.Release();
            }
        }

        [HttpPost("import/flight")]
        public async Task<IActionResult> ImportFlightData(CancellationToken cancellationToken)
        {
            if (!await _importLock.WaitAsync(0, cancellationToken))
            {
                return Conflict(new { message = "Another master data import is currently in progress." });
            }

            try
            {
                var airportResult = await _importer.ImportAirportsAsync(cancellationToken);
                var airlineResult = await _importer.ImportAirlinesAsync(cancellationToken);

                return Ok(new
                {
                    Airports = airportResult,
                    Airlines = airlineResult
                });
            }
            finally
            {
                _importLock.Release();
            }
        }

        [HttpPost("import/all")]
        public async Task<IActionResult> ImportAll(CancellationToken cancellationToken)
        {
            if (!await _importLock.WaitAsync(0, cancellationToken))
            {
                return Conflict(new { message = "Another master data import is currently in progress." });
            }

            try
            {
                var results = await _importer.ImportAllAsync(cancellationToken);
                return Ok(results);
            }
            finally
            {
                _importLock.Release();
            }
        }

        [HttpGet("status")]
        public async Task<IActionResult> GetStatus(CancellationToken cancellationToken)
        {
            var status = await _importer.GetMasterDataStatusAsync(cancellationToken);
            return Ok(status);
        }
    }
}
