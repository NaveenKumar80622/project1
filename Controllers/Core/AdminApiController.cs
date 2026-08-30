using Microsoft.AspNetCore.Authorization;

namespace PickNBook.Api.Controllers;

[Authorize(Roles = AuthRoles.AdminOrSuperAdmin)]
public abstract class AdminApiController : BaseApiController
{
}
