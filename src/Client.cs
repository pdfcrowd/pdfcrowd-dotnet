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

namespace pdfcrowd
{
  //
  // Pdfcrowd API client.
  // 
  public class Client
  {
    //
    // Client constructor.
    // 
    // username - your username at Pdfcrowd
    // api_key  - your API key
    // 
    public Client(string username, string api_key)
    {
      useSSL(false);
      fields.Add("username", username);
      fields.Add("key", api_key);
      fields.Add("pdf_scaling_factor", "1");
      fields.Add("html_zoom", "200");
    }

    //
    // Client constructor.
    // 
    // username - your username at Pdfcrowd
    // api_key  - your API key
    // 
    // 
    public Client(string username, string api_key, string hostname)
    {
      fields.Add("username", username);
      fields.Add("key", api_key);
      fields.Add("pdf_scaling_factor", "1");
      fields.Add("html_zoom", "200");
      HOST = hostname;
      useSSL(false);
    }
          

    //
    // Converts a web page.
    //
    // uri        - a web page URL
    // out_stream - a System.IO.Stream implementation
    // 
    public void convertURI(string uri, Stream out_stream)
    {
      convert(out_stream, "uri", uri);
    }
    
    //
    // Converts an in-memory html document.
    //
    // content    - a string containing a html document
    // out_stream - a System.IO.Stream implementation
    // 
    public void convertHtml(string content, Stream out_stream)
    {
      convert(out_stream, "html", content);
    }

    //
    // Converts an html file.
    //
    // fpath      - a path to an html file
    // out_stream - a System.IO.Stream implementation
    // 
    public void convertFile(string fpath, Stream out_stream)
    {
      post_multipart(fpath, out_stream);
    }

    //
    // Returns the number of available conversion tokens.
    // 
    public int numTokens()
    {
      string uri = String.Format("{0}user/{1}/tokens/", api_uri, fields["username"]);
      MemoryStream stream = new MemoryStream();
      call_api(uri, stream, null);
      stream.Position = 0;
      string value = read_stream(stream);
      return Int32.Parse(value);
    }
          
    
    public void useSSL(bool use_ssl)
    {
      if(use_ssl)
      {
          api_uri = string.Format("https://{0}:{1}{2}", HOST, HTTPS_PORT, API_SELECTOR_BASE);
      }
      else
      {
          api_uri = string.Format("http://{0}:{1}{2}", HOST, HTTP_PORT, API_SELECTOR_BASE);
      }
    }
    
    public void setUsername(string username)
    {
      fields["username"] = username;
    }

    public void setApiKey(string key)
    {
      fields["key"] = key;
    }

    public void setPageWidth(string value)
    {
      fields["width"] = value;
    }

    public void setPageWidth(double value)
    {
      fields["width"] = value.ToString();;
    }
        
    public void setPageHeight(string value)
    {
      fields["height"] = value;
    }
          
    public void setPageHeight(double value)
    {
      fields["height"] = value.ToString();
    }

    public void setHorizontalMargin(double value) 
    {
      fields["margin_right"] = value.ToString();
      fields["margin_left"] = value.ToString();
    }

    public void setHorizontalMargin(string value) 
    {
      fields["margin_right"] = value;
      fields["margin_left"] = value;
    }

    public void setVerticalMargin(double value) 
    {
      fields["margin_top"] = value.ToString();
      fields["margin_bottom"] = value.ToString();
    }
    
    public void setVerticalMargin(string value) 
    {
      fields["margin_top"] = value;
      fields["margin_bottom"] = value;
    }

    public void setPageMargins(string top, string right, string bottom, string left) 
    {
      fields["margin_top"] = top;
      fields["margin_right"] = right;
      fields["margin_bottom"] = bottom;
      fields["margin_left"] = left;
    }

    public void setEncrypted(bool value)
    {
      fields["encrypted"] = value ? "true" : null;
    }
    
    public void setEncrypted()
    {
      setEncrypted(true);
    }
    
    public void setUserPassword(string pwd)
    {
      fields["user_pwd"] = pwd;
    }
    
    public void setOwnerPassword(string pwd)
    {
      fields["owner_pwd"] = pwd;
    }

    public void setNoPrint(bool value)
    {
      fields["no_print"] = value ? "true" : null;
    }
    
    public void setNoPrint()
    {
      setNoPrint(true);
    }
    
    public void setNoModify(bool value)
    {
      fields["no_modify"] = value ? "true" : null;
    }

    public void setNoModify()
    {
      setNoModify(true);
    }

    public void setNoCopy(bool value)
    {
      fields["no_copy"] = value ? "true" : null;
    }

    public void setNoCopy()
    {
      setNoCopy(true);
    }

    // constants for setPageLayout()
    static public int SINGLE_PAGE = 1;
    static public int CONTINUOUS = 2;
    static public int CONTINUOUS_FACING = 3;
          
