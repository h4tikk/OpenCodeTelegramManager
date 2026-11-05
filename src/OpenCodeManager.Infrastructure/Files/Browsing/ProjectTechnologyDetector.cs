using System.Xml;
using Microsoft.EntityFrameworkCore;
using OpenCodeManager.Application;

namespace OpenCodeManager.Infrastructure;
public sealed class ProjectTechnologyDetector(IPathPolicy paths) : IProjectTechnologyDetector
{
    public string Detect(string path)
    {
        path = paths.DemandAllowed(path);
        var files = Directory.EnumerateFiles(path).Select(Path.GetFileName).ToArray();
        var tags = new List<string>();
        if (files.Any(x => x!.EndsWith(".csproj") || x.EndsWith(".slnx") || x.EndsWith(".sln")))
        {
            bool web = false;
            foreach (var project in Directory.EnumerateFiles(path, "*.csproj"))
            {
                if (new FileInfo(project).Length > 1024 * 1024)
                    continue;
                try
                {
                    using var xml = XmlReader.Create(project, new() { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 1_048_576 });
                    while (xml.Read())
                        if (xml.NodeType == XmlNodeType.Element && xml.Name == "Project" && xml.GetAttribute("Sdk") == "Microsoft.NET.Sdk.Web")
                            web = true;
                }
                catch (XmlException)
                {
                }
            }

            tags.Add(web ? "ASP.NET Core" : ".NET");
        }

        if (File.Exists(Path.Combine(path, "config/application.rb")))
            tags.Add("Rails");
        else if (files.Contains("Gemfile"))
            tags.Add("Ruby");
        if (files.Contains("Cargo.toml"))
            tags.Add("Rust");
        if (files.Contains("pyproject.toml"))
            tags.Add("Python");
        if (files.Contains("CMakeLists.txt"))
            tags.Add("C/C++");
        if (files.Contains("package.json"))
            tags.Add("JavaScript/TypeScript");
        return tags.Count > 0 ? string.Join(", ", tags) : "Unknown";
    }
}
