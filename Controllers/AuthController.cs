using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Notariat.Api.Domain;

namespace Notariat.Api.Controllers;

[ApiController, Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _um;
    private readonly IConfiguration _cfg;
    public AuthController(UserManager<ApplicationUser> um, IConfiguration cfg) { _um = um; _cfg = cfg; }

    [HttpPost("login")]//Test#2026
    public async Task<IActionResult> Login(LoginDto dto)
    {
        var u = await _um.FindByNameAsync(dto.UserName);
        if (u == null || await _um.IsLockedOutAsync(u)) return Unauthorized(new { message = "Identifiants invalides." });
        if (!await _um.CheckPasswordAsync(u, dto.Password))
        {
            await _um.AccessFailedAsync(u);
            return Unauthorized(new { message = "Identifiants invalides." });
        }
        await _um.ResetAccessFailedCountAsync(u);

        var roles = await _um.GetRolesAsync(u);
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, u.Id), new(ClaimTypes.Name, u.NomComplet) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_cfg["Jwt:Key"]!));
        var token = new JwtSecurityToken(_cfg["Jwt:Issuer"], _cfg["Jwt:Audience"], claims,
            expires: DateTime.UtcNow.AddHours(8),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return Ok(new { token = new JwtSecurityTokenHandler().WriteToken(token), nom = u.NomComplet, roles });
    }
}
