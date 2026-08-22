using ClinicSaaS.API.Data;
using ClinicSaaS.API.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;

namespace ClinicSaaS.API.Filters
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public class RequireActiveSubscriptionAttribute : Attribute, IAsyncActionFilter
    {
        public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            var db = context.HttpContext.RequestServices.GetRequiredService<ApplicationDbContext>();
            var clinicContext = context.HttpContext.RequestServices.GetRequiredService<IClinicContext>();

            if (clinicContext.Role == "SuperAdmin" || clinicContext.Role == "ClinicStaff")
            {
                await next();
                return;
            }

            var subscription = await db.Subscriptions
                .AsNoTracking()
                .Where(s => s.ClinicId == clinicContext.ClinicId && s.IsActive)
                .OrderByDescending(s => s.EndDate)
                .FirstOrDefaultAsync();

            if (subscription == null || subscription.EndDate <= DateTime.UtcNow)
            {
                context.Result = new ObjectResult(new
                {
                    code = "SUBSCRIPTION_EXPIRED",
                    message = "انتهى اشتراك العيادة. يرجى التجديد للمتابعة.",
                    expiredAt = subscription?.EndDate
                })
                { StatusCode = StatusCodes.Status402PaymentRequired };
                return;
            }

            await next();
        }
    }
}