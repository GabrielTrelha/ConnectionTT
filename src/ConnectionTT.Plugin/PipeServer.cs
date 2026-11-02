using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using ConnectionTT.Core;
using ConnectionTT.IpcContracts;

namespace ConnectionTT.Plugin;

public sealed class PipeServer
{
    private volatile bool _running;
    private Thread? _thread;
    private NamedPipeServerStream? _current;

    public bool IsRunning => _running;

    public void Start()
    {
        if (_running)
        {
            return;
        }

        _running = true;
        _thread = new Thread(Loop) { IsBackground = true, Name = "ConnectionTT.PipeServer" };
        _thread.Start();
    }

    public void Stop()
    {
        _running = false;
        try
        {
            _current?.Dispose();
        }
        catch
        {
        }
    }

    private void Loop()
    {
        while (_running)
        {
            try
            {
                var server = new NamedPipeServerStream(
                    PipeNames.ServerName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                _current = server;
                try
                {
                    server.WaitForConnection();
                    _current = null;

                    using var writer = new StreamWriter(server, new UTF8Encoding(false), 1024, leaveOpen: true) { AutoFlush = true };
                    using var reader = new StreamReader(server, new UTF8Encoding(false), false, 1024, leaveOpen: true);

                    HandleConnection(reader, writer);
                }
                finally
                {
                    _current = null;
                    server.Dispose();
                }
            }
            catch (Exception)
            {
                Thread.Sleep(500);
            }
        }
    }

    private static void HandleConnection(TextReader reader, TextWriter writer)
    {
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            IpcRequest? request;
            try
            {
                request = JsonSerializer.Deserialize<IpcRequest>(line, JsonDefaults.Options);
            }
            catch (JsonException)
            {
                continue;
            }

            if (request == null)
            {
                continue;
            }

            var response = ToolDispatcher.Current.Dispatch(request);
            writer.WriteLine(JsonSerializer.Serialize(response, JsonDefaults.Options));
        }
    }
}
