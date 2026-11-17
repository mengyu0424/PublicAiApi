using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XmlDocument = System.Xml.XmlDocument;
using XmlDeclaration = System.Xml.XmlDeclaration;
using ApiStudio;

internal static class SmokeTests
{
    private static int passed;
    private static void Check(bool condition, string message)
    { if (!condition) throw new Exception("FAIL: " + message); passed++; Console.WriteLine("PASS: " + message); }
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            JsonAndStorage(); XmlFormatting(); SoapBodies(); WsTable(); WsAddress(); SoapActions(); CollectionOperations(); Network().GetAwaiter().GetResult();
            if (args.Length == 1) LegacyWorkspace(args[0]);
            Console.WriteLine("All " + passed + " checks passed."); return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void JsonAndStorage()
    {
        string raw = "{\"中文\":\"引号\\\"与反斜杠\\\\\",\"big\":123456789012345678901234567890,\"a\":[true,null,{},[]],\"n\":1.234567890123456789}";
        string pretty = JsonText.Format(raw);
        Check(pretty.Contains("\r\n") && pretty.Contains("中文"), "JSON indents and preserves Unicode");
        Check(JsonText.Compact(pretty) == raw, "JSON preserves numeric precision and escapes");
        Check(JsonText.Format(" [ ] ") == "[]", "empty arrays format correctly");
        Check(JsonText.Format("true") == "true", "JSON scalar supported");
        bool rejected = false; try { JsonText.Format("{not json}"); } catch { rejected = true; } Check(rejected, "invalid JSON rejected");
        foreach (string invalid in new[] { "{\"a\":1,}", "/* comment */ {}", "[1] true" })
        {
            rejected = false; try { JsonText.Format(invalid); } catch { rejected = true; }
            Check(rejected, "JSON validation rejects nonstandard input: " + invalid);
        }
        const string scientific = "{\"n\":-1.234567890123456789e+100,\"text\":\"\\u4e2d\\u6587\\ud83d\\ude00\"}";
        Check(JsonText.Compact(JsonText.Format(scientific)) == scientific, "the new JSON parser keeps scientific notation and Unicode escapes verbatim");
        string folder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "scratch-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new WorkspaceStore(Path.Combine(folder, "workspace.json"));
            Check(store.Load().Items.Count == 0, "first launch starts with empty workspace"); var data = WorkspaceStore.CreateDemo(); Check(data.Items.Count == 2 && data.Items[0].Children.Count == 3, "demo folders and all three methods");
            data.Items[0].Children[1].Body = raw; data.Items[0].Children[1].Response = "测试响应";
            for (int i = 0; i < ApiStatus.Values.Length; i++) data.Items.Add(new ApiItem { Name = "状态" + i, Status = ApiStatus.Values[i] });
            data.Items.Add(new ApiItem { Name = "WebService", Method = "POST", RequestType = RequestTypes.Soap12,
                SoapAction = "urn:service/调用", AutoSoapAction = true, Body = RequestTypes.Envelope(RequestTypes.Soap12) });
            store.Save(data); var loaded = store.Load();
            var soap = loaded.Items[loaded.Items.Count - 1];
            Check(soap.RequestType == RequestTypes.Soap12 && soap.SoapAction == "urn:service/调用" && soap.AutoSoapAction && soap.Body == RequestTypes.Envelope(RequestTypes.Soap12),
                "WebService type, Action and XML survive save/export and reload");
            for (int i = 0; i < ApiStatus.Values.Length; i++) Check(loaded.Items[2 + i].Status == ApiStatus.Values[i], "workflow status survives export and reload: " + ApiStatus.Values[i]);
            Check(loaded.Items[0].Children[1].Body == raw && loaded.Items[0].Children[1].Response == "测试响应", "request and response persistence round trip");
            store.Save(loaded); Check(File.Exists(store.FilePath + ".bak"), "atomic save retains previous backup");
            loaded.Items.Clear(); store.Save(loaded); Check(store.Load().Items.Count == 0, "empty workspace stays empty after reload");
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Items\":[{\"Name\":\"旧接口\",\"Method\":\"GET\"},{\"Name\":\"未知状态\",\"Method\":\"GET\",\"Status\":\"unknown\"}]}");
            loaded = store.Load(); Check(loaded.Items[0].Status == "开发中" && loaded.Items[1].Status == "开发中", "old workspace and unknown statuses default to development");
            Check(loaded.Items[0].RequestType == RequestTypes.Http && loaded.Items[0].SoapAction == "" && !loaded.Items[0].AutoSoapAction,
                "old workspaces retain manual Action mode and ordinary HTTP requests");
            var preferences = new ThemePreferenceStore(Path.Combine(folder, "settings.json"));
            Check(preferences.Load() == AppThemeMode.System, "theme defaults to following Windows");
            foreach (AppThemeMode mode in Enum.GetValues(typeof(AppThemeMode)))
            { preferences.Save(mode); Check(preferences.Load() == mode, "theme preference persists: " + mode); }
            File.WriteAllText(preferences.FilePath, "{\"Theme\":\"unknown\"}");
            Check(preferences.Load() == AppThemeMode.System, "unknown theme preference falls back to system");
            File.WriteAllText(preferences.FilePath, "not json");
            Check(preferences.Load() == AppThemeMode.System, "damaged appearance preferences do not block startup");
            File.WriteAllText(store.FilePath, "{\"Version\":1,\"Items\":[null]}");
            rejected = false; try { store.Load(); } catch { rejected = true; } Check(rejected, "malformed import rejected");
            File.WriteAllText(store.FilePath, "{\"version\":1,\"items\":[{\"name\":\"小写字段\",\"method\":\"POST\",\"body\":\"{\\\"中文\\\":123}\",\"children\":null}]}");
            loaded = store.Load();
            Check(loaded.Items[0].Name == "小写字段" && loaded.Items[0].Body == "{\"中文\":123}" && loaded.Items[0].Children.Count == 0,
                "the new serializer accepts older case-insensitive property names and null child lists");
        }
        finally
        {
            string root = Path.GetFullPath(AppDomain.CurrentDomain.BaseDirectory);
            if (Path.GetFullPath(folder).StartsWith(root, StringComparison.OrdinalIgnoreCase) && Directory.Exists(folder)) Directory.Delete(folder, true);
        }
    }
    private static void XmlFormatting()
    {
        const string xml = "<?xml version=\"1.0\" encoding=\"utf-8\"?><root xmlns=\"urn:test\"><value>中文 &amp; 符号</value><data><![CDATA[<raw> & {}[]]]></data></root>";
        string formatted = XmlText.Format(xml);
        Check(formatted.Contains("\r\n  <value>") && XmlText.Compact(formatted) == xml, "XML formatting preserves namespaces, UTF-8 declaration, escapes and CDATA");
        const string mixed = "<root>before<b>bold</b> <i>text</i>after</root>";
        Check(XmlText.Compact(XmlText.Format(mixed)) == mixed, "XML formatting preserves spaces in mixed content");
        const string preserve = "<root xml:space=\"preserve\">  <value> x </value> \n </root>";
        Check(XmlText.Compact(preserve) == preserve, "XML compression respects xml:space preserve");
        bool rejected = false;
        try { XmlText.Format("<!DOCTYPE root [<!ENTITY external SYSTEM 'file:///not-read'>]><root>&external;</root>"); } catch (System.Xml.XmlException) { rejected = true; }
        Check(rejected, "XML formatting does not resolve DTDs or external entities");
        rejected = false; try { XmlText.Format("<root>"); } catch (System.Xml.XmlException) { rejected = true; }
        Check(rejected, "invalid XML is rejected by the format action");
        Check(RequestTypes.SwitchDefaultHeaders("Accept: application/json\r\nContent-Type: application/json\r\nAuthorization: Bearer example", RequestTypes.Http, RequestTypes.Soap11) ==
            "Accept: text/xml\r\nContent-Type: text/xml; charset=utf-8\r\nAuthorization: Bearer example", "SOAP defaults preserve custom authorization headers");
    }
    private static void SoapBodies()
    {
        const string operation = "<Add xmlns=\"http://tempuri.org/\">\r\n  <intA>10</intA>\r\n  <intB>20</intB>\r\n</Add>";
        foreach (string type in new[] { RequestTypes.Soap11, RequestTypes.Soap12 })
        {
            string ns = type == RequestTypes.Soap12 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/";
            Check(IsWrappedOperation(RequestTypes.PrepareBody(operation, type), ns, operation), "method-only XML is wrapped with the selected SOAP namespace: " + type);
            string full = RequestTypes.Envelope(type).Replace("soap:", "env:").Replace("xmlns:soap=", "xmlns:env=");
            Check(RequestTypes.PrepareBody(full, type) == full, "complete envelopes with alternate prefixes are sent verbatim: " + type);
        }
        const string cdata = "<Call xmlns=\"urn:service\">\n  <value><![CDATA[中文 <xml> & {}[]]]></value>\n</Call>";
        Check(RequestTypes.PrepareBody(cdata, RequestTypes.Soap11).Contains(cdata), "automatic SOAP wrapping keeps CDATA, Unicode and whitespace verbatim");
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(RequestTypes.PrepareBody("\uFEFF<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" + operation, RequestTypes.Soap11));
        Check(document.GetElementsByTagName("Add", "http://tempuri.org/").Count == 1 && document.FirstChild is XmlDeclaration,
            "a method XML declaration is moved to the outer SOAP document");
        string businessEnvelope = "<Envelope xmlns=\"urn:business\"><value>1</value></Envelope>";
        document.LoadXml(RequestTypes.PrepareBody(businessEnvelope, RequestTypes.Soap11));
        Check(document.GetElementsByTagName("Envelope", "urn:business").Count == 1 && document.DocumentElement.NamespaceURI == "http://schemas.xmlsoap.org/soap/envelope/",
            "a business element named Envelope is still wrapped as a method");
        Check(RequestTypes.PrepareBody(operation, RequestTypes.Http) == operation, "ordinary HTTP XML is not automatically wrapped");
        foreach (string invalid in new[] { "", "<Add>", "<Add/><Other/>", "<!DOCTYPE Add [<!ENTITY value 'x'>]><Add>&value;</Add>" })
        {
            bool rejected = false;
            try { RequestTypes.PrepareBody(invalid, RequestTypes.Soap11); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid or DTD-bearing SOAP method XML fails before sending: " + invalid);
        }
    }
    private static void WsTable()
    {
        var parameters = new List<WsParam>
        {
            new WsParam { Name = "intA", Value = "10" },
            new WsParam { Name = "note", Value = "中文 & <标签> \"引号\"" },
            new WsParam { Name = "  ", Value = "ignored" }
        };
        string xml = RequestTypes.BuildMethodXml("Add", "http://tempuri.org/", parameters);
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(xml);
        Check(document.DocumentElement.LocalName == "Add" && document.DocumentElement.NamespaceURI == "http://tempuri.org/",
            "the parameter table builds a namespaced method element");
        Check(document.GetElementsByTagName("intA", "http://tempuri.org/")[0].InnerText == "10" &&
            document.GetElementsByTagName("note", "http://tempuri.org/")[0].InnerText == "中文 & <标签> \"引号\"" &&
            document.GetElementsByTagName("intA", "http://tempuri.org/").Count == 1 && xml.IndexOf("ignored") < 0,
            "parameter values are XML-escaped and rows with blank names are skipped");
        Check(xml == "<Add xmlns=\"http://tempuri.org/\"><intA>10</intA><note>中文 &amp; &lt;标签&gt; \"引号\"</note></Add>",
            "the generated method XML preserves value text without extra declarations");
        Check(RequestTypes.BuildMethodXml("Add", "", parameters).StartsWith("<Add>"), "an empty namespace omits the xmlns attribute");
        bool rejected = false;
        try { RequestTypes.BuildMethodXml("", "urn:x", parameters); } catch (ArgumentException) { rejected = true; }
        Check(rejected, "a blank method name is rejected with a clear error");
        rejected = false;
        try { RequestTypes.BuildMethodXml("<bad", "urn:x", parameters); } catch (System.Xml.XmlException) { rejected = true; }
        Check(rejected, "an invalid method name is rejected before sending");

        // 旧手填 XML（方法 XML 与完整 Envelope）可解析回表格入参。
        string method, ns;
        var parsed = new List<WsParam>();
        RequestTypes.ParseMethodXml(parameters.Count > 0 ? xml : "", out method, out ns, parsed);
        Check(method == "Add" && ns == "http://tempuri.org/" && parsed.Count == 2 && parsed[0].Name == "intA" && parsed[0].Value == "10",
            "method XML is parsed back into method, namespace and parameters");
        string full = "<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\">" +
            "<s:Header><a:Action xmlns:a=\"http://www.w3.org/2005/08/addressing\">urn:hdr</a:Action></s:Header>" +
            "<s:Body><m:Subtract xmlns:m=\"urn:calc\"><x>7</x><y>2</y></m:Subtract></s:Body></s:Envelope>";
        RequestTypes.ParseMethodXml(full, out method, out ns, parsed);
        Check(method == "Subtract" && ns == "urn:calc" && parsed.Count == 2 && parsed[0].Name == "x" && parsed[0].Value == "7",
            "a legacy full envelope is parsed back into the Body method and its parameters");
        RequestTypes.ParseMethodXml("", out method, out ns, parsed);
        Check(method == "" && parsed.Count == 0, "empty legacy bodies yield no table data");
    }
    private static void WsAddress()
    {
        Check(RequestTypes.MethodFromUrl("http://100.2.2.217:8089/HospitalService.asmx?op=GetBoxInfo") == "GetBoxInfo",
            "the method name is read from ?op= in an ASMX address");
        Check(RequestTypes.MethodFromUrl("https://host/Service.asmx?op=Get%20Box&x=1") == "", "an ?op= value that is not a valid XML name is ignored");
        Check(RequestTypes.MethodFromUrl("http://host/Service.asmx?OP=Add&other=1") == "Add", "the op key and surrounding query parameters are matched case-insensitively");
        Check(RequestTypes.MethodFromUrl("http://host/HospitalService.asmx/GetBoxInfo?x=1") == "GetBoxInfo", "a .asmx/ path form also supplies the method name");
        Check(RequestTypes.MethodFromUrl("http://host/Service.asmx") == "" && RequestTypes.MethodFromUrl("") == "" && RequestTypes.MethodFromUrl(null) == "",
            "addresses without a method name yield an empty result instead of a guess");
        Check(RequestTypes.MethodFromUrl("http://host/Service.asmx?op=<Add>") == "", "invalid characters in ?op= are rejected");
    }
    private static void SoapActions()
    {
        Check(RequestTypes.InferSoapAction("<Add xmlns=\"http://tempuri.org/\"><intA>10</intA></Add>") == "http://tempuri.org/Add", "Action is inferred from a method namespace and local name");
        Check(RequestTypes.InferSoapAction("<m:Add xmlns:m='http://tempuri.org'/>") == "http://tempuri.org/Add", "prefixed methods and namespaces without a trailing slash infer the same Action");
        Check(RequestTypes.InferSoapAction("<m:Call xmlns:m='urn:service#'/>") == "urn:service#Call", "Action inference preserves a namespace fragment separator");
        foreach (string ns in new[] { "http://schemas.xmlsoap.org/soap/envelope/", "http://www.w3.org/2003/05/soap-envelope" })
        {
            string full = "<s:Envelope xmlns:s='" + ns + "' xmlns:m='http://tempuri.org/'><s:Header><m:Other/></s:Header><s:Body><m:Add/></s:Body></s:Envelope>";
            Check(RequestTypes.InferSoapAction(full) == "http://tempuri.org/Add", "full-envelope inference finds the Body method rather than a Header element: " + ns);
            foreach (string addressing in new[] { "http://www.w3.org/2005/08/addressing", "http://schemas.xmlsoap.org/ws/2004/08/addressing" })
            {
                string addressed = full.Replace("<m:Other/>", "<a:Action xmlns:a='" + addressing + "'>urn:custom-action</a:Action>");
                Check(RequestTypes.InferSoapAction(addressed) == "urn:custom-action", "an explicit WS-Addressing Action is preferred to the namespace convention: " + addressing);
            }
        }
        foreach (string incomplete in new[] { "", "<Add/>", "<Add xmlns=\"", "<!DOCTYPE Add [<!ENTITY name 'value'>]><Add>&name;</Add>" })
            Check(RequestTypes.InferSoapAction(incomplete) == "", "incomplete or unsupported XML does not invent an Action or interrupt typing");
    }
    private static bool IsWrappedOperation(string payload, string ns, string operation)
    {
        var document = new XmlDocument { XmlResolver = null };
        document.LoadXml(payload);
        var root = document.DocumentElement;
        var body = root["Body", ns];
        var method = body == null ? null : body["Add", "http://tempuri.org/"];
        return root.LocalName == "Envelope" && root.NamespaceURI == ns && document.GetElementsByTagName("Envelope", ns).Count == 1 &&
            method != null && method["intA", "http://tempuri.org/"].InnerText == "10" && method["intB", "http://tempuri.org/"].InnerText == "20" && payload.Contains(operation);
    }
    private static void LegacyWorkspace(string directory)
    {
        string path = Path.Combine(directory, "workspace.json");
        byte[] original = File.ReadAllBytes(path);
        var workspace = WorkspaceStore.Read(path);
        string canonical = JsonText.Serialize(workspace);
        string scratch = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "legacy-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            WorkspaceStore.Write(scratch, workspace);
            Check(JsonText.Serialize(WorkspaceStore.Read(scratch)) == canonical,
                "the existing workspace round-trips without changing requests, responses, order or IDs");
            Check(Convert.ToBase64String(File.ReadAllBytes(path)) == Convert.ToBase64String(original),
                "the compatibility check leaves the original workspace file untouched");
        }
        finally { if (File.Exists(scratch)) File.Delete(scratch); }
    }
    private static void CollectionOperations()
    {
        var data = new Workspace();
        var request = WorkspaceTree.CreateRequest(data, null, "根接口", "");
        Check(data.Items.Contains(request), "blank directory creates request at root");
        request.Url = "http://localhost/test"; request.Headers = "X-Test: value"; request.Body = "输入"; request.Response = "输出"; request.ResponseHeaders = "X-Response: value";
        var other = WorkspaceTree.CreateRequest(data, null, "目录接口", "用户目录");
        var folder = data.Items.Find(x => x.IsFolder && x.Name == "用户目录");
        Check(folder != null && folder.Children.Contains(other), "optional directory is created with request inside");
        var sibling = WorkspaceTree.CreateRequest(data, folder, "同级接口", "  ");
        Check(folder.Children.Contains(sibling), "blank directory uses current parent folder");
        var reused = WorkspaceTree.CreateRequest(data, null, "复用目录", "用户目录");
        Check(data.Items.FindAll(x => x.IsFolder).Count == 1 && folder.Children.Contains(reused), "same-named sibling directory is reused");
        var nested = WorkspaceTree.CreateRequest(data, folder, "嵌套接口", "子目录");
        var subfolder = folder.Children.Find(x => x.IsFolder);
        Check(subfolder.Children.Contains(nested), "optional directory can be created under selected parent");
        Check(WorkspaceTree.Move(data, request, folder) && !data.Items.Contains(request) && folder.Children.Contains(request), "move transfers rather than copies request");
        Check(request.Url == "http://localhost/test" && request.Headers == "X-Test: value" && request.Body == "输入" && request.Response == "输出" && request.ResponseHeaders == "X-Response: value", "move preserves request and response content");
        Check(!WorkspaceTree.Move(data, folder, subfolder) && !WorkspaceTree.Move(data, folder, folder), "folder cannot be moved into itself or descendants");
        Check(!WorkspaceTree.Move(data, request, other), "request cannot be used as a destination folder");
        Check(!WorkspaceTree.Move(data, new ApiItem(), folder), "detached item cannot be moved into workspace");
        Check(WorkspaceTree.Move(data, request, null) && data.Items.Contains(request), "request can move back to root");
        Check(!WorkspaceTree.Move(data, request, null), "same-parent move is harmless");
        Check(WorkspaceTree.Move(data, subfolder, null) && subfolder.Children.Contains(nested), "moving folder retains its child requests");
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "collection-roundtrip.json");
        WorkspaceStore.Write(path, data); var loaded = WorkspaceStore.Read(path);
        Check(loaded.Items.Find(x => x.Name == "根接口").Response == "输出" && loaded.Items.Find(x => x.Name == "子目录").Children[0].Name == "嵌套接口", "moved structure and responses survive save and reload");
        var deep = new Workspace(); ApiItem parent = null;
        for (int i = 0; i < 33; i++) { var next = new ApiItem { IsFolder = true, Name = "目录" + i }; if (parent == null) deep.Items.Add(next); else parent.Children.Add(next); parent = next; }
        var root = WorkspaceTree.CreateRequest(deep, null, "根接口", "");
        Check(!WorkspaceTree.CanMove(deep, root, parent), "move rejects unsupported nesting depth");
        var ordered = new Workspace();
        var a = WorkspaceTree.CreateRequest(ordered, null, "A", "");
        var b = WorkspaceTree.CreateRequest(ordered, null, "B", "");
        var c = WorkspaceTree.CreateRequest(ordered, null, "C", "");
        Check(WorkspaceTree.MoveRelative(ordered, c, a, false) && ordered.Items[0] == c, "dragging upwards inserts before sibling");
        Check(WorkspaceTree.MoveRelative(ordered, c, b, true) && ordered.Items[2] == c, "dragging downwards inserts after sibling without index drift");
        Check(!WorkspaceTree.MoveRelative(ordered, a, b, false) && !WorkspaceTree.MoveRelative(ordered, a, a, true), "adjacent and self drops do not alter order");
        var group = new ApiItem { Name = "组", IsFolder = true }; ordered.Items.Add(group);
        var child = WorkspaceTree.CreateRequest(ordered, group, "子项", "");
        Check(!WorkspaceTree.MoveRelative(ordered, group, child, true), "relative drop cannot place folder below its own descendant");
        Check(WorkspaceTree.MoveRelative(ordered, b, child, false) && group.Children[0] == b && !ordered.Items.Contains(b), "relative drop inserts into anchor parent across folders");
        Check(WorkspaceTree.MoveRelative(ordered, group, a, false) && ordered.Items[0] == group && group.Children.Contains(child), "folders can reorder with children preserved");
        WorkspaceStore.Write(path, ordered); loaded = WorkspaceStore.Read(path);
        Check(loaded.Items[0].Name == "组" && loaded.Items[0].Children[0].Name == "B" && loaded.Items[2].Name == "C", "custom item order survives save and reload");
    }
    private static async Task Network()
    {
        using (var server = new EchoServer())
        using (var client = new RequestService())
        {
            var get = await client.SendAsync("GET", server.Url + "echo?q=%E4%B8%AD%E6%96%87", "X-Test: demo", "ignored", CancellationToken.None);
            Check(get.StatusCode == 200 && get.Body.Contains("GET /echo?q=%E4%B8%AD%E6%96%87"), "GET preserves query string");
            Check(get.Body.Contains("X-Test: demo") && get.Body.EndsWith("BODY:"), "custom headers sent; GET body omitted");
            Check(get.Body.Contains("User-Agent: ApiStudio/1.0\r\n"), "requests include a default User-Agent for gateway compatibility");
            string body = "{\"name\":\"中文用户\"}";
            var post = await client.SendAsync("POST", server.Url + "echo", "Content-Type: application/json\r\nX-Token: test-token\r\nuser-agent: ExampleClient/2.0", body, CancellationToken.None);
            Check(post.Body.Contains("POST /echo") && post.Body.EndsWith("BODY:" + body), "POST sends exact UTF-8 JSON body");
            Check(post.Body.IndexOf("User-Agent: ExampleClient/2.0\r\n", StringComparison.OrdinalIgnoreCase) >= 0 && !post.Body.Contains("ApiStudio/1.0"),
                "a custom User-Agent replaces the default without duplicate values");
            var put = await client.SendAsync("PUT", server.Url + "created", "Content-Type: text/plain; charset=utf-8", "update=中文", CancellationToken.None);
            Check(put.StatusCode == 201 && put.Body.EndsWith("BODY:update=中文") && put.Body.Contains("text/plain"), "PUT and custom content type supported");
            var soap = new ApiItem { RequestType = RequestTypes.Soap11, Method = "GET", Url = server.Url + "soap", Headers = "X-Test: soap", SoapAction = "urn:service/Call",
                Body = RequestTypes.Envelope(RequestTypes.Soap11).Replace("<parameter>值</parameter>", "<parameter><![CDATA[中文 & <xml>]]></parameter>") };
            var soap11 = await client.SendAsync(soap, CancellationToken.None);
            Check(soap11.Body.StartsWith("POST /soap HTTP/") && soap11.Body.Contains("Content-Type: text/xml; charset=utf-8") && soap11.Body.Contains("SOAPAction: \"urn:service/Call\""),
                "SOAP 1.1 uses POST, text/xml and a quoted SOAPAction");
            Check(soap11.Body.EndsWith("BODY:" + soap.Body), "SOAP sends XML, Unicode and CDATA verbatim without JSON wrapping");
            soap.SoapAction = "\"urn:service/Call\"";
            var quoted = await client.SendAsync(soap, CancellationToken.None);
            Check(quoted.Body.Contains("SOAPAction: \"urn:service/Call\"\r\n"), "already quoted SOAPAction is not double quoted");
            soap.SoapAction = "";
            var empty = await client.SendAsync(soap, CancellationToken.None);
            Check(empty.Body.Contains("SOAPAction: \"\"\r\n"), "SOAP 1.1 sends an empty quoted Action when omitted");
            soap.Headers = "SOAPAction: \"urn:custom\"";
            var custom = await client.SendAsync(soap, CancellationToken.None);
            Check(custom.Body.Contains("SOAPAction: \"urn:custom\"\r\n"), "an explicit SOAPAction header is honored when the Action field is blank");
            soap.RequestType = RequestTypes.Soap12; soap.SoapAction = "urn:service/Call"; soap.Headers = "";
            soap.Body = RequestTypes.Envelope(RequestTypes.Soap12);
            var soap12 = await client.SendAsync(soap, CancellationToken.None);
            Check(soap12.Body.Contains("Content-Type: application/soap+xml; charset=utf-8; action=\"urn:service/Call\"") && !soap12.Body.Contains("SOAPAction:"),
                "SOAP 1.2 uses the action parameter without adding a SOAPAction header");
            soap.SoapAction = "";
            var optional = await client.SendAsync(soap, CancellationToken.None);
            Check(!optional.Body.Contains("action=") && optional.Body.EndsWith("BODY:" + soap.Body), "SOAP 1.2 supports an omitted optional Action");
            const string operation = "<Add xmlns=\"http://tempuri.org/\">\r\n  <intA>10</intA>\r\n  <intB>20</intB>\r\n</Add>";
            soap.Body = operation;
            foreach (string type in new[] { RequestTypes.Soap11, RequestTypes.Soap12 })
            {
                soap.RequestType = type;
                var result = await client.SendAsync(soap, CancellationToken.None);
                string payload = result.Body.Substring(result.Body.IndexOf("BODY:", StringComparison.Ordinal) + 5);
                string ns = type == RequestTypes.Soap12 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/";
                Check(result.StatusCode == 200 && IsWrappedOperation(payload, ns, operation) && soap.Body == operation,
                    "method-only XML is wrapped on the wire without changing the saved request: " + type);
            }
            soap.AutoSoapAction = true; soap.SoapAction = "urn:stale-cached-value";
            foreach (string type in new[] { RequestTypes.Soap11, RequestTypes.Soap12 })
            {
                soap.RequestType = type;
                var automatic = await client.SendAsync(soap, CancellationToken.None);
                Check(automatic.Body.Contains(type == RequestTypes.Soap11 ? "SOAPAction: \"http://tempuri.org/Add\"" : "action=\"http://tempuri.org/Add\""),
                    "sending recomputes automatic Action from XML rather than a cached field: " + type);
            }
            soap.RequestType = RequestTypes.Soap11; soap.Headers = "SOAPAction: \"urn:manual-header\"";
            var headerAction = await client.SendAsync(soap, CancellationToken.None);
            Check(headerAction.Body.Contains("SOAPAction: \"urn:manual-header\"") && !headerAction.Body.Contains("SOAPAction: \"http://tempuri.org/Add\""),
                "an explicit SOAPAction header overrides automatic inference");
            soap.RequestType = RequestTypes.Soap12; soap.Headers = "Content-Type: application/soap+xml; charset=utf-8; action=\"urn:manual-parameter\"";
            var parameterAction = await client.SendAsync(soap, CancellationToken.None);
            Check(parameterAction.Body.Contains("action=\"urn:manual-parameter\"") && !parameterAction.Body.Contains("action=\"http://tempuri.org/Add\""),
                "an explicit SOAP 1.2 action parameter overrides automatic inference");
            soap.AutoSoapAction = false; soap.SoapAction = "urn:manual-field"; soap.Headers = "";
            var manualAction = await client.SendAsync(soap, CancellationToken.None);
            Check(manualAction.Body.Contains("action=\"urn:manual-field\""), "manual Action mode preserves service-specific Actions");
            soap.Url = server.Url + "soap-fault";
            var fault = await client.SendAsync(soap, CancellationToken.None);
            Check(fault.StatusCode == 500 && fault.Body.Contains("<soap:Fault>") && fault.Body.Contains("调用失败"), "SOAP fault XML is retained for HTTP 500 responses");
            var error = await client.SendAsync("GET", server.Url + "error", "", "", CancellationToken.None);
            Check(error.StatusCode == 400 && error.Body.Length > 0 && error.Headers.Contains("X-Echo"), "HTTP error response body and headers retained");
            foreach (string charset in new[] { "gb2312", "gbk", "gb18030" })
            {
                var chinese = await client.SendAsync("GET", server.Url + charset, "", "", CancellationToken.None);
                Check(chinese.Body == "中文接口响应：调用成功", "legacy Chinese response charset is decoded correctly: " + charset);
            }
            bool rejected = false; try { await client.SendAsync("GET", "file:///c:/test", "", "", CancellationToken.None); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "non-HTTP URL rejected");
            rejected = false; try { await client.SendAsync("GET", server.Url, "invalid-header", "", CancellationToken.None); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "malformed request header rejected");
            using (var cancel = new CancellationTokenSource(120))
            {
                bool cancelled = false; try { await client.SendAsync("GET", server.Url + "slow", "", "", cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
                Check(cancelled, "in-flight request can be cancelled");
            }
        }

        using (var server = new EchoServer())
        using (var client = new RequestService())
        using (var cancel = new CancellationTokenSource())
        {
            var pending = client.SendAsync("GET", server.Url + "stall-after-headers", "", "", cancel.Token);
            Check(await Task.WhenAny(server.BodyStarted, Task.Delay(5000)) == server.BodyStarted,
                "the server sent headers and began the response body");
            cancel.CancelAfter(150);
            Check(await Task.WhenAny(pending, Task.Delay(5000)) == pending,
                "cancellation interrupts an incomplete response body");
            bool cancelled = false;
            try { await pending; } catch (OperationCanceledException) { cancelled = true; }
            Check(cancelled, "body cancellation is reported as an OperationCanceledException");
        }
    }
    private sealed class EchoServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly Task worker;
        private readonly TaskCompletionSource<bool> bodyStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> stopped = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        public string Url { get; private set; }
        public Task BodyStarted => bodyStarted.Task;
        public EchoServer()
        {
            listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            Url = "http://127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port + "/";
            worker = Task.Run((Func<Task>)Loop);
        }
        private async Task Loop()
        {
            try
            {
                while (true)
                {
                    using (var connection = await listener.AcceptTcpClientAsync())
                    using (var stream = connection.GetStream())
                    {
                        var header = new MemoryStream(); var one = new byte[1];
                        while (await stream.ReadAsync(one, 0, 1) > 0) { header.WriteByte(one[0]); if (header.Length >= 4 && Encoding.ASCII.GetString(header.ToArray()).EndsWith("\r\n\r\n")) break; }
                        string text = Encoding.ASCII.GetString(header.ToArray()); int size = 0;
                        foreach (var line in text.Split('\n')) if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase)) size = int.Parse(line.Substring(15).Trim());
                        var body = new byte[size]; int received = 0;
                        while (received < size) { int read = await stream.ReadAsync(body, received, size - received); if (read == 0) break; received += read; }
                        if (text.Contains("/slow")) await Task.Delay(600);
                        bool fault = text.Contains("/soap-fault");
                        string status = fault ? "500 Internal Server Error" : text.Contains("/error") ? "400 Bad Request" : text.Contains("/created") ? "201 Created" : "200 OK";
                        string charset = text.Contains("/gb18030") ? "gb18030" : text.Contains("/gb2312") ? "gb2312" : text.Contains("/gbk") ? "gbk" : "utf-8";
                        byte[] output = fault ? Encoding.UTF8.GetBytes("<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\"><soap:Body><soap:Fault><soap:Reason><soap:Text xml:lang=\"zh\">调用失败</soap:Text></soap:Reason></soap:Fault></soap:Body></soap:Envelope>") :
                            charset == "utf-8" ? Encoding.UTF8.GetBytes(text + "BODY:" + Encoding.UTF8.GetString(body)) :
                            Encoding.GetEncoding(charset).GetBytes("中文接口响应：调用成功");
                        byte[] prefix = Encoding.ASCII.GetBytes("HTTP/1.1 " + status + "\r\nContent-Type: " + (fault ? "application/soap+xml" : "text/plain") + "; charset=" + charset + "\r\nX-Echo: yes\r\nContent-Length: " + output.Length + "\r\nConnection: close\r\n\r\n");
                        await stream.WriteAsync(prefix, 0, prefix.Length);
                        if (text.Contains("/stall-after-headers"))
                        {
                            await stream.WriteAsync(output, 0, 1);
                            bodyStarted.TrySetResult(true);
                            await stopped.Task;
                            return;
                        }
                        await stream.WriteAsync(output, 0, output.Length);
                    }
                }
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) when (stopped.Task.IsCompleted) { }
            catch (SocketException) { }
            catch (IOException) { }
        }
        public void Dispose()
        {
            stopped.TrySetResult(true);
            listener.Stop();
            worker.Wait(2000);
        }
    }
}
