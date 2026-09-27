using ShoutCalendar.Core;

var zone = TimeZoneInfo.Local;
for (var i = 0; i < args.Length - 1; i++)
{
    if (args[i] != "--zone")
        continue;
    zone = TimeZoneInfo.FindSystemTimeZoneById(args[i + 1]);
}

var roots = ChatLogPaths.CandidateRoots(
    args,
    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
    Environment.GetEnvironmentVariable("HOME"),
    Environment.GetEnvironmentVariable("USERPROFILE"));
Console.WriteLine(ChatWatch.Report(ChatLogPaths.LogDirectories(roots), zone: zone));
return 0;
