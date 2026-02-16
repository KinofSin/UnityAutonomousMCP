using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace AutonomousMcp.Editor
{
    internal sealed class AutonomousMcpTransportHost : IDisposable
    {
        private readonly AutonomousMcpSettings _settings;
        private HttpListener _httpListener;
        private TcpListener _tcpListener;
        private Thread _httpThread;
        private Thread _tcpThread;
        private volatile bool _running;

        public AutonomousMcpTransportHost(AutonomousMcpSettings settings)
        {
            _settings = settings;
        }

        public void Start()
        {
            if (_running)
            {
                return;
            }

            _running = true;
            StartHttp();
            StartTcp();
        }

        public void Stop()
        {
            _running = false;

            try
            {
                _httpListener?.Stop();
            }
            catch
            {
                // ignored
            }

            try
            {
                _tcpListener?.Stop();
            }
            catch
            {
                // ignored
            }

            _httpThread?.Join(500);
            _tcpThread?.Join(500);
            _httpListener = null;
            _tcpListener = null;
            _httpThread = null;
            _tcpThread = null;
        }

        public void Dispose()
        {
            Stop();
        }

        private void StartHttp()
        {
            _httpListener = new HttpListener();
            _httpListener.Prefixes.Add($"http://{_settings.Host}:{_settings.HttpPort}/");
            _httpListener.Start();

            _httpThread = new Thread(HttpLoop)
            {
                IsBackground = true,
                Name = "AutonomousMcpHttp"
            };
            _httpThread.Start();

            Debug.Log($"[AutonomousMCP] HTTP transport listening on {_settings.Host}:{_settings.HttpPort}");
        }

        private void StartTcp()
        {
            var ip = ResolveHostToIp(_settings.Host);
            _tcpListener = new TcpListener(ip, _settings.TcpPort);
            _tcpListener.Start();

            _tcpThread = new Thread(TcpLoop)
            {
                IsBackground = true,
                Name = "AutonomousMcpTcp"
            };
            _tcpThread.Start();

            Debug.Log($"[AutonomousMCP] TCP transport listening on {_settings.Host}:{_settings.TcpPort}");
        }

        private static IPAddress ResolveHostToIp(string host)
        {
            if (IPAddress.TryParse(host, out var direct))
            {
                return direct;
            }

            try
            {
                var addresses = Dns.GetHostAddresses(host);
                foreach (var address in addresses)
                {
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return address;
                    }
                }

                if (addresses.Length > 0)
                {
                    return addresses[0];
                }
            }
            catch
            {
                // ignored, fallback below
            }

            return IPAddress.Loopback;
        }

        private void HttpLoop()
        {
            while (_running && _httpListener != null)
            {
                try
                {
                    var context = _httpListener.GetContext();
                    HandleHttpRequest(context);
                }
                catch (HttpListenerException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutonomousMCP] HTTP loop error: {ex.Message}");
                }
            }
        }

        private static void WriteHttpResponse(HttpListenerResponse response, int statusCode, string content)
        {
            response.StatusCode = statusCode;
            response.ContentType = "application/json";
            var bytes = Encoding.UTF8.GetBytes(content);
            response.ContentLength64 = bytes.Length;
            using var stream = response.OutputStream;
            stream.Write(bytes, 0, bytes.Length);
        }

        private static AutonomousMcpEnvelope ParseEnvelope(string json)
        {
            return JsonConvert.DeserializeObject<AutonomousMcpEnvelope>(json) ?? new AutonomousMcpEnvelope();
        }

        private void HandleHttpRequest(HttpListenerContext context)
        {
            if (context.Request.HttpMethod != "POST" || context.Request.Url == null || context.Request.Url.AbsolutePath != "/mcp/tool")
            {
                WriteHttpResponse(context.Response, 404, "{\"success\":false,\"error\":\"Not found\"}");
                return;
            }

            string requestBody;
            using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
            {
                requestBody = reader.ReadToEnd();
            }

            var envelope = ParseEnvelope(requestBody);
            envelope.@params ??= new JObject();
            var toolResponse = AutonomousMcpToolDispatcher.Dispatch(envelope);
            var payload = JsonConvert.SerializeObject(toolResponse);
            WriteHttpResponse(context.Response, 200, payload);
        }

        private void TcpLoop()
        {
            while (_running && _tcpListener != null)
            {
                TcpClient client = null;
                try
                {
                    client = _tcpListener.AcceptTcpClient();
                    using (client)
                    using (var stream = client.GetStream())
                    using (var reader = new StreamReader(stream, Encoding.UTF8, false, 1024, true))
                    using (var writer = new StreamWriter(stream, Encoding.UTF8, 1024, true) { AutoFlush = true })
                    {
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line))
                        {
                            writer.WriteLine("{\"success\":false,\"error\":\"Empty TCP request\"}");
                            continue;
                        }

                        var envelope = ParseEnvelope(line);
                        envelope.@params ??= new JObject();
                        var toolResponse = AutonomousMcpToolDispatcher.Dispatch(envelope);
                        writer.WriteLine(JsonConvert.SerializeObject(toolResponse));
                    }
                }
                catch (SocketException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[AutonomousMCP] TCP loop error: {ex.Message}");
                    try
                    {
                        client?.Close();
                    }
                    catch
                    {
                        // ignored
                    }
                }
            }
        }
    }
}
