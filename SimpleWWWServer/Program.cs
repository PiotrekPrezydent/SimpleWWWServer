using System.Net.Sockets;
using System.Net;
using System.Text;
using System.Text.Json;

namespace SimpleWWWServer
{
    internal class Program
    {

        static void Main(string[] args)
        {
            string configFile = "";
#if DEBUG
            configFile = "config.json";
#else
            if (args.Length != 1)
            {
                Console.WriteLine("Run program with path to configuration file as argument");
                return;
            }
            configFile = args[0];
            if (!File.Exists(configFile))
            {
                Console.WriteLine($"Configuration file {configFile} not found.");
                return;
            }
#endif
            string configJson = File.ReadAllText(configFile);
            var config = JsonSerializer.Deserialize<Config>(configJson);

            foreach (var server in config!.Servers)
            {
                Thread serverThread = new Thread(() => StartServer(server.Port, server.BaseDir, server.AllowedExtensions));
                serverThread.Start();
            }
        }

        static void StartServer(int port, string baseDir, string[] allowedExtensions)
        {
            if (!Directory.Exists(baseDir))
                Directory.CreateDirectory(baseDir);
            //start listening on given port
            TcpListener listener = new TcpListener(IPAddress.Any, port);
            listener.Start();
            Console.WriteLine($"Server listening on port {port}...");

            try
            {
                //await any request that is inceming from given port
                while (true)
                {
                    TcpClient client = listener.AcceptTcpClient();
                    //handle request
                    Thread clientThread = new Thread(() => HandleClient(client, baseDir, allowedExtensions));
                    clientThread.Start();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                listener.Stop();
            }
        }

        static void HandleClient(TcpClient client, string baseDir, string[] allowedExtensions)
        {
            try
            {
                NetworkStream stream = client.GetStream();

                byte[] buffer = new byte[4096];
                int bytesRead = stream.Read(buffer, 0, buffer.Length);
                string request = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                Console.WriteLine("Received request:\n" + request);

                string[] lines = request.Split('\n');

                if (lines.Length <= 0)
                    return;

                string[] requestLine = lines[0].Split(' ');

                if (requestLine.Length < 3)
                {
                    RedirectToCatErrorPage(stream, 400);
                    return;
                }

                string method = requestLine[0];
                string path = requestLine[1];

                if (method != "GET" && method != "HEAD")
                {
                    RedirectToCatErrorPage(stream, 405);
                    return;
                }

                string sanitizedPath = Path.Combine(baseDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
                //if we requesting directory return index.html, else we try to get that file
                if (Directory.Exists(sanitizedPath))
                    sanitizedPath = Path.Combine(sanitizedPath, "index.html");

                string fileExtension = Path.GetExtension(sanitizedPath).ToLower();
                if (!File.Exists(sanitizedPath) || Array.IndexOf(allowedExtensions, fileExtension) == -1)
                {
                    RedirectToCatErrorPage(stream,404);
                    return;
                }

                string contentType = GetContentType(sanitizedPath);
                byte[] body = method == "GET" ? File.ReadAllBytes(sanitizedPath) : new byte[1];
                SendResponse(stream, 200, "OK", body, contentType);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling client: {ex.Message}");
            }
            finally
            {
                client.Close();
            }
        }

        static void RedirectToCatErrorPage(NetworkStream stream, int statusCode)
        {
            string errorPage = $"<html><head><meta http-equiv='refresh' content='0; url=https://http.cat/{statusCode}'></head><body></body></html>";
            byte[] body = Encoding.UTF8.GetBytes(errorPage);

            StringBuilder response = new StringBuilder();
            response.AppendLine($"HTTP/1.1 {statusCode} Error");
            response.AppendLine("Content-Type: text/html");
            response.AppendLine($"Content-Length: {body.Length}");
            response.AppendLine();

            byte[] headers = Encoding.UTF8.GetBytes(response.ToString());
            stream.Write(headers, 0, headers.Length);
            stream.Write(body, 0, body.Length);
        }

        static void SendResponse(NetworkStream stream, int statusCode, string statusMessage, byte[] body = null, string contentType = "text/plain")
        {
            StringBuilder response = new StringBuilder();
            response.AppendLine($"HTTP/1.1 {statusCode} {statusMessage}");
            response.AppendLine($"Content-Type: {contentType}");
            if (body != null)
            {
                response.AppendLine($"Content-Length: {body.Length}");
            }
            response.AppendLine();

            byte[] headers = Encoding.UTF8.GetBytes(response.ToString());
            stream.Write(headers, 0, headers.Length);

            if (body != null)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        static string GetContentType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".txt" => "text/plain",
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                _ => "application/octet-stream",
            };
        }
    }

}