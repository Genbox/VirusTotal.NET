﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using RestSharp;
using RestSharp.Deserializers;
using VirusTotalNET.Exceptions;
using VirusTotalNET.Objects;

namespace VirusTotalNET
{
    public partial class VirusTotal
    {
        private readonly RestClient _client = new RestClient();
        private readonly string _apiKey;
        private bool _useTls;
        private const long FileSizeLimit = 33554432; //32 MB
        private int _retryCounter;
        private int _retry;

        /// <summary>
        /// Public constructor for VirusTotal.
        /// </summary>
        /// <param name="apiKey">The API key you got from Virus Total</param>
        /// <exception cref="ArgumentException"></exception>
        public VirusTotal(string apiKey)
        {
            if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 64)
                throw new ArgumentException("You have to set an API key.", "apiKey");

            _apiKey = apiKey;
            // Taking care of RestSharp 104.5->105.0 
            // https://github.com/restsharp/RestSharp/commit/b15ee7f60b695e0578b6def7a3e1279b62d6fccd
            _client.BaseUrl = new Uri("http://www.virustotal.com/vtapi/v2/");
            _client.FollowRedirects = false;

            Retry = 3;
        }

        /// <summary>
        /// Set to true to use HTTPS instead of HTTP. HTTP is used by default.
        /// </summary>
        public bool UseTLS
        {
            get { return _useTls; }
            set
            {
                _useTls = value;
                // Taking care of RestSharp 104.5->105.0 
                // https://github.com/restsharp/RestSharp/commit/b15ee7f60b695e0578b6def7a3e1279b62d6fccd
                _client.BaseUrl = value ? new Uri(_client.BaseUrl.ToString().Replace("http://", "https://")) : new Uri(_client.BaseUrl.ToString().Replace("https://", "http://"));
            }
        }

        /// <summary>
        /// Get or set the proxy.
        /// </summary>
        public IWebProxy Proxy { get { return _client.Proxy; } set { _client.Proxy = value; } }

        /// <summary>
        /// The number of retries to attempt if an serialization error happens.
        /// It is set to 3 by default.
        /// </summary>
        public int Retry
        {
            get { return _retry; }
            set
            {
                _retry = value;
                _retryCounter = value;
            }
        }

        /// <summary>
        /// Get or set the timeout.
        /// </summary>
        public int Timeout { get { return _client.Timeout; } set { _client.Timeout = value; } }

        /// <summary>
        /// Scan a file.
        /// Note: It is highly encouraged to get the report of the file before scanning, in case it has already been scanned before.
        /// </summary>
        /// <param name="file">The file to scan</param>
        /// <returns>The scan results.</returns>
        public ScanResult ScanFile(FileInfo file)
        {
            if (!file.Exists)
                throw new FileNotFoundException("The file was not found.", file.Name);

            byte[] fileContent = File.ReadAllBytes(file.FullName);
            return ScanFile(fileContent, file.Name);
        }

        /// <summary>
        /// Scan a file.
        /// Note: It is highly encouraged to get the report of the file before scanning, in case it has already been scanned before.
        /// Note: Ýou are also strongly encouraged to provide the filename as it is rich metadata for the Virus Total database.
        /// </summary>
        /// <param name="file">The file to scan</param>
        /// <param name="filename">The filename of the file</param>
        /// <returns>The scan results.</returns>
        public ScanResult ScanFile(byte[] file, string filename)
        {
            if (file == null || file.Length <= 0)
                throw new ArgumentException("You must provide a file", "file");

            if (file.Length > FileSizeLimit)
                throw new SizeLimitException("The filesize limit on VirusTotal is 32 MB. Your file is " + file.Length / 1024 / 1024 + " MB");

            if (string.IsNullOrWhiteSpace(filename))
                throw new ArgumentException("You must provide a filename. Preferably the original filename.");

            //https://www.virustotal.com/vtapi/v2/file/scan
            RestRequest request = PrepareRequest("file/scan");
            request.AddFile("file", file, filename);

            //Output
            return GetResults<ScanResult>(request);
        }

