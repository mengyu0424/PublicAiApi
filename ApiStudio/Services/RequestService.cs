using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ApiStudio
{
    public sealed class RequestResult
    {
        public int StatusCode; public string StatusText; public string Body; public string Headers; public long ElapsedMs; public long Bytes;
    }
    public sealed class RequestService : IDisposable
    {
        private readonly HttpMessageInvoker client;
        public RequestService()
        {
            // Pass cancellation directly to the handler so it can abort reads after the headers arrive.
            client = new HttpMessageInvoker(new HttpClientHandler { AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate });
        }
        public Task<RequestResult> SendAsync(string method, string url, string headers, string body, CancellationToken token)
        { return SendAsync(method, url, headers, body, RequestTypes.Http, "", false, token); }
        public Task<RequestResult> SendAsync(ApiItem item, CancellationToken token)
        { return SendAsync(item.Method, item.Url, item.Headers, item.Body, item.RequestType, item.SoapAction, item.AutoSoapAction, token); }
        private async Task<RequestResult> SendAsync(string method, string url, string headers, string body, string requestType, string soapAction, bool automaticAction, CancellationToken token)
        {
            bool soap = RequestTypes.IsSoap(requestType);
            if (soap) method = "POST";
            Uri uri;
            if (!Uri.TryCreate(url, UriKind.Absolute, out uri) || (uri.Scheme != "http" && uri.Scheme != "https")) throw new ArgumentException("请输入完整的 HTTP 或 HTTPS 地址。");
            if (method != "GET" && method != "POST" && method != "PUT") throw new ArgumentException("仅支持 GET、POST、PUT。");
            using (var request = new HttpRequestMessage(new HttpMethod(method), uri))
            {
                if (method != "GET") request.Content = new StringContent(RequestTypes.PrepareBody(body, requestType), Encoding.UTF8, RequestTypes.MediaType(requestType));
                bool userAgentSpecified = false;
                foreach (var raw in (headers ?? "").Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (string.IsNullOrWhiteSpace(raw) || raw.TrimStart().StartsWith("#")) continue;
                    int colon = raw.IndexOf(':');
                    if (colon <= 0) throw new ArgumentException("请求头格式应为 名称: 值。错误行：" + raw);
                    string name = raw.Substring(0, colon).Trim(), value = raw.Substring(colon + 1).Trim();
                    if (name.Equals("User-Agent", StringComparison.OrdinalIgnoreCase)) userAgentSpecified = true;
                    if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("Content-Length 由程序自动计算，请移除此请求头。");
                    if (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        if (request.Content != null) request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(value);
                    }
                    else if (!request.Headers.TryAddWithoutValidation(name, value))
                    {
                        if (request.Content == null || !request.Content.Headers.TryAddWithoutValidation(name, value)) throw new ArgumentException("无法添加请求头：" + name);
                    }
                }
                // Some gateways reject requests without a client identifier.
                // Keep the user's explicit header in preference to this default.
                if (!userAgentSpecified) request.Headers.UserAgent.ParseAdd("ApiStudio/1.0");
                if (soap)
                {
                    if (automaticAction)
                    {
                        bool explicitAction = requestType == RequestTypes.Soap11 && request.Headers.Contains("SOAPAction");
                        if (requestType == RequestTypes.Soap12)
                            foreach (var parameter in request.Content.Headers.ContentType.Parameters)
                                if (parameter.Name.Equals("action", StringComparison.OrdinalIgnoreCase)) explicitAction = true;
                        soapAction = explicitAction ? "" : RequestTypes.InferSoapAction(body);
                    }
                    string action = QuoteAction(soapAction);
                    if (requestType == RequestTypes.Soap11 && (!request.Headers.Contains("SOAPAction") || !string.IsNullOrWhiteSpace(soapAction)))
                    {
                        request.Headers.Remove("SOAPAction");
                        request.Headers.TryAddWithoutValidation("SOAPAction", action);
                    }
                    else if (requestType == RequestTypes.Soap12 && !string.IsNullOrWhiteSpace(soapAction))
                    {
                        var contentType = request.Content.Headers.ContentType;
                        var previous = new System.Collections.Generic.List<NameValueHeaderValue>();
                        foreach (var parameter in contentType.Parameters)
                            if (parameter.Name.Equals("action", StringComparison.OrdinalIgnoreCase)) previous.Add(parameter);
                        foreach (var parameter in previous) contentType.Parameters.Remove(parameter);
                        contentType.Parameters.Add(new NameValueHeaderValue("action", action));
                    }
                }
                var watch = Stopwatch.StartNew();
                using (var requestCancellation = CancellationTokenSource.CreateLinkedTokenSource(token))
                using (var response = await client.SendAsync(request, requestCancellation.Token).ConfigureAwait(false))
                using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                using (var buffer = new MemoryStream())
                {
                    byte[] chunk = new byte[8192]; int read;
                    try
                    {
                        while ((read = await stream.ReadAsync(chunk, 0, chunk.Length, token).ConfigureAwait(false)) != 0)
                        {
                            if (buffer.Length + read > 16 * 1024 * 1024) throw new InvalidOperationException("响应超过 16 MB 显示上限，请使用下载工具获取大文件。");
                            buffer.Write(chunk, 0, read);
                        }
                        token.ThrowIfCancellationRequested();
                    }
                    catch (Exception ex) when (token.IsCancellationRequested &&
                        (ex is IOException || ex is WebException || ex is ObjectDisposedException))
                    {
                        throw new OperationCanceledException("请求已取消。", ex, token);
                    }
                    var encoding = Encoding.UTF8;
                    string charset = response.Content.Headers.ContentType == null ? null : response.Content.Headers.ContentType.CharSet;
                    if (!string.IsNullOrEmpty(charset)) { try { encoding = Encoding.GetEncoding(charset.Trim('"')); } catch (ArgumentException) { } }
                    string output = encoding.GetString(buffer.ToArray()).TrimStart('\uFEFF');
                    watch.Stop();
                    return new RequestResult { StatusCode = (int)response.StatusCode, StatusText = response.ReasonPhrase, Body = output, Headers = response.Headers + "" + response.Content.Headers, ElapsedMs = watch.ElapsedMilliseconds, Bytes = buffer.Length };
                }
            }
        }
        private static string QuoteAction(string value)
        {
            string action = (value ?? "").Trim();
            if (action.Length >= 2 && action[0] == '"' && action[action.Length - 1] == '"') action = action.Substring(1, action.Length - 2);
            foreach (char c in action)
                if (char.IsControl(c) || c == '"' || c == '\\') throw new ArgumentException("Action 不能包含控制字符、双引号或反斜杠，请填写服务文档中的 Action 地址。");
            return "\"" + action + "\"";
        }
        public void Dispose() { client.Dispose(); }
    }
}
