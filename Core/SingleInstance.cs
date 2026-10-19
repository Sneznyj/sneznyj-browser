using System.IO;
using System.IO.Pipes;

namespace SneznyjBrowser;

/// <summary>Передаёт аргументы запуска (ссылки) уже открытому окну через именованный канал.</summary>
public static class SingleInstance
{
    static string PipeName => "SneznyjBrowser.Pipe." + AppPaths.InstanceKey;

    public static void SendToRunning(string[] args)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(1500);
            using var writer = new StreamWriter(client);
            writer.Write(string.Join("\n", args.Length == 0 ? new[] { "--activate" } : args));
        }
        catch { }
    }

    public static void Listen(Action<string[]> onArgs)
    {
        var thread = new Thread(() =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1);
                    server.WaitForConnection();
                    using var reader = new StreamReader(server);
                    var args = reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    onArgs(args);
                }
                catch
                {
                    Thread.Sleep(500);
                }
            }
        })
        { IsBackground = true, Name = "single-instance" };
        thread.Start();
    }
}