        /// <summary>
        /// Scan multiple files.
        /// Note: It is highly encouraged to get the report of the files before scanning, in case it they already been scanned before.
        /// Note: Ýou are also strongly encouraged to provide the filename as it is rich metadata for the Virus Total database.
        /// </summary>
        /// <param name="files">The files you wish to scan. They are a tuple of file content and filename.</param>
        /// <returns>The scan results.</returns>
        public IEnumerable<ScanResult> ScanFiles(IEnumerable<Tuple<byte[], string>> files)
        {
            foreach (Tuple<byte[], string> fileInfo in files)
            {
                yield return ScanFile(fileInfo.Item1, fileInfo.Item2);
            }
        }

        /// <summary>
        /// Scan multiple files.
        /// Note: It is highly encouraged to get the report of the files before scanning, in case it they already been scanned before.
        /// </summary>
        /// <param name="files">The files you wish to scan.</param>
        /// <returns>The scan results.</returns>
        public IEnumerable<ScanResult> ScanFiles(IEnumerable<FileInfo> files)
        {
            foreach (FileInfo fileInfo in files)
            {
                yield return ScanFile(fileInfo);
            }
        }

        /// <summary>
        /// Tell VirusTotal to rescan a file without sending the actual file to VirusTotal.
        /// Note: Before requesting a rescan you should retrieve the latest report on the file.
        /// </summary>
        /// <param name="resource">A hash of the file. It can be an MD5, SHA1 or SHA256</param>
        /// <returns>The scan results.</returns>
        public ScanResult RescanFile(string resource)
        {
            return RescanFiles(new[] { resource }).FirstOrDefault();
        }

        /// <summary>
        /// Tell VirusTotal to rescan a file without sending the actual file to VirusTotal.
        /// Note: Before requesting a rescan you should retrieve the latest report on the file.
        /// </summary>
        /// <returns>The scan results.</returns>
        public ScanResult RescanFile(FileInfo file)
        {
            return RescanFiles(new[] { file }).FirstOrDefault();
        }

        /// <summary>
        /// Tell VirusTotal to rescan a file without sending the actual file to VirusTotal.
        /// Note: Before requesting a rescan you should retrieve the latest report on the file.
        /// </summary>
        /// <returns>The scan results.</returns>
        public ScanResult RescanFile(byte[] file)
        {
            return RescanFiles(new[] { file }).FirstOrDefault();
        }

        /// <summary>
        /// Tell VirusTotal to rescan a file.
        /// Note: This does not send the files to VirusTotal. It hashes the file and sends that instead.
        /// Note: Before requesting a rescan you should retrieve the latest report on the files.
        /// </summary>
        /// <returns>The scan results.</returns>
        public List<ScanResult> RescanFiles(IEnumerable<FileInfo> files)
        {
            return RescanFiles(GetResourcesFromFiles(files));
        }

        /// <summary>
        /// Tell VirusTotal to rescan a file.
        /// Note: This does not send the files to VirusTotal. It hashes the file and sends that instead.
        /// Note: Before requesting a rescan you should retrieve the latest report on the files.
        /// </summary>
        /// <returns>The scan results.</returns>
        public List<ScanResult> RescanFiles(IEnumerable<byte[]> files)
        {
            return RescanFiles(GetResourcesFromFiles(files));
        }

