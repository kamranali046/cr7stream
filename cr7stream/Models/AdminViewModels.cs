using cr7stream.Logic.Models;

namespace cr7stream.Models;

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

