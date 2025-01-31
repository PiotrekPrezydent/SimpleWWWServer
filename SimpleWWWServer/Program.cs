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
                Thread serverThread = new Thread(() => StartServer(server.Port, server.BaseDir, server.AllowedExtensions,server.DownloadableExtensions));
                serverThread.Start();
            }
        }

        static void StartServer(int port, string baseDir, string[] allowedExtensions, string[] downloadableExtensions)
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
                    Thread clientThread = new Thread(() => HandleClient(client, baseDir, allowedExtensions,downloadableExtensions));
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

        static void HandleClient(TcpClient client, string baseDir, string[] allowedExtensions, string[] downloadableExtensions)
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
                    RedirectToErrorPage(stream, 400,baseDir);
                    return;
                }

                string method = requestLine[0];
                string path = requestLine[1];

                if (method != "GET" && method != "HEAD")
                {
                    RedirectToErrorPage(stream, 405,baseDir);
                    return;
                }

                string sanitizedPath = Path.GetFullPath(Path.Combine(baseDir, path.TrimStart('/').Replace('/', Path.DirectorySeparatorChar)));

                if (!sanitizedPath.StartsWith(Path.GetFullPath(baseDir)))
                {
                    RedirectToErrorPage(stream, 403, baseDir);
                    return;
                }

                //if we requesting directory try to renturn index.html if it doesnt exist we list all files
                if (Directory.Exists(sanitizedPath))
                {
                    string indexPath = Path.Combine(sanitizedPath, "index.html");
                    if (File.Exists(indexPath))
                        sanitizedPath = indexPath;
                    else
                    {
                        GenerateFileExplorerPage(stream, sanitizedPath, path.TrimStart('/'));
                        return;
                    }
                }

                string fileExtension = Path.GetExtension(sanitizedPath).ToLower();


                if (!File.Exists(sanitizedPath) || Array.IndexOf(allowedExtensions, fileExtension) == -1)
                {
                    RedirectToErrorPage(stream,404,baseDir);
                    return;
                }

                string contentType = GetContentType(sanitizedPath);
                byte[] body = method == "GET" ? File.ReadAllBytes(sanitizedPath) : new byte[1];
                SendResponse(stream, 200, "OK", body, contentType,sanitizedPath,downloadableExtensions);
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

        static void RedirectToErrorPage(NetworkStream stream, int statusCode,string baseDir)
        {
            var path = Path.Combine(baseDir, "errorpages", statusCode.ToString()+".html");
            byte[] body;
            string contentType = GetContentType(path);
            if (!File.Exists(path))
            {
                string errorPage = $"<html><head></head><body>ERROR: {statusCode}</body></html>";
                body = Encoding.UTF8.GetBytes(errorPage);
            }
            else
                body = File.ReadAllBytes(path);

            SendResponse(stream, statusCode, "Error", body, contentType);
        }

        static void SendResponse(NetworkStream stream, int statusCode, string statusMessage, byte[] body = null!, string contentType = "text/plain", string filePath = "", params string[] downloadableExtensions)
        {
            StringBuilder response = new StringBuilder();
            response.AppendLine($"HTTP/1.1 {statusCode} {statusMessage}");
            response.AppendLine($"Content-Type: {contentType}");

            if (ShouldDownloadFile(filePath,downloadableExtensions))
                response.AppendLine("Content-Disposition: attachment");

            if (body != null)
                response.AppendLine($"Content-Length: {body.Length}");

            response.AppendLine();

            byte[] headers = Encoding.UTF8.GetBytes(response.ToString());
            stream.Write(headers, 0, headers.Length);

            if (body != null)
            {
                stream.Write(body, 0, body.Length);
            }
        }

        static bool ShouldDownloadFile(string filePath, string[] downloadableExtensions)
        {
            string extension = Path.GetExtension(filePath).ToLower();

            return downloadableExtensions.Contains(extension);
        }

        static void GenerateFileExplorerPage(NetworkStream stream, string directoryPath, string relativePath)
        {
            try
            {
                // Pobieramy wszystkie foldery i pliki w danym katalogu
                string[] directories = Directory.GetDirectories(directoryPath);
                string[] files = Directory.GetFiles(directoryPath);

                string parentDirectory = Path.GetDirectoryName(directoryPath);
                string parentDirectoryRelativePath = string.IsNullOrEmpty(relativePath) ? "/" : Path.GetDirectoryName(relativePath);

                string htmlContent = "<html><head><title>File Explorer</title></head><body>";

                if (!string.IsNullOrEmpty(parentDirectory))
                {
                    htmlContent += $"<h1>File Explorer: {relativePath}</h1>";
                    htmlContent += $"<a href=\"/{parentDirectoryRelativePath}\">.. (Parent Directory)</a><br><br>";
                }

                if (directories.Length > 0)
                {
                    htmlContent += "<h3>Directories:</h3><ul>";
                    foreach (var dir in directories)
                    {
                        string dirName = Path.GetFileName(dir);
                        string relativeDirPath = Path.Combine(relativePath, dirName);
                        htmlContent += $"<li><a href=\"/{relativeDirPath}\">[DIR] {dirName}</a></li>";
                    }
                    htmlContent += "</ul>";
                }

                if (files.Length > 0)
                {
                    htmlContent += "<h3>Files:</h3><ul>";
                    foreach (var file in files)
                    {
                        string fileName = Path.GetFileName(file);
                        string relativeFilePath = Path.Combine(relativePath, fileName);
                        htmlContent += $"<li><a href=\"/{relativeFilePath}\">{fileName}</a></li>";
                    }
                    htmlContent += "</ul>";
                }
                else
                {
                    htmlContent += "<p>No files found.</p>";
                }

                htmlContent += "</body></html>";

                byte[] body = Encoding.UTF8.GetBytes(htmlContent);
                SendResponse(stream, 200, "OK", body, "text/html");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error generating file explorer page: {ex.Message}");
            }
        }

        static string GetContentType(string filePath)
        {
            string extension = Path.GetExtension(filePath).ToLower();
            return extension switch
            {
                // Dokumenty
                ".html" => "text/html",
                ".css" => "text/css",
                ".js" => "application/javascript",
                ".json" => "application/json",
                ".xml" => "application/xml",
                ".csv" => "text/csv",
                ".txt" => "text/plain",
                ".pdf" => "application/pdf",

                // Obrazy
                ".png" => "image/png",
                ".jpg" or ".jpeg" => "image/jpeg",
                ".gif" => "image/gif",
                ".svg" => "image/svg+xml",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",

                // Czcionki
                ".woff" => "font/woff",
                ".woff2" => "font/woff2",
                ".ttf" => "font/ttf",
                ".otf" => "font/otf",
                ".eot" => "application/vnd.ms-fontobject",

                // Wideo i audio
                ".mp4" => "video/mp4",
                ".webm" => "video/webm",
                ".ogg" => "audio/ogg",
                ".mp3" => "audio/mpeg",

                // Archiwa
                ".zip" => "application/zip",
                ".tar" => "application/x-tar",
                ".rar" => "application/vnd.rar",
                ".7z" => "application/x-7z-compressed",

                // Domyślna wartość dla nieznanych typów
                _ => "application/octet-stream",
            };
        }
    }

}