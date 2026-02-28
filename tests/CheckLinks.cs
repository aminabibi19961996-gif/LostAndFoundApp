using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Collections.Generic;

class CheckLinks
{
    static void Main()
    {
        var viewsDir = Path.Combine(Directory.GetCurrentDirectory(), "Views");
        var controllersDir = Path.Combine(Directory.GetCurrentDirectory(), "Controllers");

        var validActions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in Directory.GetFiles(controllersDir, "*Controller.cs", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            var controllerName = Path.GetFileNameWithoutExtension(file).Replace("Controller", "");
            
            var matches = Regex.Matches(content, @"public\s+(async\s+Task<IActionResult>|IActionResult|Task<IActionResult>)\s+([A-Za-z0-9_]+)\s*\(");
            foreach (Match m in matches)
            {
                validActions.Add($"{controllerName}/{m.Groups[2].Value}");
            }
        }

        foreach (var file in Directory.GetFiles(viewsDir, "*.cshtml", SearchOption.AllDirectories))
        {
            var content = File.ReadAllText(file);
            var matches = Regex.Matches(content, @"asp-controller=""([^""]+)""\s+asp-action=""([^""]+)""");
            
            foreach (Match m in matches)
            {
                var target = $"{m.Groups[1].Value}/{m.Groups[2].Value}";
                if (!validActions.Contains(target) && target != "Account/Logout" && target != "Account/Login") // special cases often handled differently
                {
                    Console.WriteLine($"Broken Link found in {file}: Pointing to {target}");
                }
            }
        }
        Console.WriteLine("Link Scan Completed.");
    }
}
