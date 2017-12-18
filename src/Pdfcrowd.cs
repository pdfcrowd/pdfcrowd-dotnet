// Copyright (C) 2009-2016 pdfcrowd.com
// 
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.

using System;
using System.Text;
using System.Net;
using System.IO;
using System.Web;
using System.Diagnostics;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Net.Security;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading;

namespace pdfcrowd
{
    public sealed class ConnectionHelper
    {
        private string userName;
        private string apiKey;
        private string apiUri;
        private bool useHttp;
        private string userAgent;
        private string debugLogUrl;
        private int credits;
        private int consumedCredits;
        private string jobId;
        private int pageCount;
        private int outputSize;

        private string proxyHost;
        private int proxyPort;
        private string proxyUserName;
        private string proxyPassword;

        private int retryCount;
        private int retry;

        private static readonly string HOST = Environment.GetEnvironmentVariable("PDFCROWD_HOST") != null
            ? Environment.GetEnvironmentVariable("PDFCROWD_HOST")
            : "api.pdfcrowd.com";
        private static readonly string MULTIPART_BOUNDARY = "----------ThIs_Is_tHe_bOUnDary_$";
        public static readonly string CLIENT_VERSION = "4.2";
        private static readonly string newLine = "\r\n";
        private static readonly CultureInfo numericInfo = CultureInfo.GetCultureInfo("en-US");

        public ConnectionHelper(String userName, String apiKey) {
            this.userName = userName;
            this.apiKey = apiKey;

            resetResponseData();
            setProxy(null, 0, null, null);
            setUseHttp(false);
            setUserAgent("pdfcrowd_dotnet_client/4.2 (http://pdfcrowd.com)");

            if( HOST != "api.pdfcrowd.com")
            {
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            }

            retryCount = 1;
        }

        private void resetResponseData()
        {
            debugLogUrl = null;
            credits = 999999;
            consumedCredits = 0;
            jobId = "";
            pageCount = 0;
            outputSize = 0;
            retry = 0;
        }

        internal static string doubleToString(double value)
        {
            return value.ToString(numericInfo);
        }

        internal static string intToString(int value)
        {
            return value.ToString(numericInfo);
        }

        private static void CopyStream(Stream input, Stream output)
        {
            byte[] buffer = new byte[32768];
            while(true)
            {
                int read = input.Read(buffer, 0, buffer.Length);
                if(read <= 0)
                    return;
                output.Write(buffer, 0, read);
            }
        }

        private WebRequest getConnection()
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(apiUri);
            if (proxyHost != null)
            {
                WebProxy proxy = new WebProxy(proxyHost, proxyPort);
                proxy.BypassProxyOnLocal = false;

                if (proxyUserName != null)
                {
                    proxy.Credentials = new NetworkCredential(proxyUserName, proxyPassword);
                }

                request.Proxy = proxy;
            }

            request.UserAgent = userAgent;

            String encoded = System.Convert.ToBase64String(System.Text.Encoding.GetEncoding("ISO-8859-1")
                                                           .GetBytes(userName + ":" + apiKey));
            request.Headers.Add("Authorization", "Basic " + encoded);
            return request;
        }

        private static int getIntHeader(HttpWebResponse response, string name, int defaultValue = 0)
        {
            string value = response.GetResponseHeader(name);
            return value == null || value == "" ? defaultValue : Int32.Parse(value);
        }

        private static string getStringHeader(HttpWebResponse response, string name, string defaultValue = "")
        {
            string value = response.GetResponseHeader(name);
            return value == null ? defaultValue : value;
        }

        private byte[] doPost(object body, string contentType, Stream outStream)
        {
            if(!useHttp && proxyHost != null)
                throw new Error("HTTPS over a proxy is not supported.");

            resetResponseData();

            while(true)
            {
                try
                {
                    return execRequest(body, contentType, outStream);
                }
                catch(Error err)
                {
                    if(err.getCode() == 502 && retryCount > retry) {
                        retry++;
                        Thread.Sleep(retry * 100);
                    } else {
                        throw err;
                    }
                }
            }
        }

        private byte[] execRequest(object body, string contentType, Stream outStream)
        {
            WebRequest request = getConnection();
            request.Method = "POST";

            byte[] byteArray;
            if ((body is byte[]))
            {
                byteArray = (byte[])body;
            }
            else
            {
                byteArray = Encoding.UTF8.GetBytes((string)body);
            }

            request.ContentType = contentType;
            request.ContentLength = byteArray.Length;
            try
            {
                using(Stream dataStream = request.GetRequestStream())
                {
                    dataStream.Write(byteArray, 0, byteArray.Length);
                }

                // Get the response.
                HttpWebResponse response = (HttpWebResponse) request.GetResponse();

                debugLogUrl = getStringHeader(response, "X-Pdfcrowd-Debug-Log");
                credits = getIntHeader(response, "X-Pdfcrowd-Remaining-Credits", 999999);
                consumedCredits = getIntHeader(response, "X-Pdfcrowd-Consumed-Credits");
                jobId = getStringHeader(response, "X-Pdfcrowd-Job-Id");
                pageCount = getIntHeader(response, "X-Pdfcrowd-Pages");
                outputSize = getIntHeader(response, "X-Pdfcrowd-Output-Size");

                if(Environment.GetEnvironmentVariable("PDFCROWD_UNIT_TEST_MODE") != null &&
                    retryCount > retry) {
                    throw new Error("test 502", 502);
                }

                if(response.StatusCode == HttpStatusCode.OK)
                {
                    // Get the stream containing content returned by the server.
                    using(Stream dataStream = response.GetResponseStream())
                    {
                        if(outStream != null)
                        {
                            CopyStream(dataStream, outStream);
                            outStream.Position = 0;
                            return null;
                        }

                        using(MemoryStream output = new MemoryStream())
                        {
                            CopyStream(dataStream, output);
                            return output.ToArray();
                        }
                    }
                }
                else
                {
                    throw new Error(response.StatusDescription, response.StatusCode);
                }
            }
            catch(WebException why)
            {
                if (why.Response != null && why.Status == WebExceptionStatus.ProtocolError)
                {
                    HttpWebResponse response = (HttpWebResponse)why.Response;

                    MemoryStream stream = new MemoryStream();
                    CopyStream(response.GetResponseStream(), stream);
                    stream.Position = 0;
                    string err = readStream(stream);
                    throw new Error(err, response.StatusCode);
                } else {
                    string innerException = "";
                    if (why.InnerException != null)
                    {
                        innerException = "\n" + why.InnerException.Message;
                    }
                    throw new Error(why.Message + innerException, HttpStatusCode.Unused);
                }
            }
        }

        private static string encodePostData(Dictionary<string, string> fields)
        {
            string result = "";

            foreach(KeyValuePair<string, string> entry in fields)
            {
                if(entry.Value != null)
                {
                    result += HttpUtility.UrlEncode(entry.Key) + "=" +
                        HttpUtility.UrlEncode(entry.Value) + "&";
                }
            }

            return result.Substring(0, result.Length - 1);
        }

        private static void beginFileField(string name, string fileName, BinaryWriter body)
        {
            string result = "--" + MULTIPART_BOUNDARY + newLine;
            result += String.Format("Content-Disposition: form-data; name=\"{0}\"; filename=\"{1}\"", name, fileName) + newLine;
            result += "Content-Type: application/octet-stream" + newLine;
            result += newLine;
            body.Write(new UTF8Encoding().GetBytes(result));
        }

        private static byte[] encodeMultipartPostData(Dictionary<string, string> fields, Dictionary<string, string> files, Dictionary<string, byte[]> rawData)
        {
            MemoryStream memw = new MemoryStream();
            BinaryWriter retval = new BinaryWriter(memw);
            UTF8Encoding utf8 = new UTF8Encoding();

            string result = "";
            foreach(KeyValuePair<string, string> entry in fields)
            {
                if(entry.Value != null)
                {
                    result += "--" + MULTIPART_BOUNDARY + newLine;
                    result += String.Format("Content-Disposition: form-data; name=\"{0}\"", entry.Key) + newLine;
                    result += newLine;
                    result += entry.Value + newLine;
                }
            }

            retval.Write(utf8.GetBytes(result));

            foreach(KeyValuePair<string, string> entry in files)
            {
                beginFileField(entry.Key, entry.Value, retval);

                // file content
                using(FileStream fin = File.OpenRead(entry.Value))
                {
                    byte[] b = new byte[8192];
                    int r;
                    while ((r = fin.Read(b, 0, b.Length)) > 0)
                        retval.Write(b, 0, r);
                }
                retval.Write(utf8.GetBytes(newLine));
            }

            foreach(KeyValuePair<string, byte[]> entry in rawData)
            {
                beginFileField(entry.Key, entry.Key, retval);

                // byte content
                retval.Write(entry.Value);
                retval.Write(utf8.GetBytes(newLine));
            }

            // finalize
            result = "--" + MULTIPART_BOUNDARY + "--" + newLine;
            result += newLine;

            retval.Write(utf8.GetBytes(result));
            retval.Flush();
            return memw.ToArray();
        }