    public void setPageLayout(int value)
    {
      Debug.Assert(value > 0 && value <= 3);
      fields["page_layout"] = value.ToString();
    }

    // constants for setPageMode()
    static public int NONE_VISIBLE = 1;
    static public int THUMBNAILS_VISIBLE = 2;
    static public int FULLSCREEN = 3;
          
    public void setPageMode(int value)
    {
      Debug.Assert(value > 0 && value <= 3);
      fields["page_mode"] = value.ToString();
    }

    public void setFooterText(string value) {
        fields["footer_text"] = value;
    }
    
    public void enableImages() {
        enableImages(true);
    }

    public void enableImages(bool value) {
        fields["no_images"] = value ? null : "true";
    }

    public void enableBackgrounds() {
        enableBackgrounds(true);
    }
    
    public void enableBackgrounds(bool value) {
        fields["no_backgrounds"] = value ? null : "true";
    }

    public void setHtmlZoom(float value) {
        fields["html_zoom"] = value.ToString();
    }

    public void enableJavaScript() {
        enableJavaScript(true);
    }
    
    public void enableJavaScript(bool value) {
        fields["no_javascript"] = value ? null : "true";
    }

    public void enableHyperlinks() {
        enableHyperlinks(true);
    }

    public void enableHyperlinks(bool value) {
        fields["no_hyperlinks"] = value ? null : "true";
    }
    
    public void setDefaultTextEncoding(string value) {
        fields["text_encoding"] = value;
    }

    public void usePrintMedia() {
        usePrintMedia(true);
    }
    
    public void usePrintMedia(bool value) {
        fields["use_print_media"] = value ? "true" : null;
    }

    public void setMaxPages(int value) {
        fields["max_pages"] = value.ToString();
    }

    public void enablePdfcrowdLogo() {
        enablePdfcrowdLogo(true);
    }

    public void enablePdfcrowdLogo(bool value) {
        fields["pdfcrowd_logo"] = value ? "true" : null;
    }

    // constants for setInitialPdfZoomType()
    static public int FIT_WIDTH = 1;
    static public int FIT_HEIGHT = 2;
    static public int FIT_PAGE = 3;
          
    public void setInitialPdfZoomType(int value) {
        Debug.Assert(value>0 && value<=3);
        fields["initial_pdf_zoom_type"] = value.ToString();
    }
    
    public void setInitialPdfExactZoom(float value) {
        fields["initial_pdf_zoom_type"] = "4";
        fields["initial_pdf_zoom"] = value.ToString();
    }
          
    public void setAuthor(string value) {
        fields["author"] = value;
    }

    public void setFailOnNon200(bool value) {
        fields["fail_on_non200"] = value ? "true" : null;
    }

    public void setPdfScalingFactor(float value) {
        fields["pdf_scaling_factor"] = value.ToString();
    }

    public void setFooterHtml(string value) {
        fields["footer_html"] = value;
    }
        
    public void setFooterUrl(string value) {
        fields["footer_url"] = value;
    }
        
    public void setHeaderHtml(string value) {
        fields["header_html"] = value;
    }
        
    public void setHeaderUrl(string value) {
        fields["header_url"] = value;
    }

    public void setPageBackgroundColor(string value) {
        fields["page_background_color"] = value;
    }



          
    public void setTransparentBackground() {
        setTransparentBackground(true);
    }
    
    public void setTransparentBackground(bool val) {
        fields["transparent_background"] = val ? "true" : null;
    }


    public void setPageNumberingOffset(int value) {
        fields["page_numbering_offset"] = value.ToString();
    }

    public void setHeaderFooterPageExcludeList(string value) {
        fields["header_footer_page_exclude_list"] = value;
    }
        
    public void setWatermark(string url, float offset_x, float offset_y) {
        fields["watermark_url"] = url;
        fields["watermark_offset_x"] = offset_x.ToString();
        fields["watermark_offset_y"] = offset_y.ToString();
    }

    public void setWatermark(string url, string offset_x, string offset_y) {
        fields["watermark_url"] = url;
        fields["watermark_offset_x"] = offset_x;
        fields["watermark_offset_y"] = offset_y;
    }
    
    public void setWatermarkRotation(double angle) {
        fields["watermark_rotation"] = angle.ToString();
    }

    public void setWatermarkInBackground() {
            setWatermarkInBackground(true);
    }
    
    public void setWatermarkInBackground(bool val) {
        fields["watermark_in_background"] = val ? "true" : null;
    }
          
          


    // ---------------------------------------------------------------------------
    //
    //                        Private stuff
    //

    static string API_SELECTOR_BASE = "/api/";

    public string HOST = "pdfcrowd.com";
    static public int HTTP_PORT = 80;
    static public int HTTPS_PORT = 443;