        /// <summary>
        /// Tell VirusTotal to rescan a file.
        /// Note: This does not send the files to VirusTotal. It hashes the file and sends that instead.
        /// Note: Before requesting a rescan you should retrieve the latest report on the files.
        /// Note: You can use MD5, SHA1 or SHA256 and even mix them.
        /// Note: You can only request a maximum of 25 rescans.
        /// </summary>
        /// <param name="resourceList">a MD5, SHA1 or SHA256 of the files. You can also specify list made up of a combination of any of the three allowed hashes (up to 25 items), this allows you to perform a batch request with one single call.
        /// Note: that the files must already be present in the files store.
        /// </param>
        /// <returns>The scan results.</returns>
        public List<ScanResult> RescanFiles(IEnumerable<string> resourceList)
        {
            string[] hashes = resourceList as string[] ?? resourceList.ToArray();

            if (!hashes.Any())
                throw new Exception("You have to supply a resource.");

            if (hashes.Length > 25)
                throw new Exception("Too many hashes. There is a maximum of 25 hashes.");

            for (int i = 0; i < hashes.Length; i++)
            {
                ValidateResource(hashes[i]);
            }

            //https://www.virustotal.com/vtapi/v2/file/rescan
            RestRequest request = PrepareRequest("file/rescan");

            //Required
            request.AddParameter("resource", string.Join(",", hashes));

            //Output
            return GetResults<List<ScanResult>>(request);
        }

        /// <summary>
        /// Gets the report of the file.
        /// Note: This does not send the files to VirusTotal. It hashes the file and sends that instead.
        /// </summary>
        /// <param name="file">The file you wish to get a report on.</param>
        public Report GetFileReport(byte[] file)
        {
            return GetFileReport(HashHelper.GetSHA256(file));
        }

        /// <summary>
        /// Gets the report of the file.
        /// Note: This does not send the files to VirusTotal. It hashes the file and sends that instead.
        /// </summary>
        /// <param name="file">The file you wish to get a report on.</param>
        public Report GetFileReport(FileInfo file)
        {
            return GetFileReport(HashHelper.GetSHA256(file));
        }

        /// <summary>
        /// Gets the report of the file.
        /// Note: This does not send the files to VirusTotal. It hashes the file and sends that instead.
        /// </summary>
        /// <param name="resource">The resource (MD5, SHA1 or SHA256) you wish to get a report on.</param>
        public Report GetFileReport(string resource)
        {
            return GetFileReports(new[] { resource }).First();
        }

        /// <summary>
        /// Gets a list of reports of the files.
        /// Note: This does not send the files to VirusTotal. It hashes the files and sends them instead.
        /// </summary>
        /// <param name="files">The files you wish to get reports on.</param>
        public List<Report> GetFileReports(IEnumerable<byte[]> files)
        {
            return GetFileReports(GetResourcesFromFiles(files));
        }

        /// <summary>
        /// Gets a list of reports of the files.
        /// Note: This does not send the files to VirusTotal. It hashes the files and sends them instead.
        /// </summary>
        /// <param name="files">The files you wish to get reports on.</param>
        public List<Report> GetFileReports(IEnumerable<FileInfo> files)
        {
            return GetFileReports(GetResourcesFromFiles(files));
        }

        /// <summary>
        /// Gets the report of the file represented by its hash or scan ID.
        /// Keep in mind that URLs sent using the API have the lowest scanning priority, depending on VirusTotal's load, it may take several hours before the file is scanned,
        /// so query the report at regular intervals until the result shows up and do not keep submitting the file over and over again.
        /// </summary>
        /// <param name="resourceList">SHA1, MD5 or SHA256 of the file. It can also be a scan ID of a previous scan.</param>
        /// <returns></returns>
        public List<Report> GetFileReports(IEnumerable<string> resourceList)
        {
            string[] hashes = resourceList as string[] ?? resourceList.ToArray();

            if (!hashes.Any())
                throw new ArgumentException("You have to supply a resource.");

            for (int i = 0; i < hashes.Length; i++)
            {
                ValidateResource(hashes[i]);
            }

            //https://www.virustotal.com/vtapi/v2/file/report
            RestRequest request = PrepareRequest("file/report");

            //Required
            request.AddParameter("resource", string.Join(",", hashes));

            //Output
            return GetResults<List<Report>>(request, true);
        }

        /// <summary>
        /// Scan the given URL. The URL will be downloaded by VirusTotal and processed.
        /// Note: Before performing your submission, you should retrieve the latest report on the URL.
        /// </summary>
        /// <param name="url">The url to process.</param>
        /// <returns>The scan results.</returns>
        public ScanResult ScanUrl(string url)
        {
            return ScanUrls(UrlToUri(new[] { url })).FirstOrDefault();
        }

