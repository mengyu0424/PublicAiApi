using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ApiStudio
{
    public sealed class Workspace
    {
        public int Version { get; set; }
        public List<ApiItem> Items { get; set; }
        public Workspace() { Version = 1; Items = new List<ApiItem>(); }
    }

    public sealed class WsParam
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public WsParam() { Name = ""; Value = ""; }
    }

    public sealed class ApiItem
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public bool IsFolder { get; set; }
        public List<ApiItem> Children { get; set; }
        public string Method { get; set; }
        public string RequestType { get; set; }
        public string SoapAction { get; set; }
        public bool AutoSoapAction { get; set; }
        public string WsMethod { get; set; }
        public string WsNamespace { get; set; }
        public List<WsParam> WsParams { get; set; }
        public string Status { get; set; }
        public string Url { get; set; }
        public string Headers { get; set; }
        public string Body { get; set; }
        public string Response { get; set; }
        public string ResponseHeaders { get; set; }
        public int StatusCode { get; set; }
        public string StatusText { get; set; }
        public long ElapsedMs { get; set; }
        public long ResponseBytes { get; set; }
        public string LastRun { get; set; }
        public ApiItem()
        {
            Id = Guid.NewGuid().ToString("N"); Children = new List<ApiItem>();
            Name = "新建接口"; Method = "GET"; Status = "开发中"; Url = ""; Headers = "Accept: application/json";
            RequestType = RequestTypes.Http; SoapAction = "";
            WsMethod = ""; WsNamespace = RequestTypes.DefaultSoapNamespace; WsParams = new List<WsParam>();
            Body = ""; Response = ""; ResponseHeaders = ""; StatusText = ""; LastRun = "";
        }
    }

    public static class ApiStatus
    {
        public static string[] Values { get { return new[] { "开发中", "测试中", "调试中", "已完成" }; } }
        public static string Normalize(string value)
        { return Array.IndexOf(Values, value) >= 0 ? value : "开发中"; }
    }

    public static class JsonText
    {
        private const int MaximumJsonLength = 32 * 1024 * 1024;
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            MaxDepth = 128,
            PropertyNameCaseInsensitive = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        private static void CheckLength(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (text.Length > MaximumJsonLength) throw new InvalidDataException("JSON 内容超过 32 MB 字符上限。");
        }
        public static string Serialize<T>(T value)
        {
            string text = JsonSerializer.Serialize(value, Options);
            CheckLength(text);
            return text;
        }
        public static T Deserialize<T>(string text)
        {
            CheckLength(text);
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        private static void ValidateJson(string text)
        {
            CheckLength(text);
            using (JsonDocument.Parse(text, new JsonDocumentOptions { MaxDepth = 128 })) { }
        }
        public static string Format(string text)
        {
            // Validate first; the formatter preserves number precision and property order.
            ValidateJson(text);
            var result = new StringBuilder();
            bool quoted = false, escaped = false;
            int depth = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (quoted)
                {
                    result.Append(c);
                    if (escaped) escaped = false;
                    else if (c == '\\') escaped = true;
                    else if (c == '"') quoted = false;
                    continue;
                }
                if (char.IsWhiteSpace(c)) continue;
                if (c == '"') { quoted = true; result.Append(c); }
                else if (c == '{' || c == '[')
                {
                    result.Append(c); depth++;
                    int next = i + 1; while (next < text.Length && char.IsWhiteSpace(text[next])) next++;
                    if (next < text.Length && (text[next] == '}' || text[next] == ']')) { result.Append(text[next]); depth--; i = next; }
                    else NewLine(result, depth);
                }
                else if (c == '}' || c == ']') { depth--; NewLine(result, depth); result.Append(c); }
                else if (c == ',') { result.Append(c); NewLine(result, depth); }
                else if (c == ':') result.Append(": ");
                else result.Append(c);
            }
            return result.ToString();
        }
        public static string Compact(string text)
        {
            ValidateJson(text);
            var result = new StringBuilder(); bool quoted = false, escaped = false;
            foreach (char c in text)
            {
                if (quoted || !char.IsWhiteSpace(c)) result.Append(c);
                if (escaped) escaped = false;
                else if (quoted && c == '\\') escaped = true;
                else if (c == '"') quoted = !quoted;
            }
            return result.ToString();
        }
        private static void NewLine(StringBuilder output, int depth) { output.Append(Environment.NewLine); output.Append(' ', Math.Max(0, depth) * 2); }
    }

    public sealed class WorkspaceStore
    {
        public string FilePath { get; private set; }
        public WorkspaceStore(string path) { FilePath = path; }
        public Workspace Load()
        {
            if (!File.Exists(FilePath)) return new Workspace();
            return Read(FilePath);
        }
        public static Workspace Read(string path)
        {
            var workspace = JsonText.Deserialize<Workspace>(File.ReadAllText(path, Encoding.UTF8));
            if (workspace == null || workspace.Items == null || workspace.Version != 1) throw new InvalidDataException("不是受支持的 API Studio 工作空间文件。");
            int count = 0; Validate(workspace.Items, 0, ref count);
            return workspace;
        }
        private static void Validate(List<ApiItem> items, int depth, ref int count)
        {
            if (depth > 32) throw new InvalidDataException("文件夹层级过深。");
            foreach (var item in items)
            {
                if (item == null || ++count > 10000) throw new InvalidDataException("接口数据无效或数量过多。");
                if (string.IsNullOrWhiteSpace(item.Name)) item.Name = item.IsFolder ? "未命名文件夹" : "未命名接口";
                if (item.Method != "GET" && item.Method != "POST" && item.Method != "PUT") throw new InvalidDataException("存在不支持的请求方法。");
                if (item.Children == null) item.Children = new List<ApiItem>();
                item.RequestType = RequestTypes.Normalize(item.RequestType);
                item.SoapAction = item.SoapAction ?? "";
                item.WsMethod = item.WsMethod ?? "";
                item.WsNamespace = string.IsNullOrWhiteSpace(item.WsNamespace) ? RequestTypes.DefaultSoapNamespace : item.WsNamespace;
                if (item.WsParams == null) item.WsParams = new List<WsParam>();
                else item.WsParams = item.WsParams.FindAll(p => p != null);
                if (RequestTypes.IsSoap(item.RequestType))
                {
                    item.Method = "POST";
                    // 兼容旧版手填 XML：没有表格数据时尝试从请求体解析出方法、命名空间和参数。
                    if (string.IsNullOrWhiteSpace(item.WsMethod) && item.WsParams.Count == 0 && !string.IsNullOrWhiteSpace(item.Body))
                    {
                        string parsedMethod, parsedNamespace;
                        RequestTypes.ParseMethodXml(item.Body, out parsedMethod, out parsedNamespace, item.WsParams);
                        item.WsMethod = parsedMethod; item.WsNamespace = parsedNamespace;
                    }
                }
                item.Status = ApiStatus.Normalize(item.Status);
                Validate(item.Children, depth + 1, ref count);
            }
        }
        public void Save(Workspace workspace) { Write(FilePath, workspace); }
        public static void Write(string path, Workspace workspace)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(path));
            Directory.CreateDirectory(directory);
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonText.Format(JsonText.Serialize(workspace)), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
        public static Workspace CreateDemo()
        {
            var users = new ApiItem { Name = "用户管理", IsFolder = true };
            users.Children.Add(new ApiItem { Name = "获取用户列表", Url = "https://jsonplaceholder.typicode.com/users", Response = JsonText.Format("{\"message\":\"欢迎使用 API Studio\",\"note\":\"这是界面预览数据，点击发送获取真实响应。\",\"data\":[{\"id\":1,\"name\":\"张三\",\"email\":\"zhangsan@example.com\",\"role\":\"admin\"},{\"id\":2,\"name\":\"李四\",\"email\":\"lisi@example.com\",\"role\":\"member\"}]}"), StatusText = "示例预览" });
            users.Children.Add(new ApiItem { Name = "创建用户", Method = "POST", Url = "https://jsonplaceholder.typicode.com/users", Headers = "Accept: application/json\r\nContent-Type: application/json", Body = JsonText.Format("{\"name\":\"张三\",\"email\":\"zhangsan@example.com\",\"username\":\"zhangsan\"}") });
            users.Children.Add(new ApiItem { Name = "更新用户", Method = "PUT", Url = "https://jsonplaceholder.typicode.com/users/1", Headers = "Accept: application/json\r\nContent-Type: application/json", Body = JsonText.Format("{\"id\":1,\"name\":\"张三\",\"email\":\"new@example.com\"}") });
            var test = new ApiItem { Name = "调试工具", IsFolder = true };
            test.Children.Add(new ApiItem { Name = "回显请求参数", Url = "https://httpbin.org/get?source=api-studio&hello=world" });
            test.Children.Add(new ApiItem { Name = "提交 JSON 数据", Method = "POST", Url = "https://httpbin.org/post", Headers = "Content-Type: application/json", Body = "{\r\n  \"hello\": \"world\"\r\n}" });
            var workspace = new Workspace(); workspace.Items.Add(users); workspace.Items.Add(test); return workspace;
        }
    }
}
