using ShoutCalendar.Core;

var zone = TimeZoneInfo.Local;
var logs = new List<string>();
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--zone")
        zone = TimeZoneInfo.FindSystemTimeZoneById(args[i + 1]);
    if (args[i] == "--logs")
        logs.Add(args[i + 1]);
}

IEnumerable<string> directories = logs;
if (logs.Count == 0)
{
    var roots = ChatLogPaths.CandidateRoots(
        args,
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        Environment.GetEnvironmentVariable("HOME"),
        Environment.GetEnvironmentVariable("USERPROFILE"));
    directories = ChatLogPaths.LogDirectories(roots);
}

Console.WriteLine(ChatWatch.Report(directories, zone: zone));
return 0;
