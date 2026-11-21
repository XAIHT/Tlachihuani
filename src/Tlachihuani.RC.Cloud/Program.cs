// Tlachihuani — Angela López Mendoza · XAIHT
using System.Diagnostics;
using Tlachihuani.RC.Cloud;

var parentStdin = args is ["--development", _, "--parent-stdin"];
if (args is not ["--development", _] && !parentStdin)
{
    Console.Error.WriteLine("Only --development <configuration.json> [--parent-stdin] is implemented. Public Internet deployment remains disabled.");
    return 2;
}
if (parentStdin && !Console.IsInputRedirected)
{
    Console.Error.WriteLine("--parent-stdin requires a redirected parent-owned input pipe.");
    return 2;
}
var configurationPath = args[1];
Tlachihuani.Contracts.TraceLog.Configure("rc-cloud", [Path.Combine(AppContext.BaseDirectory, "logs", "rc-cloud.log")]);
var stage = "configuration validation";
try
{
    var configuration = CloudConfiguration.Load(configurationPath);
    stage = "HTTPS host and dependency construction";
    await using var app = CloudWeb.Create(configuration);
    Console.WriteLine("Tlachihuani-RC Cloud: development-loopback only; public remote access and post-quantum transport are disabled.");
    Console.WriteLine("Listening on " + configuration.Origin);
    stage = "host startup or execution";
    using var parentCancellation = new CancellationTokenSource();
    if (parentStdin)
        _ = Task.Run(() => WatchParentAsync(app.Lifetime, parentCancellation.Token));
    try { await app.RunAsync(); }
    finally { parentCancellation.Cancel(); }
    // A console input reader can remain blocked after an unrelated host stop.
    // Do not wait for it: the host and its certificate still dispose normally.
    return 0;
}
catch (Exception exception) when (exception is not OutOfMemoryException)
{
    // Configuration/certificate values and arbitrary exception messages can contain
    // secrets. Stage, exception types and method identifiers retain safe diagnostics.
    Console.Error.WriteLine("Cloud failure during " + stage + ": " + exception.GetType().Name);
    for (var current = exception; current is not null; current = current.InnerException)
    {
        if (current is InvalidDataException
            || (current is InvalidOperationException && current.Message.StartsWith("Unable to resolve service for type '", StringComparison.Ordinal)))
            Console.Error.WriteLine(current.Message);
        foreach (var frame in new StackTrace(current, false).GetFrames().Take(4))
        {
            var method = frame.GetMethod();
            Console.Error.WriteLine("  " + current.GetType().Name + " at " + method?.DeclaringType?.FullName + "." + method?.Name);
        }
    }
    return 1;
}


static async Task WatchParentAsync(Microsoft.Extensions.Hosting.IHostApplicationLifetime lifetime, CancellationToken cancellation)
{
    using var input = Console.OpenStandardInput();
    var buffer = new byte[256];
    var line = new byte[4];
    var length = 0;
    var oversized = false;
    try
    {
        while (!cancellation.IsCancellationRequested)
        {
            var count = await input.ReadAsync(buffer.AsMemory(), cancellation);
            if (count == 0) { lifetime.StopApplication(); return; }
            for (var index = 0; index < count; index++)
            {
                var value = buffer[index];
                if (value is 10 or 13)
                {
                    if (!oversized && length == 4 && line.AsSpan().SequenceEqual("stop"u8))
                    { lifetime.StopApplication(); return; }
                    length = 0;
                    oversized = false;
                }
                else if (length < line.Length) line[length++] = value;
                else oversized = true;
            }
        }
    }
    catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
    catch (IOException) { if (!cancellation.IsCancellationRequested) lifetime.StopApplication(); }
}