        /// <summary>
        /// Scan the given URL. The URL will be downloaded by VirusTotal and processed.
        /// Note: Before performing your submission, you should retrieve the latest report on the URL.
        /// </summary>
        /// <param name="url">The url to process.</param>
        /// <returns>The scan results.</returns>
        public ScanResult ScanUrl(Uri url)
        {
            return ScanUrls(new[] { url }).FirstOrDefault();
        }

        /// <summary>
        /// Scan the given URLs. The URLs will be downloaded by VirusTotal and processed.
        /// Note: Before performing your submission, you should retrieve the latest reports on the URLs.
        /// </summary>
        /// <param name="urlList">The urls to process.</param>
        /// <returns>The scan results.</returns>
        public List<ScanResult> ScanUrls(IEnumerable<string> urlList)
        {
            return ScanUrls(UrlToUri(urlList));
        }

        /// <summary>
        /// Scan the given URLs. The URLs will be downloaded by VirusTotal and processed.
        /// Note: Before performing your submission, you should retrieve the latest reports on the URLs.
        /// </summary>
        /// <param name="urlList">The urls to process.</param>
        /// <returns>The scan results.</returns>
        public List<ScanResult> ScanUrls(IEnumerable<Uri> urlList)
        {
            IEnumerable<Uri> urls = urlList as Uri[] ?? urlList.ToArray();

            if (!urls.Any())
                throw new Exception("You have to supply an URL.");

            //https://www.virustotal.com/vtapi/v2/url/scan
            RestRequest request = PrepareRequest("url/scan");

            //Required
            request.AddParameter("url", string.Join(Environment.NewLine, urls));

            //Output
            return GetResults<List<ScanResult>>(request);
        }

        /// <summary>
        /// Gets a scan report from an URL
        /// </summary>
        /// <param name="url">The URL you wish to get the report on.</param>
        /// <param name="scanIfNoReport">Set to true if you wish VirusTotal to scan the URL if it is not present in the database.</param>
        /// <returns>A list of reports</returns>
        public Report GetUrlReport(string url, bool scanIfNoReport = false)
        {
            return GetUrlReports(UrlToUri(new[] { url }), scanIfNoReport).FirstOrDefault();
        }

        /// <summary>
        /// Gets a scan report from an URL
        /// </summary>
        /// <param name="url">The URL you wish to get the report on.</param>
        /// <param name="scanIfNoReport">Set to true if you wish VirusTotal to scan the URL if it is not present in the database.</param>
        /// <returns>A list of reports</returns>
        public Report GetUrlReport(Uri url, bool scanIfNoReport = false)
        {
            return GetUrlReports(new[] { url }, scanIfNoReport).First();
        }

        /// <summary>
        /// Gets a scan report from a list of URLs
        /// </summary>
        /// <param name="urlList">The URLs you wish to get the reports on.</param>
        /// <param name="scanIfNoReport">Set to true if you wish VirusTotal to scan the URLs if it is not present in the database.</param>
        /// <returns>A list of reports</returns>
        public List<Report> GetUrlReports(IEnumerable<string> urlList, bool scanIfNoReport = false)
        {
            return GetUrlReports(UrlToUri(urlList), scanIfNoReport);
        }

        /// <summary>
        /// Gets a scan report from a list of URLs
        /// </summary>
        /// <param name="urlList">The URLs you wish to get the reports on.</param>
        /// <param name="scanIfNoReport">Set to true if you wish VirusTotal to scan the URLs if it is not present in the database.</param>
        /// <returns>A list of reports</returns>
        public List<Report> GetUrlReports(IEnumerable<Uri> urlList, bool scanIfNoReport = false)
        {
            IEnumerable<Uri> urls = urlList as Uri[] ?? urlList.ToArray();

            if (!urls.Any())
                throw new Exception("You have to supply an URL.");

            RestRequest request = PrepareRequest("url/report");

            //Required
            request.AddParameter("resource", string.Join(Environment.NewLine, urls));

            //Optional
            if (scanIfNoReport)
                request.AddParameter("scan", 1);

            //Output
            return GetResults<List<Report>>(request, true);
        }

