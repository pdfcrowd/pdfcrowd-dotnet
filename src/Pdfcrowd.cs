// Copyright (C) 2009-2018 pdfcrowd.com
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
        private string converterVersion;

        private static readonly string HOST = Environment.GetEnvironmentVariable("PDFCROWD_HOST") != null
            ? Environment.GetEnvironmentVariable("PDFCROWD_HOST")
            : "api.pdfcrowd.com";
        private static readonly string MULTIPART_BOUNDARY = "----------ThIs_Is_tHe_bOUnDary_$";
        public static readonly string CLIENT_VERSION = "5.8.0";
        private static readonly string newLine = "\r\n";
        private static readonly CultureInfo numericInfo = CultureInfo.GetCultureInfo("en-US");

        public ConnectionHelper(String userName, String apiKey) {
            this.userName = userName;
            this.apiKey = apiKey;

            resetResponseData();
            setProxy(null, 0, null, null);
            setUseHttp(false);
            setUserAgent("pdfcrowd_dotnet_client/5.8.0 (https://pdfcrowd.com)");

            if( HOST != "api.pdfcrowd.com")
            {
                ServicePointManager.ServerCertificateValidationCallback = delegate { return true; };
            }

            retryCount = 1;
            converterVersion = "20.10";
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

        internal static string intToString(int value)
        {
            return value.ToString(numericInfo);
        }

        public static void CopyStream(Stream input, Stream output)
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

        internal static byte[] ReadStream(Stream inStream)
        {
            var memStream = new MemoryStream();
            CopyStream(inStream, memStream);
            return memStream.ToArray();
        }

        private static bool IsSslException(WebException why)
        {
            if(why.Status == WebExceptionStatus.TrustFailure ||
               why.Status == WebExceptionStatus.SecureChannelFailure)
            {
                return true;
            }

            return why.Status == WebExceptionStatus.UnknownError &&
                   why.InnerException != null &&
                   why.InnerException.Message.StartsWith(
                       "The SSL connection could not be established");
        }

        private WebRequest getConnection()
        {
            HttpWebRequest request = (HttpWebRequest) WebRequest.Create(
                string.Format("{0}{1}/", apiUri, converterVersion));
            if(proxyHost != null)
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
                        throw;
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
                    dataStream.Flush();
                    dataStream.Close();
                }

                // Get the response.
                using(HttpWebResponse response = (HttpWebResponse) request.GetResponse())
                {
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
            }
            catch(WebException why)
            {
                if(IsSslException(why))
                {
                   throw new Error("There was a problem connecting to Pdfcrowd servers over HTTPS:\n" +
                                   why.Message +
                                   "\nYou can still use the API over HTTP, you just need to add the following line right after Pdfcrowd client initialization:\nclient.setUseHttp(true);",
                                   481);
                }

                if (why.Response != null && why.Status == WebExceptionStatus.ProtocolError)
                {
                    HttpWebResponse response = (HttpWebResponse)why.Response;

                    using(MemoryStream stream = new MemoryStream())
                    {
                        using(Stream dataStream = response.GetResponseStream())
                        {
                            CopyStream(dataStream, stream);
                            stream.Position = 0;
                            string err = readStream(stream);
                            throw new Error(err, response.StatusCode);
                        }
                    }
                }

                string innerException = "";
                if (why.InnerException != null)
                {
                    innerException = "\n" + why.InnerException.Message;
                }
                throw new Error(why.Message + innerException, HttpStatusCode.Unused);
            }
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

        internal void setConverterVersion(string converterVersion)
        {
            this.converterVersion = converterVersion;
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

        internal String getConverterVersion()
        {
            return converterVersion;
        }

        internal static string createInvalidValueMessage(object value, string field, string converter, string hint, string id)
        {
            string message = string.Format("Invalid value '{0}' for {1}.", value, field);
            if(hint != null)
            {
                message += " " + hint;
            }
            return message + " " + string.Format("Details: https://www.pdfcrowd.com/api/{0}-dotnet/ref/#{1}", converter, id);
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrl", "html-to-pdf", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrlToStream::url", "html-to-pdf", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertUrlToFile::file_path", "html-to-pdf", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertUrlToStream(url, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFile", "html-to-pdf", "The file must exist and not be empty.", "convert_file"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFileToStream::file", "html-to-pdf", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertFileToFile::file_path", "html-to-pdf", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertFileToStream(file, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "convertString", "html-to-pdf", "The string must not be empty.", "convert_string"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "convertStringToStream::text", "html-to-pdf", "The string must not be empty.", "convert_string_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStringToFile::file_path", "html-to-pdf", "The string must not be empty.", "convert_string_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertStringToStream(text, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert the contents of an input stream.
        *
        * @param inStream The input stream with source data.<br> The stream can contain either HTML code or an archive (.zip, .tar.gz, .tar.bz2).<br>The archive can contain HTML code and its external assets (images, style sheets, javascript).
        * @return Byte array containing the conversion output.
        */
        public byte[] convertStream(Stream inStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert the contents of an input stream and write the result to an output stream.
        *
        * @param inStream The input stream with source data.<br> The stream can contain either HTML code or an archive (.zip, .tar.gz, .tar.bz2).<br>The archive can contain HTML code and its external assets (images, style sheets, javascript).
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertStreamToStream(Stream inStream, Stream outStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert the contents of an input stream and write the result to a local file.
        *
        * @param inStream The input stream with source data.<br> The stream can contain either HTML code or an archive (.zip, .tar.gz, .tar.bz2).<br>The archive can contain HTML code and its external assets (images, style sheets, javascript).
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertStreamToFile(Stream inStream, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStreamToFile::file_path", "html-to-pdf", "The string must not be empty.", "convert_stream_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertStreamToStream(inStream, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Set the file name of the main HTML document stored in the input archive. If not specified, the first HTML file in the archive is used for conversion. Use this method if the input archive contains multiple HTML documents.
        *
        * @param filename The file name.
        * @return The converter object.
        */
        public HtmlToPdfClient setZipMainFilename(string filename)
        {
            fields["zip_main_filename"] = filename;
            return this;
        }

        /**
        * Set the output page size.
        *
        * @param size Allowed values are A0, A1, A2, A3, A4, A5, A6, Letter.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageSize(string size)
        {
            if (!Regex.Match(size, "(?i)^(A0|A1|A2|A3|A4|A5|A6|Letter)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(size, "setPageSize", "html-to-pdf", "Allowed values are A0, A1, A2, A3, A4, A5, A6, Letter.", "set_page_size"), 470);
            
            fields["page_size"] = size;
            return this;
        }

        /**
        * Set the output page width. The safe maximum is <span class='field-value'>200in</span> otherwise some PDF viewers may be unable to open the PDF.
        *
        * @param width The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setPageWidth(string width)
        {
            if (!Regex.Match(width, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(width, "setPageWidth", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_page_width"), 470);
            
            fields["page_width"] = width;
            return this;
        }

        /**
        * Set the output page height. Use <span class='field-value'>-1</span> for a single page PDF. The safe maximum is <span class='field-value'>200in</span> otherwise some PDF viewers may be unable to open the PDF.
        *
        * @param height The value must be -1 or specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setPageHeight(string height)
        {
            if (!Regex.Match(height, "(?i)^0$|^\\-1$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(height, "setPageHeight", "html-to-pdf", "The value must be -1 or specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_page_height"), 470);
            
            fields["page_height"] = height;
            return this;
        }

        /**
        * Set the output page dimensions.
        *
        * @param width Set the output page width. The safe maximum is <span class='field-value'>200in</span> otherwise some PDF viewers may be unable to open the PDF. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @param height Set the output page height. Use <span class='field-value'>-1</span> for a single page PDF. The safe maximum is <span class='field-value'>200in</span> otherwise some PDF viewers may be unable to open the PDF. The value must be -1 or specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(orientation, "setOrientation", "html-to-pdf", "Allowed values are landscape, portrait.", "set_orientation"), 470);
            
            fields["orientation"] = orientation;
            return this;
        }

        /**
        * Set the output page top margin.
        *
        * @param top The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginTop(string top)
        {
            if (!Regex.Match(top, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(top, "setMarginTop", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_margin_top"), 470);
            
            fields["margin_top"] = top;
            return this;
        }

        /**
        * Set the output page right margin.
        *
        * @param right The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginRight(string right)
        {
            if (!Regex.Match(right, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(right, "setMarginRight", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_margin_right"), 470);
            
            fields["margin_right"] = right;
            return this;
        }

        /**
        * Set the output page bottom margin.
        *
        * @param bottom The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginBottom(string bottom)
        {
            if (!Regex.Match(bottom, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(bottom, "setMarginBottom", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_margin_bottom"), 470);
            
            fields["margin_bottom"] = bottom;
            return this;
        }

        /**
        * Set the output page left margin.
        *
        * @param left The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setMarginLeft(string left)
        {
            if (!Regex.Match(left, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(left, "setMarginLeft", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_margin_left"), 470);
            
            fields["margin_left"] = left;
            return this;
        }

        /**
        * Disable page margins.
        *
        * @param value Set to <span class='field-value'>true</span> to disable margins.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoMargins(bool value)
        {
            fields["no_margins"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the output page margins.
        *
        * @param top Set the output page top margin. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @param right Set the output page right margin. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @param bottom Set the output page bottom margin. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @param left Set the output page left margin. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
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
        * Set the page range to print.
        *
        * @param pages A comma separated list of page numbers or ranges.
        * @return The converter object.
        */
        public HtmlToPdfClient setPrintPageRange(string pages)
        {
            if (!Regex.Match(pages, "^(?:\\s*(?:\\d+|(?:\\d*\\s*\\-\\s*\\d+)|(?:\\d+\\s*\\-\\s*\\d*))\\s*,\\s*)*\\s*(?:\\d+|(?:\\d*\\s*\\-\\s*\\d+)|(?:\\d+\\s*\\-\\s*\\d*))\\s*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pages, "setPrintPageRange", "html-to-pdf", "A comma separated list of page numbers or ranges.", "set_print_page_range"), 470);
            
            fields["print_page_range"] = pages;
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
        * Set the top left X coordinate of the content area. It is relative to the top left X coordinate of the print area.
        *
        * @param x The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt". It may contain a negative value.
        * @return The converter object.
        */
        public HtmlToPdfClient setContentAreaX(string x)
        {
            if (!Regex.Match(x, "(?i)^0$|^\\-?[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(x, "setContentAreaX", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\". It may contain a negative value.", "set_content_area_x"), 470);
            
            fields["content_area_x"] = x;
            return this;
        }

        /**
        * Set the top left Y coordinate of the content area. It is relative to the top left Y coordinate of the print area.
        *
        * @param y The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt". It may contain a negative value.
        * @return The converter object.
        */
        public HtmlToPdfClient setContentAreaY(string y)
        {
            if (!Regex.Match(y, "(?i)^0$|^\\-?[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(y, "setContentAreaY", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\". It may contain a negative value.", "set_content_area_y"), 470);
            
            fields["content_area_y"] = y;
            return this;
        }

        /**
        * Set the width of the content area. It should be at least 1 inch.
        *
        * @param width The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setContentAreaWidth(string width)
        {
            if (!Regex.Match(width, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(width, "setContentAreaWidth", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_content_area_width"), 470);
            
            fields["content_area_width"] = width;
            return this;
        }

        /**
        * Set the height of the content area. It should be at least 1 inch.
        *
        * @param height The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setContentAreaHeight(string height)
        {
            if (!Regex.Match(height, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(height, "setContentAreaHeight", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_content_area_height"), 470);
            
            fields["content_area_height"] = height;
            return this;
        }

        /**
        * Set the content area position and size. The content area enables to specify a web page area to be converted.
        *
        * @param x Set the top left X coordinate of the content area. It is relative to the top left X coordinate of the print area. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt". It may contain a negative value.
        * @param y Set the top left Y coordinate of the content area. It is relative to the top left Y coordinate of the print area. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt". It may contain a negative value.
        * @param width Set the width of the content area. It should be at least 1 inch. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @param height Set the height of the content area. It should be at least 1 inch. The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setContentArea(string x, string y, string width, string height)
        {
            this.setContentAreaX(x);
            this.setContentAreaY(y);
            this.setContentAreaWidth(width);
            this.setContentAreaHeight(height);
            return this;
        }

        /**
        * Specifies behavior in presence of CSS @page rules. It may affect the page size, margins and orientation.
        *
        * @param mode The page rule mode. Allowed values are default, mode1, mode2.
        * @return The converter object.
        */
        public HtmlToPdfClient setCssPageRuleMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(default|mode1|mode2)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setCssPageRuleMode", "html-to-pdf", "Allowed values are default, mode1, mode2.", "set_css_page_rule_mode"), 470);
            
            fields["css_page_rule_mode"] = mode;
            return this;
        }

        /**
        * Load an HTML code from the specified URL and use it as the page header. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of the converted document</li> <li><span class='field-value'>pdfcrowd-source-title</span> - the title of the converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals. Allowed values: <ul> <li><span class='field-value'>arabic</span> - Arabic numerals, they are used by default</li> <li><span class='field-value'>roman</span> - Roman numerals</li> <li><span class='field-value'>eastern-arabic</span> - Eastern Arabic numerals</li> <li><span class='field-value'>bengali</span> - Bengali numerals</li> <li><span class='field-value'>devanagari</span> - Devanagari numerals</li> <li><span class='field-value'>thai</span> - Thai numerals</li> <li><span class='field-value'>east-asia</span> - Chinese, Vietnamese, Japanese and Korean numerals</li> <li><span class='field-value'>chinese-formal</span> - Chinese formal numerals</li> </ul> Please contact us if you need another type of numerals.<br> Example:<br> &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt; </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL. Allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul> </li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setHeaderUrl", "html-to-pdf", "The supported protocols are http:// and https://.", "set_header_url"), 470);
            
            fields["header_url"] = url;
            return this;
        }

        /**
        * Use the specified HTML code as the page header. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of the converted document</li> <li><span class='field-value'>pdfcrowd-source-title</span> - the title of the converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals. Allowed values: <ul> <li><span class='field-value'>arabic</span> - Arabic numerals, they are used by default</li> <li><span class='field-value'>roman</span> - Roman numerals</li> <li><span class='field-value'>eastern-arabic</span> - Eastern Arabic numerals</li> <li><span class='field-value'>bengali</span> - Bengali numerals</li> <li><span class='field-value'>devanagari</span> - Devanagari numerals</li> <li><span class='field-value'>thai</span> - Thai numerals</li> <li><span class='field-value'>east-asia</span> - Chinese, Vietnamese, Japanese and Korean numerals</li> <li><span class='field-value'>chinese-formal</span> - Chinese formal numerals</li> </ul> Please contact us if you need another type of numerals.<br> Example:<br> &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt; </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL. Allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul> </li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        *
        * @param html The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderHtml(string html)
        {
            if (!(!String.IsNullOrEmpty(html)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(html, "setHeaderHtml", "html-to-pdf", "The string must not be empty.", "set_header_html"), 470);
            
            fields["header_html"] = html;
            return this;
        }

        /**
        * Set the header height.
        *
        * @param height The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderHeight(string height)
        {
            if (!Regex.Match(height, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(height, "setHeaderHeight", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_header_height"), 470);
            
            fields["header_height"] = height;
            return this;
        }

        /**
        * Set the file name of the header HTML document stored in the input archive. Use this method if the input archive contains multiple HTML documents.
        *
        * @param filename The file name.
        * @return The converter object.
        */
        public HtmlToPdfClient setZipHeaderFilename(string filename)
        {
            fields["zip_header_filename"] = filename;
            return this;
        }

        /**
        * Load an HTML code from the specified URL and use it as the page footer. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of the converted document</li> <li><span class='field-value'>pdfcrowd-source-title</span> - the title of the converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals. Allowed values: <ul> <li><span class='field-value'>arabic</span> - Arabic numerals, they are used by default</li> <li><span class='field-value'>roman</span> - Roman numerals</li> <li><span class='field-value'>eastern-arabic</span> - Eastern Arabic numerals</li> <li><span class='field-value'>bengali</span> - Bengali numerals</li> <li><span class='field-value'>devanagari</span> - Devanagari numerals</li> <li><span class='field-value'>thai</span> - Thai numerals</li> <li><span class='field-value'>east-asia</span> - Chinese, Vietnamese, Japanese and Korean numerals</li> <li><span class='field-value'>chinese-formal</span> - Chinese formal numerals</li> </ul> Please contact us if you need another type of numerals.<br> Example:<br> &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt; </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL. Allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul> </li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setFooterUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setFooterUrl", "html-to-pdf", "The supported protocols are http:// and https://.", "set_footer_url"), 470);
            
            fields["footer_url"] = url;
            return this;
        }

        /**
        * Use the specified HTML as the page footer. The following classes can be used in the HTML. The content of the respective elements will be expanded as follows: <ul> <li><span class='field-value'>pdfcrowd-page-count</span> - the total page count of printed pages</li> <li><span class='field-value'>pdfcrowd-page-number</span> - the current page number</li> <li><span class='field-value'>pdfcrowd-source-url</span> - the source URL of the converted document</li> <li><span class='field-value'>pdfcrowd-source-title</span> - the title of the converted document</li> </ul> The following attributes can be used: <ul> <li><span class='field-value'>data-pdfcrowd-number-format</span> - specifies the type of the used numerals. Allowed values: <ul> <li><span class='field-value'>arabic</span> - Arabic numerals, they are used by default</li> <li><span class='field-value'>roman</span> - Roman numerals</li> <li><span class='field-value'>eastern-arabic</span> - Eastern Arabic numerals</li> <li><span class='field-value'>bengali</span> - Bengali numerals</li> <li><span class='field-value'>devanagari</span> - Devanagari numerals</li> <li><span class='field-value'>thai</span> - Thai numerals</li> <li><span class='field-value'>east-asia</span> - Chinese, Vietnamese, Japanese and Korean numerals</li> <li><span class='field-value'>chinese-formal</span> - Chinese formal numerals</li> </ul> Please contact us if you need another type of numerals.<br> Example:<br> &lt;span class='pdfcrowd-page-number' data-pdfcrowd-number-format='roman'&gt;&lt;/span&gt; </li> <li><span class='field-value'>data-pdfcrowd-placement</span> - specifies where to place the source URL. Allowed values: <ul> <li>The URL is inserted to the content <ul> <li> Example: &lt;span class='pdfcrowd-source-url'&gt;&lt;/span&gt;<br> will produce &lt;span&gt;http://example.com&lt;/span&gt; </li> </ul> </li> <li><span class='field-value'>href</span> - the URL is set to the href attribute <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href'&gt;Link to source&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;Link to source&lt;/a&gt; </li> </ul> </li> <li><span class='field-value'>href-and-content</span> - the URL is set to the href attribute and to the content <ul> <li> Example: &lt;a class='pdfcrowd-source-url' data-pdfcrowd-placement='href-and-content'&gt;&lt;/a&gt;<br> will produce &lt;a href='http://example.com'&gt;http://example.com&lt;/a&gt; </li> </ul> </li> </ul> </li> </ul>
        *
        * @param html The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setFooterHtml(string html)
        {
            if (!(!String.IsNullOrEmpty(html)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(html, "setFooterHtml", "html-to-pdf", "The string must not be empty.", "set_footer_html"), 470);
            
            fields["footer_html"] = html;
            return this;
        }

        /**
        * Set the footer height.
        *
        * @param height The value must be specified in inches "in", millimeters "mm", centimeters "cm", or points "pt".
        * @return The converter object.
        */
        public HtmlToPdfClient setFooterHeight(string height)
        {
            if (!Regex.Match(height, "(?i)^0$|^[0-9]*\\.?[0-9]+(pt|px|mm|cm|in)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(height, "setFooterHeight", "html-to-pdf", "The value must be specified in inches \"in\", millimeters \"mm\", centimeters \"cm\", or points \"pt\".", "set_footer_height"), 470);
            
            fields["footer_height"] = height;
            return this;
        }

        /**
        * Set the file name of the footer HTML document stored in the input archive. Use this method if the input archive contains multiple HTML documents.
        *
        * @param filename The file name.
        * @return The converter object.
        */
        public HtmlToPdfClient setZipFooterFilename(string filename)
        {
            fields["zip_footer_filename"] = filename;
            return this;
        }

        /**
        * Disable horizontal page margins for header and footer. The header/footer contents width will be equal to the physical page width.
        *
        * @param value Set to <span class='field-value'>true</span> to disable horizontal margins for header and footer.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoHeaderFooterHorizontalMargins(bool value)
        {
            fields["no_header_footer_horizontal_margins"] = value ? "true" : null;
            return this;
        }

        /**
        * The page header is not printed on the specified pages.
        *
        * @param pages List of physical page numbers. Negative numbers count backwards from the last page: -1 is the last page, -2 is the last but one page, and so on. A comma separated list of page numbers.
        * @return The converter object.
        */
        public HtmlToPdfClient setExcludeHeaderOnPages(string pages)
        {
            if (!Regex.Match(pages, "^(?:\\s*\\-?\\d+\\s*,)*\\s*\\-?\\d+\\s*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pages, "setExcludeHeaderOnPages", "html-to-pdf", "A comma separated list of page numbers.", "set_exclude_header_on_pages"), 470);
            
            fields["exclude_header_on_pages"] = pages;
            return this;
        }

        /**
        * The page footer is not printed on the specified pages.
        *
        * @param pages List of physical page numbers. Negative numbers count backwards from the last page: -1 is the last page, -2 is the last but one page, and so on. A comma separated list of page numbers.
        * @return The converter object.
        */
        public HtmlToPdfClient setExcludeFooterOnPages(string pages)
        {
            if (!Regex.Match(pages, "^(?:\\s*\\-?\\d+\\s*,)*\\s*\\-?\\d+\\s*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pages, "setExcludeFooterOnPages", "html-to-pdf", "A comma separated list of page numbers.", "set_exclude_footer_on_pages"), 470);
            
            fields["exclude_footer_on_pages"] = pages;
            return this;
        }

        /**
        * Set the scaling factor (zoom) for the header and footer.
        *
        * @param factor The percentage value. The value must be in the range 10-500.
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderFooterScaleFactor(int factor)
        {
            if (!(factor >= 10 && factor <= 500))
                throw new Error(ConnectionHelper.createInvalidValueMessage(factor, "setHeaderFooterScaleFactor", "html-to-pdf", "The value must be in the range 10-500.", "set_header_footer_scale_factor"), 470);
            
            fields["header_footer_scale_factor"] = ConnectionHelper.intToString(factor);
            return this;
        }

        /**
        * Apply a watermark to each page of the output PDF file. A watermark can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the watermark.
        *
        * @param watermark The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageWatermark(string watermark)
        {
            if (!(File.Exists(watermark) && new FileInfo(watermark).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(watermark, "setPageWatermark", "html-to-pdf", "The file must exist and not be empty.", "set_page_watermark"), 470);
            
            files["page_watermark"] = watermark;
            return this;
        }

        /**
        * Load a file from the specified URL and apply the file as a watermark to each page of the output PDF. A watermark can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the watermark.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageWatermarkUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setPageWatermarkUrl", "html-to-pdf", "The supported protocols are http:// and https://.", "set_page_watermark_url"), 470);
            
            fields["page_watermark_url"] = url;
            return this;
        }

        /**
        * Apply each page of a watermark to the corresponding page of the output PDF. A watermark can be either a PDF or an image.
        *
        * @param watermark The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setMultipageWatermark(string watermark)
        {
            if (!(File.Exists(watermark) && new FileInfo(watermark).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(watermark, "setMultipageWatermark", "html-to-pdf", "The file must exist and not be empty.", "set_multipage_watermark"), 470);
            
            files["multipage_watermark"] = watermark;
            return this;
        }

        /**
        * Load a file from the specified URL and apply each page of the file as a watermark to the corresponding page of the output PDF. A watermark can be either a PDF or an image.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setMultipageWatermarkUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setMultipageWatermarkUrl", "html-to-pdf", "The supported protocols are http:// and https://.", "set_multipage_watermark_url"), 470);
            
            fields["multipage_watermark_url"] = url;
            return this;
        }

        /**
        * Apply a background to each page of the output PDF file. A background can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the background.
        *
        * @param background The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageBackground(string background)
        {
            if (!(File.Exists(background) && new FileInfo(background).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(background, "setPageBackground", "html-to-pdf", "The file must exist and not be empty.", "set_page_background"), 470);
            
            files["page_background"] = background;
            return this;
        }

        /**
        * Load a file from the specified URL and apply the file as a background to each page of the output PDF. A background can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the background.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageBackgroundUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setPageBackgroundUrl", "html-to-pdf", "The supported protocols are http:// and https://.", "set_page_background_url"), 470);
            
            fields["page_background_url"] = url;
            return this;
        }

        /**
        * Apply each page of a background to the corresponding page of the output PDF. A background can be either a PDF or an image.
        *
        * @param background The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setMultipageBackground(string background)
        {
            if (!(File.Exists(background) && new FileInfo(background).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(background, "setMultipageBackground", "html-to-pdf", "The file must exist and not be empty.", "set_multipage_background"), 470);
            
            files["multipage_background"] = background;
            return this;
        }

        /**
        * Load a file from the specified URL and apply each page of the file as a background to the corresponding page of the output PDF. A background can be either a PDF or an image.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public HtmlToPdfClient setMultipageBackgroundUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setMultipageBackgroundUrl", "html-to-pdf", "The supported protocols are http:// and https://.", "set_multipage_background_url"), 470);
            
            fields["multipage_background_url"] = url;
            return this;
        }

        /**
        * The page background color in RGB or RGBA hexadecimal format. The color fills the entire page regardless of the margins.
        *
        * @param color The value must be in RRGGBB or RRGGBBAA hexadecimal format.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageBackgroundColor(string color)
        {
            if (!Regex.Match(color, "^[0-9a-fA-F]{6,8}$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(color, "setPageBackgroundColor", "html-to-pdf", "The value must be in RRGGBB or RRGGBBAA hexadecimal format.", "set_page_background_color"), 470);
            
            fields["page_background_color"] = color;
            return this;
        }

        /**
        * Use the print version of the page if available (@media print).
        *
        * @param value Set to <span class='field-value'>true</span> to use the print version of the page.
        * @return The converter object.
        */
        public HtmlToPdfClient setUsePrintMedia(bool value)
        {
            fields["use_print_media"] = value ? "true" : null;
            return this;
        }

        /**
        * Do not print the background graphics.
        *
        * @param value Set to <span class='field-value'>true</span> to disable the background graphics.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoBackground(bool value)
        {
            fields["no_background"] = value ? "true" : null;
            return this;
        }

        /**
        * Do not execute JavaScript.
        *
        * @param value Set to <span class='field-value'>true</span> to disable JavaScript in web pages.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisableJavascript(bool value)
        {
            fields["disable_javascript"] = value ? "true" : null;
            return this;
        }

        /**
        * Do not load images.
        *
        * @param value Set to <span class='field-value'>true</span> to disable loading of images.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisableImageLoading(bool value)
        {
            fields["disable_image_loading"] = value ? "true" : null;
            return this;
        }

        /**
        * Disable loading fonts from remote sources.
        *
        * @param value Set to <span class='field-value'>true</span> disable loading remote fonts.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisableRemoteFonts(bool value)
        {
            fields["disable_remote_fonts"] = value ? "true" : null;
            return this;
        }

        /**
        * Use a mobile user agent.
        *
        * @param value Set to <span class='field-value'>true</span> to use a mobile user agent.
        * @return The converter object.
        */
        public HtmlToPdfClient setUseMobileUserAgent(bool value)
        {
            fields["use_mobile_user_agent"] = value ? "true" : null;
            return this;
        }

        /**
        * Specifies how iframes are handled.
        *
        * @param iframes Allowed values are all, same-origin, none.
        * @return The converter object.
        */
        public HtmlToPdfClient setLoadIframes(string iframes)
        {
            if (!Regex.Match(iframes, "(?i)^(all|same-origin|none)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(iframes, "setLoadIframes", "html-to-pdf", "Allowed values are all, same-origin, none.", "set_load_iframes"), 470);
            
            fields["load_iframes"] = iframes;
            return this;
        }

        /**
        * Try to block ads. Enabling this option can produce smaller output and speed up the conversion.
        *
        * @param value Set to <span class='field-value'>true</span> to block ads in web pages.
        * @return The converter object.
        */
        public HtmlToPdfClient setBlockAds(bool value)
        {
            fields["block_ads"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the default HTML content text encoding.
        *
        * @param encoding The text encoding of the HTML content.
        * @return The converter object.
        */
        public HtmlToPdfClient setDefaultEncoding(string encoding)
        {
            fields["default_encoding"] = encoding;
            return this;
        }

        /**
        * Set the locale for the conversion. This may affect the output format of dates, times and numbers.
        *
        * @param locale The locale code according to ISO 639.
        * @return The converter object.
        */
        public HtmlToPdfClient setLocale(string locale)
        {
            fields["locale"] = locale;
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
        * Set credentials to access HTTP base authentication protected websites.
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
        * @param value Set to <span class='field-value'>true</span> to enable SSL certificate verification.
        * @return The converter object.
        */
        public HtmlToPdfClient setVerifySslCertificates(bool value)
        {
            fields["verify_ssl_certificates"] = value ? "true" : null;
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
        * Abort the conversion if any of the sub-request HTTP status code is greater than or equal to 400 or if some sub-requests are still pending. See details in a debug log.
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
        * Do not send the X-Pdfcrowd HTTP header in Pdfcrowd HTTP requests.
        *
        * @param value Set to <span class='field-value'>true</span> to disable sending X-Pdfcrowd HTTP header.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoXpdfcrowdHeader(bool value)
        {
            fields["no_xpdfcrowd_header"] = value ? "true" : null;
            return this;
        }

        /**
        * Run a custom JavaScript after the document is loaded and ready to print. The script is intended for post-load DOM manipulation (add/remove elements, update CSS, ...). In addition to the standard browser APIs, the custom JavaScript code can use helper functions from our <a href='/api/libpdfcrowd/'>JavaScript library</a>.
        *
        * @param javascript A string containing a JavaScript code. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setCustomJavascript(string javascript)
        {
            if (!(!String.IsNullOrEmpty(javascript)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(javascript, "setCustomJavascript", "html-to-pdf", "The string must not be empty.", "set_custom_javascript"), 470);
            
            fields["custom_javascript"] = javascript;
            return this;
        }

        /**
        * Run a custom JavaScript right after the document is loaded. The script is intended for early DOM manipulation (add/remove elements, update CSS, ...). In addition to the standard browser APIs, the custom JavaScript code can use helper functions from our <a href='/api/libpdfcrowd/'>JavaScript library</a>.
        *
        * @param javascript A string containing a JavaScript code. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setOnLoadJavascript(string javascript)
        {
            if (!(!String.IsNullOrEmpty(javascript)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(javascript, "setOnLoadJavascript", "html-to-pdf", "The string must not be empty.", "set_on_load_javascript"), 470);
            
            fields["on_load_javascript"] = javascript;
            return this;
        }

        /**
        * Set a custom HTTP header that is sent in Pdfcrowd HTTP requests.
        *
        * @param header A string containing the header name and value separated by a colon.
        * @return The converter object.
        */
        public HtmlToPdfClient setCustomHttpHeader(string header)
        {
            if (!Regex.Match(header, "^.+:.+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(header, "setCustomHttpHeader", "html-to-pdf", "A string containing the header name and value separated by a colon.", "set_custom_http_header"), 470);
            
            fields["custom_http_header"] = header;
            return this;
        }

        /**
        * Wait the specified number of milliseconds to finish all JavaScript after the document is loaded. Your API license defines the maximum wait time by "Max Delay" parameter.
        *
        * @param delay The number of milliseconds to wait. Must be a positive integer number or 0.
        * @return The converter object.
        */
        public HtmlToPdfClient setJavascriptDelay(int delay)
        {
            if (!(delay >= 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(delay, "setJavascriptDelay", "html-to-pdf", "Must be a positive integer number or 0.", "set_javascript_delay"), 470);
            
            fields["javascript_delay"] = ConnectionHelper.intToString(delay);
            return this;
        }

        /**
        * Convert only the specified element from the main document and its children. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. If the element is not found, the conversion fails. If multiple elements are found, the first one is used.
        *
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setElementToConvert(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "setElementToConvert", "html-to-pdf", "The string must not be empty.", "set_element_to_convert"), 470);
            
            fields["element_to_convert"] = selectors;
            return this;
        }

        /**
        * Specify the DOM handling when only a part of the document is converted. This can affect the CSS rules used.
        *
        * @param mode Allowed values are cut-out, remove-siblings, hide-siblings.
        * @return The converter object.
        */
        public HtmlToPdfClient setElementToConvertMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(cut-out|remove-siblings|hide-siblings)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setElementToConvertMode", "html-to-pdf", "Allowed values are cut-out, remove-siblings, hide-siblings.", "set_element_to_convert_mode"), 470);
            
            fields["element_to_convert_mode"] = mode;
            return this;
        }

        /**
        * Wait for the specified element in a source document. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. The element is searched for in the main document and all iframes. If the element is not found, the conversion fails. Your API license defines the maximum wait time by "Max Delay" parameter.
        *
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setWaitForElement(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "setWaitForElement", "html-to-pdf", "The string must not be empty.", "set_wait_for_element"), 470);
            
            fields["wait_for_element"] = selectors;
            return this;
        }

        /**
        * The main HTML element for conversion is detected automatically.
        *
        * @param value Set to <span class='field-value'>true</span> to detect the main element.
        * @return The converter object.
        */
        public HtmlToPdfClient setAutoDetectElementToConvert(bool value)
        {
            fields["auto_detect_element_to_convert"] = value ? "true" : null;
            return this;
        }

        /**
        * The input HTML is automatically enhanced to improve the readability.
        *
        * @param enhancements Allowed values are none, readability-v1, readability-v2, readability-v3.
        * @return The converter object.
        */
        public HtmlToPdfClient setReadabilityEnhancements(string enhancements)
        {
            if (!Regex.Match(enhancements, "(?i)^(none|readability-v1|readability-v2|readability-v3)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(enhancements, "setReadabilityEnhancements", "html-to-pdf", "Allowed values are none, readability-v1, readability-v2, readability-v3.", "set_readability_enhancements"), 470);
            
            fields["readability_enhancements"] = enhancements;
            return this;
        }

        /**
        * Set the viewport width in pixels. The viewport is the user's visible area of the page.
        *
        * @param width The value must be in the range 96-65000.
        * @return The converter object.
        */
        public HtmlToPdfClient setViewportWidth(int width)
        {
            if (!(width >= 96 && width <= 65000))
                throw new Error(ConnectionHelper.createInvalidValueMessage(width, "setViewportWidth", "html-to-pdf", "The value must be in the range 96-65000.", "set_viewport_width"), 470);
            
            fields["viewport_width"] = ConnectionHelper.intToString(width);
            return this;
        }

        /**
        * Set the viewport height in pixels. The viewport is the user's visible area of the page. If the input HTML uses lazily loaded images, try using a large value that covers the entire height of the HTML, e.g. 100000.
        *
        * @param height Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToPdfClient setViewportHeight(int height)
        {
            if (!(height > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(height, "setViewportHeight", "html-to-pdf", "Must be a positive integer number.", "set_viewport_height"), 470);
            
            fields["viewport_height"] = ConnectionHelper.intToString(height);
            return this;
        }

        /**
        * Set the viewport size. The viewport is the user's visible area of the page.
        *
        * @param width Set the viewport width in pixels. The viewport is the user's visible area of the page. The value must be in the range 96-65000.
        * @param height Set the viewport height in pixels. The viewport is the user's visible area of the page. If the input HTML uses lazily loaded images, try using a large value that covers the entire height of the HTML, e.g. 100000. Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToPdfClient setViewport(int width, int height)
        {
            this.setViewportWidth(width);
            this.setViewportHeight(height);
            return this;
        }

        /**
        * Set the rendering mode.
        *
        * @param mode The rendering mode. Allowed values are default, viewport.
        * @return The converter object.
        */
        public HtmlToPdfClient setRenderingMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(default|viewport)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setRenderingMode", "html-to-pdf", "Allowed values are default, viewport.", "set_rendering_mode"), 470);
            
            fields["rendering_mode"] = mode;
            return this;
        }

        /**
        * Specifies the scaling mode used for fitting the HTML contents to the print area.
        *
        * @param mode The smart scaling mode. Allowed values are default, disabled, viewport-fit, content-fit, single-page-fit, mode1.
        * @return The converter object.
        */
        public HtmlToPdfClient setSmartScalingMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(default|disabled|viewport-fit|content-fit|single-page-fit|mode1)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setSmartScalingMode", "html-to-pdf", "Allowed values are default, disabled, viewport-fit, content-fit, single-page-fit, mode1.", "set_smart_scaling_mode"), 470);
            
            fields["smart_scaling_mode"] = mode;
            return this;
        }

        /**
        * Set the scaling factor (zoom) for the main page area.
        *
        * @param factor The percentage value. The value must be in the range 10-500.
        * @return The converter object.
        */
        public HtmlToPdfClient setScaleFactor(int factor)
        {
            if (!(factor >= 10 && factor <= 500))
                throw new Error(ConnectionHelper.createInvalidValueMessage(factor, "setScaleFactor", "html-to-pdf", "The value must be in the range 10-500.", "set_scale_factor"), 470);
            
            fields["scale_factor"] = ConnectionHelper.intToString(factor);
            return this;
        }

        /**
        * Set the quality of embedded JPEG images. A lower quality results in a smaller PDF file but can lead to compression artifacts.
        *
        * @param quality The percentage value. The value must be in the range 1-100.
        * @return The converter object.
        */
        public HtmlToPdfClient setJpegQuality(int quality)
        {
            if (!(quality >= 1 && quality <= 100))
                throw new Error(ConnectionHelper.createInvalidValueMessage(quality, "setJpegQuality", "html-to-pdf", "The value must be in the range 1-100.", "set_jpeg_quality"), 470);
            
            fields["jpeg_quality"] = ConnectionHelper.intToString(quality);
            return this;
        }

        /**
        * Specify which image types will be converted to JPEG. Converting lossless compression image formats (PNG, GIF, ...) to JPEG may result in a smaller PDF file.
        *
        * @param images The image category. Allowed values are none, opaque, all.
        * @return The converter object.
        */
        public HtmlToPdfClient setConvertImagesToJpeg(string images)
        {
            if (!Regex.Match(images, "(?i)^(none|opaque|all)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(images, "setConvertImagesToJpeg", "html-to-pdf", "Allowed values are none, opaque, all.", "set_convert_images_to_jpeg"), 470);
            
            fields["convert_images_to_jpeg"] = images;
            return this;
        }

        /**
        * Set the DPI of images in PDF. A lower DPI may result in a smaller PDF file.  If the specified DPI is higher than the actual image DPI, the original image DPI is retained (no upscaling is performed). Use <span class='field-value'>0</span> to leave the images unaltered.
        *
        * @param dpi The DPI value. Must be a positive integer number or 0.
        * @return The converter object.
        */
        public HtmlToPdfClient setImageDpi(int dpi)
        {
            if (!(dpi >= 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(dpi, "setImageDpi", "html-to-pdf", "Must be a positive integer number or 0.", "set_image_dpi"), 470);
            
            fields["image_dpi"] = ConnectionHelper.intToString(dpi);
            return this;
        }

        /**
        * Convert HTML forms to fillable PDF forms. Details can be found in the <a href='https://pdfcrowd.com/blog/create-fillable-pdf-form/'>blog post</a>.
        *
        * @param value Set to <span class='field-value'>true</span> to make fillable PDF forms.
        * @return The converter object.
        */
        public HtmlToPdfClient setEnablePdfForms(bool value)
        {
            fields["enable_pdf_forms"] = value ? "true" : null;
            return this;
        }

        /**
        * Create linearized PDF. This is also known as Fast Web View.
        *
        * @param value Set to <span class='field-value'>true</span> to create linearized PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setLinearize(bool value)
        {
            fields["linearize"] = value ? "true" : null;
            return this;
        }

        /**
        * Encrypt the PDF. This prevents search engines from indexing the contents.
        *
        * @param value Set to <span class='field-value'>true</span> to enable PDF encryption.
        * @return The converter object.
        */
        public HtmlToPdfClient setEncrypt(bool value)
        {
            fields["encrypt"] = value ? "true" : null;
            return this;
        }

        /**
        * Protect the PDF with a user password. When a PDF has a user password, it must be supplied in order to view the document and to perform operations allowed by the access permissions.
        *
        * @param password The user password.
        * @return The converter object.
        */
        public HtmlToPdfClient setUserPassword(string password)
        {
            fields["user_password"] = password;
            return this;
        }

        /**
        * Protect the PDF with an owner password.  Supplying an owner password grants unlimited access to the PDF including changing the passwords and access permissions.
        *
        * @param password The owner password.
        * @return The converter object.
        */
        public HtmlToPdfClient setOwnerPassword(string password)
        {
            fields["owner_password"] = password;
            return this;
        }

        /**
        * Disallow printing of the output PDF.
        *
        * @param value Set to <span class='field-value'>true</span> to set the no-print flag in the output PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoPrint(bool value)
        {
            fields["no_print"] = value ? "true" : null;
            return this;
        }

        /**
        * Disallow modification of the output PDF.
        *
        * @param value Set to <span class='field-value'>true</span> to set the read-only only flag in the output PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoModify(bool value)
        {
            fields["no_modify"] = value ? "true" : null;
            return this;
        }

        /**
        * Disallow text and graphics extraction from the output PDF.
        *
        * @param value Set to <span class='field-value'>true</span> to set the no-copy flag in the output PDF.
        * @return The converter object.
        */
        public HtmlToPdfClient setNoCopy(bool value)
        {
            fields["no_copy"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the title of the PDF.
        *
        * @param title The title.
        * @return The converter object.
        */
        public HtmlToPdfClient setTitle(string title)
        {
            fields["title"] = title;
            return this;
        }

        /**
        * Set the subject of the PDF.
        *
        * @param subject The subject.
        * @return The converter object.
        */
        public HtmlToPdfClient setSubject(string subject)
        {
            fields["subject"] = subject;
            return this;
        }

        /**
        * Set the author of the PDF.
        *
        * @param author The author.
        * @return The converter object.
        */
        public HtmlToPdfClient setAuthor(string author)
        {
            fields["author"] = author;
            return this;
        }

        /**
        * Associate keywords with the document.
        *
        * @param keywords The string with the keywords.
        * @return The converter object.
        */
        public HtmlToPdfClient setKeywords(string keywords)
        {
            fields["keywords"] = keywords;
            return this;
        }

        /**
        * Extract meta tags (author, keywords and description) from the input HTML and use them in the output PDF.
        *
        * @param value Set to <span class='field-value'>true</span> to extract meta tags.
        * @return The converter object.
        */
        public HtmlToPdfClient setExtractMetaTags(bool value)
        {
            fields["extract_meta_tags"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify the page layout to be used when the document is opened.
        *
        * @param layout Allowed values are single-page, one-column, two-column-left, two-column-right.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageLayout(string layout)
        {
            if (!Regex.Match(layout, "(?i)^(single-page|one-column|two-column-left|two-column-right)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(layout, "setPageLayout", "html-to-pdf", "Allowed values are single-page, one-column, two-column-left, two-column-right.", "set_page_layout"), 470);
            
            fields["page_layout"] = layout;
            return this;
        }

        /**
        * Specify how the document should be displayed when opened.
        *
        * @param mode Allowed values are full-screen, thumbnails, outlines.
        * @return The converter object.
        */
        public HtmlToPdfClient setPageMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(full-screen|thumbnails|outlines)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setPageMode", "html-to-pdf", "Allowed values are full-screen, thumbnails, outlines.", "set_page_mode"), 470);
            
            fields["page_mode"] = mode;
            return this;
        }

        /**
        * Specify how the page should be displayed when opened.
        *
        * @param zoomType Allowed values are fit-width, fit-height, fit-page.
        * @return The converter object.
        */
        public HtmlToPdfClient setInitialZoomType(string zoomType)
        {
            if (!Regex.Match(zoomType, "(?i)^(fit-width|fit-height|fit-page)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(zoomType, "setInitialZoomType", "html-to-pdf", "Allowed values are fit-width, fit-height, fit-page.", "set_initial_zoom_type"), 470);
            
            fields["initial_zoom_type"] = zoomType;
            return this;
        }

        /**
        * Display the specified page when the document is opened.
        *
        * @param page Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToPdfClient setInitialPage(int page)
        {
            if (!(page > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(page, "setInitialPage", "html-to-pdf", "Must be a positive integer number.", "set_initial_page"), 470);
            
            fields["initial_page"] = ConnectionHelper.intToString(page);
            return this;
        }

        /**
        * Specify the initial page zoom in percents when the document is opened.
        *
        * @param zoom Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToPdfClient setInitialZoom(int zoom)
        {
            if (!(zoom > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(zoom, "setInitialZoom", "html-to-pdf", "Must be a positive integer number.", "set_initial_zoom"), 470);
            
            fields["initial_zoom"] = ConnectionHelper.intToString(zoom);
            return this;
        }

        /**
        * Specify whether to hide the viewer application's tool bars when the document is active.
        *
        * @param value Set to <span class='field-value'>true</span> to hide tool bars.
        * @return The converter object.
        */
        public HtmlToPdfClient setHideToolbar(bool value)
        {
            fields["hide_toolbar"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to hide the viewer application's menu bar when the document is active.
        *
        * @param value Set to <span class='field-value'>true</span> to hide the menu bar.
        * @return The converter object.
        */
        public HtmlToPdfClient setHideMenubar(bool value)
        {
            fields["hide_menubar"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to hide user interface elements in the document's window (such as scroll bars and navigation controls), leaving only the document's contents displayed.
        *
        * @param value Set to <span class='field-value'>true</span> to hide ui elements.
        * @return The converter object.
        */
        public HtmlToPdfClient setHideWindowUi(bool value)
        {
            fields["hide_window_ui"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to resize the document's window to fit the size of the first displayed page.
        *
        * @param value Set to <span class='field-value'>true</span> to resize the window.
        * @return The converter object.
        */
        public HtmlToPdfClient setFitWindow(bool value)
        {
            fields["fit_window"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to position the document's window in the center of the screen.
        *
        * @param value Set to <span class='field-value'>true</span> to center the window.
        * @return The converter object.
        */
        public HtmlToPdfClient setCenterWindow(bool value)
        {
            fields["center_window"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether the window's title bar should display the document title. If false , the title bar should instead display the name of the PDF file containing the document.
        *
        * @param value Set to <span class='field-value'>true</span> to display the title.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisplayTitle(bool value)
        {
            fields["display_title"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the predominant reading order for text to right-to-left. This option has no direct effect on the document's contents or page numbering but can be used to determine the relative positioning of pages when displayed side by side or printed n-up
        *
        * @param value Set to <span class='field-value'>true</span> to set right-to-left reading order.
        * @return The converter object.
        */
        public HtmlToPdfClient setRightToLeft(bool value)
        {
            fields["right_to_left"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the input data for template rendering. The data format can be JSON, XML, YAML or CSV.
        *
        * @param dataString The input data string.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataString(string dataString)
        {
            fields["data_string"] = dataString;
            return this;
        }

        /**
        * Load the input data for template rendering from the specified file. The data format can be JSON, XML, YAML or CSV.
        *
        * @param dataFile The file path to a local file containing the input data.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataFile(string dataFile)
        {
            files["data_file"] = dataFile;
            return this;
        }

        /**
        * Specify the input data format.
        *
        * @param dataFormat The data format. Allowed values are auto, json, xml, yaml, csv.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataFormat(string dataFormat)
        {
            if (!Regex.Match(dataFormat, "(?i)^(auto|json|xml|yaml|csv)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(dataFormat, "setDataFormat", "html-to-pdf", "Allowed values are auto, json, xml, yaml, csv.", "set_data_format"), 470);
            
            fields["data_format"] = dataFormat;
            return this;
        }

        /**
        * Set the encoding of the data file set by <a href='#set_data_file'>setDataFile</a>.
        *
        * @param encoding The data file encoding.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataEncoding(string encoding)
        {
            fields["data_encoding"] = encoding;
            return this;
        }

        /**
        * Ignore undefined variables in the HTML template. The default mode is strict so any undefined variable causes the conversion to fail. You can use <span class='field-value text-nowrap'>&#x007b;&#x0025; if variable is defined &#x0025;&#x007d;</span> to check if the variable is defined.
        *
        * @param value Set to <span class='field-value'>true</span> to ignore undefined variables.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataIgnoreUndefined(bool value)
        {
            fields["data_ignore_undefined"] = value ? "true" : null;
            return this;
        }

        /**
        * Auto escape HTML symbols in the input data before placing them into the output.
        *
        * @param value Set to <span class='field-value'>true</span> to turn auto escaping on.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataAutoEscape(bool value)
        {
            fields["data_auto_escape"] = value ? "true" : null;
            return this;
        }

        /**
        * Auto trim whitespace around each template command block.
        *
        * @param value Set to <span class='field-value'>true</span> to turn auto trimming on.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataTrimBlocks(bool value)
        {
            fields["data_trim_blocks"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the advanced data options:<ul><li><span class='field-value'>csv_delimiter</span> - The CSV data delimiter, the default is <span class='field-value'>,</span>.</li><li><span class='field-value'>xml_remove_root</span> - Remove the root XML element from the input data.</li><li><span class='field-value'>data_root</span> - The name of the root element inserted into the input data without a root node (e.g. CSV), the default is <span class='field-value'>data</span>.</li></ul>
        *
        * @param options Comma separated list of options.
        * @return The converter object.
        */
        public HtmlToPdfClient setDataOptions(string options)
        {
            fields["data_options"] = options;
            return this;
        }

        /**
        * Turn on the debug logging. Details about the conversion are stored in the debug log. The URL of the log can be obtained from the <a href='#get_debug_log_url'>getDebugLogUrl</a> method or available in <a href='/user/account/log/conversion/'>conversion statistics</a>.
        *
        * @param value Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public HtmlToPdfClient setDebugLog(bool value)
        {
            fields["debug_log"] = value ? "true" : null;
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
        * This method can only be called after a call to one of the convertXtoY methods.
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
        * Get the version details.
        * @return API version, converter version, and client version.
        */
        public string getVersion()
        {
            return string.Format("client {0}, API v2, converter {1}", ConnectionHelper.CLIENT_VERSION, helper.getConverterVersion());
        }

        /**
        * Tag the conversion with a custom value. The tag is used in <a href='/user/account/log/conversion/'>conversion statistics</a>. A value longer than 32 characters is cut off.
        *
        * @param tag A string with the custom tag.
        * @return The converter object.
        */
        public HtmlToPdfClient setTag(string tag)
        {
            fields["tag"] = tag;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTP scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public HtmlToPdfClient setHttpProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpProxy", "html-to-pdf", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_http_proxy"), 470);
            
            fields["http_proxy"] = proxy;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTPS scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public HtmlToPdfClient setHttpsProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpsProxy", "html-to-pdf", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_https_proxy"), 470);
            
            fields["https_proxy"] = proxy;
            return this;
        }

        /**
        * A client certificate to authenticate Pdfcrowd converter on your web server. The certificate is used for two-way SSL/TLS authentication and adds extra security.
        *
        * @param certificate The file must be in PKCS12 format. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToPdfClient setClientCertificate(string certificate)
        {
            if (!(File.Exists(certificate) && new FileInfo(certificate).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(certificate, "setClientCertificate", "html-to-pdf", "The file must exist and not be empty.", "set_client_certificate"), 470);
            
            files["client_certificate"] = certificate;
            return this;
        }

        /**
        * A password for PKCS12 file with a client certificate if it is needed.
        *
        * @param password
        * @return The converter object.
        */
        public HtmlToPdfClient setClientCertificatePassword(string password)
        {
            fields["client_certificate_password"] = password;
            return this;
        }

        /**
        * Set the internal DPI resolution used for positioning of PDF contents. It can help in situations when there are small inaccuracies in the PDF. It is recommended to use values that are a multiple of 72, such as 288 or 360.
        *
        * @param dpi The DPI value. The value must be in the range of 72-600.
        * @return The converter object.
        */
        public HtmlToPdfClient setLayoutDpi(int dpi)
        {
            if (!(dpi >= 72 && dpi <= 600))
                throw new Error(ConnectionHelper.createInvalidValueMessage(dpi, "setLayoutDpi", "html-to-pdf", "The value must be in the range of 72-600.", "set_layout_dpi"), 470);
            
            fields["layout_dpi"] = ConnectionHelper.intToString(dpi);
            return this;
        }

        /**
        * A 2D transformation matrix applied to the main contents on each page. The origin [0,0] is located at the top-left corner of the contents. The resolution is 72 dpi.
        *
        * @param matrix A comma separated string of matrix elements: "scaleX,skewX,transX,skewY,scaleY,transY"
        * @return The converter object.
        */
        public HtmlToPdfClient setContentsMatrix(string matrix)
        {
            fields["contents_matrix"] = matrix;
            return this;
        }

        /**
        * A 2D transformation matrix applied to the page header contents. The origin [0,0] is located at the top-left corner of the header. The resolution is 72 dpi.
        *
        * @param matrix A comma separated string of matrix elements: "scaleX,skewX,transX,skewY,scaleY,transY"
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderMatrix(string matrix)
        {
            fields["header_matrix"] = matrix;
            return this;
        }

        /**
        * A 2D transformation matrix applied to the page footer contents. The origin [0,0] is located at the top-left corner of the footer. The resolution is 72 dpi.
        *
        * @param matrix A comma separated string of matrix elements: "scaleX,skewX,transX,skewY,scaleY,transY"
        * @return The converter object.
        */
        public HtmlToPdfClient setFooterMatrix(string matrix)
        {
            fields["footer_matrix"] = matrix;
            return this;
        }

        /**
        * Disable automatic height adjustment that compensates for pixel to point rounding errors.
        *
        * @param value Set to <span class='field-value'>true</span> to disable automatic height scale.
        * @return The converter object.
        */
        public HtmlToPdfClient setDisablePageHeightOptimization(bool value)
        {
            fields["disable_page_height_optimization"] = value ? "true" : null;
            return this;
        }

        /**
        * Add special CSS classes to the main document's body element. This allows applying custom styling based on these classes:
  <ul>
    <li><span class='field-value'>pdfcrowd-page-X</span> - where X is the current page number</li>
    <li><span class='field-value'>pdfcrowd-page-odd</span> - odd page</li>
    <li><span class='field-value'>pdfcrowd-page-even</span> - even page</li>
  </ul>
        * Warning: If your custom styling affects the contents area size (e.g. by using different margins, padding, border width), the resulting PDF may contain duplicit contents or some contents may be missing.
        *
        * @param value Set to <span class='field-value'>true</span> to add the special CSS classes.
        * @return The converter object.
        */
        public HtmlToPdfClient setMainDocumentCssAnnotation(bool value)
        {
            fields["main_document_css_annotation"] = value ? "true" : null;
            return this;
        }

        /**
        * Add special CSS classes to the header/footer's body element. This allows applying custom styling based on these classes:
  <ul>
    <li><span class='field-value'>pdfcrowd-page-X</span> - where X is the current page number</li>
    <li><span class='field-value'>pdfcrowd-page-count-X</span> - where X is the total page count</li>
    <li><span class='field-value'>pdfcrowd-page-first</span> - the first page</li>
    <li><span class='field-value'>pdfcrowd-page-last</span> - the last page</li>
    <li><span class='field-value'>pdfcrowd-page-odd</span> - odd page</li>
    <li><span class='field-value'>pdfcrowd-page-even</span> - even page</li>
  </ul>
        *
        * @param value Set to <span class='field-value'>true</span> to add the special CSS classes.
        * @return The converter object.
        */
        public HtmlToPdfClient setHeaderFooterCssAnnotation(bool value)
        {
            fields["header_footer_css_annotation"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the converter version. Different versions may produce different output. Choose which one provides the best output for your case.
        *
        * @param version The version identifier. Allowed values are latest, 20.10, 18.10.
        * @return The converter object.
        */
        public HtmlToPdfClient setConverterVersion(string version)
        {
            if (!Regex.Match(version, "(?i)^(latest|20.10|18.10)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(version, "setConverterVersion", "html-to-pdf", "Allowed values are latest, 20.10, 18.10.", "set_converter_version"), 470);
            
            helper.setConverterVersion(version);
            return this;
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * Warning: Using HTTP is insecure as data sent over HTTP is not encrypted. Enable this option only if you know what you are doing.
        *
        * @param value Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public HtmlToPdfClient setUseHttp(bool value)
        {
            helper.setUseHttp(value);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be useful if you are behind a proxy or a firewall.
        *
        * @param agent The user agent string.
        * @return The converter object.
        */
        public HtmlToPdfClient setUserAgent(string agent)
        {
            helper.setUserAgent(agent);
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
        * Specifies the number of automatic retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        *
        * @param count Number of retries.
        * @return The converter object.
        */
        public HtmlToPdfClient setRetryCount(int count)
        {
            helper.setRetryCount(count);
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(outputFormat, "setOutputFormat", "html-to-image", "Allowed values are png, jpg, gif, tiff, bmp, ico, ppm, pgm, pbm, pnm, psb, pct, ras, tga, sgi, sun, webp.", "set_output_format"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrl", "html-to-image", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrlToStream::url", "html-to-image", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertUrlToFile::file_path", "html-to-image", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertUrlToStream(url, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFile", "html-to-image", "The file must exist and not be empty.", "convert_file"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFileToStream::file", "html-to-image", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertFileToFile::file_path", "html-to-image", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertFileToStream(file, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "convertString", "html-to-image", "The string must not be empty.", "convert_string"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(text, "convertStringToStream::text", "html-to-image", "The string must not be empty.", "convert_string_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStringToFile::file_path", "html-to-image", "The string must not be empty.", "convert_string_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertStringToStream(text, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert the contents of an input stream.
        *
        * @param inStream The input stream with source data.<br> The stream can contain either HTML code or an archive (.zip, .tar.gz, .tar.bz2).<br>The archive can contain HTML code and its external assets (images, style sheets, javascript).
        * @return Byte array containing the conversion output.
        */
        public byte[] convertStream(Stream inStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert the contents of an input stream and write the result to an output stream.
        *
        * @param inStream The input stream with source data.<br> The stream can contain either HTML code or an archive (.zip, .tar.gz, .tar.bz2).<br>The archive can contain HTML code and its external assets (images, style sheets, javascript).
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertStreamToStream(Stream inStream, Stream outStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert the contents of an input stream and write the result to a local file.
        *
        * @param inStream The input stream with source data.<br> The stream can contain either HTML code or an archive (.zip, .tar.gz, .tar.bz2).<br>The archive can contain HTML code and its external assets (images, style sheets, javascript).
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertStreamToFile(Stream inStream, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStreamToFile::file_path", "html-to-image", "The string must not be empty.", "convert_stream_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertStreamToStream(inStream, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Set the file name of the main HTML document stored in the input archive. If not specified, the first HTML file in the archive is used for conversion. Use this method if the input archive contains multiple HTML documents.
        *
        * @param filename The file name.
        * @return The converter object.
        */
        public HtmlToImageClient setZipMainFilename(string filename)
        {
            fields["zip_main_filename"] = filename;
            return this;
        }

        /**
        * Use the print version of the page if available (@media print).
        *
        * @param value Set to <span class='field-value'>true</span> to use the print version of the page.
        * @return The converter object.
        */
        public HtmlToImageClient setUsePrintMedia(bool value)
        {
            fields["use_print_media"] = value ? "true" : null;
            return this;
        }

        /**
        * Do not print the background graphics.
        *
        * @param value Set to <span class='field-value'>true</span> to disable the background graphics.
        * @return The converter object.
        */
        public HtmlToImageClient setNoBackground(bool value)
        {
            fields["no_background"] = value ? "true" : null;
            return this;
        }

        /**
        * Do not execute JavaScript.
        *
        * @param value Set to <span class='field-value'>true</span> to disable JavaScript in web pages.
        * @return The converter object.
        */
        public HtmlToImageClient setDisableJavascript(bool value)
        {
            fields["disable_javascript"] = value ? "true" : null;
            return this;
        }

        /**
        * Do not load images.
        *
        * @param value Set to <span class='field-value'>true</span> to disable loading of images.
        * @return The converter object.
        */
        public HtmlToImageClient setDisableImageLoading(bool value)
        {
            fields["disable_image_loading"] = value ? "true" : null;
            return this;
        }

        /**
        * Disable loading fonts from remote sources.
        *
        * @param value Set to <span class='field-value'>true</span> disable loading remote fonts.
        * @return The converter object.
        */
        public HtmlToImageClient setDisableRemoteFonts(bool value)
        {
            fields["disable_remote_fonts"] = value ? "true" : null;
            return this;
        }

        /**
        * Use a mobile user agent.
        *
        * @param value Set to <span class='field-value'>true</span> to use a mobile user agent.
        * @return The converter object.
        */
        public HtmlToImageClient setUseMobileUserAgent(bool value)
        {
            fields["use_mobile_user_agent"] = value ? "true" : null;
            return this;
        }

        /**
        * Specifies how iframes are handled.
        *
        * @param iframes Allowed values are all, same-origin, none.
        * @return The converter object.
        */
        public HtmlToImageClient setLoadIframes(string iframes)
        {
            if (!Regex.Match(iframes, "(?i)^(all|same-origin|none)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(iframes, "setLoadIframes", "html-to-image", "Allowed values are all, same-origin, none.", "set_load_iframes"), 470);
            
            fields["load_iframes"] = iframes;
            return this;
        }

        /**
        * Try to block ads. Enabling this option can produce smaller output and speed up the conversion.
        *
        * @param value Set to <span class='field-value'>true</span> to block ads in web pages.
        * @return The converter object.
        */
        public HtmlToImageClient setBlockAds(bool value)
        {
            fields["block_ads"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the default HTML content text encoding.
        *
        * @param encoding The text encoding of the HTML content.
        * @return The converter object.
        */
        public HtmlToImageClient setDefaultEncoding(string encoding)
        {
            fields["default_encoding"] = encoding;
            return this;
        }

        /**
        * Set the locale for the conversion. This may affect the output format of dates, times and numbers.
        *
        * @param locale The locale code according to ISO 639.
        * @return The converter object.
        */
        public HtmlToImageClient setLocale(string locale)
        {
            fields["locale"] = locale;
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
        * Set credentials to access HTTP base authentication protected websites.
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
        * @param value Set to <span class='field-value'>true</span> to enable SSL certificate verification.
        * @return The converter object.
        */
        public HtmlToImageClient setVerifySslCertificates(bool value)
        {
            fields["verify_ssl_certificates"] = value ? "true" : null;
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
        * Abort the conversion if any of the sub-request HTTP status code is greater than or equal to 400 or if some sub-requests are still pending. See details in a debug log.
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
        * Do not send the X-Pdfcrowd HTTP header in Pdfcrowd HTTP requests.
        *
        * @param value Set to <span class='field-value'>true</span> to disable sending X-Pdfcrowd HTTP header.
        * @return The converter object.
        */
        public HtmlToImageClient setNoXpdfcrowdHeader(bool value)
        {
            fields["no_xpdfcrowd_header"] = value ? "true" : null;
            return this;
        }

        /**
        * Run a custom JavaScript after the document is loaded and ready to print. The script is intended for post-load DOM manipulation (add/remove elements, update CSS, ...). In addition to the standard browser APIs, the custom JavaScript code can use helper functions from our <a href='/api/libpdfcrowd/'>JavaScript library</a>.
        *
        * @param javascript A string containing a JavaScript code. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setCustomJavascript(string javascript)
        {
            if (!(!String.IsNullOrEmpty(javascript)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(javascript, "setCustomJavascript", "html-to-image", "The string must not be empty.", "set_custom_javascript"), 470);
            
            fields["custom_javascript"] = javascript;
            return this;
        }

        /**
        * Run a custom JavaScript right after the document is loaded. The script is intended for early DOM manipulation (add/remove elements, update CSS, ...). In addition to the standard browser APIs, the custom JavaScript code can use helper functions from our <a href='/api/libpdfcrowd/'>JavaScript library</a>.
        *
        * @param javascript A string containing a JavaScript code. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setOnLoadJavascript(string javascript)
        {
            if (!(!String.IsNullOrEmpty(javascript)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(javascript, "setOnLoadJavascript", "html-to-image", "The string must not be empty.", "set_on_load_javascript"), 470);
            
            fields["on_load_javascript"] = javascript;
            return this;
        }

        /**
        * Set a custom HTTP header that is sent in Pdfcrowd HTTP requests.
        *
        * @param header A string containing the header name and value separated by a colon.
        * @return The converter object.
        */
        public HtmlToImageClient setCustomHttpHeader(string header)
        {
            if (!Regex.Match(header, "^.+:.+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(header, "setCustomHttpHeader", "html-to-image", "A string containing the header name and value separated by a colon.", "set_custom_http_header"), 470);
            
            fields["custom_http_header"] = header;
            return this;
        }

        /**
        * Wait the specified number of milliseconds to finish all JavaScript after the document is loaded. Your API license defines the maximum wait time by "Max Delay" parameter.
        *
        * @param delay The number of milliseconds to wait. Must be a positive integer number or 0.
        * @return The converter object.
        */
        public HtmlToImageClient setJavascriptDelay(int delay)
        {
            if (!(delay >= 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(delay, "setJavascriptDelay", "html-to-image", "Must be a positive integer number or 0.", "set_javascript_delay"), 470);
            
            fields["javascript_delay"] = ConnectionHelper.intToString(delay);
            return this;
        }

        /**
        * Convert only the specified element from the main document and its children. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. If the element is not found, the conversion fails. If multiple elements are found, the first one is used.
        *
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setElementToConvert(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "setElementToConvert", "html-to-image", "The string must not be empty.", "set_element_to_convert"), 470);
            
            fields["element_to_convert"] = selectors;
            return this;
        }

        /**
        * Specify the DOM handling when only a part of the document is converted. This can affect the CSS rules used.
        *
        * @param mode Allowed values are cut-out, remove-siblings, hide-siblings.
        * @return The converter object.
        */
        public HtmlToImageClient setElementToConvertMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(cut-out|remove-siblings|hide-siblings)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setElementToConvertMode", "html-to-image", "Allowed values are cut-out, remove-siblings, hide-siblings.", "set_element_to_convert_mode"), 470);
            
            fields["element_to_convert_mode"] = mode;
            return this;
        }

        /**
        * Wait for the specified element in a source document. The element is specified by one or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a>. The element is searched for in the main document and all iframes. If the element is not found, the conversion fails. Your API license defines the maximum wait time by "Max Delay" parameter.
        *
        * @param selectors One or more <a href='https://developer.mozilla.org/en-US/docs/Learn/CSS/Introduction_to_CSS/Selectors'>CSS selectors</a> separated by commas. The string must not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setWaitForElement(string selectors)
        {
            if (!(!String.IsNullOrEmpty(selectors)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(selectors, "setWaitForElement", "html-to-image", "The string must not be empty.", "set_wait_for_element"), 470);
            
            fields["wait_for_element"] = selectors;
            return this;
        }

        /**
        * The main HTML element for conversion is detected automatically.
        *
        * @param value Set to <span class='field-value'>true</span> to detect the main element.
        * @return The converter object.
        */
        public HtmlToImageClient setAutoDetectElementToConvert(bool value)
        {
            fields["auto_detect_element_to_convert"] = value ? "true" : null;
            return this;
        }

        /**
        * The input HTML is automatically enhanced to improve the readability.
        *
        * @param enhancements Allowed values are none, readability-v1, readability-v2, readability-v3.
        * @return The converter object.
        */
        public HtmlToImageClient setReadabilityEnhancements(string enhancements)
        {
            if (!Regex.Match(enhancements, "(?i)^(none|readability-v1|readability-v2|readability-v3)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(enhancements, "setReadabilityEnhancements", "html-to-image", "Allowed values are none, readability-v1, readability-v2, readability-v3.", "set_readability_enhancements"), 470);
            
            fields["readability_enhancements"] = enhancements;
            return this;
        }

        /**
        * Set the output image width in pixels.
        *
        * @param width The value must be in the range 96-65000.
        * @return The converter object.
        */
        public HtmlToImageClient setScreenshotWidth(int width)
        {
            if (!(width >= 96 && width <= 65000))
                throw new Error(ConnectionHelper.createInvalidValueMessage(width, "setScreenshotWidth", "html-to-image", "The value must be in the range 96-65000.", "set_screenshot_width"), 470);
            
            fields["screenshot_width"] = ConnectionHelper.intToString(width);
            return this;
        }

        /**
        * Set the output image height in pixels. If it is not specified, actual document height is used.
        *
        * @param height Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToImageClient setScreenshotHeight(int height)
        {
            if (!(height > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(height, "setScreenshotHeight", "html-to-image", "Must be a positive integer number.", "set_screenshot_height"), 470);
            
            fields["screenshot_height"] = ConnectionHelper.intToString(height);
            return this;
        }

        /**
        * Set the scaling factor (zoom) for the output image.
        *
        * @param factor The percentage value. Must be a positive integer number.
        * @return The converter object.
        */
        public HtmlToImageClient setScaleFactor(int factor)
        {
            if (!(factor > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(factor, "setScaleFactor", "html-to-image", "Must be a positive integer number.", "set_scale_factor"), 470);
            
            fields["scale_factor"] = ConnectionHelper.intToString(factor);
            return this;
        }

        /**
        * The output image background color.
        *
        * @param color The value must be in RRGGBB or RRGGBBAA hexadecimal format.
        * @return The converter object.
        */
        public HtmlToImageClient setBackgroundColor(string color)
        {
            if (!Regex.Match(color, "^[0-9a-fA-F]{6,8}$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(color, "setBackgroundColor", "html-to-image", "The value must be in RRGGBB or RRGGBBAA hexadecimal format.", "set_background_color"), 470);
            
            fields["background_color"] = color;
            return this;
        }

        /**
        * Set the input data for template rendering. The data format can be JSON, XML, YAML or CSV.
        *
        * @param dataString The input data string.
        * @return The converter object.
        */
        public HtmlToImageClient setDataString(string dataString)
        {
            fields["data_string"] = dataString;
            return this;
        }

        /**
        * Load the input data for template rendering from the specified file. The data format can be JSON, XML, YAML or CSV.
        *
        * @param dataFile The file path to a local file containing the input data.
        * @return The converter object.
        */
        public HtmlToImageClient setDataFile(string dataFile)
        {
            files["data_file"] = dataFile;
            return this;
        }

        /**
        * Specify the input data format.
        *
        * @param dataFormat The data format. Allowed values are auto, json, xml, yaml, csv.
        * @return The converter object.
        */
        public HtmlToImageClient setDataFormat(string dataFormat)
        {
            if (!Regex.Match(dataFormat, "(?i)^(auto|json|xml|yaml|csv)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(dataFormat, "setDataFormat", "html-to-image", "Allowed values are auto, json, xml, yaml, csv.", "set_data_format"), 470);
            
            fields["data_format"] = dataFormat;
            return this;
        }

        /**
        * Set the encoding of the data file set by <a href='#set_data_file'>setDataFile</a>.
        *
        * @param encoding The data file encoding.
        * @return The converter object.
        */
        public HtmlToImageClient setDataEncoding(string encoding)
        {
            fields["data_encoding"] = encoding;
            return this;
        }

        /**
        * Ignore undefined variables in the HTML template. The default mode is strict so any undefined variable causes the conversion to fail. You can use <span class='field-value text-nowrap'>&#x007b;&#x0025; if variable is defined &#x0025;&#x007d;</span> to check if the variable is defined.
        *
        * @param value Set to <span class='field-value'>true</span> to ignore undefined variables.
        * @return The converter object.
        */
        public HtmlToImageClient setDataIgnoreUndefined(bool value)
        {
            fields["data_ignore_undefined"] = value ? "true" : null;
            return this;
        }

        /**
        * Auto escape HTML symbols in the input data before placing them into the output.
        *
        * @param value Set to <span class='field-value'>true</span> to turn auto escaping on.
        * @return The converter object.
        */
        public HtmlToImageClient setDataAutoEscape(bool value)
        {
            fields["data_auto_escape"] = value ? "true" : null;
            return this;
        }

        /**
        * Auto trim whitespace around each template command block.
        *
        * @param value Set to <span class='field-value'>true</span> to turn auto trimming on.
        * @return The converter object.
        */
        public HtmlToImageClient setDataTrimBlocks(bool value)
        {
            fields["data_trim_blocks"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the advanced data options:<ul><li><span class='field-value'>csv_delimiter</span> - The CSV data delimiter, the default is <span class='field-value'>,</span>.</li><li><span class='field-value'>xml_remove_root</span> - Remove the root XML element from the input data.</li><li><span class='field-value'>data_root</span> - The name of the root element inserted into the input data without a root node (e.g. CSV), the default is <span class='field-value'>data</span>.</li></ul>
        *
        * @param options Comma separated list of options.
        * @return The converter object.
        */
        public HtmlToImageClient setDataOptions(string options)
        {
            fields["data_options"] = options;
            return this;
        }

        /**
        * Turn on the debug logging. Details about the conversion are stored in the debug log. The URL of the log can be obtained from the <a href='#get_debug_log_url'>getDebugLogUrl</a> method or available in <a href='/user/account/log/conversion/'>conversion statistics</a>.
        *
        * @param value Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public HtmlToImageClient setDebugLog(bool value)
        {
            fields["debug_log"] = value ? "true" : null;
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
        * This method can only be called after a call to one of the convertXtoY methods.
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
        * Get the version details.
        * @return API version, converter version, and client version.
        */
        public string getVersion()
        {
            return string.Format("client {0}, API v2, converter {1}", ConnectionHelper.CLIENT_VERSION, helper.getConverterVersion());
        }

        /**
        * Tag the conversion with a custom value. The tag is used in <a href='/user/account/log/conversion/'>conversion statistics</a>. A value longer than 32 characters is cut off.
        *
        * @param tag A string with the custom tag.
        * @return The converter object.
        */
        public HtmlToImageClient setTag(string tag)
        {
            fields["tag"] = tag;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTP scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public HtmlToImageClient setHttpProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpProxy", "html-to-image", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_http_proxy"), 470);
            
            fields["http_proxy"] = proxy;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTPS scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public HtmlToImageClient setHttpsProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpsProxy", "html-to-image", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_https_proxy"), 470);
            
            fields["https_proxy"] = proxy;
            return this;
        }

        /**
        * A client certificate to authenticate Pdfcrowd converter on your web server. The certificate is used for two-way SSL/TLS authentication and adds extra security.
        *
        * @param certificate The file must be in PKCS12 format. The file must exist and not be empty.
        * @return The converter object.
        */
        public HtmlToImageClient setClientCertificate(string certificate)
        {
            if (!(File.Exists(certificate) && new FileInfo(certificate).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(certificate, "setClientCertificate", "html-to-image", "The file must exist and not be empty.", "set_client_certificate"), 470);
            
            files["client_certificate"] = certificate;
            return this;
        }

        /**
        * A password for PKCS12 file with a client certificate if it is needed.
        *
        * @param password
        * @return The converter object.
        */
        public HtmlToImageClient setClientCertificatePassword(string password)
        {
            fields["client_certificate_password"] = password;
            return this;
        }

        /**
        * Set the converter version. Different versions may produce different output. Choose which one provides the best output for your case.
        *
        * @param version The version identifier. Allowed values are latest, 20.10, 18.10.
        * @return The converter object.
        */
        public HtmlToImageClient setConverterVersion(string version)
        {
            if (!Regex.Match(version, "(?i)^(latest|20.10|18.10)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(version, "setConverterVersion", "html-to-image", "Allowed values are latest, 20.10, 18.10.", "set_converter_version"), 470);
            
            helper.setConverterVersion(version);
            return this;
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * Warning: Using HTTP is insecure as data sent over HTTP is not encrypted. Enable this option only if you know what you are doing.
        *
        * @param value Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public HtmlToImageClient setUseHttp(bool value)
        {
            helper.setUseHttp(value);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be useful if you are behind a proxy or a firewall.
        *
        * @param agent The user agent string.
        * @return The converter object.
        */
        public HtmlToImageClient setUserAgent(string agent)
        {
            helper.setUserAgent(agent);
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
        * Specifies the number of automatic retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        *
        * @param count Number of retries.
        * @return The converter object.
        */
        public HtmlToImageClient setRetryCount(int count)
        {
            helper.setRetryCount(count);
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrl", "image-to-image", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrlToStream::url", "image-to-image", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertUrlToFile::file_path", "image-to-image", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertUrlToStream(url, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert a local file.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertFile(string file)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFile", "image-to-image", "The file must exist and not be empty.", "convert_file"), 470);
            
            files["file"] = file;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a local file and write the result to an output stream.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertFileToStream(string file, Stream outStream)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFileToStream::file", "image-to-image", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
            files["file"] = file;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a local file and write the result to a local file.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertFileToFile(string file, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertFileToFile::file_path", "image-to-image", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertFileToStream(file, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertRawDataToFile::file_path", "image-to-image", "The string must not be empty.", "convert_raw_data_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertRawDataToStream(data, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert the contents of an input stream.
        *
        * @param inStream The input stream with source data.<br>
        * @return Byte array containing the conversion output.
        */
        public byte[] convertStream(Stream inStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert the contents of an input stream and write the result to an output stream.
        *
        * @param inStream The input stream with source data.<br>
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertStreamToStream(Stream inStream, Stream outStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert the contents of an input stream and write the result to a local file.
        *
        * @param inStream The input stream with source data.<br>
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertStreamToFile(Stream inStream, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStreamToFile::file_path", "image-to-image", "The string must not be empty.", "convert_stream_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertStreamToStream(inStream, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(outputFormat, "setOutputFormat", "image-to-image", "Allowed values are png, jpg, gif, tiff, bmp, ico, ppm, pgm, pbm, pnm, psb, pct, ras, tga, sgi, sun, webp.", "set_output_format"), 470);
            
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
        * Turn on the debug logging. Details about the conversion are stored in the debug log. The URL of the log can be obtained from the <a href='#get_debug_log_url'>getDebugLogUrl</a> method or available in <a href='/user/account/log/conversion/'>conversion statistics</a>.
        *
        * @param value Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public ImageToImageClient setDebugLog(bool value)
        {
            fields["debug_log"] = value ? "true" : null;
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
        * This method can only be called after a call to one of the convertXtoY methods.
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
        * Get the version details.
        * @return API version, converter version, and client version.
        */
        public string getVersion()
        {
            return string.Format("client {0}, API v2, converter {1}", ConnectionHelper.CLIENT_VERSION, helper.getConverterVersion());
        }

        /**
        * Tag the conversion with a custom value. The tag is used in <a href='/user/account/log/conversion/'>conversion statistics</a>. A value longer than 32 characters is cut off.
        *
        * @param tag A string with the custom tag.
        * @return The converter object.
        */
        public ImageToImageClient setTag(string tag)
        {
            fields["tag"] = tag;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTP scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public ImageToImageClient setHttpProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpProxy", "image-to-image", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_http_proxy"), 470);
            
            fields["http_proxy"] = proxy;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTPS scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public ImageToImageClient setHttpsProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpsProxy", "image-to-image", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_https_proxy"), 470);
            
            fields["https_proxy"] = proxy;
            return this;
        }

        /**
        * Set the converter version. Different versions may produce different output. Choose which one provides the best output for your case.
        *
        * @param version The version identifier. Allowed values are latest, 20.10, 18.10.
        * @return The converter object.
        */
        public ImageToImageClient setConverterVersion(string version)
        {
            if (!Regex.Match(version, "(?i)^(latest|20.10|18.10)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(version, "setConverterVersion", "image-to-image", "Allowed values are latest, 20.10, 18.10.", "set_converter_version"), 470);
            
            helper.setConverterVersion(version);
            return this;
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * Warning: Using HTTP is insecure as data sent over HTTP is not encrypted. Enable this option only if you know what you are doing.
        *
        * @param value Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public ImageToImageClient setUseHttp(bool value)
        {
            helper.setUseHttp(value);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be useful if you are behind a proxy or a firewall.
        *
        * @param agent The user agent string.
        * @return The converter object.
        */
        public ImageToImageClient setUserAgent(string agent)
        {
            helper.setUserAgent(agent);
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
        * Specifies the number of automatic retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        *
        * @param count Number of retries.
        * @return The converter object.
        */
        public ImageToImageClient setRetryCount(int count)
        {
            helper.setRetryCount(count);
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(action, "setAction", "pdf-to-pdf", "Allowed values are join, shuffle.", "set_action"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertToFile", "pdf-to-pdf", "The string must not be empty.", "convert_to_file"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "addPdfFile", "pdf-to-pdf", "The file must exist and not be empty.", "add_pdf_file"), 470);
            
            files["f_" + ConnectionHelper.intToString(fileId)] = filePath;
            fileId++;
            return this;
        }

        /**
        * Add in-memory raw PDF data to the list of the input PDFs.<br>Typical usage is for adding PDF created by another Pdfcrowd converter.<br><br> Example in PHP:<br> <b>$clientPdf2Pdf</b>-&gt;addPdfRawData(<b>$clientHtml2Pdf</b>-&gt;convertUrl('http://www.example.com'));
        *
        * @param data The raw PDF data. The input data must be PDF content.
        * @return The converter object.
        */
        public PdfToPdfClient addPdfRawData(byte[] data)
        {
            if (!(data != null && data.Length > 300 && data[0] == '%' && data[1] == 'P' && data[2] == 'D' && data[3] == 'F'))
                throw new Error(ConnectionHelper.createInvalidValueMessage("raw PDF data", "addPdfRawData", "pdf-to-pdf", "The input data must be PDF content.", "add_pdf_raw_data"), 470);
            
            rawData["f_" + ConnectionHelper.intToString(fileId)] = data;
            fileId++;
            return this;
        }

        /**
        * Password to open the encrypted PDF file.
        *
        * @param password The input PDF password.
        * @return The converter object.
        */
        public PdfToPdfClient setInputPdfPassword(string password)
        {
            fields["input_pdf_password"] = password;
            return this;
        }

        /**
        * Apply a watermark to each page of the output PDF file. A watermark can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the watermark.
        *
        * @param watermark The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public PdfToPdfClient setPageWatermark(string watermark)
        {
            if (!(File.Exists(watermark) && new FileInfo(watermark).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(watermark, "setPageWatermark", "pdf-to-pdf", "The file must exist and not be empty.", "set_page_watermark"), 470);
            
            files["page_watermark"] = watermark;
            return this;
        }

        /**
        * Load a file from the specified URL and apply the file as a watermark to each page of the output PDF. A watermark can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the watermark.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public PdfToPdfClient setPageWatermarkUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setPageWatermarkUrl", "pdf-to-pdf", "The supported protocols are http:// and https://.", "set_page_watermark_url"), 470);
            
            fields["page_watermark_url"] = url;
            return this;
        }

        /**
        * Apply each page of a watermark to the corresponding page of the output PDF. A watermark can be either a PDF or an image.
        *
        * @param watermark The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public PdfToPdfClient setMultipageWatermark(string watermark)
        {
            if (!(File.Exists(watermark) && new FileInfo(watermark).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(watermark, "setMultipageWatermark", "pdf-to-pdf", "The file must exist and not be empty.", "set_multipage_watermark"), 470);
            
            files["multipage_watermark"] = watermark;
            return this;
        }

        /**
        * Load a file from the specified URL and apply each page of the file as a watermark to the corresponding page of the output PDF. A watermark can be either a PDF or an image.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public PdfToPdfClient setMultipageWatermarkUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setMultipageWatermarkUrl", "pdf-to-pdf", "The supported protocols are http:// and https://.", "set_multipage_watermark_url"), 470);
            
            fields["multipage_watermark_url"] = url;
            return this;
        }

        /**
        * Apply a background to each page of the output PDF file. A background can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the background.
        *
        * @param background The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public PdfToPdfClient setPageBackground(string background)
        {
            if (!(File.Exists(background) && new FileInfo(background).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(background, "setPageBackground", "pdf-to-pdf", "The file must exist and not be empty.", "set_page_background"), 470);
            
            files["page_background"] = background;
            return this;
        }

        /**
        * Load a file from the specified URL and apply the file as a background to each page of the output PDF. A background can be either a PDF or an image. If a multi-page file (PDF or TIFF) is used, the first page is used as the background.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public PdfToPdfClient setPageBackgroundUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setPageBackgroundUrl", "pdf-to-pdf", "The supported protocols are http:// and https://.", "set_page_background_url"), 470);
            
            fields["page_background_url"] = url;
            return this;
        }

        /**
        * Apply each page of a background to the corresponding page of the output PDF. A background can be either a PDF or an image.
        *
        * @param background The file path to a local file. The file must exist and not be empty.
        * @return The converter object.
        */
        public PdfToPdfClient setMultipageBackground(string background)
        {
            if (!(File.Exists(background) && new FileInfo(background).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(background, "setMultipageBackground", "pdf-to-pdf", "The file must exist and not be empty.", "set_multipage_background"), 470);
            
            files["multipage_background"] = background;
            return this;
        }

        /**
        * Load a file from the specified URL and apply each page of the file as a background to the corresponding page of the output PDF. A background can be either a PDF or an image.
        *
        * @param url The supported protocols are http:// and https://.
        * @return The converter object.
        */
        public PdfToPdfClient setMultipageBackgroundUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "setMultipageBackgroundUrl", "pdf-to-pdf", "The supported protocols are http:// and https://.", "set_multipage_background_url"), 470);
            
            fields["multipage_background_url"] = url;
            return this;
        }

        /**
        * Create linearized PDF. This is also known as Fast Web View.
        *
        * @param value Set to <span class='field-value'>true</span> to create linearized PDF.
        * @return The converter object.
        */
        public PdfToPdfClient setLinearize(bool value)
        {
            fields["linearize"] = value ? "true" : null;
            return this;
        }

        /**
        * Encrypt the PDF. This prevents search engines from indexing the contents.
        *
        * @param value Set to <span class='field-value'>true</span> to enable PDF encryption.
        * @return The converter object.
        */
        public PdfToPdfClient setEncrypt(bool value)
        {
            fields["encrypt"] = value ? "true" : null;
            return this;
        }

        /**
        * Protect the PDF with a user password. When a PDF has a user password, it must be supplied in order to view the document and to perform operations allowed by the access permissions.
        *
        * @param password The user password.
        * @return The converter object.
        */
        public PdfToPdfClient setUserPassword(string password)
        {
            fields["user_password"] = password;
            return this;
        }

        /**
        * Protect the PDF with an owner password.  Supplying an owner password grants unlimited access to the PDF including changing the passwords and access permissions.
        *
        * @param password The owner password.
        * @return The converter object.
        */
        public PdfToPdfClient setOwnerPassword(string password)
        {
            fields["owner_password"] = password;
            return this;
        }

        /**
        * Disallow printing of the output PDF.
        *
        * @param value Set to <span class='field-value'>true</span> to set the no-print flag in the output PDF.
        * @return The converter object.
        */
        public PdfToPdfClient setNoPrint(bool value)
        {
            fields["no_print"] = value ? "true" : null;
            return this;
        }

        /**
        * Disallow modification of the output PDF.
        *
        * @param value Set to <span class='field-value'>true</span> to set the read-only only flag in the output PDF.
        * @return The converter object.
        */
        public PdfToPdfClient setNoModify(bool value)
        {
            fields["no_modify"] = value ? "true" : null;
            return this;
        }

        /**
        * Disallow text and graphics extraction from the output PDF.
        *
        * @param value Set to <span class='field-value'>true</span> to set the no-copy flag in the output PDF.
        * @return The converter object.
        */
        public PdfToPdfClient setNoCopy(bool value)
        {
            fields["no_copy"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the title of the PDF.
        *
        * @param title The title.
        * @return The converter object.
        */
        public PdfToPdfClient setTitle(string title)
        {
            fields["title"] = title;
            return this;
        }

        /**
        * Set the subject of the PDF.
        *
        * @param subject The subject.
        * @return The converter object.
        */
        public PdfToPdfClient setSubject(string subject)
        {
            fields["subject"] = subject;
            return this;
        }

        /**
        * Set the author of the PDF.
        *
        * @param author The author.
        * @return The converter object.
        */
        public PdfToPdfClient setAuthor(string author)
        {
            fields["author"] = author;
            return this;
        }

        /**
        * Associate keywords with the document.
        *
        * @param keywords The string with the keywords.
        * @return The converter object.
        */
        public PdfToPdfClient setKeywords(string keywords)
        {
            fields["keywords"] = keywords;
            return this;
        }

        /**
        * Use metadata (title, subject, author and keywords) from the n-th input PDF.
        *
        * @param index Set the index of the input PDF file from which to use the metadata. 0 means no metadata. Must be a positive integer number or 0.
        * @return The converter object.
        */
        public PdfToPdfClient setUseMetadataFrom(int index)
        {
            if (!(index >= 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(index, "setUseMetadataFrom", "pdf-to-pdf", "Must be a positive integer number or 0.", "set_use_metadata_from"), 470);
            
            fields["use_metadata_from"] = ConnectionHelper.intToString(index);
            return this;
        }

        /**
        * Specify the page layout to be used when the document is opened.
        *
        * @param layout Allowed values are single-page, one-column, two-column-left, two-column-right.
        * @return The converter object.
        */
        public PdfToPdfClient setPageLayout(string layout)
        {
            if (!Regex.Match(layout, "(?i)^(single-page|one-column|two-column-left|two-column-right)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(layout, "setPageLayout", "pdf-to-pdf", "Allowed values are single-page, one-column, two-column-left, two-column-right.", "set_page_layout"), 470);
            
            fields["page_layout"] = layout;
            return this;
        }

        /**
        * Specify how the document should be displayed when opened.
        *
        * @param mode Allowed values are full-screen, thumbnails, outlines.
        * @return The converter object.
        */
        public PdfToPdfClient setPageMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(full-screen|thumbnails|outlines)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setPageMode", "pdf-to-pdf", "Allowed values are full-screen, thumbnails, outlines.", "set_page_mode"), 470);
            
            fields["page_mode"] = mode;
            return this;
        }

        /**
        * Specify how the page should be displayed when opened.
        *
        * @param zoomType Allowed values are fit-width, fit-height, fit-page.
        * @return The converter object.
        */
        public PdfToPdfClient setInitialZoomType(string zoomType)
        {
            if (!Regex.Match(zoomType, "(?i)^(fit-width|fit-height|fit-page)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(zoomType, "setInitialZoomType", "pdf-to-pdf", "Allowed values are fit-width, fit-height, fit-page.", "set_initial_zoom_type"), 470);
            
            fields["initial_zoom_type"] = zoomType;
            return this;
        }

        /**
        * Display the specified page when the document is opened.
        *
        * @param page Must be a positive integer number.
        * @return The converter object.
        */
        public PdfToPdfClient setInitialPage(int page)
        {
            if (!(page > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(page, "setInitialPage", "pdf-to-pdf", "Must be a positive integer number.", "set_initial_page"), 470);
            
            fields["initial_page"] = ConnectionHelper.intToString(page);
            return this;
        }

        /**
        * Specify the initial page zoom in percents when the document is opened.
        *
        * @param zoom Must be a positive integer number.
        * @return The converter object.
        */
        public PdfToPdfClient setInitialZoom(int zoom)
        {
            if (!(zoom > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(zoom, "setInitialZoom", "pdf-to-pdf", "Must be a positive integer number.", "set_initial_zoom"), 470);
            
            fields["initial_zoom"] = ConnectionHelper.intToString(zoom);
            return this;
        }

        /**
        * Specify whether to hide the viewer application's tool bars when the document is active.
        *
        * @param value Set to <span class='field-value'>true</span> to hide tool bars.
        * @return The converter object.
        */
        public PdfToPdfClient setHideToolbar(bool value)
        {
            fields["hide_toolbar"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to hide the viewer application's menu bar when the document is active.
        *
        * @param value Set to <span class='field-value'>true</span> to hide the menu bar.
        * @return The converter object.
        */
        public PdfToPdfClient setHideMenubar(bool value)
        {
            fields["hide_menubar"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to hide user interface elements in the document's window (such as scroll bars and navigation controls), leaving only the document's contents displayed.
        *
        * @param value Set to <span class='field-value'>true</span> to hide ui elements.
        * @return The converter object.
        */
        public PdfToPdfClient setHideWindowUi(bool value)
        {
            fields["hide_window_ui"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to resize the document's window to fit the size of the first displayed page.
        *
        * @param value Set to <span class='field-value'>true</span> to resize the window.
        * @return The converter object.
        */
        public PdfToPdfClient setFitWindow(bool value)
        {
            fields["fit_window"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether to position the document's window in the center of the screen.
        *
        * @param value Set to <span class='field-value'>true</span> to center the window.
        * @return The converter object.
        */
        public PdfToPdfClient setCenterWindow(bool value)
        {
            fields["center_window"] = value ? "true" : null;
            return this;
        }

        /**
        * Specify whether the window's title bar should display the document title. If false , the title bar should instead display the name of the PDF file containing the document.
        *
        * @param value Set to <span class='field-value'>true</span> to display the title.
        * @return The converter object.
        */
        public PdfToPdfClient setDisplayTitle(bool value)
        {
            fields["display_title"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the predominant reading order for text to right-to-left. This option has no direct effect on the document's contents or page numbering but can be used to determine the relative positioning of pages when displayed side by side or printed n-up
        *
        * @param value Set to <span class='field-value'>true</span> to set right-to-left reading order.
        * @return The converter object.
        */
        public PdfToPdfClient setRightToLeft(bool value)
        {
            fields["right_to_left"] = value ? "true" : null;
            return this;
        }

        /**
        * Turn on the debug logging. Details about the conversion are stored in the debug log. The URL of the log can be obtained from the <a href='#get_debug_log_url'>getDebugLogUrl</a> method or available in <a href='/user/account/log/conversion/'>conversion statistics</a>.
        *
        * @param value Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public PdfToPdfClient setDebugLog(bool value)
        {
            fields["debug_log"] = value ? "true" : null;
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
        * This method can only be called after a call to one of the convertXtoY methods.
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
        * Get the version details.
        * @return API version, converter version, and client version.
        */
        public string getVersion()
        {
            return string.Format("client {0}, API v2, converter {1}", ConnectionHelper.CLIENT_VERSION, helper.getConverterVersion());
        }

        /**
        * Tag the conversion with a custom value. The tag is used in <a href='/user/account/log/conversion/'>conversion statistics</a>. A value longer than 32 characters is cut off.
        *
        * @param tag A string with the custom tag.
        * @return The converter object.
        */
        public PdfToPdfClient setTag(string tag)
        {
            fields["tag"] = tag;
            return this;
        }

        /**
        * Set the converter version. Different versions may produce different output. Choose which one provides the best output for your case.
        *
        * @param version The version identifier. Allowed values are latest, 20.10, 18.10.
        * @return The converter object.
        */
        public PdfToPdfClient setConverterVersion(string version)
        {
            if (!Regex.Match(version, "(?i)^(latest|20.10|18.10)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(version, "setConverterVersion", "pdf-to-pdf", "Allowed values are latest, 20.10, 18.10.", "set_converter_version"), 470);
            
            helper.setConverterVersion(version);
            return this;
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * Warning: Using HTTP is insecure as data sent over HTTP is not encrypted. Enable this option only if you know what you are doing.
        *
        * @param value Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public PdfToPdfClient setUseHttp(bool value)
        {
            helper.setUseHttp(value);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be useful if you are behind a proxy or a firewall.
        *
        * @param agent The user agent string.
        * @return The converter object.
        */
        public PdfToPdfClient setUserAgent(string agent)
        {
            helper.setUserAgent(agent);
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
        * Specifies the number of automatic retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        *
        * @param count Number of retries.
        * @return The converter object.
        */
        public PdfToPdfClient setRetryCount(int count)
        {
            helper.setRetryCount(count);
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrl", "image-to-pdf", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrlToStream::url", "image-to-pdf", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertUrlToFile::file_path", "image-to-pdf", "The string must not be empty.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertUrlToStream(url, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert a local file.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertFile(string file)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFile", "image-to-pdf", "The file must exist and not be empty.", "convert_file"), 470);
            
            files["file"] = file;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a local file and write the result to an output stream.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertFileToStream(string file, Stream outStream)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFileToStream::file", "image-to-pdf", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
            files["file"] = file;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a local file and write the result to a local file.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertFileToFile(string file, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertFileToFile::file_path", "image-to-pdf", "The string must not be empty.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertFileToStream(file, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertRawDataToFile::file_path", "image-to-pdf", "The string must not be empty.", "convert_raw_data_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertRawDataToStream(data, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert the contents of an input stream.
        *
        * @param inStream The input stream with source data.<br>
        * @return Byte array containing the conversion output.
        */
        public byte[] convertStream(Stream inStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert the contents of an input stream and write the result to an output stream.
        *
        * @param inStream The input stream with source data.<br>
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertStreamToStream(Stream inStream, Stream outStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert the contents of an input stream and write the result to a local file.
        *
        * @param inStream The input stream with source data.<br>
        * @param filePath The output file path. The string must not be empty.
        */
        public void convertStreamToFile(Stream inStream, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStreamToFile::file_path", "image-to-pdf", "The string must not be empty.", "convert_stream_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertStreamToStream(inStream, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
        * Turn on the debug logging. Details about the conversion are stored in the debug log. The URL of the log can be obtained from the <a href='#get_debug_log_url'>getDebugLogUrl</a> method or available in <a href='/user/account/log/conversion/'>conversion statistics</a>.
        *
        * @param value Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public ImageToPdfClient setDebugLog(bool value)
        {
            fields["debug_log"] = value ? "true" : null;
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
        * This method can only be called after a call to one of the convertXtoY methods.
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
        * Get the version details.
        * @return API version, converter version, and client version.
        */
        public string getVersion()
        {
            return string.Format("client {0}, API v2, converter {1}", ConnectionHelper.CLIENT_VERSION, helper.getConverterVersion());
        }

        /**
        * Tag the conversion with a custom value. The tag is used in <a href='/user/account/log/conversion/'>conversion statistics</a>. A value longer than 32 characters is cut off.
        *
        * @param tag A string with the custom tag.
        * @return The converter object.
        */
        public ImageToPdfClient setTag(string tag)
        {
            fields["tag"] = tag;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTP scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public ImageToPdfClient setHttpProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpProxy", "image-to-pdf", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_http_proxy"), 470);
            
            fields["http_proxy"] = proxy;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTPS scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public ImageToPdfClient setHttpsProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpsProxy", "image-to-pdf", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_https_proxy"), 470);
            
            fields["https_proxy"] = proxy;
            return this;
        }

        /**
        * Set the converter version. Different versions may produce different output. Choose which one provides the best output for your case.
        *
        * @param version The version identifier. Allowed values are latest, 20.10, 18.10.
        * @return The converter object.
        */
        public ImageToPdfClient setConverterVersion(string version)
        {
            if (!Regex.Match(version, "(?i)^(latest|20.10|18.10)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(version, "setConverterVersion", "image-to-pdf", "Allowed values are latest, 20.10, 18.10.", "set_converter_version"), 470);
            
            helper.setConverterVersion(version);
            return this;
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * Warning: Using HTTP is insecure as data sent over HTTP is not encrypted. Enable this option only if you know what you are doing.
        *
        * @param value Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public ImageToPdfClient setUseHttp(bool value)
        {
            helper.setUseHttp(value);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be useful if you are behind a proxy or a firewall.
        *
        * @param agent The user agent string.
        * @return The converter object.
        */
        public ImageToPdfClient setUserAgent(string agent)
        {
            helper.setUserAgent(agent);
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
        * Specifies the number of automatic retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        *
        * @param count Number of retries.
        * @return The converter object.
        */
        public ImageToPdfClient setRetryCount(int count)
        {
            helper.setRetryCount(count);
            return this;
        }

    }

    /**
    * Conversion from PDF to HTML.
    */
    public sealed class PdfToHtmlClient
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
        public PdfToHtmlClient(string userName, string apiKey)
        {
            this.helper = new ConnectionHelper(userName, apiKey);
            fields["input_format"] = "pdf";
            fields["output_format"] = "html";
        }

        /**
        * Convert a PDF.
        *
        * @param url The address of the PDF to convert. The supported protocols are http:// and https://.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertUrl(string url)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrl", "pdf-to-html", "The supported protocols are http:// and https://.", "convert_url"), 470);
            
            fields["url"] = url;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a PDF and write the result to an output stream.
        *
        * @param url The address of the PDF to convert. The supported protocols are http:// and https://.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertUrlToStream(string url, Stream outStream)
        {
            if (!Regex.Match(url, "(?i)^https?://.*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(url, "convertUrlToStream::url", "pdf-to-html", "The supported protocols are http:// and https://.", "convert_url_to_stream"), 470);
            
            fields["url"] = url;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a PDF and write the result to a local file.
        *
        * @param url The address of the PDF to convert. The supported protocols are http:// and https://.
        * @param filePath The output file path. The string must not be empty. The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.
        */
        public void convertUrlToFile(string url, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertUrlToFile::file_path", "pdf-to-html", "The string must not be empty.", "convert_url_to_file"), 470);
            
            if (!(isOutputTypeValid(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertUrlToFile::file_path", "pdf-to-html", "The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.", "convert_url_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertUrlToStream(url, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert a local file.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @return Byte array containing the conversion output.
        */
        public byte[] convertFile(string file)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFile", "pdf-to-html", "The file must exist and not be empty.", "convert_file"), 470);
            
            files["file"] = file;
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert a local file and write the result to an output stream.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertFileToStream(string file, Stream outStream)
        {
            if (!(File.Exists(file) && new FileInfo(file).Length > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(file, "convertFileToStream::file", "pdf-to-html", "The file must exist and not be empty.", "convert_file_to_stream"), 470);
            
            files["file"] = file;
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert a local file and write the result to a local file.
        *
        * @param file The path to a local file to convert.<br>  The file must exist and not be empty.
        * @param filePath The output file path. The string must not be empty. The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.
        */
        public void convertFileToFile(string file, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertFileToFile::file_path", "pdf-to-html", "The string must not be empty.", "convert_file_to_file"), 470);
            
            if (!(isOutputTypeValid(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertFileToFile::file_path", "pdf-to-html", "The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.", "convert_file_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertFileToStream(file, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
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
        * @param filePath The output file path. The string must not be empty. The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.
        */
        public void convertRawDataToFile(byte[] data, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertRawDataToFile::file_path", "pdf-to-html", "The string must not be empty.", "convert_raw_data_to_file"), 470);
            
            if (!(isOutputTypeValid(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertRawDataToFile::file_path", "pdf-to-html", "The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.", "convert_raw_data_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertRawDataToStream(data, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Convert the contents of an input stream.
        *
        * @param inStream The input stream with source data.<br>
        * @return Byte array containing the conversion output.
        */
        public byte[] convertStream(Stream inStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            return helper.post(fields, files, rawData, null);
        }

        /**
        * Convert the contents of an input stream and write the result to an output stream.
        *
        * @param inStream The input stream with source data.<br>
        * @param outStream The output stream that will contain the conversion output.
        */
        public void convertStreamToStream(Stream inStream, Stream outStream)
        {
            rawData["stream"] = ConnectionHelper.ReadStream(inStream);
            helper.post(fields, files, rawData, outStream);
        }

        /**
        * Convert the contents of an input stream and write the result to a local file.
        *
        * @param inStream The input stream with source data.<br>
        * @param filePath The output file path. The string must not be empty. The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.
        */
        public void convertStreamToFile(Stream inStream, string filePath)
        {
            if (!(!String.IsNullOrEmpty(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStreamToFile::file_path", "pdf-to-html", "The string must not be empty.", "convert_stream_to_file"), 470);
            
            if (!(isOutputTypeValid(filePath)))
                throw new Error(ConnectionHelper.createInvalidValueMessage(filePath, "convertStreamToFile::file_path", "pdf-to-html", "The converter generates an HTML or ZIP file. If ZIP file is generated, the file path must have a ZIP or zip extension.", "convert_stream_to_file"), 470);
            
            FileStream outputFile = new FileStream(filePath, FileMode.CreateNew);
            try
            {
                convertStreamToStream(inStream, outputFile);
                outputFile.Close();
            }
            catch(Error)
            {
                outputFile.Close();
                File.Delete(filePath);
                throw;
            }
        }

        /**
        * Password to open the encrypted PDF file.
        *
        * @param password The input PDF password.
        * @return The converter object.
        */
        public PdfToHtmlClient setPdfPassword(string password)
        {
            fields["pdf_password"] = password;
            return this;
        }

        /**
        * Set the scaling factor (zoom) for the main page area.
        *
        * @param factor The percentage value. Must be a positive integer number.
        * @return The converter object.
        */
        public PdfToHtmlClient setScaleFactor(int factor)
        {
            if (!(factor > 0))
                throw new Error(ConnectionHelper.createInvalidValueMessage(factor, "setScaleFactor", "pdf-to-html", "Must be a positive integer number.", "set_scale_factor"), 470);
            
            fields["scale_factor"] = ConnectionHelper.intToString(factor);
            return this;
        }

        /**
        * Set the page range to print.
        *
        * @param pages A comma separated list of page numbers or ranges.
        * @return The converter object.
        */
        public PdfToHtmlClient setPrintPageRange(string pages)
        {
            if (!Regex.Match(pages, "^(?:\\s*(?:\\d+|(?:\\d*\\s*\\-\\s*\\d+)|(?:\\d+\\s*\\-\\s*\\d*))\\s*,\\s*)*\\s*(?:\\d+|(?:\\d*\\s*\\-\\s*\\d+)|(?:\\d+\\s*\\-\\s*\\d*))\\s*$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(pages, "setPrintPageRange", "pdf-to-html", "A comma separated list of page numbers or ranges.", "set_print_page_range"), 470);
            
            fields["print_page_range"] = pages;
            return this;
        }

        /**
        * Specifies where the images are stored.
        *
        * @param mode The image storage mode. Allowed values are embed, separate.
        * @return The converter object.
        */
        public PdfToHtmlClient setImageMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(embed|separate)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setImageMode", "pdf-to-html", "Allowed values are embed, separate.", "set_image_mode"), 470);
            
            fields["image_mode"] = mode;
            return this;
        }

        /**
        * Specifies where the style sheets are stored.
        *
        * @param mode The style sheet storage mode. Allowed values are embed, separate.
        * @return The converter object.
        */
        public PdfToHtmlClient setCssMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(embed|separate)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setCssMode", "pdf-to-html", "Allowed values are embed, separate.", "set_css_mode"), 470);
            
            fields["css_mode"] = mode;
            return this;
        }

        /**
        * Specifies where the fonts are stored.
        *
        * @param mode The font storage mode. Allowed values are embed, separate.
        * @return The converter object.
        */
        public PdfToHtmlClient setFontMode(string mode)
        {
            if (!Regex.Match(mode, "(?i)^(embed|separate)$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(mode, "setFontMode", "pdf-to-html", "Allowed values are embed, separate.", "set_font_mode"), 470);
            
            fields["font_mode"] = mode;
            return this;
        }

        /**
        * A helper method to determine if the output file is a zip archive. The output of the conversion may be either an HTML file or a zip file containing the HTML and its external assets.
        * @return <span class='field-value'>True</span> if the conversion output is a zip file, otherwise <span class='field-value'>False</span>.
        */
        public bool isZippedOutput()
        {
            return (fields.ContainsKey("image_mode") && fields["image_mode"] == "separate") || (fields.ContainsKey("css_mode") && fields["css_mode"] == "separate") || (fields.ContainsKey("font_mode") && fields["font_mode"] == "separate") || (fields.ContainsKey("force_zip") && fields["force_zip"] == "true");
        }

        /**
        * Enforces the zip output format.
        *
        * @param value Set to <span class='field-value'>true</span> to get the output as a zip archive.
        * @return The converter object.
        */
        public PdfToHtmlClient setForceZip(bool value)
        {
            fields["force_zip"] = value ? "true" : null;
            return this;
        }

        /**
        * Set the HTML title. The title from the input PDF is used by default.
        *
        * @param title The HTML title.
        * @return The converter object.
        */
        public PdfToHtmlClient setTitle(string title)
        {
            fields["title"] = title;
            return this;
        }

        /**
        * Set the HTML subject. The subject from the input PDF is used by default.
        *
        * @param subject The HTML subject.
        * @return The converter object.
        */
        public PdfToHtmlClient setSubject(string subject)
        {
            fields["subject"] = subject;
            return this;
        }

        /**
        * Set the HTML author. The author from the input PDF is used by default.
        *
        * @param author The HTML author.
        * @return The converter object.
        */
        public PdfToHtmlClient setAuthor(string author)
        {
            fields["author"] = author;
            return this;
        }

        /**
        * Associate keywords with the HTML document. Keywords from the input PDF are used by default.
        *
        * @param keywords The string containing the keywords.
        * @return The converter object.
        */
        public PdfToHtmlClient setKeywords(string keywords)
        {
            fields["keywords"] = keywords;
            return this;
        }

        /**
        * Turn on the debug logging. Details about the conversion are stored in the debug log. The URL of the log can be obtained from the <a href='#get_debug_log_url'>getDebugLogUrl</a> method or available in <a href='/user/account/log/conversion/'>conversion statistics</a>.
        *
        * @param value Set to <span class='field-value'>true</span> to enable the debug logging.
        * @return The converter object.
        */
        public PdfToHtmlClient setDebugLog(bool value)
        {
            fields["debug_log"] = value ? "true" : null;
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
        * This method can only be called after a call to one of the convertXtoY methods.
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
        * Get the version details.
        * @return API version, converter version, and client version.
        */
        public string getVersion()
        {
            return string.Format("client {0}, API v2, converter {1}", ConnectionHelper.CLIENT_VERSION, helper.getConverterVersion());
        }

        /**
        * Tag the conversion with a custom value. The tag is used in <a href='/user/account/log/conversion/'>conversion statistics</a>. A value longer than 32 characters is cut off.
        *
        * @param tag A string with the custom tag.
        * @return The converter object.
        */
        public PdfToHtmlClient setTag(string tag)
        {
            fields["tag"] = tag;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTP scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public PdfToHtmlClient setHttpProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpProxy", "pdf-to-html", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_http_proxy"), 470);
            
            fields["http_proxy"] = proxy;
            return this;
        }

        /**
        * A proxy server used by Pdfcrowd conversion process for accessing the source URLs with HTTPS scheme. It can help to circumvent regional restrictions or provide limited access to your intranet.
        *
        * @param proxy The value must have format DOMAIN_OR_IP_ADDRESS:PORT.
        * @return The converter object.
        */
        public PdfToHtmlClient setHttpsProxy(string proxy)
        {
            if (!Regex.Match(proxy, "(?i)^([a-z0-9]+(-[a-z0-9]+)*\\.)+[a-z0-9]{1,}:\\d+$").Success)
                throw new Error(ConnectionHelper.createInvalidValueMessage(proxy, "setHttpsProxy", "pdf-to-html", "The value must have format DOMAIN_OR_IP_ADDRESS:PORT.", "set_https_proxy"), 470);
            
            fields["https_proxy"] = proxy;
            return this;
        }

        /**
        * Specifies if the client communicates over HTTP or HTTPS with Pdfcrowd API.
        * Warning: Using HTTP is insecure as data sent over HTTP is not encrypted. Enable this option only if you know what you are doing.
        *
        * @param value Set to <span class='field-value'>true</span> to use HTTP.
        * @return The converter object.
        */
        public PdfToHtmlClient setUseHttp(bool value)
        {
            helper.setUseHttp(value);
            return this;
        }

        /**
        * Set a custom user agent HTTP header. It can be useful if you are behind a proxy or a firewall.
        *
        * @param agent The user agent string.
        * @return The converter object.
        */
        public PdfToHtmlClient setUserAgent(string agent)
        {
            helper.setUserAgent(agent);
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
        public PdfToHtmlClient setProxy(string host, int port, string userName, string password)
        {
            helper.setProxy(host, port, userName, password);
            return this;
        }

        /**
        * Specifies the number of automatic retries when the 502 HTTP status code is received. The 502 status code indicates a temporary network issue. This feature can be disabled by setting to 0.
        *
        * @param count Number of retries.
        * @return The converter object.
        */
        public PdfToHtmlClient setRetryCount(int count)
        {
            helper.setRetryCount(count);
            return this;
        }

        private bool isOutputTypeValid(string file_path) {
            string extension = Path.GetExtension(file_path);
            return (extension == ".zip") == isZippedOutput();
        }
    }


}
