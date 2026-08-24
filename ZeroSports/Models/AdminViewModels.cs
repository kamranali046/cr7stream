using ZeroSports.Logic.Models;

namespace ZeroSports.Models;

public class LoginViewModel
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AdminCategoryViewModel
{
    public League League { get; set; } = new();
    public List<Match> Matches { get; set; } = new();
}
