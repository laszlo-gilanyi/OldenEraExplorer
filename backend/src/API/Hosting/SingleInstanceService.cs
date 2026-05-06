using System.IO.Pipes;
using System.Text;

namespace API.Hosting;

public static class SingleInstanceService
{
    private const string PipeName = "OldenEraExplorer-SingleInstance";

    // Returns false if another instance is already running (and has been signalled to focus).
    public static bool EnsureSingleInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(150);

            using var writer = new StreamWriter(client, Encoding.UTF8);
            writer.WriteLine("FOCUS");
            writer.Flush();

            Console.WriteLine("OldenEraExplorer is already running. Signalled existing instance to open browser.");
            return false;
        }
        catch
        {
            return true;
        }
    }

    public static void StartIpcListener(string url)
    {
        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    using var server = new NamedPipeServerStream(PipeName, PipeDirection.In);
                    await server.WaitForConnectionAsync();

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var command = await reader.ReadLineAsync();

                    if (command == "FOCUS")
                    {
                        Console.WriteLine("Second instance launched — opening browser for existing instance.");
                        BrowserLauncher.Open(url);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"IPC listener error: {ex.Message}");
                    await Task.Delay(1000);
                }
            }
        });
    }
}