        /// <summary>
        /// Gets a scan report from an IP
        /// </summary>
        /// <param name="ip">The IP you wish to get the report on.</param>
        /// <returns>A report</returns>
        public IPReport GetIPReport(string ip)
        {
            return GetIPReport(IPAddress.Parse(ip));
        }

        /// <summary>
        /// Gets a scan report from an IP
        /// </summary>
        /// <param name="ip">The IP you wish to get the report on.</param>
        /// <returns>A report</returns>
        public IPReport GetIPReport(IPAddress ip)
        {
            if (ip == null)
                throw new Exception("You have to supply an IP.");

            if (ip.AddressFamily != AddressFamily.InterNetwork)
                throw new Exception("Only IPv4 addresses are supported");

            RestRequest request = PrepareRequest("ip-address/report", Method.GET);

            //Required
            request.AddParameter("ip", ip.ToString());

            //Output
            return GetResults<IPReport>(request);
        }

        /// <summary>
        /// Gets a scan report from a domain
        /// </summary>
        /// <param name="domain">The domain you wish to get the report on.</param>
        /// <returns>A report</returns>
        public DomainReport GetDomainReport(string domain)
        {
            if (string.IsNullOrWhiteSpace(domain))
                throw new Exception("You have to supply a domain.");

            RestRequest request = PrepareRequest("domain/report", Method.GET);

            //Required
            request.AddParameter("domain", domain);

            //Output
            return GetResults<DomainReport>(request);
        }

        /// <summary>
        /// Creates a comment on a file denoted by its hash and/or scan ID.
        /// </summary>
        /// <param name="file">The file you wish to create a comment on</param>
        /// <param name="comment">The comment you wish to add.</param>
        /// <returns>A ScanResult object containing information about the resource.</returns>
        public ScanResult CreateComment(byte[] file, string comment)
        {
            return CreateComment(HashHelper.GetSHA256(file), comment);
        }

        /// <summary>
        /// Creates a comment on a file denoted by its hash and/or scan ID.
        /// </summary>
        /// <param name="file">The file you wish to create a comment on</param>
        /// <param name="comment">The comment you wish to add.</param>
        /// <returns>A ScanResult object containing information about the resource.</returns>
        public ScanResult CreateComment(FileInfo file, string comment)
        {
            return CreateComment(HashHelper.GetSHA256(file), comment);
        }

        /// <summary>
        /// Creates a comment on a file denoted by its hash and/or scan ID.
        /// </summary>
        /// <param name="resource">The SHA256 hash or scan ID of the resource.</param>
        /// <param name="comment">The comment you wish to add.</param>
        /// <returns>A ScanResult object containing information about the resource.</returns>
        public ScanResult CreateComment(string resource, string comment)
        {
            ValidateResource(resource);

            if (string.IsNullOrWhiteSpace(comment))
                throw new ArgumentException("Comment must not be null or whitespace", "comment");

            //https://www.virustotal.com/vtapi/v2/comments/put
            RestRequest request = PrepareRequest("comments/put");

            //Required
            request.AddParameter("resource", resource);
            request.AddParameter("comment", comment);

            //Output
            return GetResults<ScanResult>(request);
        }

        /// <summary>
        /// Gives you a link to a file analysis based on its hash.
        /// </summary>
        /// <returns>A link to VirusTotal that contains the report</returns>
        public string GetPublicFileScanLink(string resource)
        {
            ValidateResource(resource);

            return string.Format("{0}://www.virustotal.com/file/{1}/analysis/", UseTLS ? "https" : "http", resource);
        }

        /// <summary>
        /// Gives you a link to a file analysis based on its hash.
        /// </summary>
        /// <returns>A link to VirusTotal that contains the report</returns>
        public string GetPublicFileScanLink(FileInfo file)
        {
            return GetPublicFileScanLink(HashHelper.GetSHA256(file));
        }

