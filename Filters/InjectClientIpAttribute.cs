using Microsoft.AspNetCore.Mvc.Filters;
using PickNBook.Api.Extensions;
using System.Linq;

namespace PickNBook.Api.Filters
{
    public class InjectClientIpAttribute : ActionFilterAttribute
    {
        public override void OnActionExecuting(ActionExecutingContext context)
        {
            var ip = context.HttpContext.GetClientIpAddress();

            // Iterate over all arguments passed to the controller action
            foreach (var arg in context.ActionArguments.Values)
            {
                if (arg == null) continue;

                // Check if the argument has an 'EndUserIp' property
                var propertyInfo = arg.GetType().GetProperty("EndUserIp");
                if (propertyInfo != null && propertyInfo.CanWrite && propertyInfo.PropertyType == typeof(string))
                {
                    // Set the dynamic IP
                    propertyInfo.SetValue(arg, ip);
                }
            }

            if (context.ActionArguments.ContainsKey("endUserIp")) { context.ActionArguments["endUserIp"] = ip; } if (context.ActionArguments.ContainsKey("EndUserIp")) { context.ActionArguments["EndUserIp"] = ip; } base.OnActionExecuting(context);
        }
    }
}

