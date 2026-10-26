// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace NaiwaCompanion
{
    internal static class PdfWord
    {
        private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        private const string V = "urn:schemas-microsoft-com:vml";
        private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private static double Number(XmlElement element, string name) { return Double.Parse(element.GetAttribute(name), CultureInfo.InvariantCulture); }
        private static string N(double number) { return number.ToString("0.###", CultureInfo.InvariantCulture); }
        private static XmlDocument Read(string file)
        { XmlDocument xml = new XmlDocument { XmlResolver = null }; using (XmlReader reader = XmlReader.Create(file, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null })) xml.Load(reader); return xml; }
        private static void Start(XmlWriter xml, string name) { xml.WriteStartElement("w", name, W); }
        private static void Attribute(XmlWriter xml, string name, string value) { xml.WriteAttributeString("w", name, W, value); }
        private static void Value(XmlWriter xml, string name, string value) { Start(xml, name); Attribute(xml, "val", value); xml.WriteEndElement(); }
        private static void TextEntry(ZipArchive zip, string name, string text)
        { using (StreamWriter writer = new StreamWriter(zip.CreateEntry(name).Open(), new UTF8Encoding(false))) writer.Write(text); }
        private static void Section(XmlWriter xml, XmlElement page)
        {
            Start(xml, "sectPr"); Value(xml, "type", "nextPage");
            Start(xml, "pgSz"); Attribute(xml, "w", N(Number(page, "width") * 20)); Attribute(xml, "h", N(Number(page, "height") * 20)); xml.WriteEndElement();
            Start(xml, "pgMar"); foreach (string side in new string[] { "top", "right", "bottom", "left", "header", "footer", "gutter" }) Attribute(xml, side, "0"); xml.WriteEndElement(); xml.WriteEndElement();
        }
        private static void Shape(XmlWriter xml, string id, double left, double top, double width, double height, bool background)
        {
            Start(xml, "r"); Start(xml, "pict"); xml.WriteStartElement("v", "rect", V);
            xml.WriteAttributeString("id", id); xml.WriteAttributeString("stroked", "f"); xml.WriteAttributeString("filled", background ? "t" : "f");
            xml.WriteAttributeString("style", "position:absolute;margin-left:" + N(left) + "pt;margin-top:" + N(top) + "pt;width:" + N(width) + "pt;height:" + N(height) + "pt;z-index:" + (background ? "-251658240" : "1") + ";mso-position-horizontal-relative:page;mso-position-vertical-relative:page;mso-wrap-style:none");
        }
        private static void EndShape(XmlWriter xml) { xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement(); }
        private static string Font(string family)
        {
            family = Regex.Replace(family, @"^[A-Z]{6}\+", "");
            return family == "MicrosoftYaHei" ? "Microsoft YaHei" : family == "MS-Gothic" ? "MS Gothic" : family == "YuGothic" ? "Yu Gothic" : family;
        }
        internal static void Convert(ConversionJob job)
        {
            string stage = Path.GetDirectoryName(job.Work), layout = Path.Combine(stage, "layout.xml"), html = Path.Combine(stage, "background.html");
            string tool = job.Tools.Find("pdftohtml.exe");
            Conversion.RunTool(job, tool, "-xml", "-zoom", "1", job.Input, layout);
            Conversion.RunTool(job, tool, "-c", "-fmt", "png", "-noframes", "-s", "-zoom", "2", job.Input, html);
            XmlDocument source = Read(layout), backgrounds = Read(html); XmlNodeList pages = source.SelectNodes("/pdf2xml/page");
            if (pages.Count == 0) throw new IOException("PDF 未生成可用页面。");
            Dictionary<string, XmlElement> fonts = new Dictionary<string, XmlElement>();
            foreach (XmlElement font in source.SelectNodes("//fontspec")) fonts[font.GetAttribute("id")] = font;
            Dictionary<int, string> images = new Dictionary<int, string>();
            foreach (XmlElement div in backgrounds.SelectNodes("//*[local-name()='div']"))
            {
                Match match = Regex.Match(div.GetAttribute("id"), @"^page(\d+)-div$"); if (!match.Success) continue;
                XmlElement image = div.SelectSingleNode("*[local-name()='img']") as XmlElement; if (image == null) throw new IOException("PDF 页面背景不完整。");
                string file = Path.GetFullPath(Path.Combine(stage, image.GetAttribute("src")));
                if (!file.StartsWith(Path.GetFullPath(stage) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || !File.Exists(file)) throw new IOException("PDF 页面背景路径异常。");
                images[Int32.Parse(match.Groups[1].Value)] = file;
            }
            if (images.Count != pages.Count) throw new IOException("PDF 页面数量与背景数量不一致。");
            string output = Path.Combine(job.Work, "result.docx");
            using (ZipArchive zip = ZipFile.Open(output, ZipArchiveMode.Create))
            {
                TextEntry(zip, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Default Extension=\"png\" ContentType=\"image/png\"/><Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/></Types>");
                TextEntry(zip, "_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"document\" Type=\"" + R + "/officeDocument\" Target=\"word/document.xml\"/></Relationships>");
                StringBuilder rels = new StringBuilder("<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
                for (int index = 1; index <= pages.Count; index++)
                { zip.CreateEntryFromFile(images[index], "word/media/page" + index + ".png"); rels.Append("<Relationship Id=\"page" + index + "\" Type=\"" + R + "/image\" Target=\"media/page" + index + ".png\"/>"); }
                TextEntry(zip, "word/_rels/document.xml.rels", rels.Append("</Relationships>").ToString());
                using (XmlWriter xml = XmlWriter.Create(zip.CreateEntry("word/document.xml").Open(), new XmlWriterSettings { Encoding = new UTF8Encoding(false), CloseOutput = true }))
                {
                    Start(xml, "document"); xml.WriteAttributeString("xmlns", "v", null, V); xml.WriteAttributeString("xmlns", "r", null, R); Start(xml, "body");
                    int shapeId = 0;
                    for (int index = 0; index < pages.Count; index++)
                    {
                        XmlElement page = (XmlElement)pages[index]; Start(xml, "p"); Start(xml, "pPr");
                        Start(xml, "spacing"); Attribute(xml, "before", "0"); Attribute(xml, "after", "0"); Attribute(xml, "line", "20"); Attribute(xml, "lineRule", "exact"); xml.WriteEndElement();
                        if (index < pages.Count - 1) Section(xml, page); xml.WriteEndElement();
                        Shape(xml, "Page" + (index + 1), 0, 0, Number(page, "width"), Number(page, "height"), true);
                        xml.WriteStartElement("v", "imagedata", V); xml.WriteAttributeString("r", "id", R, "page" + (index + 1)); xml.WriteEndElement(); EndShape(xml);
                        foreach (XmlElement text in page.SelectNodes("text"))
                        {
                            if (String.IsNullOrWhiteSpace(text.InnerText)) continue;
                            XmlElement font = fonts[text.GetAttribute("font")]; double size = Number(font, "size");
                            Shape(xml, "Text" + (++shapeId), Number(text, "left"), Math.Max(0, Number(text, "top") - 1), Math.Max(12, Number(text, "width") + 12), Math.Max(Number(text, "height") + 6, size * 1.5), false);
                            xml.WriteStartElement("v", "textbox", V); xml.WriteAttributeString("inset", "0,0,0,0");
                            Start(xml, "txbxContent"); Start(xml, "p"); Start(xml, "pPr");
                            Start(xml, "spacing"); Attribute(xml, "before", "0"); Attribute(xml, "after", "0"); Attribute(xml, "line", N((size + 1) * 20)); Attribute(xml, "lineRule", "exact"); xml.WriteEndElement(); xml.WriteEndElement();
                            Start(xml, "r"); Start(xml, "rPr"); Start(xml, "rFonts"); string family = Font(font.GetAttribute("family")); Attribute(xml, "ascii", family); Attribute(xml, "hAnsi", family); Attribute(xml, "eastAsia", family); xml.WriteEndElement();
                            Value(xml, "sz", N(size * 2)); Value(xml, "szCs", N(size * 2)); Value(xml, "color", font.GetAttribute("color").TrimStart('#'));
                            if (text.SelectSingleNode(".//b") != null) Value(xml, "b", "1"); if (text.SelectSingleNode(".//i") != null) Value(xml, "i", "1"); xml.WriteEndElement();
                            Start(xml, "t"); xml.WriteAttributeString("xml", "space", "http://www.w3.org/XML/1998/namespace", "preserve"); xml.WriteString(text.InnerText); xml.WriteEndElement();
                            xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement(); EndShape(xml);
                        }
                        xml.WriteEndElement();
                    }
                    Section(xml, (XmlElement)pages[pages.Count - 1]); xml.WriteEndElement(); xml.WriteEndElement();
                }
            }
            OfficeConversion.VerifyWord(output, pages.Count, job);
        }
    }
}
