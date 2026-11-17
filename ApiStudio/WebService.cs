using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace ApiStudio
{
    public static class RequestTypes
    {
        public const string Http = "HTTP", Soap11 = "SOAP 1.1", Soap12 = "SOAP 1.2";
        public static readonly string[] Values = { Http, Soap11, Soap12 };
        public static readonly string[] Names = { "HTTP", "WebService · SOAP 1.1", "WebService · SOAP 1.2" };
        // 界面只暴露两种调用方式；内部仍区分 SOAP 版本以兼容既有数据。
        public static readonly string[] ModeNames = { "API 接口", "WebService 接口" };
        public const string DefaultSoapNamespace = "http://tempuri.org/";
        public static string Normalize(string value) { return IsSoap(value) ? value : Http; }
        public static bool IsSoap(string value) { return value == Soap11 || value == Soap12; }
        public static int ModeIndex(string value) { return IsSoap(value) ? 1 : 0; }
        public static string ModeRequestType(int index, string currentSoapVersion)
        { return index == 1 ? (IsSoap(currentSoapVersion) ? currentSoapVersion : Soap11) : Http; }
        public static string MediaType(string value) { return value == Soap12 ? "application/soap+xml" : value == Soap11 ? "text/xml" : "application/json"; }
        private static string EnvelopeNamespace(string type)
        { return type == Soap12 ? "http://www.w3.org/2003/05/soap-envelope" : "http://schemas.xmlsoap.org/soap/envelope/"; }
        // 常见 ASMX 调用地址自带方法名：.../Service.asmx?op=GetBoxInfo，也支持 .../Service.asmx/GetBoxInfo。
        // 解析不到或不是合法的 XML 名称时返回空字符串，调用方保留用户填写的值。
        public static string MethodFromUrl(string url)
        {
            string text = (url ?? "").Trim();
            if (text.Length == 0) return "";
            int query = text.IndexOf('?');
            string path = query < 0 ? text : text.Substring(0, query);
            if (query >= 0)
            {
                foreach (var pair in text.Substring(query + 1).Split('&'))
                {
                    int equals = pair.IndexOf('=');
                    if (equals <= 0) continue;
                    if (!pair.Substring(0, equals).Trim().Equals("op", StringComparison.OrdinalIgnoreCase)) continue;
                    return VerifyName(Unescape(pair.Substring(equals + 1)));
                }
            }
            int slash = path.IndexOf(".asmx/", StringComparison.OrdinalIgnoreCase);
            return slash < 0 ? "" : VerifyName(Unescape(path.Substring(slash + 6)));
        }
        private static string Unescape(string value)
        {
            try { return Uri.UnescapeDataString(value ?? ""); }
            catch (UriFormatException) { return value ?? ""; }
        }
        private static string VerifyName(string value)
        {
            string name = (value ?? "").Trim();
            if (name.Length == 0) return "";
            try { return XmlConvert.VerifyNCName(name); }
            catch (XmlException) { return ""; }
        }
        public static string InferSoapAction(string body)
        {
            try
            {
                using (var reader = XmlText.CreateReader((body ?? "").TrimStart('\uFEFF')))
                {
                    if (reader.MoveToContent() != XmlNodeType.Element) return "";
                    string ns = reader.NamespaceURI;
                    if (reader.LocalName != "Envelope" || (ns != EnvelopeNamespace(Soap11) && ns != EnvelopeNamespace(Soap12)))
                        return MethodAction(reader);
                    while (reader.Read())
                    {
                        if (reader.NodeType != XmlNodeType.Element || reader.Depth != 1 || reader.NamespaceURI != ns) continue;
                        if (reader.LocalName == "Header")
                        {
                            using (var header = reader.ReadSubtree())
                                while (header.Read())
                                    if (header.NodeType == XmlNodeType.Element && header.Depth == 1 && header.LocalName == "Action" &&
                                        (header.NamespaceURI == "http://www.w3.org/2005/08/addressing" || header.NamespaceURI == "http://schemas.xmlsoap.org/ws/2004/08/addressing"))
                                        return header.ReadElementContentAsString().Trim();
                        }
                        else if (reader.LocalName == "Body")
                        {
                            using (var content = reader.ReadSubtree())
                                while (content.Read())
                                    if (content.NodeType == XmlNodeType.Element && content.Depth == 1) return MethodAction(content);
                            return "";
                        }
                    }
                }
            }
            catch (XmlException) { } // Incomplete XML is normal while typing.
            return "";
        }
        private static string MethodAction(XmlReader method)
        {
            string ns = method.NamespaceURI;
            if (string.IsNullOrWhiteSpace(ns)) return "";
            return ns + (ns.EndsWith("/", StringComparison.Ordinal) || ns.EndsWith("#", StringComparison.Ordinal) || ns.EndsWith(":", StringComparison.Ordinal) ? "" : "/") + method.LocalName;
        }
        public static string PrepareBody(string body, string type)
        {
            if (!IsSoap(type)) return body ?? "";
            string content = (body ?? "").TrimStart('\uFEFF');
            try
            {
                using (var reader = XmlText.CreateReader(content))
                {
                    reader.MoveToContent();
                    // Complete envelopes, including signed messages, stay verbatim.
                    if (reader.LocalName == "Envelope" &&
                        (reader.NamespaceURI == EnvelopeNamespace(Soap11) || reader.NamespaceURI == EnvelopeNamespace(Soap12))) return body;
                    // A method must be a single, well-formed XML element.
                    while (reader.Read()) { }
                }
            }
            catch (XmlException ex)
            { throw new ArgumentException("WebService 请求体应为完整的方法 XML 或 SOAP Envelope：" + ex.Message, ex); }
            // A declaration belongs to the outer document, not inside soap:Body.
            // Keep the method XML itself untouched, including whitespace and CDATA.
            if (content.StartsWith("<?xml", StringComparison.Ordinal) && content.Length > 5 && char.IsWhiteSpace(content[5]))
                content = content.Substring(content.IndexOf("?>", StringComparison.Ordinal) + 2);
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<soap:Envelope xmlns:soap=\"" + EnvelopeNamespace(type) + "\">\r\n" +
                "  <soap:Body>\r\n" + content + "\r\n  </soap:Body>\r\n</soap:Envelope>";
        }
        // 由表格化入参拼出 SOAP Body 内的方法 XML。方法名与参数名做基本合法性处理，
        // 参数值按 XML 文本转义；命名空间作为方法元素的默认 xmlns。
        public static string BuildMethodXml(string method, string ns, List<WsParam> parameters)
        {
            string methodName = (method ?? "").Trim();
            if (methodName.Length == 0) throw new ArgumentException("请填写 WebService 方法名。");
            string name = XmlConvert.VerifyNCName(methodName);
            string xmlns = (ns ?? "").Trim();
            var xml = new StringBuilder();
            xml.Append('<').Append(name);
            if (xmlns.Length > 0) xml.Append(" xmlns=\"").Append(EscapeAttribute(xmlns)).Append('"');
            xml.Append('>');
            foreach (var p in parameters ?? new List<WsParam>())
            {
                if (p == null || string.IsNullOrWhiteSpace(p.Name)) continue;
                string paramName = XmlConvert.VerifyNCName(p.Name.Trim());
                xml.Append('<').Append(paramName).Append('>');
                xml.Append(EscapeText(p.Value ?? ""));
                xml.Append("</").Append(paramName).Append('>');
            }
            xml.Append("</").Append(name).Append('>');
            return xml.ToString();
        }
        private static string EscapeText(string value)
        {
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
        private static string EscapeAttribute(string value)
        {
            return EscapeText(value).Replace("\"", "&quot;");
        }
        // 兼容旧版手填 XML：从方法 XML 或完整 Envelope 中读出方法名、命名空间和一级参数。
        public static void ParseMethodXml(string body, out string method, out string ns, List<WsParam> parameters)
        {
            method = ""; ns = DefaultSoapNamespace;
            if (parameters == null) parameters = new List<WsParam>();
            parameters.Clear();
            string content = (body ?? "").TrimStart('\uFEFF').Trim();
            if (content.Length == 0) return;
            try
            {
                XmlElement root;
                using (var reader = XmlText.CreateReader(content))
                {
                    var doc = new XmlDocument { XmlResolver = null };
                    doc.Load(reader);
                    root = doc.DocumentElement;
                }
                if (root == null) return;
                if (root.LocalName == "Envelope" &&
                    (root.NamespaceURI == EnvelopeNamespace(Soap11) || root.NamespaceURI == EnvelopeNamespace(Soap12)))
                {
                    XmlElement bodyElement = null;
                    foreach (XmlNode node in root.ChildNodes)
                        if (node is XmlElement e && e.LocalName == "Body" && e.NamespaceURI == root.NamespaceURI) { bodyElement = e; break; }
                    if (bodyElement == null) return;
                    foreach (XmlNode node in bodyElement.ChildNodes)
                        if (node is XmlElement e) { root = e; break; }
                }
                method = root.LocalName;
                ns = string.IsNullOrWhiteSpace(root.NamespaceURI) ? DefaultSoapNamespace : root.NamespaceURI;
                parameters.Clear();
                foreach (XmlNode node in root.ChildNodes)
                    if (node is XmlElement e) parameters.Add(new WsParam { Name = e.LocalName, Value = e.InnerText });
            }
            catch (XmlException) { method = ""; parameters.Clear(); }
        }
        public static string Envelope(string type)
        {
            string ns = EnvelopeNamespace(type);
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\r\n" +
                "<soap:Envelope xmlns:soap=\"" + ns + "\">\r\n" +
                "  <soap:Body>\r\n" +
                "    <!-- 按服务文档修改方法名、命名空间和参数 -->\r\n" +
                "    <YourMethod xmlns=\"http://tempuri.org/\">\r\n" +
                "      <parameter>值</parameter>\r\n" +
                "    </YourMethod>\r\n" +
                "  </soap:Body>\r\n</soap:Envelope>";
        }
        public static string SwitchDefaultHeaders(string headers, string previous, string next)
        {
            var lines = (headers ?? "").Split(new[] { "\r\n", "\n", "\r" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                int colon = lines[i].IndexOf(':');
                if (colon < 0) continue;
                string name = lines[i].Substring(0, colon).Trim(), value = lines[i].Substring(colon + 1).Trim();
                if ((name.Equals("Accept", StringComparison.OrdinalIgnoreCase) || name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)) &&
                    (value.Equals(MediaType(previous), StringComparison.OrdinalIgnoreCase) ||
                     value.Equals(MediaType(previous) + "; charset=utf-8", StringComparison.OrdinalIgnoreCase)))
                    lines[i] = name + ": " + MediaType(next) + (name.Equals("Content-Type", StringComparison.OrdinalIgnoreCase) ? "; charset=utf-8" : "");
            }
            return string.Join("\r\n", lines);
        }
    }

    public static class XmlText
    {
        public static bool LooksLikeXml(string text) { return (text ?? "").TrimStart(' ', '\t', '\r', '\n', '\uFEFF').StartsWith("<", StringComparison.Ordinal); }
        public static string Format(string text) { return Rewrite(text, false); }
        public static string Compact(string text) { return Rewrite(text, true); }
        internal static XmlReader CreateReader(string text)
        {
            return XmlReader.Create(new StringReader(text ?? ""), new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 32 * 1024 * 1024, CloseInput = true
            });
        }
        private static string Rewrite(string text, bool compact)
        {
            var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
            using (var reader = CreateReader(text)) document.Load(reader);
            RemoveIndentation(document, false);
            using (var stream = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(stream, new XmlWriterSettings
                {
                    Encoding = new UTF8Encoding(false), Indent = !compact, IndentChars = "  ", NewLineChars = "\r\n",
                    NewLineHandling = NewLineHandling.None, OmitXmlDeclaration = !(document.FirstChild is XmlDeclaration)
                })) document.Save(writer);
                return Encoding.UTF8.GetString(stream.ToArray());
            }
        }
        private static void RemoveIndentation(XmlNode node, bool preserve)
        {
            var element = node as XmlElement;
            if (element != null)
            {
                string space = element.GetAttribute("space", "http://www.w3.org/XML/1998/namespace");
                if (space == "preserve") preserve = true;
                else if (space == "default") preserve = false;
            }
            bool mixed = false;
            foreach (XmlNode child in node.ChildNodes)
                if (child.NodeType == XmlNodeType.Text || child.NodeType == XmlNodeType.CDATA || child.NodeType == XmlNodeType.EntityReference) mixed = true;
            for (int i = node.ChildNodes.Count - 1; i >= 0; i--)
            {
                XmlNode child = node.ChildNodes[i];
                if (!preserve && !mixed && (child.NodeType == XmlNodeType.Whitespace || child.NodeType == XmlNodeType.SignificantWhitespace)) node.RemoveChild(child);
                else if (child is XmlElement) RemoveIndentation(child, preserve);
            }
        }
    }
}
