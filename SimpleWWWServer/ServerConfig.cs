namespace SimpleWWWServer
{
    internal class ServerConfig
    {
        public int Port { get; set; }
        public string BaseDir { get; set; }

        public string[] AllowedExtensions { get; set; }

        public string[] DownloadableExtensions { get; set; }
    }
}
