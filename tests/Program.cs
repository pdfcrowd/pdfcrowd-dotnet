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
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using pdfcrowd;

namespace tests
{
    class Program
    {
      private static void run_test( Tests test_obj, 
                                    string name,
                                    bool use_ssl)
      {
          System.Console.WriteLine(name);
          MethodInfo method = typeof(Tests).GetMethod(name);
          object[] args = new object[1];
          args[0] = use_ssl;
          method.Invoke( test_obj, args);
      }
      
      static void Main(string[] args)
      {
        Tests t = new Tests(args);
        run_test(t, "TestConvertByURI", false);
        run_test(t, "TestConvertFile", false);
        run_test(t, "TestConvertHtml", false);
        run_test(t, "TestTokens", false);
        run_test(t, "TestStreams", false);

        if (t.client.HOST == "pdfcrowd.com")
        {
            run_test(t, "TestConvertByURI", true);
            run_test(t, "TestConvertFile", true);
            run_test(t, "TestConvertHtml", true);
            run_test(t, "TestTokens", true);
        }
      }
    }
}
