using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Notariat.Api.Data;

namespace Notariat.Api.Controllers;

[ApiController, Route("api/notifications"), Authorize]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;
    public NotificationsController(AppDbContext db) => _db = db;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpGet]
    public async Task<IActionResult> Liste() =>
        Ok(await _db.Notifications.Where(n => n.UserId == UserId)
            .OrderByDescending(n => n.Id).Take(50).ToListAsync());

    [HttpPost("lues")]
    public async Task<IActionResult> MarquerLues()
    {
        var non = await _db.Notifications.Where(n => n.UserId == UserId && !n.Lue).ToListAsync();
        non.ForEach(n => n.Lue = true);
        await _db.SaveChangesAsync();
        return NoContent();
    }
}