    StringDictionary fields = new StringDictionary();
    string api_uri;

          
    private void convert(Stream out_stream, string method, string src)
    {
      string uri = String.Format("{0}pdf/convert/{1}/", api_uri, method);
      call_api(uri, out_stream, src);
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
    

    private void call_api(string uri, Stream out_stream, string src)
    {
      StringDictionary extra_data = new StringDictionary();
      if (src != null)
      {
          extra_data["src"] = src;
      }
      string data = encode_post_data(extra_data);
      do_request(uri, out_stream, data, "application/x-www-form-urlencoded");
    }

    private static void do_request(string uri, Stream out_stream, object data, string content_type)
    {
      WebRequest request = WebRequest.Create(uri);
      request.Method = "POST";
     
      byte[] byteArray;
      if ((data is byte[]))
      {
          byteArray = (byte[])data;
      }
      else
      {
          byteArray = Encoding.UTF8.GetBytes((string)data);
      }
      request.ContentType = content_type;
      request.ContentLength = byteArray.Length;
      try
      {
          using(Stream dataStream = request.GetRequestStream())
          {
              dataStream.Write(byteArray, 0, byteArray.Length);
          }
          
          // Get the response.
          using(HttpWebResponse response = (HttpWebResponse) request.GetResponse())
          {
              if(response.StatusCode == HttpStatusCode.OK)
              {
                  // Get the stream containing content returned by the server.
                  using(Stream dataStream = response.GetResponseStream())
                  {
                      CopyStream(dataStream, out_stream);
                      out_stream.Position = 0;
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
          if (why.Status == WebExceptionStatus.ProtocolError)
          {
              HttpWebResponse response = (HttpWebResponse)why.Response;

              MemoryStream stream = new MemoryStream();
              CopyStream(response.GetResponseStream(), stream);
              stream.Position = 0;
              string err = read_stream(stream);
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
    
    private string encode_post_data(StringDictionary extra_data)
    {
      StringDictionary data = new StringDictionary();
      if(extra_data != null)
      {
          foreach(DictionaryEntry entry in extra_data)
          {
              if(entry.Value != null)
              {
                  data.Add(entry.Key.ToString(), entry.Value.ToString());
              }
          }
      }
      
      foreach(DictionaryEntry entry in fields)
      {
          if(entry.Value != null)
          {
              data.Add(entry.Key.ToString(), entry.Value.ToString());
          }
      }
      string result = "";
      int i = 0;
      foreach(DictionaryEntry entry in data)
      {
          result += HttpUtility.UrlEncode(entry.Key.ToString()) + "=" + HttpUtility.UrlEncode(entry.Value.ToString());
          if(i < data.Count)
          {
              result += "&";
          }
      }
      return result.Substring(0, result.Length - 1);
    }
    
    private static string boundary = "----------ThIs_Is_tHe_bOUnDary_$";
    private static string multipart_content_type = "multipart/form-data; boundary=" + boundary;
    private static string new_line = "\r\n";

    private byte[] encode_multipart_post_data(string filename)
    {
      MemoryStream memw = new MemoryStream();
      BinaryWriter retval = new BinaryWriter(memw);
      UTF8Encoding utf8 = new UTF8Encoding();

      string result = "";
      foreach(DictionaryEntry entry in fields)
        {
          if(entry.Value != null)
            {
              result += "--" + boundary + new_line;
              result += String.Format("Content-Disposition: form-data; name=\"{0}\"", entry.Key) + new_line;
              result += new_line;
              result += entry.Value.ToString() + new_line;
            }
        }
      // filename
      result += "--" + boundary + new_line;
      result += String.Format("Content-Disposition: form-data; name=\"src\"; filename=\"{0}\"", filename) + new_line;
      result += "Content-Type: application/octet-stream" + new_line;
      result += new_line;
      retval.Write(utf8.GetBytes(result));
      // filename contents
      using(FileStream fin = File.OpenRead(filename))
        {
          byte[] b = new byte[8192];
          int r;
          while ((r = fin.Read(b, 0, b.Length)) > 0)
            retval.Write(b, 0, r);
        }
      // finalize
      result = new_line + "--" + boundary + "--" + new_line;
      result += new_line;

      retval.Write(utf8.GetBytes(result));
      retval.Flush();
      return memw.ToArray();
    }

    private static string read_stream(Stream stream)
    {
      using(StreamReader reader = new StreamReader(stream, Encoding.UTF8))
      {
          return reader.ReadToEnd();
      }
    }

    private void post_multipart(string fpath, Stream out_stream)
    {
      byte[] data = encode_multipart_post_data(fpath);
      do_request(api_uri + "pdf/convert/html/", out_stream, data, multipart_content_type);
    }
    

  }
}
