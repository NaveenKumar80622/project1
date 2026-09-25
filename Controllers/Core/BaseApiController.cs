using Microsoft.AspNetCore.Mvc;

namespace PickNBook.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public abstract class BaseApiController : ControllerBase
{
}