        /// <summary>
        /// Gives you a link to a URL analysis.
        /// </summary>
        /// <returns>A link to VirusTotal that contains the report</returns>
        public string GetPublicUrlScanLink(string url)
        {
            return string.Format("{0}://www.virustotal.com/url/{1}/analysis/", UseTLS ? "https" : "http", HashHelper.GetSHA256(NormalizeUrl(url)));
        }

        private RestRequest PrepareRequest(string path, Method methodType = Method.POST)
        {
            RestRequest request = new RestRequest(path, methodType);

            //Required
            request.AddParameter("apikey", _apiKey);

            return request;
        }

        private T GetResults<T>(RestRequest request, bool applyHack = false)
        {
            RestResponse response = (RestResponse)_client.Execute(request);

            if (response.StatusCode == HttpStatusCode.NoContent)
                throw new RateLimitException("You have reached the 5 requests pr. min. limit of VirusTotal");

            if (response.StatusCode == HttpStatusCode.Forbidden)
                throw new AccessDeniedException("You don't have access to the service. Make sure your API key is working correctly.");

            if (response.ErrorException != null)
                throw response.ErrorException;

            if (response.StatusCode != HttpStatusCode.OK)
                throw new Exception("API gave error code " + response.StatusCode);

            if (string.IsNullOrWhiteSpace(response.Content))
                throw new Exception("There were no content in the response.");

            if (applyHack)
            {
                //Warning: Huge hack... sorry :(
                response.Content = Regex.Replace(response.Content, "\"([\\w\\d -\\._]+)\": \\{\"detected\":", "{\"name\": \"$1\", \"detected\":", RegexOptions.Compiled | RegexOptions.CultureInvariant);
                response.Content = response.Content.Replace("scans\": {", "scans\": [");
                response.Content = response.Content.Replace("}}", "}]");
            }

            IDeserializer deserializer = new JsonDeserializer();
            T results;

            try
            {
                results = deserializer.Deserialize<T>(response);
            }
            catch (SerializationException)
            {
                //retry request.
                try
                {
                    _retryCounter--;

                    if (_retryCounter <= 0)
                    {
                        _retryCounter = Retry;
                        return default(T);
                    }
                    results = GetResults<T>(request, applyHack);
                }
                catch (SerializationException ex)
                {
                    throw new Exception("Failed to deserialize request.", ex);
                }
            }

            //reset retry counter
            _retryCounter = Retry;

            return results;
        }

        private string NormalizeUrl(string url)
        {
            if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                url = "http://" + url;

            return new Uri(url).ToString();
        }

        private IEnumerable<string> GetResourcesFromFiles(IEnumerable<FileInfo> files)
        {
            foreach (FileInfo fileInfo in files)
            {
                yield return HashHelper.GetSHA256(fileInfo);
            }
        }

        private IEnumerable<string> GetResourcesFromFiles(IEnumerable<byte[]> files)
        {
            foreach (byte[] fileBytes in files)
            {
                yield return HashHelper.GetSHA256(fileBytes);
            }
        }

        private IEnumerable<Uri> UrlToUri(IEnumerable<string> urls)
        {
            foreach (string url in urls)
            {
                Uri uri;
                try
                {
                    string tempUri = url.Trim();

                    if (!tempUri.StartsWith("http://") && !tempUri.StartsWith("https://"))
                        tempUri = "http://" + tempUri;

                    uri = new Uri(tempUri);
                }
                catch (Exception ex)
                {
                    throw new Exception("There was an error converting " + url + " to an uri. See InnerException for details.", ex);
                }

                yield return uri;
            }
        }

        private void ValidateResource(string resource)
        {
            if (string.IsNullOrWhiteSpace(resource))
                throw new ArgumentException("Resource must not be null or whitespace", "resource");

            if (resource.Length != 32 && resource.Length != 40 && resource.Length != 64 && resource.Length != 75)
                throw new InvalidResourceException("Resource " + resource + " has to be either a MD5, SHA1, SHA256 or scan id");
        }
    }
}
