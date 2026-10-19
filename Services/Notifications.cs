using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Notariat.Api.Data;
using Notariat.Api.Domain;

namespace Notariat.Api.Services;

[Authorize]
public class NotificationHub : Hub { }

public interface INotificationService
{
    Task NotifierAsync(string userId, string message, int? declarationId = null);
    Task NotifierRoleAsync(string role, string message, int? declarationId = null);
}

public class NotificationService : INotificationService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<NotificationHub> _hub;
    private readonly UserManager<ApplicationUser> _users;
    public NotificationService(AppDbContext db, IHubContext<NotificationHub> hub, UserManager<ApplicationUser> users)
    { _db = db; _hub = hub; _users = users; }

    public async Task NotifierAsync(string userId, string message, int? declarationId = null)
    {
        var n = new Notification { UserId = userId, Message = message, DeclarationId = declarationId };
        _db.Notifications.Add(n);
        await _db.SaveChangesAsync();
        await _hub.Clients.User(userId).SendAsync("notification",
            new { id = n.Id, message, declarationId, lue = false, dateCreation = n.DateCreation });
    }

    public async Task NotifierRoleAsync(string role, string message, int? declarationId = null)
    {
        foreach (var u in await _users.GetUsersInRoleAsync(role))
            await NotifierAsync(u.Id, message, declarationId);
    }
}