        private static string readStream(Stream stream)
        {
            using(StreamReader reader = new StreamReader(stream, Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        internal byte[] post(Dictionary<string, string> fields, Dictionary<string, string> files, Dictionary<string, byte[]> rawData, Stream outStream)
        {
            return files.Count == 0 && rawData.Count == 0 ?
                postUrlEncoded(fields, outStream) :
                postMultipart(fields, files, rawData, outStream);
        }

        private byte[] postUrlEncoded(Dictionary<string, string> fields, Stream outStream)
        {
            string body = encodePostData(fields);
            string contentType = "application/x-www-form-urlencoded";
            return doPost(body, contentType, outStream);
        }

        private byte[] postMultipart(Dictionary<string, string> fields, Dictionary<string, string> files, Dictionary<string, byte[]> rawData, Stream outStream)
        {
            byte[] body = encodeMultipartPostData(fields, files, rawData);
            string contentType = "multipart/form-data; boundary=" + MULTIPART_BOUNDARY;
            return doPost(body, contentType, outStream);
        }

        internal void setUseHttp(bool useHttp)
        {
            if(useHttp)
            {
                apiUri = string.Format("http://{0}:80/convert/", HOST);
            }
            else
            {
                apiUri = string.Format("https://{0}:443/convert/", HOST);
            }
            this.useHttp = useHttp;
        }

        internal void setUserAgent(String userAgent)
        {
            this.userAgent = userAgent;
        }

        internal void setRetryCount(int retryCount)
        {
            this.retryCount = retryCount;
        }

        internal void setProxy(String host, int port, String userName, String password)
        {
            proxyHost = host;
            proxyPort = port;
            proxyUserName = userName;
            proxyPassword = password;
        }

        internal String getDebugLogUrl()
        {
            return debugLogUrl;
        }

        internal int getRemainingCreditCount()
        {
            return credits;
        }

        internal int getConsumedCreditCount()
        {
            return consumedCredits;
        }

        internal String getJobId()
        {
            return jobId;
        }

        internal int getPageCount()
        {
            return pageCount;
        }

        internal int getOutputSize()
        {
            return outputSize;
        }

        internal static string createInvalidValueMessage(object value, string field, string converter, string hint, string id)
        {
            string message = string.Format("Invalid value '{0}' for a field '{1}'.", value, field);
            if(hint != null)
            {
                message += " " + hint;
            }
            return message + " " + string.Format("Details: https://www.pdfcrowd.com/doc/api/{0}/dotnet/#{1}", converter, id);
        }
    }

// generated code

    /**
    * Conversion from HTML to PDF.
    */
    public sealed class HtmlToPdfClient
    {
        private ConnectionHelper helper;
        private Dictionary<string, string> fields = new Dictionary<string, string>();
        private Dictionary<string, string> files = new Dictionary<string, string>();
        private Dictionary<string, byte[]> rawData = new Dictionary<string, byte[]>();

        #pragma warning disable CS0414
        private int fileId = 1;
        #pragma warning restore CS0414

        /**
        * Constructor for the Pdfcrowd API client.
        * 
        * @param userName Your username at Pdfcrowd.
        * @param apiKey Your API key.
        */
        public HtmlToPdfClient(string userName, string apiKey)
        {
            this.helper = new ConnectionHelper(userName, apiKey);
            fields["input_format"] = "html";
            fields["output_format"] = "pdf";
        }

        /**
        * Convert a web page.
        * 
        * @param url The address of the web page to convert. The supported protocols are http:// and https://.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "html-to-pdf", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
            fields["url"] = url;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a web page and write the result to an output stream.
        * 
        * @param url The address of the web page to convert. The supported protocols are http:// and https://.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertUrlToStream(string url, Stream outStream)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "html-to-pdf", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
            fields["url"] = url;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a web page and write the result to a local file.
        * 
        * @param url The address of the web page to convert. The supported protocols are http:// and https://.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertUrlToFile(string url, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "html-to-pdf", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertUrlToStream(url, outputFile);
            outputFile.Close();
        }

        /**
        * Convert a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip).<br> If the HTML document refers to local external assets (images, style sheets, javascript), zip the document together with the assets. The file must exist and not be empty. The file name must have a valid extension.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertFile(string file)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-pdf", "The file must exist and not be empty.", "convert_file"), 470);
            
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-pdf", "The file name must have a valid extension.", "convert_file"), 470);
            
            files["file"] = file;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a local file and write the result to an output stream.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip).<br> If the HTML document refers to local external assets (images, style sheets, javascript), zip the document together with the assets. The file must exist and not be empty. The file name must have a valid extension.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertFileToStream(string file, Stream outStream)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-pdf", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-pdf", "The file name must have a valid extension.", "convert_file_to_stream"), 470);
            
            files["file"] = file;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a local file and write the result to a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip).<br> If the HTML document refers to local external assets (images, style sheets, javascript), zip the document together with the assets. The file must exist and not be empty. The file name must have a valid extension.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertFileToFile(string file, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "html-to-pdf", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertFileToStream(file, outputFile);
            outputFile.Close();
        }

        /**
        * Convert a string.
        * 
        * @param text The string content to convert. The string must not be empty.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertString(string text)
        {
            if (!(!String.IsNullOrEmpty(text)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "text", "html-to-pdf", "The string must not be empty.", "convert_string"), 470);
            
            fields["text"] = text;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a string and write the output to an output stream.
        * 
        * @param text The string content to convert. The string must not be empty.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertStringToStream(string text, Stream outStream)
        {
            if (!(!String.IsNullOrEmpty(text)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "text", "html-to-pdf", "The string must not be empty.", "convert_string_to_stream"), 470);
            
            fields["text"] = text;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a string and write the output to a file.
        * 
        * @param text The string content to convert. The string must not be empty.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertStringToFile(string text, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "html-to-pdf", "The string must not be empty.", "convert_string_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertStringToStream(text, outputFile);
            outputFile.Close();
        }

        /**
        * Set the output page size.
        * 
        * @param pageSize Allowed values are A2, A3, A4, A5, A6, Letter.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageSize(string pageSize)
        {
            if (!Regex.Match(pageSize, "(?i)^(A2|A3|A4|A5|A6|Letter)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pageSize, "page_size", "html-to-pdf", "Allowed values are A2, A3, A4, A5, A6, Letter.", "set_page_size"), 470);
            
            fields["page_size"] = pageSize;
            return this;
        }

        /**
        * Set the output page width.
        * 
        * @param pageWidth Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setPageWidth(string pageWidth)
        {
            if (!Regex.Match(pageWidth, "(?i)^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pageWidth, "page_width", "html-to-pdf", "Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_page_width"), 470);
            
            fields["page_width"] = pageWidth;
            return this;
        }

        /**
        * Set the output page height. Use <span class='field-value'>-1</span> for a single page PDF.
        * 
        * @param pageHeight Can be -1 or specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setPageHeight(string pageHeight)
        {
            if (!Regex.Match(pageHeight, "(?i)^\\-1$|^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pageHeight, "page_height", "html-to-pdf", "Can be -1 or specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_page_height"), 470);
            
            fields["page_height"] = pageHeight;
            return this;
        }

        /**
        * Set the output page dimensions.
        * 
        * @param width Set the output page width. Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @param height Set the output page height. Use <span class='field-value'>-1</span> for a single page PDF. Can be -1 or specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setPageDimensions(string width, string height)
        {
            this.setPageWidth(width);
            this.setPageHeight(height);
            return this;
        }

        /**
        * Set the output page orientation.
        * 
        * @param orientation Allowed values are landscape, portrait.
        * @return The converter object.
        */
        public HtmlToPdfClient setOrientation(string orientation)
        {
            if (!Regex.Match(orientation, "(?i)^(landscape|portrait)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(orientation, "orientation", "html-to-pdf", "Allowed values are landscape, portrait.", "set_orientation"), 470);
            
            fields["orientation"] = orientation;
            return this;
        }

        /**
        * Set the output page top margin.
        * 
        * @param marginTop Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginTop(string marginTop)
        {
            if (!Regex.Match(marginTop, "(?i)^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(marginTop, "margin_top", "html-to-pdf", "Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_margin_top"), 470);
            
            fields["margin_top"] = marginTop;
            return this;
        }

        /**
        * Set the output page right margin.
        * 
        * @param marginRight Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginRight(string marginRight)
        {
            if (!Regex.Match(marginRight, "(?i)^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(marginRight, "margin_right", "html-to-pdf", "Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_margin_right"), 470);
            
            fields["margin_right"] = marginRight;
            return this;
        }

        /**
        * Set the output page bottom margin.
        * 
        * @param marginBottom Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginBottom(string marginBottom)
        {
            if (!Regex.Match(marginBottom, "(?i)^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(marginBottom, "margin_bottom", "html-to-pdf", "Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_margin_bottom"), 470);
            
            fields["margin_bottom"] = marginBottom;
            return this;
        }

        /**
        * Set the output page left margin.
        * 
        * @param marginLeft Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginLeft(string marginLeft)
        {
            if (!Regex.Match(marginLeft, "(?i)^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(marginLeft, "margin_left", "html-to-pdf", "Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_margin_left"), 470);
            
            fields["margin_left"] = marginLeft;
            return this;
        }

        /**
        * Disable margins.
        * 
        * @param noMargins Set to <span class='field-value'>true</span> to disable margins.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoMargins(bool noMargins)
        {
            fields["no_margins"] = noMargins ? "true" : null;
            return this;
        }

        /**
        * Set the output page margins.
        * 
        * @param top Set the output page top margin. Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @param right Set the output page right margin. Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @param bottom Set the output page bottom margin. Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @param left Set the output page left margin. Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setPageMargins(string top, string right, string bottom, string left)
        {
            this.setMarginTop(top);
            this.setMarginRight(right);
            this.setMarginBottom(bottom);
            this.setMarginLeft(left);
            return this;
        }

        /**
        * Load an HTML code from the specified URL and use it as the page header. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of a converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals <ul> <li>Arabic numerals are used by default.</li> <li>Roman numerals can be generated by the <span class='field-value'>roman</span> and <span class='field-value'>roman-lowercase</span> values</li> <li>Example: &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt;</li> </ul> </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL, allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul>
</li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        * 
        * @param headerUrl The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderUrl(string headerUrl)
        {
            if (!Regex.Match(headerUrl, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(headerUrl, "header_url", "html-to-pdf", "The supported protocols are http:// and https://.", "set_header_url"), 470);
            
            fields["header_url"] = headerUrl;
            return this;
        }

        /**
        * Use the specified HTML code as the page header. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of a converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals <ul> <li>Arabic numerals are used by default.</li> <li>Roman numerals can be generated by the <span class='field-value'>roman</span> and <span class='field-value'>roman-lowercase</span> values</li> <li>Example: &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt;</li> </ul> </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL, allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul>
</li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        * 
        * @param headerHtml The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderHtml(string headerHtml)
        {
            if (!(!String.IsNullOrEmpty(headerHtml)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(headerHtml, "header_html", "html-to-pdf", "The string must not be empty.", "set_header_html"), 470);
            
            fields["header_html"] = headerHtml;
            return this;
        }

        /**
        * Set the header height.
        * 
        * @param headerHeight Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderHeight(string headerHeight)
        {
            if (!Regex.Match(headerHeight, "(?i)^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(headerHeight, "header_height", "html-to-pdf", "Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_header_height"), 470);
            
            fields["header_height"] = headerHeight;
            return this;
        }

        /**
        * Load an HTML code from the specified URL and use it as the page footer. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of a converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals <ul> <li>Arabic numerals are used by default.</li> <li>Roman numerals can be generated by the <span class='field-value'>roman</span> and <span class='field-value'>roman-lowercase</span> values</li> <li>Example: &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt;</li> </ul> </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL, allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul>
</li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        * 
        * @param footerUrl The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setFooterUrl(string footerUrl)
        {
            if (!Regex.Match(footerUrl, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(footerUrl, "footer_url", "html-to-pdf", "The supported protocols are http:// and https://.", "set_footer_url"), 470);
            
            fields["footer_url"] = footerUrl;
            return this;
        }

        /**
        * Use the specified HTML as the page footer. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of a converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals <ul> <li>Arabic numerals are used by default.</li> <li>Roman numerals can be generated by the <span class='field-value'>roman</span> and <span class='field-value'>roman-lowercase</span> values</li> <li>Example: &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt;</li> </ul> </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL, allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul>
</li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        * 
        * @param footerHtml The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setFooterHtml(string footerHtml)
        {
            if (!(!String.IsNullOrEmpty(footerHtml)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(footerHtml, "footer_html", "html-to-pdf", "The string must not be empty.", "set_footer_html"), 470);
            
            fields["footer_html"] = footerHtml;
            return this;
        }

        /**
        * Set the footer height.
        * 
        * @param footerHeight Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).
        * @return The converter object.
        */
        public HtmlToPdfClient setFooterHeight(string footerHeight)
        {
            if (!Regex.Match(footerHeight, "(?i)^[0-9]*(\\.[0-9]+)?(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(footerHeight, "footer_height", "html-to-pdf", "Can be specified in inches (in), millimeters (mm), centimeters (cm), or points (pt).", "set_footer_height"), 470);
            
            fields["footer_height"] = footerHeight;
            return this;
        }

        /**
        * Set the page range to print.
        * 
        * @param pages A comma seperated list of page numbers or ranges.
        * @return The converter object.
        */
        public HtmlToPdfClient setPrintPageRange(string pages)
        {
            if (!Regex.Match(pages, "^(?:\\s*(?:\\d+|(?:\\d*\\s*\\-\\s*\\d+)|(?:\\d+\\s*\\-\\s*\\d*))\\s*,\\s*)*\\s*(?:\\d+|(?:\\d*\\s*\\-\\s*\\d+)|(?:\\d+\\s*\\-\\s*\\d*))\\s*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pages, "pages", "html-to-pdf", "A comma seperated list of page numbers or ranges.", "set_print_page_range"), 470);
            
            fields["print_page_range"] = pages;
            return this;
        }

        /**
        * Apply the first page of the watermark PDF to every page of the output PDF.
        * 
        * @param pageWatermark The file path to a local watermark PDF file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageWatermark(string pageWatermark)
        {
            if (!(File.Exists(pageWatermark) && new FileInfo(pageWatermark).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(pageWatermark, "page_watermark", "html-to-pdf", "The file must exist and not be empty.", "set_page_watermark"), 470);
            
            files["page_watermark"] = pageWatermark;
            return this;
        }

        /**
        * Apply each page of the specified watermark PDF to the corresponding page of the output PDF.
        * 
        * @param multipageWatermark The file path to a local watermark PDF file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setMultipageWatermark(string multipageWatermark)
        {
            if (!(File.Exists(multipageWatermark) && new FileInfo(multipageWatermark).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(multipageWatermark, "multipage_watermark", "html-to-pdf", "The file must exist and not be empty.", "set_multipage_watermark"), 470);
            
            files["multipage_watermark"] = multipageWatermark;
            return this;
        }

        /**
        * Apply the first page of the specified PDF to the background of every page of the output PDF.
        * 
        * @param pageBackground The file path to a local background PDF file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageBackground(string pageBackground)
        {
            if (!(File.Exists(pageBackground) && new FileInfo(pageBackground).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(pageBackground, "page_background", "html-to-pdf", "The file must exist and not be empty.", "set_page_background"), 470);
            
            files["page_background"] = pageBackground;
            return this;
        }

        /**
        * Apply each page of the specified PDF to the background of the corresponding page of the output PDF.
        * 
        * @param multipageBackground The file path to a local background PDF file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setMultipageBackground(string multipageBackground)
        {
            if (!(File.Exists(multipageBackground) && new FileInfo(multipageBackground).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(multipageBackground, "multipage_background", "html-to-pdf", "The file must exist and not be empty.", "set_multipage_background"), 470);
            
            files["multipage_background"] = multipageBackground;
            return this;
        }

        /**
        * The page header is not printed on the specified pages.
        * 
        * @param pages List of physical page numbers. Negative numbers count backwards from the last page: -1 is the last page, -2 is the last but one page, and so on. A comma seperated list of page numbers.
        * @return The converter object.
        */
        public HtmlToPdfClient setExcludeHeaderOnPages(string pages)
        {
            if (!Regex.Match(pages, "^(?:\\s*\\-?\\d+\\s*,)*\\s*\\-?\\d+\\s*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pages, "pages", "html-to-pdf", "A comma seperated list of page numbers.", "set_exclude_header_on_pages"), 470);
            
            fields["exclude_header_on_pages"] = pages;
            return this;
        }

        /**
        * The page footer is not printed on the specified pages.
        * 
        * @param pages List of physical page numbers. Negative numbers count backwards from the last page: -1 is the last page, -2 is the last but one page, and so on. A comma seperated list of page numbers.
        * @return The converter object.
        */
        public HtmlToPdfClient setExcludeFooterOnPages(string pages)
        {
            if (!Regex.Match(pages, "^(?:\\s*\\-?\\d+\\s*,)*\\s*\\-?\\d+\\s*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pages, "pages", "html-to-pdf", "A comma seperated list of page numbers.", "set_exclude_footer_on_pages"), 470);
            
            fields["exclude_footer_on_pages"] = pages;
            return this;
        }

        /**
        * Set an offset between physical and logical page numbers.
        * 
        * @param offset Integer specifying page offset.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageNumberingOffset(int offset)
        {
            fields["page_numbering_offset"] = ConnectionHelper.intToString(offset);
            return this;
        }

        /**
        * Do not print the background graphics.
        * 
        * @param noBackground Set to <span class='field-value'>true</span> to disable the background graphics.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoBackground(bool noBackground)
        {
            fields["no_background"] = noBackground ? "true" : null;
            return this;
        }

        /**
        * Do not execute JavaScript.
        * 
        * @param disableJavascript Set to <span class='field-value'>true</span> to disable JavaScript in web pages.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisableJavascript(bool disableJavascript)
        {
            fields["disable_javascript"] = disableJavascript ? "true" : null;
            return this;
        }

        /**
        * Do not load images.
        * 
        * @param disableImageLoading Set to <span class='field-value'>true</span> to disable loading of images.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisableImageLoading(bool disableImageLoading)
        {
            fields["disable_image_loading"] = disableImageLoading ? "true" : null;
            return this;
        }

        /**
        * Disable loading fonts from remote sources.
        * 
        * @param disableRemoteFonts Set to <span class='field-value'>true</span> disable loading remote fonts.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisableRemoteFonts(bool disableRemoteFonts)
        {
            fields["disable_remote_fonts"] = disableRemoteFonts ? "true" : null;
            return this;
        }

        /**
        * Try to block ads. Enabling this option can produce smaller output and speed up the conversion.
        * 
        * @param blockAds Set to <span class='field-value'>true</span> to block ads in web pages.
        * @return The converter object.
        */
        public HtmlToPdfClient setBlockAds(bool blockAds)
        {
            fields["block_ads"] = blockAds ? "true" : null;
            return this;
        }

        /**
        * Set the default HTML content text encoding.
        * 
        * @param defaultEncoding The text encoding of the HTML content.
        * @return The converter object.
        */
        public HtmlToPdfClient setDefaultEncoding(string defaultEncoding)
        {
            fields["default_encoding"] = defaultEncoding;
            return this;
        }

        /**
        * Set the HTTP authentication user name.
        * 
        * @param userName The user name.
        * @return The converter object.
        */
        public HtmlToPdfClient setHttpAuthUserName(string userName)
        {
            fields["http_auth_user_name"] = userName;
            return this;
        }

        /**
        * Set the HTTP authentication password.
        * 
        * @param password The password.
        * @return The converter object.
        */
        public HtmlToPdfClient setHttpAuthPassword(string password)
        {
            fields["http_auth_password"] = password;
            return this;
        }

        /**
        * Set the HTTP authentication.
        * 
        * @param userName Set the HTTP authentication user name.
        * @param password Set the HTTP authentication password.
        * @return The converter object.
        */
        public HtmlToPdfClient setHttpAuth(string userName, string password)
        {
            this.setHttpAuthUserName(userName);
            this.setHttpAuthPassword(password);
            return this;
        }

        /**
        * Use the print version of the page if available (@media print).
        * 
        * @param usePrintMedia Set to <span class='field-value'>true</span> to use the print version of the page.
        * @return The converter object.
        */
        public HtmlToPdfClient setUsePrintMedia(bool usePrintMedia)
        {
            fields["use_print_media"] = usePrintMedia ? "true" : null;
            return this;
        }

        /**
        * Do not send the X-Pdfcrowd HTTP header in Pdfcrowd HTTP requests.
        * 
        * @param noXpdfcrowdHeader Set to <span class='field-value'>true</span> to disable sending X-Pdfcrowd HTTP header.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoXpdfcrowdHeader(bool noXpdfcrowdHeader)
        {
            fields["no_xpdfcrowd_header"] = noXpdfcrowdHeader ? "true" : null;
            return this;
        }

        /**
        * Set cookies that are sent in Pdfcrowd HTTP requests.
        * 
        * @param cookies The cookie string.
        * @return The converter object.
        */
        public HtmlToPdfClient setCookies(string cookies)
        {
            fields["cookies"] = cookies;
            return this;
        }

        /**
        * Do not allow insecure HTTPS connections.
        * 
        * @param verifySslCertificates Set to <span class='field-value'>true</span> to enable SSL certificate verification.
        * @return The converter object.
        */
        public HtmlToPdfClient setVerifySslCertificates(bool verifySslCertificates)
        {
            fields["verify_ssl_certificates"] = verifySslCertificates ? "true" : null;
            return this;
        }

        /**
        * Abort the conversion if the main URL HTTP status code is greater than or equal to 400.
        * 
        * @param failOnError Set to <span class='field-value'>true</span> to abort the conversion.
        * @return The converter object.
        */
        public HtmlToPdfClient setFailOnMainUrlError(bool failOnError)
        {
            fields["fail_on_main_url_error"] = failOnError ? "true" : null;
            return this;
        }

        /**
        * Abort the conversion if any of the sub-request HTTP status code is greater than or equal to 400.
        * 
        * @param failOnError Set to <span class='field-value'>true</span> to abort the conversion.
        * @return The converter object.
        */
        public HtmlToPdfClient setFailOnAnyUrlError(bool failOnError)
        {
            fields["fail_on_any_url_error"] = failOnError ? "true" : null;
            return this;
        }

        /**
        * Run a custom JavaScript after the document is loaded. The script is intended for post-load DOM manipulation (add/remove elements, update CSS, ...).
        * 
        * @param customJavascript String containing a JavaScript code. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setCustomJavascript(string customJavascript)
        {
            if (!(!String.IsNullOrEmpty(customJavascript)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(customJavascript, "custom_javascript", "html-to-pdf", "The string must not be empty.", "set_custom_javascript"), 470);
            
            fields["custom_javascript"] = customJavascript;
            return this;
        }

        /**
        * Set a custom HTTP header that is sent in Pdfcrowd HTTP requests.
        * 
        * @param customHttpHeader A string containing the header name and value separated by a colon.
        * @return The converter object.
        */
        public HtmlToPdfClient setCustomHttpHeader(string customHttpHeader)
        {
            if (!Regex.Match(customHttpHeader, "^.+:.+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(customHttpHeader, "custom_http_header", "html-to-pdf", "A string containing the header name and value separated by a colon.", "set_custom_http_header"), 470);
            
            fields["custom_http_header"] = customHttpHeader;
            return this;
        }

        /**
        * Wait the specified number of milliseconds to finish all JavaScript after the document is loaded. The maximum value is determined by your API license.
        * 
        * @param javascriptDelay The number of milliseconds to wait. Must be a positive integer number or 0.
        * @return The converter object.
        */
        public HtmlToPdfClient setJavascriptDelay(int javascriptDelay)
        {
            if (!(javascriptDelay >= 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(javascriptDelay, "javascript_delay", "html-to-pdf", "Must be a positive integer number or 0.", "set_javascript_delay"), 470);
            
            fields["javascript_delay"] = ConnectionHelper.intToString(javascriptDelay);
            return this;
        }

        /**
        * Convert only the specified element and its children. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. If the element is not found, the conversion fails. If multiple elements are found, the first one is used.
        * 
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setElementToConvert(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "selectors", "html-to-pdf", "The string must not be empty.", "set_element_to_convert"), 470);
            
            fields["element_to_convert"] = selectors;
            return this;
        }

        /**
        * Specify the DOM handling when only a part of the document is converted.
        * 
        * @param mode Allowed values are cut-out, remove-siblings, hide-siblings.
        * @return The converter object.
        */
        public HtmlToPdfClient setElementToConvertMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(cut-out|remove-siblings|hide-siblings)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "mode", "html-to-pdf", "Allowed values are cut-out, remove-siblings, hide-siblings.", "set_element_to_convert_mode"), 470);
            
            fields["element_to_convert_mode"] = mode;
            return this;
        }

        /**
        * Wait for the specified element in a source document. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. If the element is not found, the conversion fails.
        * 
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setWaitForElement(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "selectors", "html-to-pdf", "The string must not be empty.", "set_wait_for_element"), 470);
            
            fields["wait_for_element"] = selectors;
            return this;
        }

        /**
        * Set the viewport width in pixels. The viewport is the user's visible area of the page.
        * 
        * @param viewportWidth The value must be in a range 96-7680.
        * @return The converter object.
        */
        public HtmlToPdfClient setViewportWidth(int viewportWidth)
        {
            if (!(viewportWidth >= 96 && viewportWidth <= 7680))
                throw new Error(ConnectionHelper.createInvalidValueMessage(viewportWidth, "viewport_width", "html-to-pdf", "The value must be in a range 96-7680.", "set_viewport_width"), 470);
            
            fields["viewport_width"] = ConnectionHelper.intToString(viewportWidth);
            return this;
        }

        /**
        * Set the viewport height in pixels. The viewport is the user's visible area of the page.
        * 
        * @param viewportHeight Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToPdfClient setViewportHeight(int viewportHeight)
        {
            if (!(viewportHeight > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(viewportHeight, "viewport_height", "html-to-pdf", "Must be a positive integer number.", "set_viewport_height"), 470);
            
            fields["viewport_height"] = ConnectionHelper.intToString(viewportHeight);
            return this;
        }

        /**
        * Set the viewport size. The viewport is the user's visible area of the page.
        * 
        * @param width Set the viewport width in pixels. The viewport is the user's visible area of the page. The value must be in a range 96-7680.
        * @param height Set the viewport height in pixels. The viewport is the user's visible area of the page. Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToPdfClient setViewport(int width, int height)
        {
            this.setViewportWidth(width);
            this.setViewportHeight(height);
            return this;
        }

        /**
        * Sets the rendering mode.
        * 
        * @param renderingMode The rendering mode. Allowed values are default, viewport.
        * @return The converter object.
        */
        public HtmlToPdfClient setRenderingMode(string renderingMode)
        {
            if (!Regex.Match(renderingMode, "(?i)^(default|viewport)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(renderingMode, "rendering_mode", "html-to-pdf", "Allowed values are default, viewport.", "set_rendering_mode"), 470);
            
            fields["rendering_mode"] = renderingMode;
            return this;
        }

        /**
        * Set the scaling factor (zoom) for the main page area.
        * 
        * @param scaleFactor The scale factor. The value must be in a range 10-500.
        * @return The converter object.
        */
        public HtmlToPdfClient setScaleFactor(int scaleFactor)
        {
            if (!(scaleFactor >= 10 && scaleFactor <= 500))
                throw new Error(ConnectionHelper.createInvalidValueMessage(scaleFactor, "scale_factor", "html-to-pdf", "The value must be in a range 10-500.", "set_scale_factor"), 470);
            
            fields["scale_factor"] = ConnectionHelper.intToString(scaleFactor);
            return this;
        }

        /**
        * Set the scaling factor (zoom) for the header and footer.
        * 
        * @param headerFooterScaleFactor The scale factor. The value must be in a range 10-500.
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderFooterScaleFactor(int headerFooterScaleFactor)
        {
            if (!(headerFooterScaleFactor >= 10 && headerFooterScaleFactor <= 500))
                throw new Error(ConnectionHelper.createInvalidValueMessage(headerFooterScaleFactor, "header_footer_scale_factor", "html-to-pdf", "The value must be in a range 10-500.", "set_header_footer_scale_factor"), 470);
            
            fields["header_footer_scale_factor"] = ConnectionHelper.intToString(headerFooterScaleFactor);
            return this;
        }

        /**
        * Create linearized PDF. This is also known as Fast Web View.
        * 
        * @param linearize Set to <span class='field-value'>true</span> to create linearized PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setLinearize(bool linearize)
        {
            fields["linearize"] = linearize ? "true" : null;
            return this;
        }

        /**
        * Encrypt the PDF. This prevents search engines from indexing the contents.
        * 
        * @param encrypt Set to <span class='field-value'>true</span> to enable PDF encryption.
        * @return The converter object.
        */
        public HtmlToPdfClient setEncrypt(bool encrypt)
        {
            fields["encrypt"] = encrypt ? "true" : null;
            return this;
        }

        /**
        * Protect the PDF with a user password. When a PDF has a user password, it must be supplied in order to view the document and to perform operations allowed by the access permissions.
        * 
        * @param userPassword The user password.
        * @return The converter object.
        */
        public HtmlToPdfClient setUserPassword(string userPassword)
        {
            fields["user_password"] = userPassword;
            return this;
        }

        /**
        * Protect the PDF with an owner password.  Supplying an owner password grants unlimited access to the PDF including changing the passwords and access permissions.
        * 
        * @param ownerPassword The owner password.
        * @return The converter object.
        */
        public HtmlToPdfClient setOwnerPassword(string ownerPassword)
        {
            fields["owner_password"] = ownerPassword;
            return this;
        }

        /**
        * Disallow printing of the output PDF.
        * 
        * @param noPrint Set to <span class='field-value'>true</span> to set the no-print flag in the output PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoPrint(bool noPrint)
        {
            fields["no_print"] = noPrint ? "true" : null;
            return this;
        }

        /**
        * Disallow modification of the ouput PDF.
        * 
        * @param noModify Set to <span class='field-value'>true</span> to set the read-only only flag in the output PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoModify(bool noModify)
        {
            fields["no_modify"] = noModify ? "true" : null;
            return this;
        }

        /**
        * Disallow text and graphics extraction from the output PDF.
        * 
        * @param noCopy Set to <span class='field-value'>true</span> to set the no-copy flag in the output PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoCopy(bool noCopy)
        {
            fields["no_copy"] = noCopy ? "true" : null;
            return this;
        }

        /**
        * Turn on the debug logging.
        * 
        * @param debugLog Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public HtmlToPdfClient setDebugLog(bool debugLog)
        {
            fields["debug_log"] = debugLog ? "true" : null;
            return this;
        }

        /**
        * Get the URL of the debug log for the last conversion.
        * @return The link to the debug log.
        */
        public string getDebugLogUrl()
        {
            return helper.getDebugLogUrl();
        }

        /**
        * Get the number of conversion credits available in your <a href='/user/account/'>account</a>.
        * The returned value can differ from the actual count if you run parallel conversions.
        * The special value <span class='field-value'>999999</span> is returned if the information is not available.
        * @return The number of credits.
        */
        public int getRemainingCreditCount()
        {
            return helper.getRemainingCreditCount();
        }

        /**
        * Get the number of credits consumed by the last conversion.
        * @return The number of credits.
        */
        public int getConsumedCreditCount()
        {
            return helper.getConsumedCreditCount();
        }

        /**
        * Get the job id.
        * @return The unique job identifier.
        */
        public string getJobId()
        {
            return helper.getJobId();
        }

        /**
        * Get the total number of pages in the output document.
        * @return The page count.
        */
        public int getPageCount()
        {
            return helper.getPageCount();
        }

        /**
        * Get the size of the output in bytes.
        * @return The count of bytes.
        */
        public int getOutputSize()
        {
            return helper.getOutputSize();
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * 
        * @param useHttp Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public HtmlToPdfClient setUseHttp(bool useHttp)
        {
            helper.setUseHttp(useHttp);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be usefull if you are behind some proxy or firewall.
        * 
        * @param userAgent The user agent string.
        * @return The converter object.
        */
        public HtmlToPdfClient setUserAgent(string userAgent)
        {
            helper.setUserAgent(userAgent);
            return this;
        }

        /**
        * Specifies an HTTP proxy that the API client library will use to connect to the internet.
        * 
        * @param host The proxy hostname.
        * @param port The proxy port.
        * @param userName The username.
        * @param password The password.
        * @return The converter object.
        */
        public HtmlToPdfClient setProxy(string host, int port, string userName, string password)
        {
            helper.setProxy(host, port, userName, password);
            return this;
        }

        /**
        * Specifies the number of retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        * 
        * @param retryCount Number of retries wanted.
        * @return The converter object.
        */
        public HtmlToPdfClient setRetryCount(int retryCount)
        {
            helper.setRetryCount(retryCount);
            return this;
        }

    }

    /**
    * Conversion from HTML to image.
    */
    public sealed class HtmlToImageClient
    {
        private ConnectionHelper helper;
        private Dictionary<string, string> fields = new Dictionary<string, string>();
        private Dictionary<string, string> files = new Dictionary<string, string>();
        private Dictionary<string, byte[]> rawData = new Dictionary<string, byte[]>();

        #pragma warning disable CS0414
        private int fileId = 1;
        #pragma warning restore CS0414

        /**
        * Constructor for the Pdfcrowd API client.
        * 
        * @param userName Your username at Pdfcrowd.
        * @param apiKey Your API key.
        */
        public HtmlToImageClient(string userName, string apiKey)
        {
            this.helper = new ConnectionHelper(userName, apiKey);
            fields["input_format"] = "html";
            fields["output_format"] = "png";
        }

        /**
        * The format of the output file.
        * 
        * @param outputFormat Allowed values are png, jpg, gif, tiff, bmp, ico, ppm, pgm, pbm, pnm, psb, pct, ras, tga, sgi, sun, webp.
        * @return The converter object.
        */
        public HtmlToImageClient setOutputFormat(string outputFormat)
        {
            if (!Regex.Match(outputFormat, "(?i)^(png|jpg|gif|tiff|bmp|ico|ppm|pgm|pbm|pnm|psb|pct|ras|tga|sgi|sun|webp)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(outputFormat, "output_format", "html-to-image", "Allowed values are png, jpg, gif, tiff, bmp, ico, ppm, pgm, pbm, pnm, psb, pct, ras, tga, sgi, sun, webp.", "set_output_format"), 470);
            
            fields["output_format"] = outputFormat;
            return this;
        }

        /**
        * Convert a web page.
        * 
        * @param url The address of the web page to convert. The supported protocols are http:// and https://.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "html-to-image", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
            fields["url"] = url;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a web page and write the result to an output stream.
        * 
        * @param url The address of the web page to convert. The supported protocols are http:// and https://.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertUrlToStream(string url, Stream outStream)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "html-to-image", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
            fields["url"] = url;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a web page and write the result to a local file.
        * 
        * @param url The address of the web page to convert. The supported protocols are http:// and https://.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertUrlToFile(string url, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "html-to-image", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertUrlToStream(url, outputFile);
            outputFile.Close();
        }

        /**
        * Convert a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip).<br> If the HTML document refers to local external assets (images, style sheets, javascript), zip the document together with the assets. The file must exist and not be empty. The file name must have a valid extension.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertFile(string file)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-image", "The file must exist and not be empty.", "convert_file"), 470);
            
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-image", "The file name must have a valid extension.", "convert_file"), 470);
            
            files["file"] = file;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a local file and write the result to an output stream.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip).<br> If the HTML document refers to local external assets (images, style sheets, javascript), zip the document together with the assets. The file must exist and not be empty. The file name must have a valid extension.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertFileToStream(string file, Stream outStream)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-image", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "html-to-image", "The file name must have a valid extension.", "convert_file_to_stream"), 470);
            
            files["file"] = file;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a local file and write the result to a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip).<br> If the HTML document refers to local external assets (images, style sheets, javascript), zip the document together with the assets. The file must exist and not be empty. The file name must have a valid extension.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertFileToFile(string file, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "html-to-image", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertFileToStream(file, outputFile);
            outputFile.Close();
        }

        /**
        * Convert a string.
        * 
        * @param text The string content to convert. The string must not be empty.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertString(string text)
        {
            if (!(!String.IsNullOrEmpty(text)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "text", "html-to-image", "The string must not be empty.", "convert_string"), 470);
            
            fields["text"] = text;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a string and write the output to an output stream.
        * 
        * @param text The string content to convert. The string must not be empty.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertStringToStream(string text, Stream outStream)
        {
            if (!(!String.IsNullOrEmpty(text)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "text", "html-to-image", "The string must not be empty.", "convert_string_to_stream"), 470);
            
            fields["text"] = text;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a string and write the output to a file.
        * 
        * @param text The string content to convert. The string must not be empty.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertStringToFile(string text, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "html-to-image", "The string must not be empty.", "convert_string_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertStringToStream(text, outputFile);
            outputFile.Close();
        }

        /**
        * Do not print the background graphics.
        * 
        * @param noBackground Set to <span class='field-value'>true</span> to disable the background graphics.
        * @return The converter object.
        */
        public HtmlToImageClient setNoBackground(bool noBackground)
        {
            fields["no_background"] = noBackground ? "true" : null;
            return this;
        }

        /**
        * Do not execute JavaScript.
        * 
        * @param disableJavascript Set to <span class='field-value'>true</span> to disable JavaScript in web pages.
        * @return The converter object.
        */
        public HtmlToImageClient setDisableJavascript(bool disableJavascript)
        {
            fields["disable_javascript"] = disableJavascript ? "true" : null;
            return this;
        }

        /**
        * Do not load images.
        * 
        * @param disableImageLoading Set to <span class='field-value'>true</span> to disable loading of images.
        * @return The converter object.
        */
        public HtmlToImageClient setDisableImageLoading(bool disableImageLoading)
        {
            fields["disable_image_loading"] = disableImageLoading ? "true" : null;
            return this;
        }

        /**
        * Disable loading fonts from remote sources.
        * 
        * @param disableRemoteFonts Set to <span class='field-value'>true</span> disable loading remote fonts.
        * @return The converter object.
        */
        public HtmlToImageClient setDisableRemoteFonts(bool disableRemoteFonts)
        {
            fields["disable_remote_fonts"] = disableRemoteFonts ? "true" : null;
            return this;
        }

        /**
        * Try to block ads. Enabling this option can produce smaller output and speed up the conversion.
        * 
        * @param blockAds Set to <span class='field-value'>true</span> to block ads in web pages.
        * @return The converter object.
        */
        public HtmlToImageClient setBlockAds(bool blockAds)
        {
            fields["block_ads"] = blockAds ? "true" : null;
            return this;
        }

        /**
        * Set the default HTML content text encoding.
        * 
        * @param defaultEncoding The text encoding of the HTML content.
        * @return The converter object.
        */
        public HtmlToImageClient setDefaultEncoding(string defaultEncoding)
        {
            fields["default_encoding"] = defaultEncoding;
            return this;
        }

        /**
        * Set the HTTP authentication user name.
        * 
        * @param userName The user name.
        * @return The converter object.
        */
        public HtmlToImageClient setHttpAuthUserName(string userName)
        {
            fields["http_auth_user_name"] = userName;
            return this;
        }

        /**
        * Set the HTTP authentication password.
        * 
        * @param password The password.
        * @return The converter object.
        */
        public HtmlToImageClient setHttpAuthPassword(string password)
        {
            fields["http_auth_password"] = password;
            return this;
        }

        /**
        * Set the HTTP authentication.
        * 
        * @param userName Set the HTTP authentication user name.
        * @param password Set the HTTP authentication password.
        * @return The converter object.
        */
        public HtmlToImageClient setHttpAuth(string userName, string password)
        {
            this.setHttpAuthUserName(userName);
            this.setHttpAuthPassword(password);
            return this;
        }

        /**
        * Use the print version of the page if available (@media print).
        * 
        * @param usePrintMedia Set to <span class='field-value'>true</span> to use the print version of the page.
        * @return The converter object.
        */
        public HtmlToImageClient setUsePrintMedia(bool usePrintMedia)
        {
            fields["use_print_media"] = usePrintMedia ? "true" : null;
            return this;
        }

        /**
        * Do not send the X-Pdfcrowd HTTP header in Pdfcrowd HTTP requests.
        * 
        * @param noXpdfcrowdHeader Set to <span class='field-value'>true</span> to disable sending X-Pdfcrowd HTTP header.
        * @return The converter object.
        */
        public HtmlToImageClient setNoXpdfcrowdHeader(bool noXpdfcrowdHeader)
        {
            fields["no_xpdfcrowd_header"] = noXpdfcrowdHeader ? "true" : null;
            return this;
        }

        /**
        * Set cookies that are sent in Pdfcrowd HTTP requests.
        * 
        * @param cookies The cookie string.
        * @return The converter object.
        */
        public HtmlToImageClient setCookies(string cookies)
        {
            fields["cookies"] = cookies;
            return this;
        }

        /**
        * Do not allow insecure HTTPS connections.
        * 
        * @param verifySslCertificates Set to <span class='field-value'>true</span> to enable SSL certificate verification.
        * @return The converter object.
        */
        public HtmlToImageClient setVerifySslCertificates(bool verifySslCertificates)
        {
            fields["verify_ssl_certificates"] = verifySslCertificates ? "true" : null;
            return this;
        }

        /**
        * Abort the conversion if the main URL HTTP status code is greater than or equal to 400.
        * 
        * @param failOnError Set to <span class='field-value'>true</span> to abort the conversion.
        * @return The converter object.
        */
        public HtmlToImageClient setFailOnMainUrlError(bool failOnError)
        {
            fields["fail_on_main_url_error"] = failOnError ? "true" : null;
            return this;
        }

        /**
        * Abort the conversion if any of the sub-request HTTP status code is greater than or equal to 400.
        * 
        * @param failOnError Set to <span class='field-value'>true</span> to abort the conversion.
        * @return The converter object.
        */
        public HtmlToImageClient setFailOnAnyUrlError(bool failOnError)
        {
            fields["fail_on_any_url_error"] = failOnError ? "true" : null;
            return this;
        }

        /**
        * Run a custom JavaScript after the document is loaded. The script is intended for post-load DOM manipulation (add/remove elements, update CSS, ...).
        * 
        * @param customJavascript String containing a JavaScript code. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setCustomJavascript(string customJavascript)
        {
            if (!(!String.IsNullOrEmpty(customJavascript)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(customJavascript, "custom_javascript", "html-to-image", "The string must not be empty.", "set_custom_javascript"), 470);
            
            fields["custom_javascript"] = customJavascript;
            return this;
        }

        /**
        * Set a custom HTTP header that is sent in Pdfcrowd HTTP requests.
        * 
        * @param customHttpHeader A string containing the header name and value separated by a colon.
        * @return The converter object.
        */
        public HtmlToImageClient setCustomHttpHeader(string customHttpHeader)
        {
            if (!Regex.Match(customHttpHeader, "^.+:.+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(customHttpHeader, "custom_http_header", "html-to-image", "A string containing the header name and value separated by a colon.", "set_custom_http_header"), 470);
            
            fields["custom_http_header"] = customHttpHeader;
            return this;
        }

        /**
        * Wait the specified number of milliseconds to finish all JavaScript after the document is loaded. The maximum value is determined by your API license.
        * 
        * @param javascriptDelay The number of milliseconds to wait. Must be a positive integer number or 0.
        * @return The converter object.
        */
        public HtmlToImageClient setJavascriptDelay(int javascriptDelay)
        {
            if (!(javascriptDelay >= 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(javascriptDelay, "javascript_delay", "html-to-image", "Must be a positive integer number or 0.", "set_javascript_delay"), 470);
            
            fields["javascript_delay"] = ConnectionHelper.intToString(javascriptDelay);
            return this;
        }

        /**
        * Convert only the specified element and its children. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. If the element is not found, the conversion fails. If multiple elements are found, the first one is used.
        * 
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setElementToConvert(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "selectors", "html-to-image", "The string must not be empty.", "set_element_to_convert"), 470);
            
            fields["element_to_convert"] = selectors;
            return this;
        }

        /**
        * Specify the DOM handling when only a part of the document is converted.
        * 
        * @param mode Allowed values are cut-out, remove-siblings, hide-siblings.
        * @return The converter object.
        */
        public HtmlToImageClient setElementToConvertMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(cut-out|remove-siblings|hide-siblings)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "mode", "html-to-image", "Allowed values are cut-out, remove-siblings, hide-siblings.", "set_element_to_convert_mode"), 470);
            
            fields["element_to_convert_mode"] = mode;
            return this;
        }

        /**
        * Wait for the specified element in a source document. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. If the element is not found, the conversion fails.
        * 
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setWaitForElement(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "selectors", "html-to-image", "The string must not be empty.", "set_wait_for_element"), 470);
            
            fields["wait_for_element"] = selectors;
            return this;
        }

        /**
        * Set the output image width in pixels.
        * 
        * @param screenshotWidth The value must be in a range 96-7680.
        * @return The converter object.
        */
        public HtmlToImageClient setScreenshotWidth(int screenshotWidth)
        {
            if (!(screenshotWidth >= 96 && screenshotWidth <= 7680))
                throw new Error(ConnectionHelper.createInvalidValueMessage(screenshotWidth, "screenshot_width", "html-to-image", "The value must be in a range 96-7680.", "set_screenshot_width"), 470);
            
            fields["screenshot_width"] = ConnectionHelper.intToString(screenshotWidth);
            return this;
        }

        /**
        * Set the output image height in pixels. If it's not specified, actual document height is used.
        * 
        * @param screenshotHeight Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToImageClient setScreenshotHeight(int screenshotHeight)
        {
            if (!(screenshotHeight > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(screenshotHeight, "screenshot_height", "html-to-image", "Must be a positive integer number.", "set_screenshot_height"), 470);
            
            fields["screenshot_height"] = ConnectionHelper.intToString(screenshotHeight);
            return this;
        }

        /**
        * Turn on the debug logging.
        * 
        * @param debugLog Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public HtmlToImageClient setDebugLog(bool debugLog)
        {
            fields["debug_log"] = debugLog ? "true" : null;
            return this;
        }

        /**
        * Get the URL of the debug log for the last conversion.
        * @return The link to the debug log.
        */
        public string getDebugLogUrl()
        {
            return helper.getDebugLogUrl();
        }

        /**
        * Get the number of conversion credits available in your <a href='/user/account/'>account</a>.
        * The returned value can differ from the actual count if you run parallel conversions.
        * The special value <span class='field-value'>999999</span> is returned if the information is not available.
        * @return The number of credits.
        */
        public int getRemainingCreditCount()
        {
            return helper.getRemainingCreditCount();
        }

        /**
        * Get the number of credits consumed by the last conversion.
        * @return The number of credits.
        */
        public int getConsumedCreditCount()
        {
            return helper.getConsumedCreditCount();
        }

        /**
        * Get the job id.
        * @return The unique job identifier.
        */
        public string getJobId()
        {
            return helper.getJobId();
        }

        /**
        * Get the size of the output in bytes.
        * @return The count of bytes.
        */
        public int getOutputSize()
        {
            return helper.getOutputSize();
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * 
        * @param useHttp Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public HtmlToImageClient setUseHttp(bool useHttp)
        {
            helper.setUseHttp(useHttp);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be usefull if you are behind some proxy or firewall.
        * 
        * @param userAgent The user agent string.
        * @return The converter object.
        */
        public HtmlToImageClient setUserAgent(string userAgent)
        {
            helper.setUserAgent(userAgent);
            return this;
        }

        /**
        * Specifies an HTTP proxy that the API client library will use to connect to the internet.
        * 
        * @param host The proxy hostname.
        * @param port The proxy port.
        * @param userName The username.
        * @param password The password.
        * @return The converter object.
        */
        public HtmlToImageClient setProxy(string host, int port, string userName, string password)
        {
            helper.setProxy(host, port, userName, password);
            return this;
        }

        /**
        * Specifies the number of retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        * 
        * @param retryCount Number of retries wanted.
        * @return The converter object.
        */
        public HtmlToImageClient setRetryCount(int retryCount)
        {
            helper.setRetryCount(retryCount);
            return this;
        }

    }

    /**
    * Conversion from one image format to another image format.
    */
    public sealed class ImageToImageClient
    {
        private ConnectionHelper helper;
        private Dictionary<string, string> fields = new Dictionary<string, string>();
        private Dictionary<string, string> files = new Dictionary<string, string>();
        private Dictionary<string, byte[]> rawData = new Dictionary<string, byte[]>();

        #pragma warning disable CS0414
        private int fileId = 1;
        #pragma warning restore CS0414

        /**
        * Constructor for the Pdfcrowd API client.
        * 
        * @param userName Your username at Pdfcrowd.
        * @param apiKey Your API key.
        */
        public ImageToImageClient(string userName, string apiKey)
        {
            this.helper = new ConnectionHelper(userName, apiKey);
            fields["input_format"] = "image";
            fields["output_format"] = "png";
        }

        /**
        * Convert an image.
        * 
        * @param url The address of the image to convert. The supported protocols are http:// and https://.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "image-to-image", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
            fields["url"] = url;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert an image and write the result to an output stream.
        * 
        * @param url The address of the image to convert. The supported protocols are http:// and https://.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertUrlToStream(string url, Stream outStream)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "image-to-image", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
            fields["url"] = url;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert an image and write the result to a local file.
        * 
        * @param url The address of the image to convert. The supported protocols are http:// and https://.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertUrlToFile(string url, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "image-to-image", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertUrlToStream(url, outputFile);
            outputFile.Close();
        }

        /**
        * Convert a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip). The file must exist and not be empty.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertFile(string file)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "image-to-image", "The file must exist and not be empty.", "convert_file"), 470);
            
            files["file"] = file;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a local file and write the result to an output stream.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip). The file must exist and not be empty.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertFileToStream(string file, Stream outStream)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "image-to-image", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
            files["file"] = file;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a local file and write the result to a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip). The file must exist and not be empty.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertFileToFile(string file, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "image-to-image", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertFileToStream(file, outputFile);
            outputFile.Close();
        }

        /**
        * Convert raw data.
        * 
        * @param data The raw content to be converted.
        * @return Byte array with the output.
        */
        public byte[] convertRawData(byte[] data)
        {
            rawData["file"] = data;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert raw data and write the result to an output stream.
        * 
        * @param data The raw content to be converted.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertRawDataToStream(byte[] data, Stream outStream)
        {
            rawData["file"] = data;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert raw data to a file.
        * 
        * @param data The raw content to be converted.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertRawDataToFile(byte[] data, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "image-to-image", "The string must not be empty.", "convert_raw_data_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertRawDataToStream(data, outputFile);
            outputFile.Close();
        }

        /**
        * The format of the output file.
        * 
        * @param outputFormat Allowed values are png, jpg, gif, tiff, bmp, ico, ppm, pgm, pbm, pnm, psb, pct, ras, tga, sgi, sun, webp.
        * @return The converter object.
        */
        public ImageToImageClient setOutputFormat(string outputFormat)
        {
            if (!Regex.Match(outputFormat, "(?i)^(png|jpg|gif|tiff|bmp|ico|ppm|pgm|pbm|pnm|psb|pct|ras|tga|sgi|sun|webp)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(outputFormat, "output_format", "image-to-image", "Allowed values are png, jpg, gif, tiff, bmp, ico, ppm, pgm, pbm, pnm, psb, pct, ras, tga, sgi, sun, webp.", "set_output_format"), 470);
            
            fields["output_format"] = outputFormat;
            return this;
        }

        /**
        * Resize the image.
        * 
        * @param resize The resize percentage or new image dimensions.
        * @return The converter object.
        */
        public ImageToImageClient setResize(string resize)
        {
            fields["resize"] = resize;
            return this;
        }

        /**
        * Rotate the image.
        * 
        * @param rotate The rotation specified in degrees.
        * @return The converter object.
        */
        public ImageToImageClient setRotate(string rotate)
        {
            fields["rotate"] = rotate;
            return this;
        }

        /**
        * Turn on the debug logging.
        * 
        * @param debugLog Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public ImageToImageClient setDebugLog(bool debugLog)
        {
            fields["debug_log"] = debugLog ? "true" : null;
            return this;
        }

        /**
        * Get the URL of the debug log for the last conversion.
        * @return The link to the debug log.
        */
        public string getDebugLogUrl()
        {
            return helper.getDebugLogUrl();
        }

        /**
        * Get the number of conversion credits available in your <a href='/user/account/'>account</a>.
        * The returned value can differ from the actual count if you run parallel conversions.
        * The special value <span class='field-value'>999999</span> is returned if the information is not available.
        * @return The number of credits.
        */
        public int getRemainingCreditCount()
        {
            return helper.getRemainingCreditCount();
        }

        /**
        * Get the number of credits consumed by the last conversion.
        * @return The number of credits.
        */
        public int getConsumedCreditCount()
        {
            return helper.getConsumedCreditCount();
        }

        /**
        * Get the job id.
        * @return The unique job identifier.
        */
        public string getJobId()
        {
            return helper.getJobId();
        }

        /**
        * Get the size of the output in bytes.
        * @return The count of bytes.
        */
        public int getOutputSize()
        {
            return helper.getOutputSize();
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * 
        * @param useHttp Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public ImageToImageClient setUseHttp(bool useHttp)
        {
            helper.setUseHttp(useHttp);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be usefull if you are behind some proxy or firewall.
        * 
        * @param userAgent The user agent string.
        * @return The converter object.
        */
        public ImageToImageClient setUserAgent(string userAgent)
        {
            helper.setUserAgent(userAgent);
            return this;
        }

        /**
        * Specifies an HTTP proxy that the API client library will use to connect to the internet.
        * 
        * @param host The proxy hostname.
        * @param port The proxy port.
        * @param userName The username.
        * @param password The password.
        * @return The converter object.
        */
        public ImageToImageClient setProxy(string host, int port, string userName, string password)
        {
            helper.setProxy(host, port, userName, password);
            return this;
        }

        /**
        * Specifies the number of retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        * 
        * @param retryCount Number of retries wanted.
        * @return The converter object.
        */
        public ImageToImageClient setRetryCount(int retryCount)
        {
            helper.setRetryCount(retryCount);
            return this;
        }

    }

    /**
    * Conversion from PDF to PDF.
    */
    public sealed class PdfToPdfClient
    {
        private ConnectionHelper helper;
        private Dictionary<string, string> fields = new Dictionary<string, string>();
        private Dictionary<string, string> files = new Dictionary<string, string>();
        private Dictionary<string, byte[]> rawData = new Dictionary<string, byte[]>();

        #pragma warning disable CS0414
        private int fileId = 1;
        #pragma warning restore CS0414

        /**
        * Constructor for the Pdfcrowd API client.
        * 
        * @param userName Your username at Pdfcrowd.
        * @param apiKey Your API key.
        */
        public PdfToPdfClient(string userName, string apiKey)
        {
            this.helper = new ConnectionHelper(userName, apiKey);
            fields["input_format"] = "pdf";
            fields["output_format"] = "pdf";
        }

        /**
        * Specifies the action to be performed on the input PDFs.
        * 
        * @param action Allowed values are join, shuffle.
        * @return The converter object.
        */
        public PdfToPdfClient setAction(string action)
        {
            if (!Regex.Match(action, "(?i)^(join|shuffle)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(action, "action", "pdf-to-pdf", "Allowed values are join, shuffle.", "set_action"), 470);
            
            fields["action"] = action;
            return this;
        }

        /**
        * Perform an action on the input files.
        * @return Byte array containing the output PDF.
        */
        public byte[] convert()
        {
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Perform an action on the input files and write the output PDF to an output stream.
        * 
        * @param outStream The output stream that will contain the output PDF.
        */
        public void convertToStream(Stream outStream)
        {
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Perform an action on the input files and write the output PDF to a file.
        * 
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertToFile(string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "pdf-to-pdf", "The string must not be empty.", "convert_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertToStream(outputFile);
            outputFile.Close();
        }

        /**
        * Add a PDF file to the list of the input PDFs.
        * 
        * @param filePath The file path to a local PDF file. The file must exist and not be empty.
        * @return The converter object.
        */
        public PdfToPdfClient addPdfFile(string filePath)
        {
            if (!(File.Exists(filePath) && new FileInfo(filePath).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "pdf-to-pdf", "The file must exist and not be empty.", "add_pdf_file"), 470);
            
            files["f_" + ConnectionHelper.intToString(fileId)] = filePath;
            fileId++;
            return this;
        }

        /**
        * Add in-memory raw PDF data to the list of the input PDFs.<br>Typical usage is for adding PDF created by another Pdfcrowd converter.<br><br> Example in PHP:<br> <b>$clientPdf2Pdf</b>-&gt;addPdfRawData(<b>$clientHtml2Pdf</b>-&gt;convertUrl('http://www.example.com'));
        * 
        * @param pdfRawData The raw PDF data. The input data must be PDF content.
        * @return The converter object.
        */
        public PdfToPdfClient addPdfRawData(byte[] pdfRawData)
        {
            if (!(pdfRawData != null && pdfRawData.Length > 300 && pdfRawData[0] == '%' && pdfRawData[1] == 'P' && pdfRawData[2] == 'D' && pdfRawData[3] == 'F'))
                throw new Error(ConnectionHelper.createInvalidValueMessage("raw PDF data", "pdf_raw_data", "pdf-to-pdf", "The input data must be PDF content.", "add_pdf_raw_data"), 470);
            
            rawData["f_" + ConnectionHelper.intToString(fileId)] = pdfRawData;
            fileId++;
            return this;
        }

        /**
        * Turn on the debug logging.
        * 
        * @param debugLog Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public PdfToPdfClient setDebugLog(bool debugLog)
        {
            fields["debug_log"] = debugLog ? "true" : null;
            return this;
        }

        /**
        * Get the URL of the debug log for the last conversion.
        * @return The link to the debug log.
        */
        public string getDebugLogUrl()
        {
            return helper.getDebugLogUrl();
        }

        /**
        * Get the number of conversion credits available in your <a href='/user/account/'>account</a>.
        * The returned value can differ from the actual count if you run parallel conversions.
        * The special value <span class='field-value'>999999</span> is returned if the information is not available.
        * @return The number of credits.
        */
        public int getRemainingCreditCount()
        {
            return helper.getRemainingCreditCount();
        }

        /**
        * Get the number of credits consumed by the last conversion.
        * @return The number of credits.
        */
        public int getConsumedCreditCount()
        {
            return helper.getConsumedCreditCount();
        }

        /**
        * Get the job id.
        * @return The unique job identifier.
        */
        public string getJobId()
        {
            return helper.getJobId();
        }

        /**
        * Get the total number of pages in the output document.
        * @return The page count.
        */
        public int getPageCount()
        {
            return helper.getPageCount();
        }

        /**
        * Get the size of the output in bytes.
        * @return The count of bytes.
        */
        public int getOutputSize()
        {
            return helper.getOutputSize();
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * 
        * @param useHttp Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public PdfToPdfClient setUseHttp(bool useHttp)
        {
            helper.setUseHttp(useHttp);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be usefull if you are behind some proxy or firewall.
        * 
        * @param userAgent The user agent string.
        * @return The converter object.
        */
        public PdfToPdfClient setUserAgent(string userAgent)
        {
            helper.setUserAgent(userAgent);
            return this;
        }

        /**
        * Specifies an HTTP proxy that the API client library will use to connect to the internet.
        * 
        * @param host The proxy hostname.
        * @param port The proxy port.
        * @param userName The username.
        * @param password The password.
        * @return The converter object.
        */
        public PdfToPdfClient setProxy(string host, int port, string userName, string password)
        {
            helper.setProxy(host, port, userName, password);
            return this;
        }

        /**
        * Specifies the number of retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        * 
        * @param retryCount Number of retries wanted.
        * @return The converter object.
        */
        public PdfToPdfClient setRetryCount(int retryCount)
        {
            helper.setRetryCount(retryCount);
            return this;
        }

    }

    /**
    * Conversion from an image to PDF.
    */
    public sealed class ImageToPdfClient
    {
        private ConnectionHelper helper;
        private Dictionary<string, string> fields = new Dictionary<string, string>();
        private Dictionary<string, string> files = new Dictionary<string, string>();
        private Dictionary<string, byte[]> rawData = new Dictionary<string, byte[]>();

        #pragma warning disable CS0414
        private int fileId = 1;
        #pragma warning restore CS0414

        /**
        * Constructor for the Pdfcrowd API client.
        * 
        * @param userName Your username at Pdfcrowd.
        * @param apiKey Your API key.
        */
        public ImageToPdfClient(string userName, string apiKey)
        {
            this.helper = new ConnectionHelper(userName, apiKey);
            fields["input_format"] = "image";
            fields["output_format"] = "pdf";
        }

        /**
        * Convert an image.
        * 
        * @param url The address of the image to convert. The supported protocols are http:// and https://.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "image-to-pdf", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
            fields["url"] = url;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert an image and write the result to an output stream.
        * 
        * @param url The address of the image to convert. The supported protocols are http:// and https://.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertUrlToStream(string url, Stream outStream)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "url", "image-to-pdf", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
            fields["url"] = url;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert an image and write the result to a local file.
        * 
        * @param url The address of the image to convert. The supported protocols are http:// and https://.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertUrlToFile(string url, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "image-to-pdf", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertUrlToStream(url, outputFile);
            outputFile.Close();
        }

        /**
        * Convert a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip). The file must exist and not be empty.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertFile(string file)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "image-to-pdf", "The file must exist and not be empty.", "convert_file"), 470);
            
            files["file"] = file;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a local file and write the result to an output stream.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip). The file must exist and not be empty.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertFileToStream(string file, Stream outStream)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "file", "image-to-pdf", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
            files["file"] = file;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a local file and write the result to a local file.
        * 
        * @param file The path to a local file to convert.<br> The file can be either a single file or an archive (.tar.gz, .tar.bz2, or .zip). The file must exist and not be empty.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertFileToFile(string file, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "image-to-pdf", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertFileToStream(file, outputFile);
            outputFile.Close();
        }

        /**
        * Convert raw data.
        * 
        * @param data The raw content to be converted.
        * @return Byte array with the output.
        */
        public byte[] convertRawData(byte[] data)
        {
            rawData["file"] = data;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert raw data and write the result to an output stream.
        * 
        * @param data The raw content to be converted.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertRawDataToStream(byte[] data, Stream outStream)
        {
            rawData["file"] = data;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert raw data to a file.
        * 
        * @param data The raw content to be converted.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertRawDataToFile(byte[] data, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "file_path", "image-to-pdf", "The string must not be empty.", "convert_raw_data_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            convertRawDataToStream(data, outputFile);
            outputFile.Close();
        }

        /**
        * Resize the image.
        * 
        * @param resize The resize percentage or new image dimensions.
        * @return The converter object.
        */
        public ImageToPdfClient setResize(string resize)
        {
            fields["resize"] = resize;
            return this;
        }

        /**
        * Rotate the image.
        * 
        * @param rotate The rotation specified in degrees.
        * @return The converter object.
        */
        public ImageToPdfClient setRotate(string rotate)
        {
            fields["rotate"] = rotate;
            return this;
        }

        /**
        * Turn on the debug logging.
        * 
        * @param debugLog Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public ImageToPdfClient setDebugLog(bool debugLog)
        {
            fields["debug_log"] = debugLog ? "true" : null;
            return this;
        }

        /**
        * Get the URL of the debug log for the last conversion.
        * @return The link to the debug log.
        */
        public string getDebugLogUrl()
        {
            return helper.getDebugLogUrl();
        }

        /**
        * Get the number of conversion credits available in your <a href='/user/account/'>account</a>.
        * The returned value can differ from the actual count if you run parallel conversions.
        * The special value <span class='field-value'>999999</span> is returned if the information is not available.
        * @return The number of credits.
        */
        public int getRemainingCreditCount()
        {
            return helper.getRemainingCreditCount();
        }

        /**
        * Get the number of credits consumed by the last conversion.
        * @return The number of credits.
        */
        public int getConsumedCreditCount()
        {
            return helper.getConsumedCreditCount();
        }

        /**
        * Get the job id.
        * @return The unique job identifier.
        */
        public string getJobId()
        {
            return helper.getJobId();
        }

        /**
        * Get the size of the output in bytes.
        * @return The count of bytes.
        */
        public int getOutputSize()
        {
            return helper.getOutputSize();
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * 
        * @param useHttp Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public ImageToPdfClient setUseHttp(bool useHttp)
        {
            helper.setUseHttp(useHttp);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be usefull if you are behind some proxy or firewall.
        * 
        * @param userAgent The user agent string.
        * @return The converter object.
        */
        public ImageToPdfClient setUserAgent(string userAgent)
        {
            helper.setUserAgent(userAgent);
            return this;
        }

        /**
        * Specifies an HTTP proxy that the API client library will use to connect to the internet.
        * 
        * @param host The proxy hostname.
        * @param port The proxy port.
        * @param userName The username.
        * @param password The password.
        * @return The converter object.
        */
        public ImageToPdfClient setProxy(string host, int port, string userName, string password)
        {
            helper.setProxy(host, port, userName, password);
            return this;
        }

        /**
        * Specifies the number of retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        * 
        * @param retryCount Number of retries wanted.
        * @return The converter object.
        */
        public ImageToPdfClient setRetryCount(int retryCount)
        {
            helper.setRetryCount(retryCount);
            return this;
        }

    }


}
