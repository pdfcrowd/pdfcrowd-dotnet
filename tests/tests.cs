// Copyright (C) 2009-2013 pdfcrowd.com
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
using System.Diagnostics;
using System.Text;
using System.Collections.Generic;
using pdfcrowd;
using System.IO;

namespace tests
{
  /// <summary>
  /// Summary description for tests
  /// </summary>
  public class Tests
  {
    public Client client;
    private string[] m_args;
    
    public Tests(string[] args)
    {
      m_args = args;

      if (args.Length == 5) {
        Client.HTTP_PORT = int.Parse(args[3]);
        Client.HTTPS_PORT = int.Parse(args[4]);
      }

      client = getClient(false);

      client.setPageMode( Client.FULLSCREEN );

      string this_dir = 
        System.IO.Path.GetDirectoryName(Environment.GetCommandLineArgs()[0]);
      Directory.SetCurrentDirectory (this_dir);
      Directory.CreateDirectory("../test_files/out");
    }

    FileStream prepare_file(string file_tag, bool use_ssl)
    {
      string fname = "../test_files/out/cs_client_" + file_tag;
      if (use_ssl)
        fname += "_ssl";
      fname += ".pdf";

      if (File.Exists(fname))
            {
              File.Delete(fname);
            }  

      return new FileStream(fname, FileMode.CreateNew);
    }

    private Client getClient(bool use_ssl)
    {
      Client client;
      if (m_args.Length > 2) 
        {
          client = new Client(m_args[0], m_args[1], m_args[2]);
        }
      else
        {
          client = new Client(m_args[0], m_args[1]);
        }
      
      if (use_ssl)
        client.useSSL(use_ssl);
      
      return client;
    }
    
    public void TestConvertByURI(bool use_ssl)
    {
      try
        {
          FileStream stream;
          stream = prepare_file("uri", use_ssl);
          client.useSSL(use_ssl);
          client.setPageWidth(8.5*72);
          client.setPageHeight(10.5*72);
          client.setHorizontalMargin(72.0);
          client.setVerticalMargin(2*72);                 
          
          client.convertURI( "https://storage.googleapis.com/pdfcrowd-legacy-tests/tests/webtopdfcom.html", stream );
          stream.Close();
          
        }
      catch(pdfcrowd.Error why)
        {
          System.Console.WriteLine(why.ToString());
          Environment.ExitCode = 1;
        }
    }

    public void TestStreams(bool use_ssl)
    {
      try
        {
          MemoryStream memStream = new MemoryStream();
          client.convertHtml( "some html",  memStream);
          FileStream fileStream = prepare_file("from_memstream", use_ssl);
          CopyStream(memStream, fileStream);
          fileStream.Close();
        }
      catch(pdfcrowd.Error why)
        {
          System.Console.WriteLine(why.ToString());
          Environment.ExitCode = 1;
        }
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
          
          
    
    private void test_convert_html( string out_name, bool use_ssl )
    {
      try
        {
          string some_html = "<html><body>Uploaded content!</body></html>";
          FileStream stream = prepare_file(out_name, use_ssl);
          client.useSSL(use_ssl);
          client.convertHtml( some_html, stream );
          stream.Close();
        }
      catch(pdfcrowd.Error why)
        {
          System.Console.WriteLine(why.ToString());
          Environment.ExitCode = 1;
        }
    }
    
    public void TestConvertHtml(bool use_ssl)
    {
      client.setPageWidth("5in");
      client.setPageHeight("10in");
      client.setHorizontalMargin("2in");
      client.setVerticalMargin("1in");                 

      test_convert_html("content", use_ssl);
    }
    
    public void TestConvertFile(bool use_ssl)
    {
      try
        {
          FileStream stream = prepare_file("upload", use_ssl);
          client.useSSL(use_ssl);
          client.convertFile(@"../test_files/in/simple.html", stream);
          stream.Close();

          stream = prepare_file("archive", use_ssl);
          client.convertFile(@"../test_files/in/archive.tar.gz", stream);
          stream.Close();
        }
      catch(pdfcrowd.Error why)
        {
          System.Console.WriteLine(why.ToString());
          Environment.ExitCode = 1;
        }
    }

    public void TestTokens(bool use_ssl)
    {
      try
        {
          int tokens = client.numTokens();
          test_convert_html("content_1.pdf", use_ssl);
          Debug.Assert( tokens - 1 == client.numTokens() );
        }
      catch(pdfcrowd.Error why)
        {
          System.Console.WriteLine(why.ToString());
          Environment.ExitCode = 1;
        }
    }

    public void TestMore(bool use_ssl)
    {
      // 4 margins
      try
        {
          Client c = getClient(use_ssl);

          c.setPageMargins("0.25in", "0.5in", "0.75in", "1.0in");
          FileStream stream = prepare_file("4margins", use_ssl);
          c.convertHtml("<div style='background-color:red;height:100%'>4 margins</div>", stream);
          stream.Close();
        }
      catch(pdfcrowd.Error why)
        {
          System.Console.WriteLine(why.ToString());
          Environment.ExitCode = 1;
        }
      
    }

  }
}
